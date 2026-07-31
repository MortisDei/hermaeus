# 01. Startup that never waits for a model

## The problem

Launch the app. The window paints quickly and every panel except the one you
opened it for is populated. The model dropdown in Chat is empty, and it stays
empty for as long as it takes a 4.2 GB Gemma at 64512 context to load, plus
however long a second server takes after that. You cannot send. If you type
something and press send, nothing happens and nothing tells you why.

Three separate mistakes stack up to produce that.

**The post-setup chain is strictly sequential.**
`CompletePostSetupInitializationAsync` (`MainWindowViewModel.cs:322-340`):

```csharp
await RunBackgroundTaskCoreAsync("load RAG datasets", () => Rag.LoadDatasetsAsync());
await RunBackgroundTaskCoreAsync("load agent", () => Agent.LoadAsync());
await RunBackgroundTaskCoreAsync("load benchmarks", () => Benchmarks.LoadAsync());
await RunBackgroundTaskCoreAsync("auto-start managed servers", () => Services.AutoStartAllAsync());
await RunBackgroundTaskCoreAsync("ensure Local API running state", () => Settings.EnsureLocalApiRunningStateAsync());
await RunBackgroundTaskCoreAsync("load chat models", () => Chat.LoadModelsAsync());
```

The method's own doc comment says hard ordering is kept "only where a step
truly depends on the previous one (auto-starting managed servers before
listing models)". That was true when written. It is no longer the reason the
ordering costs anything, because of the third point below.

**Auto-start is itself sequential, and each server is a full model load.**
`AutoStartAllAsync` (`ServicesViewModel.cs:1394-1398`) is a `foreach` with an
`await` in it. Each `AutoStartIfConfiguredAsync` reaches
`ServerProcessManager.StartAsync`, which awaits `WaitForHealthAsync`
(`ServerProcessManager.cs:95`) against a **five-minute deadline** polled every
600 ms (`:695-719`). The owner's install auto-starts two servers. The second
one has no reason to wait for the first: separate processes, separate ports,
separate models. They wait anyway.

**And the wait buys nothing, because the refresh already happens on an
event.** `OnServerPropertyChanged` (`ServicesViewModel.cs:1474-1486`) fires
`ServerAvailabilityChanged` whenever a server's `Status` changes.
`MainWindowViewModel.cs:189` subscribes it to
`RefreshModelsAfterServerChangeAsync`, which calls
`Chat.LoadModelsAsync(force: true)` (`:959-971`), and `force: true` bypasses
the 30 second staleness guard in `LoadModelsCoreAsync`
(`ChatViewModel.cs:593`). A server reaching `Running` already re-lists the
models by itself.

So step 6 is not waiting for step 4 in order to work. It is waiting for step
4 because it is written on the line below it.

## 1.1 The post-setup chain stops serialising

`CompletePostSetupInitializationAsync` becomes: the three independent store
loads run concurrently, server auto-start leaves the awaited chain entirely,
and the model listing keeps its one direct call for the case where a server
is *already* running (an externally-started llama-server, or a restart while
the app was closed) and will therefore never raise a status transition.

```
Concurrent:  load RAG datasets | load agent | load benchmarks
Then:        ensure Local API running state
Then:        load chat models        (covers already-running servers)
Fire and forget: auto-start managed servers
```

Requirements that are not negotiable:

- **Each step keeps its own isolation.** `RunBackgroundTaskCoreAsync` catches
  per-step and logs the operation name (`MainWindowViewModel.cs:978-990`).
  Running three concurrently must not collapse them into one `Task.WhenAll`
  whose first exception hides the other two. Wrap each in its own
  `RunBackgroundTaskCoreAsync` and `WhenAll` the wrapped tasks, so every
  failure still names itself exactly as it does today (r12 3.2).
- **`_postSetupInitialized` stays a single guard set before any work
  starts** (`:324-325`). The first-run path calls this method from the wizard
  completion handler as well, and the guard is what stops the two racing.
