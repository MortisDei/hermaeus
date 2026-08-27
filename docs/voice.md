# Voice

Text-to-speech (output) and, as of r24, speech-to-text (input). Both are
off-by-default, local-first by default, and never persist audio beyond the
moment they need it.

## Voice Architecture and Providers

Hermaeus uses a pluggable voice layer managed by `TtsSettingsViewModel`.
Settings bind through `Tts.*` so provider state stays isolated and easier to
test.

Supported providers:

- **Kokoro (native)** - default. Fully in-process Kokoro: no Python
  subprocess, ONNX inference runs directly in Hermaeus. English voices only;
  downloads its model once via the Doctor install action, then runs fully
  offline.
- **Kokoro (Python)** - the original fast local readback path, run as a
  managed Python subprocess. Kept as an advanced fallback now that Kokoro
  (native) is the default; still useful if you need its specific voice set.
- **F5-TTS** - advanced local voice cloning with high fidelity, heavy resource
  demands, and a dedicated Python venv requirement.
- **XTTS v2** - legacy Coqui-compatible cloning retained for existing user
  workflows that already depend on it.
- **OpenAI** - remote voice synthesis through a cloud API with no local model
  footprint.

Kokoro (Python), F5-TTS, and XTTS v2 are grouped under **Advanced** in
**Services -> Voice**; Kokoro (native) is **Recommended**.

## Local Environment and Hardware Setup

The first-run Setup Wizard and Local AI setup can detect available hardware
backends and suggest an inference device. You can override the selected device
in **Services -> Voice**.

- **NVIDIA** - `cuda`
- **AMD/ROCm** - `rocm`
- **Apple Silicon** - `mps`
- **Fallback** - `cpu`

### Python Virtual Environments

Use isolated Python virtual environments for heavy cloning providers.
F5-TTS and XTTS v2 have different, sensitive PyTorch and CUDA dependency
requirements. Installing them into the same venv can break one or both
providers.

Hermaeus Doctor validates configured Python paths and voice backend health before
installs or playback.

## Kokoro Setup

Kokoro (native) is the default voice provider. It runs fully in-process with no
Python subprocess: the Doctor install action downloads its ONNX model once,
after which it runs fully offline. The first-run Setup Wizard shows Kokoro
onboarding details in the voice step, including the install plan and risk
notes before you continue. When the assets are already present, onboarding
reports Voice as ready and removes the install action. A successful install
rechecks provider state immediately, and the same state is read again when the
step is revisited or the app restarts.

The Kokoro (Python) fallback, along with F5-TTS and XTTS v2, run as managed
Python subprocesses. For those, Hermaeus can detect available hardware backends
when creating a Python venv and will suggest a device (`cuda`, `rocm`, `mps`,
or `cpu`) to use for TTS inference. You can still override the selected
device in **Services -> Voice** after setup.

TTS speed is configurable per-provider and persisted in settings.

## Local Asset Configuration

XTTS v2 and F5-TTS use local asset paths for Python, model files, scripts,
devices, and voice samples. XTTS v2 still supports explicit paths for service
URL, Python/script path, model directory, output directory, voice folder,
selected speaker, model version, and device (`cpu`, `auto`, `cuda`, `rocm`, or
`mps`).

Local AI Setup can scan a selected AI folder and show readiness for:

- GGUF models
- Python venv integrity
- XTTS v2 model files
- XTTS API script
- Voices folder
- Output folder
- F5-TTS voice samples
- Optional RAG reranker
- Platform-matched `llama-server` downloads if missing

Missing setup actions are approval-gated and show:

- Target path
- Command preview
- Install plan
- Risk assessment
- Expected result

If no GGUF models are found, Hermaeus can offer the default Phi-4 mini reasoning
download. If `llama-server` is not available, Hermaeus can offer a matching
binary download for the current platform.

## Pronunciation and Text Normalization

Before phonemization, Kokoro speech text is normalized so LLM chat output
reads naturally instead of being spelled out or mispronounced:

- Numbers, currency, percentages, ordinals, and clock times expand to words
  (e.g. "$5.20" -> "five dollars twenty cents").
- Em dashes (U+2014), en dashes (U+2013), and a standalone `--` become a
  comma pause, which also splits words a dash fused together without spaces.
- Curly quotes normalize to straight quotes and an ellipsis (U+2026) expands
  to three periods, so quoted or trailing-off text does not fall through to
  the letter-by-letter fallback just because of the punctuation attached to
  it.
