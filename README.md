# Aether

A native, local-first Avalonia desktop AI workspace for `llama.cpp`, Ollama,
OpenAI-compatible APIs, Oghma-grade RAG, and XTTS voice playback.

> Built with Avalonia UI + .NET 10. Linux Wayland/X11 and Windows.

## Why Aether

- Native desktop UI, not a WebView shell.
- Local-first data and runtime control.
- Direct `llama.cpp` / `llama-server` management.
- Ollama and OpenAI-compatible runtime profiles.
- Native markdown rendering with virtualized long chats.
- Oghma-grade RAG: hybrid retrieval, parent-child context, citations, traces, evals.
- XTTS v2 readback with memory-only generated audio.

## Features

- Chat history with rename, delete, search, folders, tags, pins, and archive.
- Model profiles with display names, descriptions, tags, visibility, and defaults.
- Runtime profiles for `llama.cpp`, Ollama, and OpenAI-compatible endpoints.
- Managed `llama-server` start/stop, auto-start, logs, and GPU auto-tune.
- RAG ingest for text/markdown, reindex diffing, corpus health warnings.
- RAG citations with `[1] [2] [3] +N`, source inspector, copy source/path.
- RAG query traces and native eval harness for `gold.json` / `stress.json`.
- XTTS v2 launch controls, voice selection, voice preview, voice sample import.
- Local tasks, reminders, and app-running scheduled automations.
- Toast notifications throughout the app.
- Configurable data root with migration, backup, restore, and conflict refusal.
- Local secret references and redacted process logs.

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

Security hardening currently includes:

- localhost binding for managed services
- shell-free process launch via `ProcessStartInfo.ArgumentList`
- unsafe restore path checks
- API-key and home-path redaction in visible server logs
- local secret references for OpenAI keys

## Build

```bash
dotnet build Aether.sln
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
docs/                   Parity matrix, security notes, RAG/eval docs
```

## Remaining Big Rocks

- Concrete OCR and Firecrawl URL loaders.
- Local cross-encoder reranker implementation.
- Global quick-chat hotkey and tray integration.
- OS keychain integration.
- Dedicated automated test project for migrations, backup/restore, and RAG eval.
