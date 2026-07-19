# 01: Orchestration hardening

Audit of the r15 sub-task orchestration implementation
(`AgentService.RunOrchestrationAsync` and friends) plus two pre-r15 agent
gaps the audit surfaced. Every item was verified by reading the shipped
code at 8b8cfcf.

## 1.1 Reconcile sub-task state before advancing (stuck-parent fix)

**Severity: high. This is the round's agent headliner.**

`AgentService.RunOrchestrationAsync` (AgentService.cs:603-672) picks
`next = parent.SubTaskPlan.FirstOrDefault(s => s.Status is Pending or Running)`
and, for a `Running` spec, unconditionally calls `RunAsync(next.TaskId!)`.
A spec's status only ever becomes terminal inside this loop (lines
655-664). But a child is an ordinary task that can reach
`Complete`/`Failed` **outside** the loop:

- The child is opened directly in the workbench and stepped/run to
  completion (`RunAsync` on a child has no `SubTaskPlan`, so it takes the
  plain path). Today that requires the review queue; once doc 03 ships
  the recent-tasks list, opening children directly is a first-class flow.
- `ApproveReviewAsync` (AgentViewModel.cs:656-677) resumes the CHILD id
  whenever the opened task is not the parent: the child-to-parent mapping
  at :673 only matches when `CurrentTask` happens to be the parent. If
  the user has the child itself open, the approval resumes the child as a
  plain task, which can run it to terminal.

Once that happens, every subsequent run of the parent loads the spec
still marked `Running`, calls `RunAsync(childTaskId)`, and
`RunStepAsync` throws `"Agent task is already finished."`
(AgentService.cs:220-221). The parent orchestration is permanently
wedged; the exception surfaces as a red status message forever.

