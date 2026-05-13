# Aether

Version: `0.9.0-alpha`

A native, local-first Avalonia desktop AI workspace for `llama.cpp`, Ollama,
OpenAI-compatible APIs, Oghma-grade RAG, agentic task work, and pluggable
local voice providers.

> Built with Avalonia UI + .NET 10. Linux Wayland/X11 and Windows.

## Why Aether

- Native desktop UI, not a WebView shell.
- Local-first data and runtime control.
- Direct `llama.cpp` / `llama-server` management.
- Ollama and OpenAI-compatible runtime profiles.
- Native markdown rendering with virtualized long chats and syntax-highlighted
  fenced code blocks.
- Oghma-grade RAG: hybrid retrieval, ONNX reranking, parent-child context,
  citations, traces, evals.
- Aether Agent: read-first task workbench with explicit state, compact context,
  retrieval, safety gates, and local logs.
- Pluggable local voice providers for readback and cloning workflows.

## Features

- Chat history with rename, delete, search, folders, tags, pins, archive, and
  direct file context injection for selected text/code files.
- Chat context usage indicator with provider-reported usage where available and
  local estimates before send.
- Model profiles with display names, descriptions, tags, visibility, and defaults.
- Runtime profiles for `llama.cpp`, Ollama, and OpenAI-compatible endpoints.
- Managed `llama-server` start/stop, auto-start, logs, and GPU auto-tune.
- RAG ingest for text/markdown and digital PDFs, optional explicit web URLs,
  reindex diffing, corpus health warnings.
- RAG citations with `[1] [2] [3] +N`, source inspector, copy source/path.
- RAG query traces, ONNX cross-encoder reranking, and native eval harness for
  `gold.json` / `stress.json`.
- Experimental Agent workspace with one-goal task state, retrieved context,
  read-only file tools, proposed next actions, local logs, and JSONL traces.
- Model benchmarks with saved run history, deterministic quality checks,
  rankings, reruns, and Markdown/JSON/CSV export.
- System overview for app version, CPU, RAM, storage, databases, managed
  components, and best-effort GPU/VRAM visibility.
- Runtime logs with filters, copy, and redacted diagnostics export.
- Aether Doctor checks for storage, runtimes, voice, RAG, GPU, and secrets.
- Voice provider controls for Kokoro, F5-TTS, and XTTS v2, with preview and
  voice sample import.
- Optional OpenAI voice provider for remote synthesis.
- Local tasks, reminders, and app-running scheduled automations.
- Tray integration, minimize-to-tray, local hotkeys, and Windows system-wide
  hotkeys.
- Toast notifications throughout the app.
- Configurable data root with migration, backup, restore, and conflict refusal.
- Configurable local AI assets root for models, XTTS, venvs, and encoders.
- Trust & Safety scan for configured local tools, hashes, AI-root scope, and
  network exposure warnings.
- OS-backed secret references and redacted process logs.
- Data-safety test harness for migration, backup/restore, and redaction.

## Quick Start

```bash
git clone https://github.com/MortisDei/aether.git
cd Aether
dotnet run --project src/Aether.Desktop
```

Use **Services** to point Aether at `llama-server` and a `.gguf` model, then
start the Chat service. For GPU acceleration, set GPU layers manually or use
**Auto Tune**.

## Chat Context

Chat can attach selected local text/code files directly to the next message.
Use the attach button or drop files on the input. Aether reads each file once at
send time, prepends a bounded context block to the model prompt, and stores only
an attachment summary in conversation history.

This is not RAG: attachments are not indexed, embedded, watched, or mutated.
Large, unsupported, or binary-looking files are skipped with visible status.

The chat bar also shows current context usage against the selected context
window. It uses provider-reported token usage when available and falls back to a
local estimate for draft input, visible history, system prompt, and ready file
attachments. At high usage, Aether warns so a fresh conversation can avoid
quality loss.

## Runtimes

### llama.cpp

- Uses OpenAI-compatible `/v1/chat/completions` and `/v1/models`.
- Managed through **Services** with executable/model pickers.
- Binds local managed services to `127.0.0.1`.