- Stray markdown emphasis characters (`*`, `` ` ``, `_`) are stripped when
  they are not part of a word, while an underscore inside an identifier
  (like `snake_case`) is left alone.
- Word lookup goes through a user override lexicon, then the embedded CMU
  Pronouncing Dictionary, then suffix-stripping retries (`-ing`, `-ed`,
  `-'s`, and so on), then unknown all-caps acronyms spelled out letter by
  letter. A final letter-by-letter rule-based fallback covers anything still
  unresolved (invented words, typos); its trailing-"e" rule correctly treats
  only the real vowel-consonant-e pattern ("joke", "hope") as silent, not any
  word ending in "e".
- Words that reach the letter-by-letter fallback are logged once per
  distinct word per session (Debug level) so pronunciation reports can be
  checked against what actually fell through, rather than guessed at.
- The user pronunciation lexicon (`{DataRoot}/voice/lexicon.txt`) always
  takes priority and is reloaded automatically when the file changes.

## Chunking and Playback Control

Kokoro synthesizes long replies in chunks rather than one pass, so chunk
boundaries matter for how natural the stitched playback sounds:

- The tokenizer splits chunks at real sentence/clause boundaries (period,
  question mark, exclamation point, semicolon, comma) with a fallback to the
  nearest earlier word break, instead of a blind character-length cut that
  could land mid-word.
- Silence is inserted between stitched chunks: 120ms after a sentence-ending
  boundary, 60ms after a clause-level one, none when a chunk had to be cut at
  a plain word break. This keeps stitched playback from running the end of
  one chunk into the start of the next.
- Paragraph breaks in the source text get their own pause via the same
  boundary-aware phonemizer path, before word-splitting.

Playback exposes explicit state instead of only start events:
`IVoiceOrchestrator.IsSpeaking` and an `UtteranceCompleted` event fire
alongside the existing `UtteranceStarted`, so the UI can show a stop control
and swap a message's speak icon to a stop icon for exactly as long as that
utterance is actually playing. Chat has a global stop-speaking control plus
a per-message speak/stop icon swap wired to this.

Audio feedback is a separate bounded semantic cue service. It covers only the
reviewed task, runtime, long-operation, and recording events, with per-event
defaults, volume, mute, visual equivalents, and suppression while TTS speaks.
It never cues ordinary token arrival, clicks, navigation, or high GPU use.
Windows playback keeps the WAV path as an argument to a fixed PowerShell
script rather than interpolating it into command text; temporary cue files are
deleted after playback.

## Audio Data and Privacy Lifecycle

- Voice previews use transient generated audio and delete temporary WAV files
  after playback when a local player needs a file path. A playback-only
  failure (a broken or missing OS audio player) no longer masks a synthesis
  that actually succeeded: `GenerateSpeechAsync` now reports success and the
  temp file's path whenever rendering the audio worked, even if playing it
  back failed, across every voice provider (OpenAI, Kokoro, F5-TTS, XTTS).
- OpenAI voice resolves saved `secret:` API key references through the Hermaeus
  secret store before sending requests.
- Hermaeus does not cache generated WAV responses.
- Imported clone samples are copied only when explicitly imported.
- Voice sample import is available in Services under the Voice card.

## Speech Recognition (Input)

Local speech-to-text, off by default. Everything here is transient: a
captured or uploaded WAV is transcribed and its temp file deleted
immediately, on every path including failure and cancellation - never
persisted, backed up, logged, or attached to a conversation. There is no
wake word and no always-on listening: every capture is started by an
explicit user action and has a visible recording indicator for its entire
duration.

### Providers

- **Native (default, local)** - in-process ONNX, no managed subprocess. The
  backend is Whisper base (`onnx-community/whisper-base`, pinned revision,
  every file SHA256-verified, roughly 291 MB across an encoder, a decoder and
  its tokenizer and generation config). Transcripts carry **punctuation and
  casing**, and the **language is detected** from 98 supported languages
  rather than assumed; Settings > Voice can force a specific language instead
  of auto-detecting.

  Audio is processed in fixed 30-second windows, so memory does not grow with
  recording length: a forty-minute file costs the same per window as a
  five-second one, and progress is reported per window.

  r25 replaced the `facebook/wav2vec2-base-960h` CTC model r24 shipped here.
  The in-process design was right and is unchanged; the model was not. Its
  vocabulary held 26 uppercase letters and an apostrophe, with no lowercase
  and no punctuation anywhere in it, so every transcript read
  `HELLO CAN YOU CHECK THE BUILD` and no post-processing could restore what
  was never produced. Whisper's own decoder turned out to be tractable
  in-process because an exported ONNX graph carries its key/value cache as
  named tensors rather than requiring the attention arithmetic to be written
  by hand.

  Same asset posture as native Kokoro: nothing downloads until the explicit
  install action in **Services -> Voice**, and inference then runs fully
  offline.
- **Remote, OpenAI-compatible** - available, never the default. Calls
  `/v1/audio/transcriptions` and reuses the same OpenAI base URL and API key
  Chat and OpenAI voice already use, rather than asking for the same
  credential a second time. Off by default; selecting it sends microphone
  audio to that endpoint, called out explicitly in Privacy Audit.

### Capture

- **Windows**: the `winmm` `waveIn` API directly (no NuGet package),
  double-buffered with an OS event so a background thread polls for
  buffer-ready state rather than marshaling a native callback delegate.
- **Linux**: the same subprocess fallback chain `AudioPlayback` already uses
  for output - `parecord`, then `arecord`, then `ffmpeg`, first one found on
  PATH, launched with `ArgumentList` (never a shell string). If none are
  installed, the unavailable message names which three it looked for.
- **No device, no silent failure.** When no input device exists or access is
  denied, every mic affordance shows disabled with the actual reason instead
  of looking like it is listening.
- **Hot-mic rules, enforced in code**: a visible recording indicator for the
  full duration of any capture; a hard maximum utterance length (default 60
  seconds, plus an unconditional 5-minute safety ceiling in the capture
  engine itself, independent of any caller-side timer); deterministic
  session disposal; temp WAV cleanup on every path.

### Where it shows up

- **Services -> Voice**: provider, input device, remote model name, and the
  native model install action (process/model/device config, so Services not
  Settings, per the same split TTS already uses).
- **Transcribe audio file...** (Services -> Voice): pick a `.wav`, transcribe
  it, copy the transcript. Exercises the whole pipeline except capture - the
  one path verifiable with no live microphone in the building - and
  validates the picked path defensively (normalized, no symlink, `.wav`
  only, size-capped) even though it came from a native file picker.
- **Dictation**: a shared mic-button control, currently wired into the chat
  input. Press to start, press again to stop; the transcript is inserted at
  the cursor for you to edit, never sent automatically. A dictated phrase
  after existing text gets a separating space if the cursor was not already
  after whitespace.
- **Doctor** gains a speech-recognition backend check (model installed and
  loadable) and a microphone check (input device enumerable), alongside the
  existing voice checks.
- **Privacy Audit** shows the active speech-recognition provider and,
  whenever the remote one is selected, an explicit line naming that
  microphone audio leaves the machine and where it goes - the same
  disclosure pattern already used for a remote chat/voice provider, given
  its own line because voice is a strictly higher-sensitivity case.

### Not yet wired

Disclosed rather than silently absent:

- **Hands-free conversation mode** has a complete, tested state machine
  (`HandsFreeStateMachine`: Idle -> Listening -> Transcribing -> Sending ->
  Speaking -> Listening, silence-based endpointing, hard Stop from any
  state, never auto-sends an empty or low-confidence transcript) but is not
  yet wired into Chat's UI or a live capture/orchestrator loop.
- **Dictation** is wired into the chat input only; the agent goal/reply box,
  the RAG query box, the workspace memory note editor, and the command
  palette do not have a mic button yet. The shared control and wiring
  pattern exist for each of those to pick up.
- **Push-to-talk** (a held hotkey instead of click-to-toggle) is not wired;
  dictation today is click-to-start, click-to-stop only.

## Settings vs. Services

The Voice card was split the same way RAG's embeddings config was -
provider selection, base URL, Start/Stop, and the voice/device/speed/path
fields (everything that manages the Kokoro/XTTS/F5 background process) now
live on **Services -> Voice**, alongside the Chat/Embeddings llama-server
cards it's a sibling of. **Settings -> Voice** keeps only voice orchestration:
mute-all, auto-speak, and per-channel routing (each channel picks a voice
directly from the active provider's own voice list, or free-types one for a
provider that cannot enumerate voices) - none of which start or stop a
process. `TtsSettingsViewModel` is a single DI-shared
instance (`ServicesViewModel.Tts` and `SettingsViewModel.Tts` are the same
object), so both pages always reflect the same live state.

**Services -> Voice saves.** Until 0.36.0-alpha it did not: the Voice and
speech-recognition cards edited that shared instance and nothing on the page
wrote it to disk, so a base URL, voice, device or speed set there was silently
discarded on restart. The Services page now has a Save button in its header
that runs the same single save flow as the Settings page's.

**The per-channel voice picker.** Each channel's picker lists the active
provider's own voices, with a chevron that opens the list and a
"(Default voice)" sentinel meaning "use the global voice". It also accepts
free text, for a provider that cannot enumerate its voices.

The shared voice settings refresh this authoritative list when voice settings
load and whenever the active provider changes. The channel section names that
provider while it is loading, reports the number of named voices when it
succeeds, and keeps Refresh as an explicit retry. If a provider cannot report
names, the picker holds only the sentinel and placeholder and says so plainly.
Typing a verified voice id remains available in that state; Hermaeus does not
silently choose a different voice.

Each refresh is owned by the provider selection that started it. A later
selection cancels the older request when possible and otherwise discards its
late result, so a stale provider can never populate the current provider's
catalogue.

Speech recognition follows the identical split: provider/device/model/install
on **Services -> Voice** (`SttSettingsViewModel`, its own DI-shared
instance); push-to-talk key, insert-at-cursor behavior, hands-free enable,
and silence/utterance-length thresholds are preference-only fields on
**Settings -> Voice** (`SttSettings`) reserved for when those flows are
wired to the UI.