**Fix (self-healing reconcile, not caller discipline):** at the top of
each `RunOrchestrationAsync` iteration, after loading the parent, load
each spec's child state for specs with a `TaskId` and non-terminal
status; if the child's persisted status is `Complete`/`Failed`, mark the
spec terminal exactly as lines 655-663 do (including the bounded
`ResultSummary` copyback from the child's `Summary`) and save the parent
before choosing `next`. The existing post-`RunAsync` block stays as-is.

Additionally fix the queue-side mapping so this path is rare, not just
survivable: add `ParentTaskId` to `AgentReviewQueueItem`
(AgentModels.cs:250-269; the index query at
FileAgentTaskStateStore.cs:104-112 already joins the parent row, so
selecting `t.parent_task_id` is free) and make
`ApproveReviewAsync` resume `item.ParentTaskId ?? item.TaskId` instead
of inferring parenthood from whatever task is currently open.

**Acceptance:**
- Parent with spec `Running` whose child's `task_state.json` says
  `Complete` advances past it on the next parent run, copies the child's
  summary into the spec, and reaches synthesis. Same for `Failed`.
- Approving a child's pending action via the review queue resumes the
  parent orchestration even when the child (or an unrelated task) is the
  one open in the workbench.
- A plain (non-orchestrated) task's approve-and-resume behaviour is
  unchanged.

## 1.2 Manual "Run Step" must not bypass orchestration

`AgentViewModel.RunCurrentStepAsync` (AgentViewModel.cs:1202-1213) calls
`_agent.RunStepAsync(CurrentTask.TaskId, ...)` directly. For an
orchestration parent with unfinished sub-tasks this runs a bare parent
model step that the orchestration design says must not exist
(archived/r15/01-subtask-orchestration.md 1.4: "a parent with an
approved, unfinished SubTaskPlan never runs a parent model step itself").
Two concrete corruptions:

- The parent model can answer `final`: parent goes `Complete` with
  children unrun, and any later parent run throws "already finished"
  (same wedge as 1.1).
- The parent model can request `plan_subtasks` again (the depth-1 check
  at AgentService.cs:303 only blocks tasks WITH a `ParentTaskId`; a
  parent passes). Approving it hits
  `ApplyPlanSubtasksApprovalAsync` (AgentService.cs:1335-1355), which
  assigns `state.SubTaskPlan = specs` unconditionally, silently
  discarding the in-flight plan and orphaning already-created children.

**Fix, two layers:**
1. `AgentService.RunStepAsync`: if the loaded state has
   `SubTaskPlan.Count > 0` with any spec non-terminal, throw
   `InvalidOperationException` with a message telling the caller to use
   `RunAsync` (which routes to `RunOrchestrationAsync`). The synthesis
   path is unaffected: `RunSynthesisAsync` only calls `RunStepAsync`
   when every spec is terminal.
2. `AgentViewModel.RunCurrentStepAsync`: when
   `CurrentTask.SubTaskPlan` has unfinished specs, call
   `RunAgentLoopAsync()` instead of the single-step API, so the button
   advances the orchestration by resuming it (one child pause boundary
   at a time is fine; the point is it goes through the loop).

**Acceptance:** clicking Run Step on a parent with unfinished sub-tasks
advances orchestration (or resumes the paused child) and can never
produce a parent `final` while specs are unfinished. A direct
`RunStepAsync` call on such a parent throws with a clear message.

## 1.3 Block plan_subtasks re-proposal on a task that already has a plan

Related but distinct from 1.2 (defense in depth; also reachable if a
model proposes plan_subtasks during the synthesis step): a task whose
`SubTaskPlan` is non-empty must never accept another `plan_subtasks`.

**Fix:** in `RunStepAsync`, extend the existing depth-1 pre-gate check
(AgentService.cs:303-311) to also return `Blocked` when
`state.SubTaskPlan.Count > 0`, reason
`"This task already has a sub-task plan."`. In
`ApplyPlanSubtasksApprovalAsync`, reject (the existing invalid-plan
path at :1340-1347) if `state.SubTaskPlan.Count > 0` at approval time,
so a stale queued approval cannot clobber a plan either.

**Acceptance:** a parent mid-orchestration (and a completed parent)
that requests `plan_subtasks` gets `Blocked` at the gate; an approval
that races a plan into existence rejects instead of replacing
`SubTaskPlan`. Scenario 12 (depth) keeps passing.

## 1.4 Persist the task's workspace root; execute approvals against it

**Severity: high. Pre-r15 gap; orchestration widens it (children make
"which task is this action for" easier to lose track of).**

`AgentTaskState` does not store the workspace root. Every execution path
builds `AgentWorkspaceOptions` from the CURRENT workbench state
(`AgentViewModel.BuildOptions()`, AgentViewModel.cs:1349-1352), including
`ApproveReviewAsync` for review-queue items (:659). The review queue
lists tasks across ALL workspaces (the index has no workspace column).
Consequence: approving task X's pending `edit_file`/`create_file`/
`run_command` while the workbench points at workspace B executes the
action **relative to B**, not the workspace X was created in. For
`run_command` the recipe re-match (`WorkspaceCommandRecipes.TryMatch`
against the current root, AgentToolExecutor.cs:134-136) means a command
declared safe in workspace A runs inside workspace B whenever B declares
the same family ("dotnet build" is near-universal).

**Fix:**
- Add `AgentTaskState.WorkspaceRoot` (JSON-additive, default empty). Set
  it in `CreateTaskAsync` from the validated
  `ResolveWorkspaceRoot(options.WorkspaceRoot)` result, and in
  `CreateChildTaskAsync` from the parent's stored root.
- In `AppendApprovalAsync`, when the loaded state has a non-empty
  `WorkspaceRoot`, execute the pending action with options rebuilt on
  that root (`options with { WorkspaceRoot = state.WorkspaceRoot }`,
  or a from-scratch `AgentWorkspaceOptions` when `options` is null,
  which also closes item 1.5). A pre-r16 state with empty
  `WorkspaceRoot` keeps today's behaviour.
- Same substitution in the VM resume paths that call `RunAsync` for a
  task other than the one the workspace picker currently shows, so a
  resumed task steps against its own root. Simplest honest shape:
  `AgentViewModel` builds options from
  `CurrentTask?.WorkspaceRoot is { Length: > 0 } root ? root : WorkspaceRoot`.
- Surface it: the review queue item template (doc 03) shows the task's
  workspace folder name when it differs from the active one.

**Explicitly rejected:** refusing cross-workspace approvals outright.
The action was already approved by the user looking at its preview; the
fix is executing it where it belongs, not adding a refusal the user
cannot act on.

**Acceptance:**
- New tasks and children persist `WorkspaceRoot`; round-trip test via
  `FileAgentTaskStateStore`.
- With workspace B active, approving task-from-A's pending `create_file`
  writes under A, not B. Regression test with two temp roots.
- Pre-r16 `task_state.json` without the field loads and behaves as
  today (test deserializes a fixture without the property).

## 1.5 Approve-with-null-options must not strand a pending action

`AppendApprovalAsync` (AgentService.cs:753-838): when `approved: true`,
`PendingToolAction` set, but `options is null`, control falls to the
final `else` (:827-835) which sets `Status = Running` and **leaves
`PendingToolAction` set and the tool unexecuted** - a silently corrupt
"running with a stale approval attached" state. All current callers pass
options, so this is latent, but it is the kind of API hole 1.4 makes
real (approval execution no longer strictly needs caller-supplied
options).

**Fix:** with 1.4, a stored `WorkspaceRoot` lets the null-options case
execute normally. If the state has no stored root AND options is null,
throw `InvalidOperationException("Workspace options are required to execute the pending action.")`
rather than half-approving. The rejection path (approved: false) is
unaffected.

**Acceptance:** unit test for both branches; no path exists that leaves
`Status = Running` with a non-null `PendingToolAction`.

## 1.6 Parent status must tell the truth while a child is paused

When a child pauses (`WaitingForUser`/`Blocked`), `RunOrchestrationAsync`
saves the parent and returns (:666-670) with the parent still
`Running` - forever, if the user never resumes. The recent-tasks list
(doc 03) would show a permanently "Running" parent doing nothing, and
`ListReviewQueueAsync` (status filter `WaitingForUser`/`Blocked`) only
surfaces the parent because its plan_subtasks approval bumped
`approval_count`.

**Fix:** before returning a paused child's result, set the parent's
status to match the child's pause kind (`WaitingForUser` or `Blocked`),
`ActiveStep` to
`$"Waiting on sub-task {index+1}/{count}: {spec.Goal}"`, and save.
`RunStepAsync` already flips any non-terminal status back to `Running`
at the top of a step, and `RunOrchestrationAsync` is entered via
`RunAsync` regardless of prior status, so resume needs one adjustment:
`AgentViewModel.ResumeAgentLoopIfRunnableAsync` (AgentViewModel.cs:709-712)
currently requires `Status == Running`; allow resume also when the task
has unfinished `SubTaskPlan` specs (the orchestration loop itself
decides whether there is anything to do).

**Acceptance:** pause a child on an approval; reload the parent from the
store: status `WaitingForUser`, ActiveStep names the sub-task. Approve
the child's action: parent resumes and ends `Complete` with a report.
Scenario 11 (gate) keeps passing.

## 1.7 Agent task index dates parse without RoundtripKind

`FileAgentTaskStateStore.ParseDate` (FileAgentTaskStateStore.cs:351-352)
uses bare `DateTime.TryParse` on the "O"-format UTC strings the store
itself writes: a "Z"-suffixed string parses to `Local` kind with
local-time conversion. `SqliteLessonStore.ParseTimestamp`
(SqliteLessonStore.cs:490-493) already does this correctly, and r11
fixed the same class of bug across Services stores.

**Fix:** parse with `CultureInfo.InvariantCulture` +
`DateTimeStyles.RoundtripKind` (either inline or by reusing the shared
helper the Services stores use, if referencable from Aether.Agent).
**Acceptance:** round-trip test asserts `Kind == Utc` and value equality
for `ListRecentAsync`/`ListReviewQueueAsync` timestamps.

## 1.8 Verified non-issues (do not "fix" these)

Recorded so a future round does not re-flag them:

- `RunSynthesisAsync` forcing `Complete` and clearing
  `PendingToolAction` after a failed/derailed synthesis step is by
  design (deterministic fallback wins; the sub-task work already
  happened). The fallback path correctly runs terminal-lesson capture
  itself (AgentService.cs:715-721).
- `AgentToolExecutor.CanExecute` returns true for `plan_subtasks` but
  `ExecuteAsync` would throw for it. This is required: `CanExecute`
  gates pending-action creation, and the approval path intercepts
  plan_subtasks before the executor (AgentService.cs:758-761). Do not
  add plan_subtasks to `ExecuteAsync`.
- The blocker-vs-gated-action precedence (a step that both reports a
  blocker and requests a tool that gets gated ends `Blocked`, clearing
  the pending action, AgentService.cs:433-442) is the r15 3.3 design:
  blockers must not hide behind a modal approval. The blocker is
  preserved in `Decisions` either way.