### Ollama

- Add or enable an Ollama runtime profile in **Services**.
- Uses `/api/tags` for model discovery and `/api/chat` for streaming.

### OpenAI-Compatible APIs

- Configure base URL and API key in **Settings** or **Services**.
- API keys are stored as Aether secret references after saving.

## RAG Workflow

1. Start an embeddings runtime in **Services**.
2. Open **RAG** and ingest a folder of `.txt` / `.md` / digital `.pdf` files.
3. Ask questions against the dataset.
4. Inspect citations, source text, grounding score, and query traces.
5. Run eval sets from the Eval Harness panel.

The optional web loader is off by default. When enabled for a dataset, Aether
fetches only the HTTP(S) pages explicitly listed in the ingest panel; it does
not crawl links or use a remote scraping service.

PDF ingest uses managed PdfPig text extraction for digital PDFs. Scanned or
image-only PDFs are skipped with health warnings; OCR remains a later release
gate.

Eval files can be either an array of cases or an object with `cases` /
`questions`. Cases support `question`, `expected_sources`, `answer_keywords`,
and `should_refuse`.

## Agent Workbench

The **Agent** workspace is an experimental local-first task runner inspired by
the Aether Agent design pack. It works one goal at a time and keeps state
outside the model instead of relying on whole-chat-history context.

The current Agent slice is read-first:

- builds explicit task state and compact context packs
- searches and reads bounded text files under a selected workspace root
- can include relevant context from an optional RAG dataset
- records `task_state.json`, `agent.log`, and `agent.trace.jsonl` under the
  Aether data root
- classifies risky actions before execution

Writes, command execution, installs, network actions, commit, and push are not
executed by this alpha agent. They are surfaced as approval-required or blocked
next actions for a later automation slice.

## Voice Providers

In **Settings**, choose a voice provider:

- **Kokoro** - recommended default for fast local readback.
- **F5-TTS** - advanced voice cloning mode with heavier install requirements.
- **XTTS v2** - legacy Coqui-compatible voice cloning backend, best kept for
  existing workflows that already depend on it.
- **OpenAI** - optional remote voice synthesis for API users.

XTTS v2 still supports the familiar local AI assets setup:

- local AI assets folder, or explicit paths
- service URL
- Python/script path
- XTTS model directory
- device (`cpu`, `auto`, `cuda`)
- model version
- voice folder
- selected speaker

Voice preview uses in-memory generated audio. Aether does not persist generated
WAV responses. Imported clone samples are copied only when explicitly imported.

Local AI Setup can scan a selected AI folder and show readiness for GGUF models,
Python venv, XTTS v2 model files, XTTS API script, voices, output folders, and
the optional RAG reranker. Missing setup actions are approval-gated and show the
target path, command preview, install plan, risk, and expected result before
Aether runs them.

Voice provider setup is moving toward a pluggable layer, with Kokoro as the
preferred built-in readback path and XTTS v2 retained as the legacy cloning
backend. Aether Doctor now validates the configured Python and voice backend
health before installs or playback.

## Data, Backup, Security

Default data root:

