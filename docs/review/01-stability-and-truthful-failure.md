# 01. Stability and truthful failure

## 1.1 Fix the app-killing crash on truncated code fences (the owner's chat crash)

Root cause found, not speculative. The owner reported the app crashing in chat when a
response runs long (correlated with hitting the 4096 default max-token cap). The crash
log at `src/Aether.Desktop/bin/Release/net10.0/aether_unhandled.log` (2026-07-21
04:59:24) has the exact stack:

```
System.ArgumentNullException: Value cannot be null. (Parameter 'source')
   at System.Linq.Enumerable.Take[TSource](...)
   at Aether.Desktop.Controls.MarkdownViewer.RenderFencedCode(FencedCodeBlock c) MarkdownViewer.cs:line 384
   at Aether.Desktop.Controls.MarkdownViewer.RenderBlock(...)
   at Aether.Desktop.Controls.MarkdownViewer.Render(...)
   at ... OnRenderTimerTick -> Avalonia dispatcher -> process death
```

Mechanism: when a streamed response is cut off at the token cap mid-code-fence (or a
fence has just been opened when the render timer ticks), Markdig produces a
`FencedCodeBlock` whose `Lines.Lines` array is null (`StringLineGroup` with zero lines).
`MarkdownViewer.RenderFencedCode` at `src/Aether.Desktop/Controls/MarkdownViewer.cs:385`
does `c.Lines.Lines.Take(c.Lines.Count)` and throws on the UI thread, which is fatal to
the whole app. The identical pattern exists at line 372 (`RenderFallback`) and line 392
(`RenderCodeBlock`).

Fix:
- Add one null-safe helper (e.g. `static string JoinLines(LeafBlock b)`) that returns
  `string.Empty` when `Lines.Lines` is null, and use it at all three sites.
- Belt and braces: wrap the body of the render pass invoked from `OnRenderTimerTick`
  (line 210) / `RenderAsync` (line 255) in a try/catch that logs to `IRuntimeLogService`
  and renders the raw source text as a plain fallback block instead of letting any
  future render bug kill the process. A markdown rendering bug must never be fatal.

Acceptance:
- A regression test that parses a source string ending in an unterminated ```` ``` ````
  fence (and one that is exactly ```` ```csharp ```` with nothing after it) through the
  same Markdig pipeline `MarkdownViewer` uses and passes the resulting blocks through
  the extracted line-joining logic without throwing. Extract the join into a testable
  static if needed (ViewModels/Tests cannot reference Avalonia controls, so test the
  logic, not the control).
- Manually: stream a long response with `MaxTokens` set low enough to cut a code block
  off; app renders the partial fence and survives.

## 1.2 Say so when a response hits the max-token cap, and offer to continue

The crash above only happened because truncation is currently invisible: generation just
stops mid-sentence and the user's only clue is a weird ending. llama.cpp's final stream
chunk carries `finish_reason: "length"` when `n_predict` is exhausted, and
`LlamaCppService.ParseStreamEvent` (`src/Aether.Services/LlamaCppService.cs:293-316`)
already deserializes `Choice.FinishReason` but discards it.

Fix:
- Add `string? FinishReason = null` to `LlmStreamEvent`
  (`src/Aether.Core/Services/ILlmService.cs:36-44`, additive default so all existing
  constructions compile).
- Populate it in `LlamaCppService.ParseStreamEvent` and in the OpenAI provider's
  equivalent (`src/Aether.Services/OpenAiService.cs`, its `Choice` record at :219
  already has `FinishReason`). Ollama's native path reports `done_reason`; map
  `"length"` through if trivially available, otherwise leave null (acceptable).
- In `ChatViewModel`'s send loop, when the final event has `FinishReason == "length"`,
  set a new `MessageViewModel.WasTruncated` flag. `MessageControl.axaml` renders a small
  notice under the message ("Stopped at the response token limit (N tokens)") plus a
  `Continue` button bound to a new `ChatViewModel.ContinueTruncatedCommand` that sends
  the literal user turn "Continue exactly where you left off." (normal send path, so
  history/persistence/memory all behave as usual). Persist `WasTruncated` with the
  message (additive field in `messages_json`) so the notice survives reload.

Acceptance: unit test that a scripted stream ending with `finish_reason:"length"`
produces `WasTruncated == true` and that `"stop"` does not; UI shows notice + Continue.

## 1.3 Crash logs belong in the data root, and Doctor must surface them

