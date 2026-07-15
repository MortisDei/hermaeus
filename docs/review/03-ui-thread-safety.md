# 03 - UI thread safety

## Problem statement

The 0.13.1-alpha crash fix marshaled the writers that field evidence
convicted (`ChatViewModel`'s Task.Run debounce, trace load, and memory
status; `LogsViewModel`'s LogAdded handler) and introduced
`UiThreadGuard` / `UiBoundCollection<T>`
(src/Aether.ViewModels/UiBoundCollection.cs), armed once in
`App.OnFrameworkInitializationCompleted`. Armed, any off-UI-thread
mutation of a guarded collection throws immediately with the offender
in the stack instead of corrupting Avalonia's container generator and
crashing later. But only ChatViewModel and LogsViewModel are guarded;
the other ~20 ViewModels still expose raw `ObservableCollection<T>`
and several demonstrably receive service events on worker threads
(`ServicesViewModel.StatusChanged`/`LogLine` handlers at
ServicesViewModel.cs:125-144, `SettingsViewModel`:153,
`TtsSettingsViewModel`'s process status events, progress callbacks in
model download / RAG ingest / benchmark runs). Finish the sweep and
make regression impossible.

Known constraint from the fix session: the guard must NOT arm based on
`SynchronizationContext.Current` presence. xunit installs a context
whose continuations legitimately hop threads; arming on presence
failed 7 tests on hops that are harmless headlessly. Explicit
`UiThreadGuard.Arm()` from the Desktop app is the contract; do not
"improve" it into context sniffing.

## 3.1 One RunOnUi

Three private copies of the same helper now exist
(MainWindowViewModel.cs:637-643, ChatViewModel, LogsViewModel), plus
TtsSettingsViewModel's bespoke `_sync` usage. Extract one helper into
Aether.ViewModels (static class or protected base member,
implementer's choice; the ViewModels project must stay free of
Avalonia references, so it stays `SynchronizationContext`-based:
capture in the constructor, `Post` when present, invoke inline when
null). Convert all existing copies.

**Acceptance criteria**

- Exactly one RunOnUi implementation remains; grep proves no private
  duplicates.
- Null-context behavior (inline invoke) preserved, so headless tests
  run synchronously as today.

## 3.2 Full ViewModel sweep

Every `ObservableCollection<T>` property on a ViewModel that is bound
(or bindable) to an ItemsControl becomes `UiBoundCollection<T>`, and
every service/process event handler that mutates VM state marshals
via RunOnUi. Sweep all of src/Aether.ViewModels (the earlier grep
found 77 ObservableCollection/dispatcher references across 23 files).
Handlers known to fire on worker threads, at minimum:
`ServicesViewModel` ctor handlers (StatusChanged, LogLine),
`SettingsViewModel`:153, `TtsSettingsViewModel` process status
events, download/ingest/benchmark progress callbacks. Property-only
mutations (string/bool status fields) marshal too: Avalonia does not
promise cross-thread INPC delivery, and marshaled handlers make the
question moot.

Order of operations per ViewModel: first marshal the writers, then
switch the collection type. Do not switch types first; an armed guard
with unmarshaled writers converts a latent race into a deterministic
in-app crash (that is the guard doing its job, but do not ship in
that state).

**Acceptance criteria**

- No public `ObservableCollection<T>` properties remain on ViewModel
  types (see 3.3 for enforcement).
- Full test suite green; app smoke run (launch, open Services, Logs,
  Chat, Settings, start/stop a fake or real server if available)
  with no `UiThreadGuard` throw.
- Commit notes list each handler that was marshaled.

## 3.3 Architecture test

Add to `ArchitectureTests`: reflect over all public types in
Aether.ViewModels; any public instance property of type
`ObservableCollection<T>` (exactly, not derived) fails the test with
a message pointing at `UiBoundCollection<T>`. Allow an explicit
opt-out list in the test (empty at introduction) for any future
collection that is provably never UI-bound, so exceptions are visible
and reviewed rather than silent.

**Acceptance criteria**

- Test fails if a raw ObservableCollection property is reintroduced
  (verified by temporarily reverting one during development, noted in
  commit notes).
- Opt-out list starts empty.

## 3.4 Guard coverage for Avalonia-side collections

`MarkdownViewer` manipulates `panel.Children` directly (fine: it is a
control, it lives on the UI thread), but Desktop-side code binding
collections it creates itself should use the same guard where an
ObservableCollection crosses a thread boundary. Audit
src/Aether.Desktop for ObservableCollection usage (ChatView.axaml.cs
and RagView.axaml.cs matched the earlier grep) and apply the same
treatment where bound. Small item; expected outcome is "audited,
nothing or one small change".

**Acceptance criteria**

- Audit result recorded in commit notes, with any changes covered by
  the 3.2 smoke run.
