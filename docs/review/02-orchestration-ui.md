# 02: Orchestration in the workbench

The workbench keeps its current shape: one goal box, one task view, one
approval queue. Orchestration is something a task can do, not a mode
the user switches into. The model proposes `plan_subtasks` when the
goal warrants it; the user approves or rejects like any other action.

## 2.1 Plan approval presentation

`AgentApprovalPreview.Describe`
(src/Aether.Agent/Services/AgentApprovalPreview.cs) gains a case for
`plan_subtasks` that renders the actual proposed plan: one line per
sub-task with profile and goal, plus the count. The user must see
exactly what work they are authorizing, in the same place run_command
previews already appear (r6 3.2 precedent).

Acceptance: a pending `plan_subtasks` approval shows every sub-task
goal and profile; a malformed arguments payload degrades to a clear
"could not parse the proposed plan" preview (and the validation in doc
01 1.2 will reject it on approval anyway).

## 2.2 Sub-task strip on the parent task

`AgentViewModel` (src/Aether.ViewModels/AgentViewModel.cs) keeps
`CurrentTask` pointed at the task the user opened. When that task has a
non-empty `SubTaskPlan`, `AgentView.axaml` shows a compact strip above
the transcript: one row per spec with a status chip
(Pending/Running/Complete/Failed/Skipped), profile name, and goal
(single line, trimmed). Reuse the existing status-chip color approach
(`AgentScenarioStatusColorConverter` pattern in Aether.Desktop; add a
converter rather than restyling shared resources).

While an orchestrated run is active, the existing step counter at
AgentViewModel.cs:360 shows orchestration progress for parents
("sub-task 2/4, step 7" style) instead of the plain step display; the
data for this comes from `SubTaskPlan` plus `OrchestrationStepsUsed`.

During the run, `onStep` results belong to the running CHILD (doc 01
1.4). The VM must label transcript entries and status text with the
child's position ("[sub-task 2: security]") so the stream reads
coherently, and must marshal to the UI thread through the existing
`RunOnUi`/`UiBoundCollection` discipline from r9/r12. No raw
cross-thread collection writes.

Acceptance: VM-level tests (headless, scripted LLM) assert the strip
source updates as specs change status, and that transcript labeling
carries the sub-task index and profile.

## 2.3 Child approvals route to the child, visibly

When a child pauses for approval, the approval UI must state which
sub-task is asking, and `ApproveAsync`/`RejectAsync` must target the
CHILD task id (the pending action lives on the child's state).
`AgentReviewQueueItem` (src/Aether.Agent/Models/AgentModels.cs:203-220)
gains an optional `string? ParentGoal` so the review queue can show
"for: <parent goal>" on child entries; populate it in the queue builder
from `ParentTaskId` when set.

Acceptance: approving a child's pending `run_command` resumes the
orchestrated run (the parent re-enters the loop and the same child
continues); rejecting it leaves the child `WaitingForUser` and the
parent resumable, per doc 01 1.4 step 4.

## 2.4 Recent tasks list and the consolidated report

Child tasks appear in the recent-tasks list like any task (they are
real tasks); prefix their display goal with an indent marker or
"sub-task:" so the list stays scannable. Opening a child shows its
normal transcript and approvals.

On a parent whose synthesis has run, show the consolidated report as
the final assistant message (it already is, via `user_message`) and an
"Open report" affordance that opens `report.md` from the task directory
with the existing open-folder/open-file helpers used elsewhere in the
app. No new viewer.

Acceptance: parent's final message contains the report; the report file
opens from the UI; a `Skipped`-truncated run's report visibly says the
budget cut it short.

## 2.5 Docs

User-visible behavior changed, so per AGENTS.md: update
`docs/features.md` and `docs/agent.md` (what orchestration is, how the
gate works, the depth-1 and sequential-only rules, budgets, where the
report lands), and `CHANGELOG.md` under 0.20.0-alpha. Do not document
anything beyond what ships.