`Program.cs:7-8` writes `aether_unhandled.log` / `aether_unobserved.log` next to the
executable (`AppContext.BaseDirectory`). That location is wrong twice: a packaged
install may not be writable, and nobody ever looks there (the owner never saw the file
that contained their crash's exact stack; this round only found it by searching the
disk).

Fix:
- Write crash logs to `{DataRoot}/logs/` alongside `runtime.log`. `Program.Main` runs
  before DI, so resolve the path the same way `AppLifecycleJournalService.JournalPath`
  does (`src/Aether.Core/Services/AppLifecycleJournalService.cs:41-51`): configured
  data root if `settings.json` is readable, else the LocalApplicationData fallback. A
  tiny static resolver duplicated in Program.cs is acceptable (precedent: the same
  fallback is already duplicated for the same bootstrap reason elsewhere; note it in a
  comment). Keep append-mode single-file with the existing timestamp format.
- Doctor's `CheckCleanShutdown` (`src/Aether.Services/DoctorService.Startup.cs:27-69`):
  when the previous session did NOT exit cleanly, look for a crash-log entry newer than
  `previous.StartedAtUtc` and, if found, include the first line of the most recent
  exception (type + message, not the whole stack) in the check detail, with the full
  path named so the user can open it. This turns "it did not shut down cleanly" into
  "here is the exception that killed it".

Acceptance: unit test for the crash-log-tail parser (given a file with two appended
entries, returns the newest entry's first line and its timestamp); Doctor check detail
includes it when timestamps line up and omits it when the file is older than the
session.

## 1.4 Restoring from the tray must not re-run app initialization (root cause of the false "did not shut down cleanly" warning)

`App.axaml.cs:47` wires `window.Opened += async (_, _) => await InitializeAppAsync(...)`.
Avalonia raises `Opened` every time the window is shown, and
`DesktopIntegrationService` hides the window on minimize-to-tray
(`src/Aether.Desktop/DesktopIntegrationService.cs:33-36`) and shows it again on tray
click. So every tray restore re-runs the entire `InitializeAppAsync`, including
`AppLifecycleJournalService.RecordStartup()` at `App.axaml.cs:94`.

`RecordStartup` (`AppLifecycleJournalService.cs:67-77`) then reads the CURRENT session's
journal (CleanExit=false, LastOperation = whatever breadcrumb ran last, in practice
"loading Kokoro native ONNX session" from the startup voice probe) and installs it as
`PreviousSession`. The next Doctor scan dutifully reports "Aether did not shut down
cleanly last time ... Kokoro native". This is exactly the owner's report: the warning
always names kokoro native, and minimize-to-tray plus restore reproduces it.

Fix:
- One-shot guard: a `private bool _initialized` (or `Interlocked.Exchange`) in `App` so
  `InitializeAppAsync` runs exactly once per process no matter how many times `Opened`
  fires. This also stops any other double-init side effects (server autostart, warm-up,
  timing log spam) on every tray restore.
- Defense in depth in the journal itself: `RecordStartup` sets a `private bool` and
  becomes a no-op (returning the already-captured `PreviousSession`) on second call in
  the same process. The XML doc already says "call exactly once"; enforce it.

Acceptance: unit test calling `RecordStartup()` twice on one instance asserts the
second call does not overwrite `PreviousSession` and does not rewrite StartedAtUtc;
manual: minimize to tray, restore, run Doctor scan, no unclean-shutdown warning.

## 1.5 Make the clean-shutdown warning name the right suspect

Even with 1.4 fixed, a REAL unclean exit will almost always blame the Kokoro/reranker
breadcrumb, because those are the only two `RecordOperation` call sites
(`KokoroOnnxModel.cs:79,139`, `OnnxCrossEncoderReranker.cs:101,138`) and both run at
startup. A crash six hours later still reports "last operation: loading Kokoro native
ONNX session", which is misleading (the owner noticed precisely this).

Fix:
- After the risky section completes, record a neutral breadcrumb: `RecordOperation`
  of "running" (call it at the end of `InitializeAppAsync`, and have the two ONNX
  loaders record "... session loaded" on success themselves so the risky window is
  bracketed tightly).
- In `CheckCleanShutdown`, treat `LastOperation == "running"` (or the loaded marker) as
  "no specific operation was in flight": wording becomes "Aether did not shut down
  cleanly last time. No risky operation was in progress; if this repeats, check
  {crash log path}." Only name an operation when the journal genuinely died inside one.

Acceptance: unit test with journal LastOperation "running" produces the neutral
wording; with "loading Kokoro native ONNX session (EnsureLoadedAsync)" produces the
named-operation wording (existing behaviour, now the rare case).
