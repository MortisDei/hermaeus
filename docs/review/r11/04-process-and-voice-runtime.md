# 04 - Process lifecycle and voice runtime

Gaps left around the r9 lifecycle work, plus the subprocess voice
providers' platform holes.

## 4.1 Aether.LocalApi child escapes the job object

`LocalApiProcessManager.StartAsync`
(src/Aether.Services/ProcessManagement/LocalApiProcessManager.cs:53-68)
starts the LocalApi host without `IProcessJobObject.TryAssign`, unlike
ServerProcessManager, KokoroProcessManager, and XttsProcessManager. An
app crash orphans the LocalApi process holding its port and its
per-app tokens live in memory; the next launch cannot bind. This is
exactly the r9 2.1 orphan class, missed for this one manager.

Acceptance criteria:

- Job object assigned on Windows with the same warning-on-failure
  logging as the other managers (constructor takes the optional
  IProcessJobObject like its siblings; test with a fake asserts the
  assign call).

## 4.2 Subprocess/remote voice providers cannot play audio on Windows

`VoiceProviderProcessRunner.PlayWavFileAsync`
(VoiceProviders/VoiceProviderProcessRunner.cs:87-95) tries only
paplay, pw-play, aplay, ffplay - Linux audio players; on a stock
Windows machine none exist and playback throws "Could not find
paplay...". `XttsV2VoiceProvider.PlayAsync`
(VoiceProviders/XttsV2VoiceProvider.cs:253-281) hardcodes ffplay. So
Kokoro (Python), F5-TTS, XTTS, and OpenAI voice are all synthesize-only
on Windows unless ffmpeg happens to be installed. The native Kokoro
provider (Aether.Voice) plays fine, which is why daily use never
surfaced this; but every non-default provider selection on Windows is
broken at the last step.

Fix direction: a shared playback helper with a Windows branch (the
simplest dependency-free option is `System.Media.SoundPlayer` via a
small P/Invoke-free path if available on .NET 10 for Windows, otherwise
winmm `PlaySound` P/Invoke, mirroring how Aether.Voice plays its own
output - reuse that code path if it is reusable across project
boundaries within the architecture rules). XTTS routes through the same
helper instead of its private ffplay copy.

Acceptance criteria:

- One playback helper used by all four providers; on Windows it does
  not depend on ffplay (unit-test the player-selection logic with an
  injected "which executables exist" seam; actual audio output is not
  asserted).
- XttsV2VoiceProvider.PlayAsync deleted in favor of the helper.

## 4.3 Orchestrated speech leaks one temp wav per utterance

`GenerateSpeechAsync` in KokoroVoiceProvider (130-143), OpenAiVoiceProvider
(74-87), F5TtsVoiceProvider (91-104), and XttsV2VoiceProvider (101-114)
synthesize to a `%TEMP%` file when `request.OutputPath` is null and
never delete it after playback; only the legacy SpeakAsync/PreviewVoice
paths clean up. VoiceOrchestrator.PlayAsync always calls
GenerateSpeechAsync with no OutputPath, so every spoken chat reply,
notification, and agent narration since r5 leaves a wav on disk.

Acceptance criteria:

- When the caller did not request a persisted OutputPath and PlayAudio
  is true, the temp file is deleted after playback (or providers
  return audio via a caller-owned temp scope); a test asserts no
  `aether-*` wav remains after a fake synthesis+playback cycle.

## 4.4 ServerProcessManager exit-handler and restart races

Two small hardening items in
ProcessManagement/ServerProcessManager.cs:

- `OnProcessExited` (522-540) reads `_process?.ExitCode` while
  `KillProcess` (542-552) may be disposing the Process on another
  thread; a hit throws ObjectDisposedException on a threadpool thread
  (process-crash class). Capture the Process reference passed via
  `sender` instead of the field, and swallow disposed-object access.
- `StartAsync` (90) replaces `_monitorCts` without disposing the
  previous instance on restart.

Acceptance criteria:

- Exit handler uses the sender/captured reference; no field access that
  can observe a disposed Process (code-review level plus a
  best-effort race test if practical).
- Old monitor CTS disposed on restart.

## 4.5 NormalizeConfig mutates the caller's ServerConfig

`ServerProcessManager.NormalizeConfig` (387-395) writes the resolved
executable/model paths back onto the ServerConfig instance the caller
passed, which is typically the settings object itself: a directory or
bare-name configuration is silently rewritten to a concrete file path
in memory and persisted by the next unrelated SaveAsync. Resolution
results should be launch-time values, not configuration edits.

Acceptance criteria:

- Start/AutoTune resolve into a copy; the caller's ServerConfig is
  byte-identical after StartAsync (test asserts).

## 4.6 Voice failure toasts fire once per process lifetime

`VoiceOrchestrator._toastedProviderFailures` (VoiceOrchestrator.cs:24,
186-193) never resets, so after one failure toast for a provider,
later distinct failures (different root cause, hours later) are
silent. Reset the key on a subsequent successful utterance for that
provider so each failure episode toasts once.

Acceptance criteria:

- fail -> toast, success, fail -> toast again (fake provider test);
  repeated consecutive failures still toast once.
