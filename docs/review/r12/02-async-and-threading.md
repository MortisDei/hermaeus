# r12-02: Async and threading in the ViewModel layer

r9 fixed the crash class (cross-thread mutation of bound collections)
with `UiBoundCollection`/`UiThreadGuard`. This round is about the
non-crashing residue: ordering bugs from posting mutations then acting
on the pre-mutation state, and unguarded/overlapping fire-and-forget
work. One semantic fact drives several items: **`RunOnUi` always
posts**, even when the caller is already on the UI thread
(`ViewModelBase.cs:16-22`), so code after a `RunOnUi(...)` call runs
*before* the posted mutation.

## 2.1 Toast history: cleared/dismissed toasts resurrect; new toasts race the save

`MainWindowViewModel`:
- `ClearToastHistoryAsync` (`MainWindowViewModel.cs:608-613`) posts
  `ToastHistory.Clear()` via `RunOnUi`, then immediately serializes
  `ToastHistory` in `SaveToastHistoryAsync`. Called from the UI thread,
  the posted Clear runs *after* the synchronous enumeration, so the
  full, un-cleared history is written to disk; on restart it all comes
  back. `DismissToastAsync` (`:599-606`) has the same shape: the
  dismissed toast is persisted anyway. `TrimToastHistory` there also
  mutates the collection directly before the posted Remove lands.
- `OnToastRaised` (`:495-519`) posts the Insert and then fires
  `SaveToastHistoryAsync` from the raising thread: the newest toast is
  missing from the saved file, and when toasts arrive from background
  threads the save enumerates `ToastHistory` concurrently with UI
  mutations (unguarded read; classic "collection was modified" in
  `Select`).

Fix: make the mutation-then-save sequence single-threaded and ordered.
Simplest shape: a `private async Task MutateAndSaveHistoryAsync(Action mutate)`
that does `await RunOnUiAsync(() => { mutate(); return SaveSnapshotAsync(); })`
or, less invasively: perform the mutation inside `RunOnUiAsync`, take a
`ToList()` snapshot on the UI thread, then serialize the snapshot.
`OnToastRaised` should schedule the save from inside the posted block.

Acceptance:
- Clear history, restart (or reload via `LoadToastHistoryAsync`):
  history is empty. Dismissing one toast survives reload.
- Unit test with an armed `UiThreadGuard` and a background-thread toast
  raise: no unguarded enumeration (save reads a UI-thread snapshot),
  and the saved file contains the new toast.

## 2.2 ChatViewModel.SendAsync has no exception handling

`SendAsync` (`ChatViewModel.cs:363-505`) has only a `finally`. Any
throw after streaming starts (`PersistAsync` on a locked DB,
`ApplyInjectedMemoryMarkersAsync` re-throwing something unexpected,
`BuildMemoryInjectionAsync`'s store call outside its inner catch)
leaves the assistant bubble with `IsStreaming = true` forever, no error
message, and the exception disappears into the AsyncRelayCommand. The
r9 pack demanded the send path be observable; a swallowed exception is
the opposite.

Fix: wrap the body in `catch (Exception ex)`: mark the streaming
message as error with the exception text (or remove it if empty), log
to `_runtimeLogs`, toast. Mirror in `RegenerateAsync`'s direct
`SendAsync()` call path (it already funnels here).

Acceptance: unit test with a conversation store whose `SaveAsync`
throws: after SendAsync, no message has `IsStreaming == true`, the
error is visible on the message or via toast, and a runtime log entry
exists.

## 2.3 Per-keystroke fire-and-forget refreshes: no debounce, no cancellation, interleaving

Three hot paths launch unawaited async work on every input change and
let executions overlap, each interleaving `Clear()`/`Add()` on a bound
collection (duplicated or mixed results when a slow older run finishes
after a newer one):

- `MemoriesViewModel.OnSearchTextChanged` (`MemoriesViewModel.cs:239-242`)
  fires `SearchCommand.Execute(null)` per keystroke: one DB search per
  character, unordered completion.
- `AgentViewModel.OnWorkspaceFileQueryChanged` (`AgentViewModel.cs:1281`)
  fires `RefreshWorkspaceFilesAsync()` per keystroke: a full workspace
  file enumeration per character (against the *user profile* by
  default, see 3.5).
- `AgentViewModel.OnSelectedWorkspaceFileChanged` (`:1282`) races
  read/summarize of the previous selection against the new one; last
  writer wins regardless of which file is selected.

Fix: reuse the debounce shape already proven in
`MainWindowViewModel.OnSearchQueryChanged` (300 ms + CTS) for the two
keystroke paths, and add a generation counter or CTS to the
selection-changed loader so stale completions are discarded. Also make
`ChatViewModel.LoadModelsAsync` re-entrancy safe (see 2.5).

Acceptance:
- Typing N characters quickly issues far fewer than N searches (test
  the debounce helper directly; extract it if needed).
- A stale workspace-file load completing after a newer selection does
  not overwrite the newer preview (unit test with two controllable
  task sources).

## 2.4 LogsViewModel rebuilds the whole list on every log line

`LogsViewModel` subscribes `_logs.LogAdded += _ => RunOnUi(Refresh)`
(`LogsViewModel.cs:53`), and `Refresh()` clears and re-adds every
visible entry. A llama-server startup emits hundreds of lines in
seconds; each line posts a full O(n) rebuild (O(n^2) total), flooding
the dispatcher precisely when the app is busiest. r8's performance doc
capped the log buffer but not this.

Fix: append incrementally when the new entry passes the current filter
(and trim from the front past the cap); keep full rebuild only for
filter changes and ClearView. Coalesce bursts: a pending-refresh flag
or ~100 ms batch timer so one post handles many lines.

Acceptance: a burst of 1,000 added lines results in bounded UI-thread
posts (assert via a counting fake sync context) and the visible list
matches the filter; filter switching still works.

## 2.5 Concurrent LoadModelsAsync interleaves Clear/Add

`ChatViewModel.LoadModelsAsync` (`ChatViewModel.cs:247-278`) is called
from panel navigation, server-availability events, and startup; two
overlapping calls interleave across the `await GetModelsAsync()`
boundary: both clear, both add, duplicating every model in
`AvailableModels`/`CompareModels` and resetting `SelectedModel` twice.
Same pattern in `AgentViewModel.LoadAsync` and
`BenchmarkViewModel.LoadAsync` (both also lack re-entrancy guards, and
`MainWindowViewModel` fires them from background task helpers).

Fix: serialize with a simple in-flight task latch (return the running
task to concurrent callers) or a bool guard per VM; after the fetch,
rebuild the collection in one pass.

Acceptance: unit test issuing two concurrent LoadModelsAsync with a
delayed fake `ILlmService`: no duplicate model ids after both complete.

## 2.6 RunOnUi semantics: execute inline when already on the UI thread

Root enabler of 2.1: `ViewModelBase.RunOnUi` posts unconditionally.
Callers naturally assume "runs my action, then I continue", which is
false on the UI thread. Change `RunOnUi` to invoke inline when
`SynchronizationContext.Current == _sync` (Avalonia exposes
`Dispatcher.UIThread.CheckAccess`, but comparing the captured context
keeps the class dispatcher-agnostic and test-friendly), else post.
Audit remaining `RunOnUi` call sites for order dependence after the
change (the 2.1 fix must not silently rely on this).

Acceptance: unit test that RunOnUi from the captured context executes
synchronously, and from another thread still posts; existing threading
tests stay green.
