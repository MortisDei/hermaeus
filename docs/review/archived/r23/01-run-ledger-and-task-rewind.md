# 01. Run Ledger and Task Rewind (the killer feature)

The pitch, and the line `docs/features.md` should carry once this is true:

> Every agent run has an undo button. Hermaeus keeps a ledger of everything
> a run changed and can put it all back, file by file, with one click.

Approval gates make mistakes rare; Rewind makes them cheap. Together they
change how boldly a user can let the agent work. This is the feature a
stranger remembers from a screenshot.

## Why this is buildable in one round

The hard parts already exist (verified at 74c0c00):

- Every mutating tool approved through the direct path captures a pre-image
  before executing and records an `Applied` `AgentDraftPatch` with
  `PreImageContent`, `PreImageExisted`, and `AppliedContent`
  (`AgentService.AppendApprovalAsync`, src/Hermaeus.Agent/Services/AgentService.cs:845-895).
- The manual draft-patch queue does the same in
  `AgentPatchReviewService.ApplyAsync` (AgentPatchReviewService.cs:32).
- Per-file revert with conflict refusal and created-file deletion already
  works: `AgentWorkspaceTools.RevertAppliedPatchAsync`
  (AgentWorkspaceTools.cs:223-247) refuses when the file changed after the
  patch, restores the pre-image, and deletes a file whose pre-image never
  existed. The UI exposes it per patch (`AgentViewModel.RevertPatchAsync`,
  src/Hermaeus.ViewModels/AgentViewModel.cs:1232).

What is missing is the aggregate: nothing shows a run's total footprint, and
nothing reverts a task as a unit. This doc adds exactly that. No new
storage; the ledger is a pure projection of `AgentTaskState`, in the same
spirit as `AgentContextReceiptBuilder`.

## 1.1 AgentRunLedgerBuilder (pure projection)

New static class in `src/Hermaeus.Agent/Services`, mirroring
`AgentContextReceiptBuilder`'s shape: a pure function over a loaded
`AgentTaskState` (plus its children's states for an orchestration parent),
no new persistence.

Output model (Core-free records in `Hermaeus.Agent.Models`):

- **Files changed**: one entry per distinct `RelativePath` across
  `DraftPatches` with status `Applied` or `Reverted`, ordered by first
  touch. Per entry: path, kind (`created` when the earliest patch for the
  path has `PreImageExisted == false`, else `edited`), number of applied
  patches, current status (`applied`, `reverted`, `conflicted` when the
  file's content no longer matches the last `AppliedContent`, computed
  lazily by the caller that has workspace access, not by the builder),
  and line delta (count lines of earliest `PreImageContent` vs latest
  `AppliedContent`; a created file counts from zero).
- **Commands run**: from `ToolResults` where the tool is `run_command`:
  command string, exit code, timed-out flag.
- **Approvals**: from `ApprovalHistory`: action label, approved/rejected,
  timestamp.
- **Sub-tasks**: for a parent, one line per sub-task spec (status, goal),
  and the child tasks' own file/command entries folded into the sections
  above, tagged with the child task id.

The builder must not touch the filesystem; conflict detection belongs to
the service layer (1.3) which can read files safely.

## 1.2 Ledger UI: a "Changes" view per task

In the Agent workbench, a Changes section for the open task (and reachable
for any recent task, since it is derived from persisted state):

- Files list with kind and status chips, patch count, and line delta.
  Selecting a file shows the before/after preview using the existing patch
  preview presentation; do not build a new diff control.
- Commands list with exit codes; approvals list beneath it.
- An empty run shows the shared `MossEmptyState` control. Copy per
  `docs/mascot.md` "Voice in UI copy", for example: "No changes yet. When
  the run edits files or runs commands, Moss records each one here."
- Every icon-only control gets a tooltip (the guard test in
  `ServiceTests` scans for this).

Keep ViewModel logic in `Hermaeus.ViewModels` (no `Avalonia.*` references)
and rendering in `Hermaeus.Desktop`; follow the existing AgentView panel
patterns.

## 1.3 Task Rewind: revert the whole run

New method on `AgentPatchReviewService` (it already owns per-patch revert):
`RevertTaskAsync(AgentTaskState task, AgentWorkspaceOptions options, ct)`.

Semantics, per distinct file path (children of an orchestration parent
included, executed against each task's own stored `WorkspaceRoot`):

1. Determine the earliest pre-image (the content before the run first
   touched the file; `null` when the run created it) and the latest
   `AppliedContent`.
2. Call the existing `RevertAppliedPatchAsync` with that pair. The existing
   conflict rule stands unchanged: if current content is not the latest
   applied content, the file is skipped with the existing refusal message.
   Rewind never overwrites content the user (or anyone else) wrote after
   the run.
3. Record the outcome per file. Flip every patch for successfully reverted
   paths to `Reverted` with `RevertedAt`/`RevertedBy` (the fields exist,
   AgentModels.cs:347-348).

Rules:

- Refuse to start when the task is `Running`, has a pending tool approval,
  or is an orchestration parent with an unfinished child; the error message
  names the reason.
- Partial success is a success with a truthful report: "Reverted 4 of 5
  files. Skipped src/Foo.cs: the file changed again after this patch was
  applied." Surface the summary in the workbench and append it to
  `agent.log`.
- Write one `task_reverted` trace event to `agent.trace.jsonl` with the
  per-file outcomes.
- No lesson is recorded from a rewind. Reverting is user judgment about
  wanted-ness, not evidence that a tool or command failed.

## 1.4 Rewind UI

- A "Rewind run" button on the Changes view, enabled only when the ledger
  has at least one applied, unreverted file and the task is in a terminal
  or waiting state.
- Confirmation dialog listing exactly which files will be restored and
  which (if any) will be deleted (created files), before anything runs.
  This is a destructive-adjacent action; the dialog is not optional and
  there is no "do not ask again".
- After a rewind, the ledger re-renders with `reverted` chips and the
  button disables.

## 1.5 Docs

- `docs/agent.md`: new "Run Ledger and Task Rewind" section covering the
  ledger contents, the conflict rule, and the parent/child behaviour.
- `docs/features.md`: add the flagship line quoted at the top of this doc
  to the Agent section, in the factual register the file already uses.
- `docs/security-review.md` (posture doc after the doc 05 split): Rewind
  is an integrity control; note that revert paths go through the same
  workspace containment checks as every other file operation.

## Explicitly out of scope for this feature

- No filesystem snapshots, copy-on-write overlays, or shadow workspaces.
  The ledger only covers what the agent itself changed through its tools;
  say so plainly in the docs.
- No revert of command side effects. The ledger shows commands ran; it
  cannot un-run `dotnet build`. The docs and the confirmation dialog must
  not imply otherwise (build outputs under bin/obj are not ledger entries).
- No auto-rewind on task failure. Rewind is always a user action.