- **Auto-start still reports failure.** Leaving the awaited chain must not
  mean leaving the log. It goes through `RunBackgroundTaskAsync` (the
  fire-and-forget wrapper at `:973-976`), which is the same isolation without
  the await.

## 1.2 Servers auto-start concurrently

`AutoStartAllAsync` starts every configured server at once rather than
waiting out each health check in turn:

```csharp
public Task AutoStartAllAsync() =>
    Task.WhenAll(Servers.Select(s => s.AutoStartIfConfiguredAsync()));
```

One caution the implementer must handle rather than discover: there is
already a port-conflict guard. `StartAsync` refuses to launch if the port is
listening (`ServerProcessManager.cs:55-65`), and
`ServicesViewModel.cs:1460-1471` stops a peer that shares a starting server's
port. That peer-stop logic runs on the UI thread and assumes it is looking at
settled state. Starting two servers concurrently must not let two servers on
the *same* port both pass the preflight. Servers are grouped by port and only
one per port is started; the rest keep today's behaviour of being stopped or
refused. If the owner's two servers are on 39201 and 39202, the common case
is unaffected, but the guard is what makes the concurrent version safe rather
than lucky.

## 1.3 Chat says the server is warming, instead of saying nothing

With 1.1 and 1.2 landed, the model dropdown fills the moment the chat server
is healthy rather than after the whole chain. That shortens the wait. It does
not explain it.

Chat gains a warming state, derived, not stored: any managed non-embedding
server whose `Status` is `Starting`, while `AvailableModels` is empty. It
renders as one line above the composer: the server name, that it is starting,
and how long it has been starting. No spinner-only state, no progress bar
(there is no honest percentage to show; a model load reports nothing until it
reports healthy).

When a server has been `Starting` for longer than 90 seconds the line adds
that this is longer than usual and links to the Services panel, where the
server log already streams. It does not offer to cancel, restart, or
diagnose. Doctor is where diagnosis lives.

If no server is starting and no models listed, the existing empty state
stands. This is an addition to a specific transient case, not a replacement
for the general one.

## 1.4 A message typed before the server is ready is queued, not swallowed

`SendAsync` (`ChatViewModel.cs:915-919`) opens with:

```csharp
if ((string.IsNullOrEmpty(text) && !attachments.Any(a => a.IsReady)) || SelectedModel is null) return;
```

`SelectedModel is null` is the launch case, and it returns silently. The user
typed a question, pressed send, and the app did nothing and said nothing.

