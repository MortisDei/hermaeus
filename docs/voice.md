# Voice Providers

## Choosing a Voice Provider

In **Settings**, choose a voice provider:

- **Kokoro** - recommended default for fast local readback.
- **F5-TTS** - advanced voice cloning mode with heavier install requirements.
- **XTTS v2** - legacy Coqui-compatible voice cloning backend, best kept for
  existing workflows that already depend on it.
- **OpenAI** - optional remote voice synthesis for API users.

## Provider Architecture

Voice provider setup is moving toward a pluggable layer:

- **Kokoro** is the preferred built-in readback path for fast, local synthesis.
- **XTTS v2** is retained as the legacy cloning backend for existing workflows.
- **F5-TTS** provides advanced cloning with better voice fidelity at the cost of
  heavier resource requirements.
- **OpenAI** enables remote synthesis for users with API access.

## Kokoro Setup

Kokoro is recommended as the default voice provider. The first-run Setup Wizard
shows Kokoro onboarding details in the voice step, including the install plan
and risk notes before you continue.

Aether can detect available GPU backends when creating a Python venv and will
suggest a device (`cuda` for NVIDIA, `rocm` for AMD/ROCm, or `cpu`) to use for
TTS inference. You can still override the selected device in
**Settings → Voice providers** after setup.

TTS speed is configurable per-provider and persisted in settings.

## XTTS v2 Configuration

XTTS v2 still supports the familiar local AI assets setup:

- Local AI assets folder, or explicit paths
- Service URL
- Python/script path
- XTTS model directory
- Device (`cpu`, `auto`, `cuda`)
- Model version
- Voice folder
- Selected speaker

### Local AI Setup

Local AI Setup can scan a selected AI folder and show readiness for:

- GGUF models
- Python venv
- XTTS v2 model files
- XTTS API script
- Voices folder
- Output folder
- Optional RAG reranker

Missing setup actions are approval-gated and show:

- Target path
- Command preview
- Install plan
- Risk assessment
- Expected result

If no GGUF models are found, Aether can offer the default Phi-4 mini reasoning
download. If `llama-server` is not available, Aether can offer a matching
binary download for the current platform.

## Voice Preview & Import

- Voice preview uses in-memory generated audio.
- Aether does not persist generated WAV responses.
- Imported clone samples are copied only when explicitly imported.
- Voice sample import is available in Settings under TTS configuration.

## Health & Validation

Aether Doctor now validates the configured Python and voice backend health
before installs or playback. This ensures voice provider readiness before
use.

## Architecture Notes

TTS settings were recently refactored into a dedicated `TtsSettingsViewModel`.
The Settings view now binds to `Tts.*` for TTS configuration and commands,
improving separation of concerns and making the TTS logic easier to test.
