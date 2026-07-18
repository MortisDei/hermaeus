# 01: Sub-task orchestration

All file references verified against the tree at 0.19.0-alpha.

## 1.1 Data model: parent and child tasks

`AgentTaskState` (src/Aether.Agent/Models/AgentModels.cs:79-140) gains,
all JSON-additive with defaults so pre-r15 `task_state.json` files load
unchanged through `FileAgentTaskStateStore`:

- `string? ParentTaskId` (null default). Non-null marks a child task.
- `List<AgentSubTaskSpec> SubTaskPlan` (empty default). Only ever
  populated on a parent, and only by an approved `plan_subtasks` action.

New model `AgentSubTaskSpec` (same file):

```
public sealed class AgentSubTaskSpec
{
    public string Goal { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty; // key into the fixed profile catalog, 1.3
    public string SuccessCriteria { get; set; } = string.Empty;
    public AgentSubTaskStatus Status { get; set; } = AgentSubTaskStatus.Pending; // Pending|Running|Complete|Failed|Skipped
    public string? TaskId { get; set; }        // set when the child task is actually created (lazily, 1.4)
    public string ResultSummary { get; set; } = string.Empty; // bounded copy of the child's outcome, written at child terminal state
}
```

`agent/task_index.db` stays a rebuildable index; if the index schema
needs a `parent_task_id` column for the recent-tasks list (doc 02), add
it through `SqliteMigrationRunner` additively.

Acceptance:
- A pre-r15 `task_state.json` without the new fields round-trips through
  load/save without data loss (regression test with a captured fixture).
- A child state always has `ParentTaskId` set and `SubTaskPlan` empty.

## 1.2 The `plan_subtasks` tool and its gate

Add `plan_subtasks` to `FixedToolDefinitions`
(src/Aether.Agent/Services/AgentService.cs:75-106) and to the tool list
in `AgentSystemPrompt` (AgentService.cs:11-64), described as: propose
splitting the current goal into 2 to 6 focused sub-tasks, each with a
goal, a profile from the fixed list, and success criteria; always
requires approval; only useful for broad, multi-domain goals; never for
goals that fit a normal plan (`set_plan` remains the right tool there).

Schema: `{"subtasks":[{"goal":string,"profile":string,"success_criteria":string}]}`,
`subtasks` required.

Gating, deterministic and layered:
1. `AgentSafetyGate.Evaluate` (src/Aether.Agent/Services/AgentSafetyGate.cs:33-61)
   gains an explicit named case for `plan_subtasks`:
   `RequiresApproval`, `AgentRiskLevel.Medium`, reason
   "Delegating the goal to sub-tasks changes how much autonomous work
   will run and requires approval." It must NOT ride the substring
   heuristics at :50-55 and must come before the unknown-tool block at :60.
2. In `AgentService.RunStepAsync`'s dispatch (AgentService.cs:261-360),
   BEFORE the gate call: if the requesting task has `ParentTaskId` set,
   force disposition `Blocked` with reason "Sub-tasks cannot create
   sub-tasks (depth limit 1)." This check is code, not prompt text.
3. Validation at approval time (see 1.4): fewer than 2 or more than 6
   entries, an empty goal, or an unknown profile name rejects the
   action with an explanatory `AgentToolResult` instead of executing;
   the task returns to `WaitingForUser`.

Acceptance:
- Gate unit tests: `plan_subtasks` is RequiresApproval on a root task;
  the depth check blocks it on a child regardless of what the model set
  in `requires_approval`.
- The system prompt and tool declaration both mention the 2-6 bound.

## 1.3 Specialist profiles: a fixed, in-code catalog

New static class `AgentSpecialistProfiles` in src/Aether.Agent/Services.
Fixed catalog, keyed by lowercase name: `general`, `correctness`,
`security`, `tests`, `performance`, `docs`. Each profile is only:

- `FocusConstraints`: 2-4 short constraint strings appended to the
  child's `AgentTaskState.Constraints` at creation (they flow into the
  context pack via AgentContextBuilder.cs:52 exactly like the existing
  constraints). Example for `security`: "Focus on input handling,
  process launching, path traversal, and secrets; report findings
  rather than refactoring broadly."

Profiles are not user-editable this round (see roadmap rejections) and
must not touch the system prompt, tool set, or gate. A profile can
never change what is allowed, only what the child pays attention to.

Acceptance:
- A test asserts every profile's constraints are non-empty and that
  resolving an unknown name falls back to `general` (used only by the
  validation path in 1.2, which rejects unknown names anyway; the
  fallback is defense in depth, not a feature).

## 1.4 Sequential execution through the existing loop

Approval path: `plan_subtasks` arrives like any gated tool via
`AgentPendingToolAction`; `AgentService.AppendApprovalAsync`
(AgentService.cs:509-590) gains a branch for it: validate (1.2 step 3),
then materialize `SubTaskPlan` on the parent state, set the parent
`Running`, and record a transcript entry summarizing the approved plan.
No child task is created yet.

