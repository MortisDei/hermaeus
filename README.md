# Aether

Version: `0.8.2-alpha`

A native, local-first Avalonia desktop AI workspace for `llama.cpp`, Ollama,
OpenAI-compatible APIs, Oghma-grade RAG, and XTTS voice playback.

> Built with Avalonia UI + .NET 10. Linux Wayland/X11 and Windows.

## Why Aether

- Native desktop UI, not a WebView shell.
- Local-first data and runtime control.
- Direct `llama.cpp` / `llama-server` management.
- Ollama and OpenAI-compatible runtime profiles.
- Native markdown rendering with virtualized long chats.
- Oghma-grade RAG: hybrid retrieval, ONNX reranking, parent-child context,
  citations, traces, evals.
- XTTS v2 readback with memory-only generated audio.

## Features

- Chat history with rename, delete, search, folders, tags, pins, and archive.
- Model profiles with display names, descriptions, tags, visibility, and defaults.
- Runtime profiles for `llama.cpp`, Ollama, and OpenAI-compatible endpoints.
- Managed `llama-server` start/stop, auto-start, logs, and GPU auto-tune.
- RAG ingest for text/markdown, reindex diffing, corpus health warnings.
- RAG citations with `[1] [2] [3] +N`, source inspector, copy source/path.
- RAG query traces, ONNX cross-encoder reranking, and native eval harness for
  `gold.json` / `stress.json`.
- Model benchmarks with saved run history, deterministic quality checks,
  rankings, reruns, and Markdown/JSON/CSV export.
- System overview for app version, CPU, RAM, storage, databases, managed
  components, and best-effort GPU/VRAM visibility.
- XTTS v2 launch controls, voice selection, voice preview, voice sample import.
- Local tasks, reminders, and app-running scheduled automations.
- Tray integration, minimize-to-tray, and local hotkeys.
- Toast notifications throughout the app.
- Configurable data root with migration, backup, restore, and conflict refusal.
- Configurable local AI assets root for models, XTTS, venvs, and encoders.
- Local secret references and redacted process logs.
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
2. Open **RAG** and ingest a folder of `.txt` / `.md` files.
3. Ask questions against the dataset.
4. Inspect citations, source text, grounding score, and query traces.
5. Run eval sets from the Eval Harness panel.

Eval files can be either an array of cases or an object with `cases` /
`questions`. Cases support `question`, `expected_sources`, `answer_keywords`,
and `should_refuse`.

## XTTS

In **Settings**, configure XTTS v2:

- local AI assets folder, or explicit paths
- service URL
- Python/script path
- device (`cpu`, `auto`, `cuda`)
- model version
- voice folder
- selected speaker

Voice preview uses in-memory generated audio. Aether does not persist generated
WAV responses. Imported clone samples are copied only when explicitly imported.

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
XTTS and reranker paths where matching assets are found.

Security hardening currently includes:

- localhost binding for managed services
- shell-free process launch via `ProcessStartInfo.ArgumentList`
- unsafe restore path checks
- API-key and home-path redaction in visible server logs
- local secret references for OpenAI keys
- data-safety tests for migration, backup/restore, and redaction

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

System-wide global hotkeys are deferred until each OS/compositor path can be
implemented reliably. Wayland support varies by compositor.

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
dotnet publish src/Aether.Desktop -c Release -r linux-x64 --self-contained false -o dist/linux
dotnet publish src/Aether.Desktop -c Release -r win-x64 --self-contained false -o dist/windows
```

## Project Structure

```text
src/Aether.Core/        Models and service interfaces
src/Aether.Services/    Runtime, storage, settings, backup, voice services
src/Aether.Rag/         Ingest, retrieval, citations, traces, eval harness
src/Aether.ViewModels/  MVVM state and commands
src/Aether.Desktop/     Avalonia views, controls, styles, entry point
tests/Aether.Tests/     Lightweight regression harness
docs/                   Security notes, RAG/eval docs, internal planning notes
```

## Public Release Gates

- OS keychain integration for secrets.
- Concrete local-first OCR loader.
- Optional web loader kept opt-in and disabled by default.
- Linux and Windows packaging.
- Security review and threat model refresh.
- System-wide global hotkey support where the OS/compositor exposes a reliable
  registration API.
- Expanded tests for RAG scoring, runtime validation, secret migration, and
  process argument safety.
