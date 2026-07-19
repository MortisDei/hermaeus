# 03: Workbench and desktop truth

Desktop/UI audit: all 40 axaml views scanned, code-behind and converters
read. The headline item builds the recent-tasks list r15 explicitly
deferred ("no existing recent-tasks list UI in the app to extend - only
a count is shown today"); the data layer (`AgentTaskListItem.ParentTaskId`,
index column) already shipped in r15.

## 3.1 Build the recent-tasks list (r15 deferred item)

**Severity: high as a capability gap.** `AgentViewModel.RecentTasks` is
populated (AgentViewModel.cs:299, 1362-1367) and `LoadTaskAsync` is a
`[RelayCommand]` (:632-641), but **no view binds either** - verified by
grep across Desktop. After an app restart the only way back into a task
is the review queue, which only lists `WaitingForUser`/`Blocked`/
approval-bearing tasks. A completed task's report, a failed task's
blockers, and any orphaned-Running task are unreachable from the UI.

**Build, in AgentView's left/task column (extend the existing layout,
no new nav panel):**
- An ItemsControl over `RecentTasks` (already capped at 25 by
  `LoadRecentTasksAsync`; no virtualization) with per-item: status chip
  (reuse the task-status coloring approach of
  `AgentSubTaskStatusColorConverter`), goal (trimmed, single line),
  relative updated time.
- Sub-task children (`ParentTaskId != null`) render indented with a
  "sub-task" tag, per the r15 doc 02 2.3 intent.
- Click invokes `LoadTaskCommand` with the item. Loading a task must
  also refresh the sub-task strip/report affordance (already handled by
  `LoadTaskAsync` -> `RefreshTaskPreview`).
- Refresh the list when the Agent panel becomes active (hook the same
  panel-activation path the wizard reload fix uses in
  `MainWindowViewModel.OnActivePanelChanged`) - not on a timer.
- Review queue rows get an "Open" button invoking the same
  `LoadTaskCommand` path (map by TaskId), which is what makes replying
  to a paused child's ask_user question possible: the reply box already
  works for whatever task is open (`IsWaitingForReply`).

Depends on doc 01 items 1.1/1.6: opening children directly is exactly
the flow that makes unreconciled orchestration state reachable, and a
parent stuck on "Running" would be visibly wrong in this list. Implement
doc 01 first (see roadmap).

**Acceptance:** restart the app, open Agent: previous tasks are listed
with truthful statuses; clicking one loads its goal, plan, sub-task
strip, transcript-derived preview, and report affordance; clicking a
child shows its parent context (goal label); the review queue's Open
button loads the queued task.

## 3.2 Services nav "any running" dot goes stale

`MainWindow.axaml:88-89` binds the green dot's `IsVisible` to
`Services.Servers` through `AnyRunningConverter`
(MainWindow.axaml.cs:62-71), which enumerates the collection once per
binding evaluation. The binding only re-evaluates when the `Servers`
PROPERTY changes; per-item `Status` changes (Start/Stop/crash) and
in-place collection mutations do not re-run the converter, so the dot
shows a snapshot from the last rebuild, not live state. r12's
rebuild-storm fingerprint fix makes rebuilds rarer, making the staleness
window longer.

**Fix:** delete the converter; add `ServicesViewModel.AnyServerRunning`
(bool, `OnPropertyChanged` raised wherever a server's status transitions
are already observed - the same place per-server `Status` updates flow
through - and at the end of `Rebuild()`), bind the dot to
`Services.AnyServerRunning`. Reproduce first (start a server, watch the
dot) to confirm the read of the binding behaviour, then fix.

**Acceptance:** starting a managed server lights the dot without any
settings save/rebuild; stopping the last one clears it; a UI-thread
assertion-clean run under the existing `UiThreadGuard`.

## 3.3 Conversation delete has no confirmation

`MainWindowViewModel.DeleteConversationAsync` (MainWindowViewModel.cs:271-278)
permanently deletes on a single context-menu click - no confirm, no
undo. Every other destructive action of this weight is confirm-gated
(RAG dataset delete, reindex, benchmark history clear, backup restore),
and `ConfirmActionDialog` already exists.

**Fix:** the same request-callback pattern the other confirms use: a
`RequestDeleteConversationConfirmation` func on `MainWindowViewModel`
(set by the view, showing `ConfirmActionDialog` with the conversation
title); command deletes only on true. Keep the toast.

**Acceptance:** delete flows show the dialog and honor cancel; the
command remains directly unit-testable with the callback stubbed true
and false.