| Platform | Path |
| --- | --- |
| Linux | `~/.local/share/Aether/` |
| Windows | `%LOCALAPPDATA%\Aether\` |

Settings can move the data root. Aether previews the move, refuses conflicting
target databases, and migrates `conversations.db*` together. Backups are ZIP
archives of the data root excluding the local secret file.

Settings can also point at a separate local AI assets folder for large model,
voice, virtualenv, and encoder files. Aether never assumes a machine-specific
path; choose the folder once, then use **Apply Detected Paths** to populate
XTTS and reranker paths where matching assets are found, or use **Scan Setup**
to create missing local support files after approval.

Security hardening currently includes:

- localhost binding for managed services
- shell-free process launch via `ProcessStartInfo.ArgumentList`
- Trust & Safety scan for configured executables, scripts, model paths, XTTS
  paths, hashes, AI-root scope, runtime endpoints, and network-facing extra args
- unsafe restore path checks
- API-key and home-path redaction in visible server logs
- OS credential-store integration for API keys where available:
  Linux Secret Service via `secret-tool`, macOS Keychain via `security`, and
  Windows Credential Manager. A user-only local fallback vault is used when no
  OS store is available.
- refreshed security review and threat model covering secrets, local runtime
  binding, backup/restore, RAG ingest, tray behavior, and packaging
- data-safety tests for migration, backup/restore, and redaction

See [docs/security-review.md](docs/security-review.md).

## Tray And Hotkeys

Aether can show a tray icon with quick actions for showing the app, opening
Quick Chat, starting a new chat, opening Services, stopping managed services,
and quitting. Minimize-to-tray is separate from close: clicking the window close
button exits Aether and stops managed `llama-server` / XTTS processes.

Local hotkeys work while Aether is focused:

| Hotkey | Action |
| --- | --- |
| `Ctrl+Space` | Toggle Quick Chat |
| `Ctrl+N` | New chat |
| `Ctrl+Shift+S` | Open Services |
| `Esc` | Close Quick Chat |

System-wide hotkeys are opt-in. On Windows, Aether registers:

| Hotkey | Action |
| --- | --- |
| `Ctrl+Alt+Space` | Toggle Quick Chat |
| `Ctrl+Alt+N` | New chat |
| `Ctrl+Alt+S` | Open Services |

Linux system-wide hotkeys remain unavailable until a reliable compositor path is
implemented. Wayland support varies by compositor, so Aether reports the feature
as unavailable instead of using a brittle fallback.

## Benchmarks And System Overview

The **Benchmarks** workspace runs local prompt suites against selected models and
stores immutable run history under the Aether data root. Runs record first-token
latency, total latency, approximate tokens/sec, deterministic quality checks,
resource deltas, pass rate, and weighted rankings. Built-in starter suites cover
speed smoke tests, instruction following, light reasoning, RAG answer style, and
refusal behavior.

The **System Overview** page shows the local machine and app environment:
version, OS, CPU, RAM, process memory, data-root storage, database footprint,
component status, and best-effort GPU/VRAM data. NVIDIA systems use
`nvidia-smi` when available; other GPU probes degrade gracefully.

## License

Aether is source-available and free for private/noncommercial use under the
PolyForm Noncommercial License 1.0.0.

Commercial use requires a separate paid commercial license. See
[LICENSE.md](LICENSE.md), [COMMERCIAL.md](COMMERCIAL.md), and
[NOTICE.md](NOTICE.md).

This repository is not OSI open source. Public release terms should be reviewed
by a qualified lawyer before Aether 1.0.

## Build

```bash
dotnet build Aether.sln
dotnet run --project tests/Aether.Tests/Aether.Tests.csproj
./build.sh --skip-restore
pwsh ./build.ps1 -SkipRestore
```

The packaging scripts create Linux `.tar.gz` and Windows `.zip` archives under
the ignored `dist/` folder. Packages are framework-dependent by default and can
be made self-contained with `./build.sh --self-contained` or
`pwsh ./build.ps1 -SelfContained`. See [docs/packaging.md](docs/packaging.md).

## Project Structure

```text
src/Aether.Core/        Models and service interfaces
src/Aether.Agent/       Agent task state, context packs, safety gates, tools
src/Aether.Services/    Runtime, storage, settings, backup, voice services
src/Aether.Rag/         Ingest, retrieval, citations, traces, eval harness
src/Aether.ViewModels/  MVVM state and commands
src/Aether.Desktop/     Avalonia views, controls, styles, entry point
tests/Aether.Tests/     Lightweight regression harness
docs/                   Security notes, RAG/eval docs, internal planning notes
```

## Public Release Gates

- Concrete local-first OCR loader.
- Linux and Windows archive packaging is available; signed installers/update
  metadata remain future public-release hardening.
- Security review and threat model refresh is complete for the current alpha;
  Trust & Safety warnings are available and remaining hardening items are
  tracked in [docs/security-review.md](docs/security-review.md).
- Windows system-wide global hotkeys are available; Linux remains deferred until
  a reliable compositor registration API is available.
