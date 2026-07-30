# 02. A workbench you can read

## The problem, measured

`src/Hermaeus.Desktop/Views/AgentView.axaml` is 1194 lines. For comparison,
the next largest views in the repository are `RagView.axaml` at 723,
`ChatView.axaml` at 678, `ServicesView.axaml` at 540 and
`BenchmarkView.axaml` at 518. The Agent panel is larger than any two of them
combined.

Inside it:

- **13 top-level expanders**, 8 of them `IsExpanded="True"`. The default
  state of the panel is almost everything open at once. 20 expanders in
  total once nested ones are counted.
- **38 buttons, 4 combo boxes, 9 text boxes** on one page.
- **A header that cannot scroll.** `AgentView.axaml:15` is
  `<Grid RowDefinitions="Auto,*">`. Row 0 (lines 16-198) is `Auto` height and
  holds the title bar, four stat tiles, the new-lessons strip, the capability
  notes, the workspace policy expander, the full sub-task list and the full
  plan list. None of it is inside a `ScrollViewer`. On a long plan with
  sub-tasks and new lessons, Row 0 grows without limit and squeezes Row 1,
  the actual working area, toward zero height. There is no scrollbar that
  recovers it. This is a layout bug, not a taste question.
- **Duplicated content.** `StatusMessage` renders at line 87 and again at
  line 299. `CurrentTaskSummaryLabel` renders at line 84 ("Summary" tile) and
  again at line 315 ("Agent response"), the second one being the real
  presentation the first one truncates.
- **Implementation verbs presented as user actions.** Lines 273-304 put six
  buttons in one undifferentiated row at identical weight: "Start Agent",
  "Refresh Queue", "Refresh Memory", "Save Memory", "Explain Workspace",
  "Save as Workspace Defaults". One of those is the thing the user came here
  to do. Three of the others are cache-invalidation controls for lists the
  app already keeps current itself.

None of this is anyone's mistake. Every panel arrived with a round that had a
reason. The panel has simply never been edited as a whole.

## The shape

One `TabControl` inside the Agent panel, following the precedent already set
by `BenchmarkView.axaml:189`, plus two things that live outside it and are
always visible.

```
+---------------------------------------------------------------+
| Agent    Complete . step 7 of 9 . fix the review queue         |
|                                        [New task] [Run step]  |
+---------------------------------------------------------------+
| Waiting on you: run_command                       (Elevated)  |
|   dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj        |
|   Recipe declared in .hermaeus/workspace.json                 |
|   [Approve]  [Reject]  [Open]                                 |
+---------------------------------------------------------------+
| [ Run ] [ Changes 3 ] [ Workspace ] [ History ]               |
+---------------------------------------------------------------+
|                                                               |
|  (the selected tab, in its own ScrollViewer)                  |
|                                                               |
+---------------------------------------------------------------+
```

**Row 1, the status line.** Fixed height, one line. Task status, step count,
goal, and the run controls. Nothing that can grow.

**Row 2, the decision strip.** Collapsed to nothing when there is no decision
waiting. This is the rule that makes tabs safe here: **the thing the agent is
waiting on is never behind a tab.** It carries whatever doc 01's queue says
needs a decision, whether that is an approval, a reply, or a blocked run
needing an instruction.

**Row 3, the tabs.** Four, named for what the user is doing, not for which
subsystem produced the data.

**Row 4, the tab content**, each tab with its own `ScrollViewer` so scroll
position is per tab rather than shared.

### 2.1 The move inventory

This is the contract. Every section that exists today exists after this
round. Check each row off.

| Today (`AgentView.axaml`) | Lands |
| --- | --- |
| 20-50 title, `CurrentStep`, New task / Run Step / Stop | Status line |
| 53-66 "Task state" tile | Status line (status + step count, one line) |
| 67-73 "Task history" tile | Split: review count to the decision strip (doc 01 1.5), recent-task count to the History tab |
| 74-80 "Workspace memory" tile | Workspace tab, above the memory list |
| 81-89 "Summary" tile | Deleted. `CurrentTaskSummaryLabel` is already rendered properly at 315; the patch counts become the Changes tab's badge; `StatusMessage` goes to the status line |
| 92-120 "New lessons from this task" | History tab, beside the Lessons list |
| 122-136 capability notes | Workspace tab (content rewritten by doc 03 3.1) |
| 138-149 workspace policy expander | Workspace tab |
| 151-176 Sub-tasks | Run tab |
| 178-196 Plan | Run tab |
| 202-271 Goal / Workspace / Model / RAG dataset | Run tab, top |
| 273-304 the six-button row | Split; see 2.3 |
| 311-321 Agent response | Run tab |
| 328-407 Review Queue | Decision strip |
| 414-437 reply box | Decision strip |
| 443-471 Continue task box | Decision strip |
| 475-490 Reservations | Run tab, with the run outcome (doc 03 3.2) |
| 493-509 Task State / Next Action | Run tab, collapsed |
| 511-619 Workspace Profile | Workspace tab |
| 621-711 Workspace Files, including the nested Draft Patch composer at 671-707 | Workspace tab. The composer stays bound to `SelectedWorkspaceFile` and moves with the file list, not with the patch queue it feeds |
| 717-728 Context receipt | Run tab, collapsed |
| 730-768 Retrieved Context | Run tab, collapsed |
| 770-843 Draft Patch Decisions | Changes tab |
| 845-970 Changes (ledger, rewind, before/after, commands, approvals) | Changes tab |
| 972-1012 Recent Tasks | History tab |
| 1014-1078 Scenario Evals | History tab |
| 1080-1118 Workspace Memory | Workspace tab |
| 1120-1181 Lessons (self-learning) | History tab |
| 1183-1190 Agent Log | History tab |