## 3.4 Sub-tasks panel shows with no task loaded (verify at runtime, then fix)

`AgentView.axaml:121-122` (r15): the Sub-tasks border's
`IsVisible="{Binding !!CurrentTask.SubTaskPlan.Count}"` traverses
`CurrentTask`, which is null before any task is loaded. A compiled
binding with a null intermediate produces UnsetValue, leaving
`IsVisible` at its default `true` - the empty "Sub-tasks" chrome would
render on a fresh workbench. The same null-traversal pattern also means
the strip only refreshes when the `CurrentTask` REFERENCE changes (fine
today: `RunAgentLoopAsync` reloads after the run settles; but a
mid-orchestration spec status change does not repaint until then).

**Fix:** add `AgentViewModel.HasSubTaskPlan => CurrentTask is { SubTaskPlan.Count: > 0 }`,
raise it in `RefreshTaskPreview` (alongside the existing `HasReport`
raise at AgentViewModel.cs:1388), bind `IsVisible` to it. Verify the
blank-panel symptom in the running app first; if the panel is somehow
already hidden (e.g. an ancestor's visibility), record that in the
implementation notes and still make the binding null-safe.

**Acceptance:** fresh workbench shows no Sub-tasks chrome; loading an
orchestration parent shows it; loading a plain task hides it again.

## 3.5 Desktop hygiene: converter sprawl and wheel-handler copies

Two mechanical cleanups, zero behaviour change intended:

- **Converters:** `ModelManagementView.axaml.cs` hosts 9 ad-hoc
  converters (four of which are the same bool-to-"X..."/"X" label
  shape); status colors are hardcoded per-converter in three different
  vocabularies (`Brushes.LimeGreen`-style names in
  `AgentSubTaskStatusColorConverter`/`AgentScenarioStatusColorConverter`,
  material hex in `FitTierBrushConverter`/`UpdateStatusBrushConverter`/
  `ErrorColorConverter`). Consolidate: one `StatusPalette` static class
  (Desktop-side) holding the small set of semantic brushes
  (ok/warn/error/info/neutral, material hex values - they read on both
  theme variants; the app runs `RequestedThemeVariant="Default"`), a
  generic `BoolToTextConverter` taking "TrueText|FalseText" via
  `ConverterParameter`, and move all shared converters into
  `Converters/`. Keep view-specific one-offs where they are if truly
  single-use.
- **Wheel handlers:** the tunnel-phase wheel hijack exists in three
  copies (`ModelManagementView.axaml.cs:68-78`, and per its own comment
  the same pattern in ServicesView and SettingsView). Extract one
  shared helper (static class with an `Attach(Control, ScrollViewer)`
  or an attached property) and use it from all three. Preserve the
  exact current behaviour including the 56px step - the known trade-off
  that inner scrollables under the pointer lose the wheel is accepted
  and documented in the helper's doc comment, not changed this round.

**Acceptance:** build stays zero-warning; the three views scroll exactly
as before (manual check); converter unit tests where logic is non-trivial
(palette mapping), no tests for pure text converters.

## 3.6 Ctrl+Q quits instantly with no confirmation

`DesktopIntegrationService.OnKeyDown` (DesktopIntegrationService.cs:111-116):
with local hotkeys enabled, Ctrl+Q anywhere (including focus inside a
TextBox mid-thought) immediately calls `Quit()` - shutdown, no prompt,
generation or agent run in progress or not. Cheapest honest fix: remove
Ctrl+Q from the local hotkey set entirely (Quit stays available via
tray menu and window close). If the owner prefers keeping it, gate it
behind `ConfirmActionDialog`. Default to removal; note the decision in
the changelog.

**Acceptance:** Ctrl+Q no longer quits (or prompts first, if kept);
remaining hotkeys unchanged; `docs/features.md` hotkey table updated.

## 3.7 Verified non-issues (do not "fix" these)

- `SingleInstanceGuard` (file-lock based, OS releases on crash) is
  sound; no stale-lock cleanup needed.
- `GlobalHotkeyService`'s window subclassing restore order is the
  standard pattern; Avalonia does not re-subclass after window creation
  in a way that breaks it. Leave as is.
- `MainWindow.OnWindowClosing` calling `vm.Shutdown()` synchronously is
  correct alongside the r10 bounded async dispose in `App` exit.
- ChatView's `AcceptsReturn`/Enter-to-send interplay (d77691c) is
  correct as shipped, including re-subscription on DataContext change.
