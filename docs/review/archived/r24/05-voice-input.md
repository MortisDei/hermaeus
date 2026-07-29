# 05. Voice input

## The problem

Hermaeus has five text-to-speech providers, an ARPAbet-to-IPA phonemizer, an
embedded CMU pronouncing dictionary, a user pronunciation lexicon, chunked
synthesis with boundary-aware pauses, per-channel voice routing, and a
playback state machine (`docs/voice.md`, `src/Hermaeus.Voice/`).

It cannot hear anything. There is no microphone path in the codebase at
all. The voice feature is a monologue.

This doc adds the other half: local speech to text, and the small number of
places it should appear.

## Design decisions, settled

**The local backend is a managed `whisper-server` process, not in-process
ONNX.** In-process was considered seriously, because `NativeKokoroVoiceProvider`
already runs ONNX in `Hermaeus.Voice` and it would avoid a second managed
runtime. It is rejected for this round: Whisper is an encoder-decoder with
an autoregressive greedy decode loop and KV cache management, which is a
genuinely difficult thing to get right from scratch and a very poor fit for
a round that is already carrying three other features. The managed-server
route reuses machinery that is proven in this codebase: pinned GitHub
release tag, `ArchiveExtractor` with its zip-slip guard, SHA256
verification, `ProcessStartInfo.ArgumentList`, loopback binding, and the
Windows job object (`LlamaServerSetupService.cs:39-48` and :133-188,
`ServerProcessManager`). Revisit in-process in a later round if the extra
process proves annoying in daily use.

**Remote is available but never the default.** An OpenAI-compatible
`/v1/audio/transcriptions` provider costs almost nothing once the
abstraction exists, mirrors how the TTS layer already offers OpenAI
alongside local providers, and is the right escape hatch. It is off by
default and it sends your voice to a third party, which 5.6 makes
impossible to miss.

**Audio is transient. Always.** Captured audio is written to a temp WAV,
transcribed, and deleted. It is never persisted, never backed up, never
logged, never attached to a conversation. The transcript is the artifact;
the recording is not. `docs/voice.md:149-159` already commits to this
posture for generated audio and this holds the input side to the same line.

**No wake word, no always-on listening.** Every capture is started by an
explicit user action and has a visible indicator for its entire duration.
An app that can silently hold a hot microphone is not a local-first privacy
story, whatever its network behaviour.

**Everything here is off by default** and a Hermaeus with no STT backend
installed behaves exactly as 0.30.0 does.

## A constraint worth stating plainly

The owner has flagged that this machine may not have a usable microphone.
That is a real risk to a feature nobody can test, and it shapes two things:

1. **Keep 5.3 when descoping.** This is a scoping instruction to the
   implementer, not a behaviour forced on the user: 5.3 is a menu action
   the user clicks, pointed at a file the user chooses. Nothing is
   recorded, copied or stored by it. It stays in scope because a
   file-based transcription path is the only way the whole pipeline
   (backend, model, decode, transcript delivery) is verifiable end to end
   with no microphone in the building.
2. Voice sequences **last** in the round (doc 06), and descopes from the
   top down within itself, so a microphone problem discovered late costs
   the hands-free layer and nothing else.

## 5.1 The recognition provider layer

`ISpeechRecognitionService` in `Hermaeus.Core.Services`, alongside
`ITtsService` and `IVoiceProvider`:

```
Task<SpeechTranscript> TranscribeAsync(
    Stream wavPcm16Mono16k, SpeechTranscribeOptions options, CancellationToken ct);
bool IsAvailable { get; }
string ProviderName { get; }
```

```
SpeechTranscript(Text, DurationMs, Language, IsLowConfidence, Error)
```

Registry mirroring `VoiceProviderRegistry` (`VoiceProviderRegistry.cs`), two
providers:

**Local: managed whisper.cpp.** A new `ServerConfig`-shaped managed entry
so it inherits the existing lifecycle: port check before launch with the
owning-process report (`docs/features.md:381-388`), job-object membership so
it dies with the app (`docs/features.md:376-380`), the Services card's
start/stop/log surface, and the leftover-process banner. Bind to 127.0.0.1
only. Launch via `ArgumentList`, never a shell string.

Setup follows `LlamaServerSetupService` exactly: a pinned release tag with
the verification date recorded in a comment the way that file does
(`LlamaServerSetupService.cs:39-48`), per-platform asset selection that is
unit-testable without network the way `SelectDownloadAsset` is
(`LlamaServerSetupService.cs:13`), download, SHA256 verify, extract through
`ArchiveExtractor`, locate the executable including the Windows `.exe`
retry (`docs/features.md:570-576`).

The model is a pinned Whisper ggml file downloaded through
`ModelDownloadService` with SHA256 verification
(`ModelDownloadService.cs:98-122`), same contract as the pinned embedding
model. Ship a small default (base or small); do not ship large as the
default and do not auto-download anything without the existing
approval-gated setup action showing target path, install plan and risk
notes (`docs/voice.md:85-95`).

**Remote: OpenAI-compatible.** Base URL plus a `secret:` reference resolved
through `ISecretStore`, never a raw key in settings, matching how OpenAI
voice already does it (`docs/voice.md:157-158`).

## 5.2 Microphone capture

`IAudioCapture` in `Hermaeus.Core`, implemented in `Hermaeus.Voice` next to
`AudioPlayback` and reusing `WavFile` for output. Target format is 16 kHz
mono PCM16, which is what Whisper wants and avoids a resampler.

```
IReadOnlyList<AudioInputDevice> EnumerateDevices();
Task<CaptureSession> StartAsync(string? deviceId, CancellationToken ct);
// session exposes: Stop(), a peak-level event for the UI meter, and the WAV path on stop
```

**Windows:** `winmm` `waveInOpen` / `waveInPrepareHeader` /
`waveInAddBuffer` / `waveInStart` via `DllImport`. No NuGet package. This
is a well-trodden API and the buffer handling is the only fiddly part;
double-buffer and copy out on each callback.

**Linux:** the subprocess fallback chain, mirroring `AudioPlayback.cs:26-43`
which already does exactly this for output: try `parecord`, then `arecord`,
then `ffmpeg`, first one found on PATH wins, launched with `ArgumentList`.