Nothing on this list is deleted except the Summary tile, whose every field is
rendered elsewhere already.

### 2.2 The header cannot eat the window

After 2.1 the status line is one row of fixed content, so the `Auto` row can
no longer grow without bound. Add the belt to the braces anyway: give the
status line a `MaxHeight` and let its goal text trim, exactly as
`AgentView.axaml:31` already trims `CurrentStep`.

Test: a task with a 30 step plan, a 12 entry sub-task plan and 5 new lessons
still leaves the tab content measurably non-zero in height. This is testable
at the view model level by asserting the status line's bound content is a
fixed set of scalars rather than a collection, which is the property that
actually guarantees it. Do not try to unit-test Avalonia layout arithmetic.

### 2.3 The button row, sorted by whether a user wants it

| Button | Becomes |
| --- | --- |
| Start Agent | The Run tab's one primary action, visually weighted as such |
| Refresh Queue | Deleted (doc 01 1.4 makes the queue keep itself current) |
| Refresh Memory | Deleted. `RefreshWorkspaceMemoryAsync` already runs on load (`AgentViewModel.cs:794`) and after every save (`:1582`), delete (`:1590`) and workspace analysis (`:1638`). The button can only ever be a no-op |
| Save Memory | Renamed. It writes an entry titled with `GoalText` whose body is the task summary (`AgentViewModel.cs:1573-1581`), so it means "save this run as a workspace note". Say that, and put it with the run outcome (doc 03 3.2) where the thing being saved is on screen |
| Explain Workspace | Renamed "Re-analyse workspace" and moved to the Workspace tab. It already runs automatically on load (`AgentViewModel.cs:797`); the button is a re-run, so it should read as one |
| Save as Workspace Defaults | Workspace tab, next to the profile it writes |

`RefreshWorkspaceMemoryCommand` and `RefreshReviewQueueCommand` stay as
commands. Only their buttons go.

### 2.4 Tabs carry a count only when the count means something

The Changes tab shows the pending patch count when it is non-zero, since a
patch waiting for review is work assigned to the user. `PendingPatchCount`
already exists (`AgentViewModel.cs`, bound at `AgentView.axaml:85`).

The other three tabs get no badge. A badge on History that counts recent
tasks is decoration, and a badge that is always lit teaches the user to
ignore badges.

### 2.5 Selected tab is view-model state

Add `SelectedTabIndex` to `AgentViewModel`, exactly as
`BenchmarkViewModel` does for `BenchmarkView.axaml:189`. It is not persisted
to settings: the Agent panel opens on Run every time, because Run is what
the panel is for.

One behaviour it does own: when a run finishes and produces file changes,
**do not** auto-switch to Changes. Surface it in the Run tab's outcome
summary (doc 03 3.2) with the Changes tab's badge lit, and let the user
choose. An app that moves the page under you while you are reading it is the
problem this doc exists to fix.

### 2.6 Every empty tab says what would fill it

The Changes tab already has a `MossEmptyState` (`AgentView.axaml:965-968`).
Recent Tasks has one (`:1007-1010`). The Workspace tab with no workspace
selected currently gets a bare `TextBlock` (`:226-228`). Use
`MossEmptyState` consistently across all four tabs, per CLAUDE.md's shared
empty-state rule, with copy that follows `docs/mascot.md`.

## Tests

This is a view restructure, so most of the guarantee is in review and in
running the app. What is testable, test:

- `SelectedTabIndex` defaults to the Run tab, on a fresh view model and after
  `LoadAsync`.
- Finishing a run with file changes does not change `SelectedTabIndex`.
- The Changes badge count matches `PendingPatchCount` and is suppressed at
  zero.
- The status line's bound members are scalars, not collections (2.2).
- An axaml guard that `AgentView.axaml` contains no `ItemsControl` or
  `ListBox` outside the tab content and the decision strip. This is the
  regression guard for 2.2, and it is a cheap string assertion of the kind
  the repository already runs for tooltips.
- The existing tooltip guard passes over the moved axaml. It will bite; the
  moved buttons keep their tooltips.

## Explicitly not in this doc

- **No new nav panel.** The Agent panel keeps one entry in the sidebar. Tabs
  are inside it.
- **No dockable, resizable or user-arrangeable layout.** A workbench the user
  has to build before using is worse than one that is merely long.
- **No change to what any panel shows.** Doc 02 moves controls and deletes
  two duplicates and two no-op buttons. Doc 03 is where content changes.
- **No persisted tab selection.**
- **No code-behind logic in `AgentView.axaml.cs`.** CLAUDE.md's rule stands;
  this is a bindings-only change.
