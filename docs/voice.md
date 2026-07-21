# Voice Providers

## Voice Architecture and Providers

Aether uses a pluggable voice layer managed by `TtsSettingsViewModel`.
Settings bind through `Tts.*` so provider state stays isolated and easier to
test.

Supported providers:

- **Kokoro** - default ultra-fast local readback for standard use.
- **F5-TTS** - advanced local voice cloning with high fidelity, heavy resource
  demands, and a dedicated Python venv requirement.
- **XTTS v2** - legacy Coqui-compatible cloning retained for existing user
  workflows that already depend on it.
- **OpenAI** - remote voice synthesis through a cloud API with no local model
  footprint.

## Local Environment and Hardware Setup

The first-run Setup Wizard and Local AI setup can detect available hardware
backends and suggest an inference device. You can override the selected device
in **Settings -> Voice providers**.

- **NVIDIA** - `cuda`
- **AMD/ROCm** - `rocm`
- **Apple Silicon** - `mps`
- **Fallback** - `cpu`

### Python Virtual Environments

Use isolated Python virtual environments for heavy cloning providers.
F5-TTS and XTTS v2 have different, sensitive PyTorch and CUDA dependency
requirements. Installing them into the same venv can break one or both
providers.

Aether Doctor validates configured Python paths and voice backend health before
installs or playback.

## Kokoro Setup

Kokoro is recommended as the default voice provider. The first-run Setup Wizard
shows Kokoro onboarding details in the voice step, including the install plan
and risk notes before you continue.

Aether can detect available hardware backends when creating a Python venv and
will suggest a device (`cuda`, `rocm`, `mps`, or `cpu`) to use for TTS
inference. You can still override the selected device in
**Settings -> Voice providers** after setup.

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

If no GGUF models are found, Aether can offer the default Phi-4 mini reasoning
download. If `llama-server` is not available, Aether can offer a matching
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

## Audio Data and Privacy Lifecycle

- Voice previews use transient generated audio and delete temporary WAV files
  after playback when a local player needs a file path. A playback-only
  failure (a broken or missing OS audio player) no longer masks a synthesis
  that actually succeeded: `GenerateSpeechAsync` now reports success and the
  temp file's path whenever rendering the audio worked, even if playing it
  back failed, across every voice provider (OpenAI, Kokoro, F5-TTS, XTTS).
- OpenAI voice resolves saved `secret:` API key references through the Aether
  secret store before sending requests.
- Aether does not cache generated WAV responses.
- Imported clone samples are copied only when explicitly imported.
- Voice sample import is available in Settings under TTS configuration.