**No device, no silent failure.** If no input device exists, or the OS
denies access, `IsAvailable` is false and every mic affordance shows as
disabled with the actual reason ("no input device found", "microphone
access denied by the system"). It must never look like it is listening when
it is not. On Linux with none of the three capture tools installed, say
which ones it looked for; that is a fixable problem and the user can only
fix it if told.

**The hot-mic rules**, all enforced in code, not by convention:

- A visible recording indicator for the entire duration of any capture.
- Capture stops on: user action, window close, app exit, and a hard maximum
  utterance length (default 60 seconds).
- A capture session is disposed deterministically. A leaked session holding
  the microphone open is a defect of the same severity as a leaked server
  process.
- The temp WAV is deleted after transcription, including on the failure and
  cancellation paths. Test the failure path specifically.

## 5.3 Transcribe a file

A "Transcribe audio file..." action in Services > Voice: pick a `.wav`,
transcribe it, show the transcript with a copy button.

This exercises the entire pipeline except capture, which makes the backend
verifiable on a machine with no microphone, gives Doctor something real to
check against, and gives the round a deterministic manual test. It is also
genuinely useful on its own.

Validate the input: it is a user-supplied file path, so normalize, reject
traversal and symlinks, cap the size, and reject a file that is not
actually a readable WAV with a clear message rather than feeding arbitrary
bytes to a subprocess.

## 5.4 Dictation, anywhere text goes

One shared control, `MicButton`, placed on: the chat input, the Agent goal
box, the Agent reply box, the RAG query box, the workspace memory note
editor, and the palette (doc 02).

Behaviour: press to start, press again to stop, or hold the configured
push-to-talk key. Live level meter while recording. On stop, transcribe and
**insert the text at the cursor for editing.** It does not send. Dictation
that auto-sends is a different feature (5.5) and conflating them means
every mis-transcription becomes a message you did not write.

Reuse the existing local-hotkey infrastructure for push-to-talk
(`GlobalHotkeyService`, `UiSettings.EnableLocalHotkeys` at
`UiSettings.cs:47`). Note that Linux has no system-wide hotkey support and
Doctor deliberately leaves it out of problem reporting
(`docs/features.md:615-618`); push-to-talk is therefore in-app on Linux, and
the settings copy must say so rather than offering something that will not
work.

Icon-only, so it needs a tooltip; the guard test will catch it otherwise.
The tooltip must reflect state (ready, recording, transcribing,
unavailable-with-reason).

## 5.5 Hands-free conversation mode

The top slice, and the first thing to descope. Off by default, enabled
explicitly, and only in Chat.

An explicit, visible state machine, because the failure mode of a hands-free
mode is not knowing which state it is in:

```
Idle -> Listening -> Transcribing -> Sending -> Speaking -> Listening
```

with a hard Stop from any state that returns to Idle immediately.

- **Endpointing** is silence-based: stop capturing after N ms below an
  amplitude threshold (default around 1200 ms), with a minimum utterance
  length so a breath does not end the turn and a maximum so a noisy room
  does not record forever.
- **Never auto-send an empty or low-confidence transcript.** Below the
  threshold, return to Listening and show why. A hands-free mode that sends
  the room's background noise to a model as a question is worse than no
  hands-free mode.
- The reply is spoken through the existing `IVoiceOrchestrator`, honouring
  mute-all and the existing channel routing (`docs/voice.md:163-175`).
- Do not listen while speaking. Playback and capture are mutually exclusive
  in this round; barge-in is a later problem.
- Every transcribed turn still lands in the message box first, visibly,
  before it sends. The user sees what it heard.

## 5.6 Doctor and Privacy Audit

**Doctor** gains an STT check alongside the existing voice checks
(`DoctorService.Voice.cs`): backend reachable, model file present and
hash-verified, and an input device enumerable. Keep the existing discipline
of not reporting a Linux limitation as a problem. Follow the CLAUDE.md hot
spot rule: the knowledge belongs with the voice subsystem, and Doctor calls
into it rather than growing a new understanding of Whisper.

**Privacy Audit** must show:

- the STT provider and whether it is local or remote;
- when remote is selected, an explicit line that **microphone audio leaves
  this machine** and where it goes. The Privacy Audit already covers
  "features that may send data remotely" and specifically calls out images
  attached to a chat message when a remote provider is selected
  (`docs/features.md:666-670`). Voice is a strictly higher-sensitivity case
  and gets a line of its own, worded plainly.
- microphone access state: whether a device is configured and whether
  anything is capturing right now.

## 5.7 Settings versus Services

Per the r22 precedent (CLAUDE.md placement rule, `docs/voice.md:163-175`):

- **Services > Voice**: STT provider selection, base URL, model path,
  device, port, Start/Stop, install actions, "Transcribe audio file...".
  Everything that manages a process or a file on disk.
- **Settings > Voice**: push-to-talk key, insert-at-cursor behaviour,
  hands-free enable, silence threshold and maximum utterance length. Only
  preferences.

## Testing

Roughly 14 to 18 tests, none needing a live microphone or network: asset
selection per platform without downloading; pinned tag and hash constants
present and well-formed; provider registry selection and fallback;
`IsAvailable` false paths producing disabled affordances with reasons;
transcript parsing including the empty and error responses; low-confidence
classification; temp WAV deleted on success, failure and cancellation;
maximum utterance length enforced; capture session disposal releasing its
resources; file-transcription path validation (traversal, symlink, size
cap, non-WAV); endpointing state machine transitions driven by a synthetic
level source, including empty-transcript refusal to send; Privacy Audit
reporting a remote provider as an outbound destination; settings/services
field placement guard if a suitable one exists.