Execution: `AgentService.RunAsync` (AgentService.cs:468-504), when the
loaded task is a parent with an unfinished `SubTaskPlan`, does NOT run a
parent model step. Instead it advances orchestration:

1. Find the first entry whose status is `Pending` or `Running`.
2. If `Pending`: create the child now via the normal `CreateTaskAsync`
   path (internal overload that sets `ParentTaskId`, appends the
   profile's `FocusConstraints`, and composes the child goal as
   `"{spec.Goal}\nSuccess criteria: {spec.SuccessCriteria}"`), store
   its id in `spec.TaskId`, mark the spec `Running`.
3. Run the child with the existing `RunAsync` loop (same
   `AgentWorkspaceOptions`, so same workspace, model, and RAG dataset).
   `onStep` fires with the child's step results unchanged; doc 02
   handles presentation.
4. If the child ends `WaitingForUser` (approval or question), the
   parent stays as-is and the orchestration run returns; the user acts
   on the CHILD task (its own approval queue entry), then resuming the
   parent re-enters this loop and continues the same child.
5. When the child reaches `Complete` or `Failed`, copy a bounded
   summary into `spec.ResultSummary` (child `Summary` plus final user
   message, truncated to 1200 chars), set spec status accordingly, save
   the parent, and continue with the next entry. A `Failed` child never
   aborts its siblings; the failure is material for the synthesis.
6. When no entries remain unfinished, run synthesis (1.6).

Approvals and lessons: children record their own approvals, lessons,
and remembered command approvals exactly as today.
`RememberedCommandApprovals` (AgentModels.cs:139) is task-scoped and
must NOT be inherited by or shared between children or parent; add a
regression test proving an approval in child A does not auto-execute
the same command in child B.

Cancellation: cancelling the run cancels the current child through the
existing `CancellationToken` path; the child lands `WaitingForUser` per
existing semantics, remaining specs stay `Pending`, and a later run of
the parent resumes where it left off.

Acceptance:
- Scripted-LLM test (FakeSequencedAgentLlm pattern, see
  src/Aether.Tests usage from r7): a parent with a 2-entry approved
  plan runs child 1 to Complete, child 2 to Complete, then synthesizes;
  the parent's `SubTaskPlan` ends with two `Complete` entries carrying
  non-empty `ResultSummary`.
- A child that pauses `WaitingForUser` leaves the parent resumable; the
  approval recorded on the child id resumes correctly.
- A `Failed` child yields a `Failed` spec entry and the run continues
  to the next spec.

## 1.5 Step budgets

New `AgentSettings.MaxOrchestrationSteps` (src/Aether.Core/Models/AgentSettings.cs),
default 60, documented as the total model steps a single orchestrated
run may spend across all children plus synthesis. Per-child runs still
respect the existing `MaxAutoSteps` (:19) per resume, unchanged.

Track spent steps on the parent (`int OrchestrationStepsUsed`,
JSON-additive) incremented per child step executed under the parent's
orchestration loop. When the ceiling is hit: mark every remaining
`Pending` spec `Skipped`, append a transcript entry saying so, and
proceed directly to synthesis, which must state that the run was
truncated by budget (honesty over completeness).

Acceptance:
- A test with a low ceiling proves remaining specs go `Skipped` and the
  synthesis report contains the truncation note.

## 1.6 Synthesis

When all specs are terminal (`Complete`, `Failed`, or `Skipped`), the
parent runs one final ordinary model step. To feed it,
`AgentContextPack` (AgentModels.cs:280-308) gains
`List<AgentRetrievedItem> SubTaskReports` (empty default, omitted from
the receipt when empty per the existing `AgentContextReceiptBuilder`
convention), and `AgentContextBuilder.BuildAsync`
(src/Aether.Agent/Services/AgentContextBuilder.cs:43-74) fills it for
parent tasks from `SubTaskPlan` (goal, profile, status, and
`ResultSummary` per child; budget 4000 tokens via the existing
`ContextPackBuilder.Pack`, most recent children favored if it cannot
fit). No child transcript replay into the parent; summaries only.

The parent model step is expected to answer `final` with the
consolidated report as `user_message`. If the model call or parse
fails, fall back deterministically: a concatenated report built from
the spec entries themselves, and mark the parent `Complete` with that
report (the sub-task work already happened; a flaky synthesis step must
not fail the whole run).

Also write the final report to `report.md` in the parent's task
directory via the existing atomic-write pattern
(src/Aether.Agent/Services/AtomicFileWriter.cs), so the artifact
outlives the UI session.

Acceptance:
- Synthesis context contains one item per spec, statuses included.
- The deterministic fallback path is tested (throwing fake LLM on the
  synthesis step only): parent still ends `Complete` with a report
  listing each child and its status.
- `report.md` exists in the task directory after synthesis.