Instead: when there is text to send and `SelectedModel` is null **because a
server is warming** (1.3's derived state), the message is held.

- The user's message is added to the conversation exactly as it would be, and
  rendered with a held indicator and the reason ("waiting for Chat server").
- The composer clears, so the user can carry on typing the next thought.
- One held message at a time. A second send while one is held replaces
  nothing and queues nothing; it is refused with the reason already on
  screen. A queue of depth one is a convenience; a queue of depth N is a
  scheduler.
- The held message is cancellable, by an explicit control on the message
  itself, which discards it and restores its text to the composer.
- When a model lists, the held message sends, once, through the ordinary
  `SendAsync` path.
- If no model lists within five minutes, or the warming server enters
  `Error`, the hold fails: the message stays in the composer, the reason is
  shown, and nothing was sent.

Rules the implementer does not get to reinterpret:

- **Nothing is sent that the user did not submit.** The hold releases a
  message the user explicitly pressed send on. It never retries a failed
  send, never resends on error, and never survives an app restart. An
  unreleased hold at shutdown is discarded, and its text is preserved in the
  composer draft if a draft mechanism already covers that; otherwise it is
  simply lost, which is honest and matches what happens today.
- **The hold is not persisted.** It lives in the view model. It is not a
  conversation state, not a task, and not written to the store until it
  actually sends and becomes a real message.
- **`SelectedModel is null` for any other reason still returns.** No models
  configured at all, no server, a misconfigured runtime: those are not
  warming, and inventing a hold for them would queue a message against a
  condition that will never clear.

## 1.5 The startup breakdown becomes visible

`InitializeAppAsync` already measures every phase and formats them
(`App.axaml.cs:97-151`, `StartupTimingFormatter.cs`). The result is one Info
line in the runtime log, which nobody reads, and it is the only evidence that
a round changed startup at all.

The phase list is recorded on a small service so the last startup's
breakdown can be read back, and shown in **System Overview**
(`SystemOverviewView.axaml`, which already carries GPU/VRAM and Components
and is the right home for it). It shows the phases and their milliseconds,
plus total. That is all: no target, no rating, no "good/slow" judgement.

Two additions to what is measured, because 1.1 makes the current phases
misleading:

- `viewmodels` currently absorbs the whole post-setup chain including a
  five-minute-capable server wait. Split the concurrent block, the Local API
  step, and the model listing so the number means something.
- Auto-start is no longer inside the total, because it is no longer on the
  critical path. Record it separately, as elapsed-to-healthy per server,
  attributed by server name. This is the number doc 03 will change, so it
  needs to exist before doc 03 lands.

## 1.6 Delete the `IsLoading` that loads nothing

`MainWindowViewModel.IsLoading` is set true at `:289` and false at `:307`,
and is bound by nothing in any axaml file in the repository. It has never
gated a control. Remove it, or bind it. Removal is correct: 1.3 gives the
one genuinely transient state a real, specific surface, and a
whole-application "loading" flag would now be lying about panels that are
already usable.

Check for a test asserting on it before deleting, and delete that too if it
only exists to observe the flag.

## Tests

| Area | Test |
| --- | --- |
| 1.1 | The three independent store loads all run even when the first one throws; each failure logs its own operation name |
| 1.1 | Auto-start failing does not prevent the model listing step from running |
| 1.1 | `_postSetupInitialized` still prevents a second concurrent entry (existing `MainWindowViewModelStartupTests` coverage must survive) |
| 1.2 | Two servers on different ports both start without either waiting for the other's health check |
| 1.2 | Two servers configured on the same port do not both pass the port preflight |
| 1.3 | Warming state is true only while a non-embedding server is `Starting` and no models are listed |
| 1.3 | Warming state clears the moment a model lists |
| 1.4 | A send with text and no model, while warming, holds the message and clears the composer |
| 1.4 | A held message sends exactly once when a model lists |
| 1.4 | A second send while one is held is refused and does not queue |
| 1.4 | Cancelling a hold restores the text to the composer and sends nothing |
| 1.4 | A send with no model and nothing warming still returns without holding |
| 1.4 | A hold that times out sends nothing and leaves the text recoverable |
| 1.5 | The recorded phase list round-trips and formats in order |
| 1.6 | No axaml binds `IsLoading` (guard, if kept) |

Every one of these is a view-model test over records and fakes. None needs a
process, a port, or a model file. If a test appears to need a real
llama-server, the seam is in the wrong place: `ServerProcessViewModel` status
is settable by the existing test helpers, and that is the input.

## What this doc explicitly does not do

- **No splash screen, and no progress bar for model load.** llama-server
  reports nothing between launch and healthy. A progress bar over an unknown
  duration is a decoration that implies knowledge the app does not have.
- **No preloading or keeping a model warm across app restarts.** That is the
  operating system's page cache's job, and an app that keeps a 4 GB process
  alive after you close it is a different product.
- **No reordering of which server starts first, or a "start the chat server
  first" priority.** 1.2 starts them concurrently, which makes priority
  moot. A priority field would be configuration that exists only to work
  around a serialisation this doc removes.
- **No auto-send, auto-retry, or send queue deeper than one.** See 1.4.
- **No change to `WaitForHealthAsync`'s five-minute deadline or 600 ms
  poll.** Both are off the critical path after 1.1 and neither has been
  reported as wrong. Shortening a timeout to make startup feel faster would
  convert a slow success into a failure.
- **No startup performance target, budget, or regression threshold in CI.**
  1.5 reports the number. A CI gate on a wall-clock measurement taken on
  shared runners is a flaky test with a stopwatch.
