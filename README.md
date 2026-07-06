# Aether

A native, local-first Avalonia desktop AI workspace for `llama.cpp`, Ollama, OpenAI-compatible APIs, local RAG, agentic task work, and pluggable voice providers.

> Built with Avalonia UI + .NET 10. Linux Wayland/X11 and Windows.

## Why Aether

- Native desktop UI, not a WebView shell.
- Local-first data and runtime control.
- Direct `llama.cpp` / `llama-server` management.
- Ollama and OpenAI-compatible runtime profiles.
- Native markdown rendering with virtualized long chats and syntax-highlighted fenced code blocks.
- Local RAG with structure-aware chunking, multi-variant query planning, hybrid retrieval, ONNX reranking, budget-aware context packing, citations, traces, and evals.
- Aether Agent: read-first task workbench with explicit state, compact context, retrieval, safety gates, and local logs.
- Pluggable local voice providers for readback and cloning workflows.

## Quick Start

```bash
git clone https://github.com/MortisDei/aether.git
cd Aether
dotnet run --project src/Aether.Desktop
```

Use **Services** to point Aether at `llama-server` and a `.gguf` model, then start the Chat service. For GPU acceleration, set GPU layers manually or use **Auto Tune**.

## Features

See [docs/features.md](docs/features.md) for a comprehensive feature overview.

Quick links:

- **[Chat & Context](docs/features.md#chat)** - file attachments, context tracking, fast search
- **[Model Management](docs/features.md#model-management)** - profiles, runtimes, auto-tune
- **[Local AI Setup](docs/features.md#local-ai-setup)** - Wizard, GPU detection, model downloads
- **[System Integration](docs/features.md#system-integration)** - tray, hotkeys, logs, tasks

## Core Workflows

- **[RAG](docs/rag.md)** - Ingest, retrieve, cite, eval datasets
- **[Agent Workbench](docs/agent.md)** - Read-first task runner, safety gates, workspace memory
- **[Voice Providers](docs/voice.md)** - Kokoro, F5-TTS, XTTS v2, OpenAI
- **[Benchmarks](docs/benchmarks.md)** - Performance suites, quality checks, system overview

## Runtimes

Aether supports three runtime integration patterns:

- **llama.cpp** - Uses OpenAI-compatible `/v1/chat/completions` and `/v1/models`. Managed through **Services** with executable/model pickers. Binds to `127.0.0.1`.
- **Ollama** - Add or enable an Ollama runtime profile in **Services**. Uses `/api/tags` and `/api/chat`.
- **OpenAI-Compatible APIs** - Configure base URL and API key in **Settings** or **Services**. API keys are stored as Aether secret references.

## Security & Data

Default data root:

| Platform | Path |
| --- | --- |
| Linux | `~/.local/share/Aether/` |
| Windows | `%LOCALAPPDATA%\Aether\` |

Key hardening features:

- Localhost binding for managed services
- Shell-free process launch via `ProcessStartInfo.ArgumentList`
- AES-256-CBC encryption for fallback secrets with PBKDF2 and per-secret salts
- Mandatory SHA256 hash verification for critical model downloads
- Symlink rejection for path traversal hardening
- OS credential-store integration with local fallback
- Trust & Safety scan for executables, scripts, paths, and network exposure
- Configurable data root with migration, backup, and restore
- Versioned SQLite schema records for local stores
- Data-safety tests for migration, backup/restore, and redaction

See [docs/security-review.md](docs/security-review.md) for the full security review and threat model.

## Tray & Hotkeys

Aether shows a tray icon with quick actions (show, Quick Chat, new chat, Services, stop services, quit). Minimize-to-tray is separate from close.

Local hotkeys (when focused):

| Hotkey | Action |
| --- | --- |
| `Ctrl+Space` | Toggle Quick Chat |
| `Ctrl+N` | New chat |
| `Ctrl+Shift+S` | Open Services |
| `Esc` | Close Quick Chat |

Windows system-wide hotkeys (opt-in):

| Hotkey | Action |
| --- | --- |
| `Ctrl+Alt+Space` | Toggle Quick Chat |
| `Ctrl+Alt+N` | New chat |
| `Ctrl+Alt+S` | Open Services |

Linux system-wide hotkeys remain unavailable pending a reliable compositor API.

## Build

```bash
dotnet build Aether.sln
dotnet test tests/Aether.Tests/Aether.Tests.csproj
./build.sh --skip-restore
pwsh ./build.ps1 -SkipRestore
```

Packaging scripts create Linux `.tar.gz` and Windows `.zip` archives under `dist/`. See [docs/packaging.md](docs/packaging.md) for details on self-contained builds.

## Project Structure

```text
src/Aether.Core/        Models and service interfaces
src/Aether.Agent/       Agent task state, context packs, safety gates, tools
src/Aether.Services/    Runtime, storage, settings, backup, voice services
src/Aether.Rag/         Ingest, retrieval, citations, traces, eval harness
src/Aether.ViewModels/  MVVM state and commands
src/Aether.Desktop/     Avalonia views, controls, styles, entry point
tests/Aether.Tests/     Lightweight regression harness
docs/                   Security notes, RAG/eval docs, internal planning
```

## License

Aether is source-available and free for private/noncommercial use under the PolyForm Noncommercial License 1.0.0.

Commercial use requires a separate paid commercial license. See
[LICENSE.md](LICENSE.md), [COMMERCIAL.md](COMMERCIAL.md), and
[NOTICE.md](NOTICE.md).

This repository is not OSI open source. Public release terms should be reviewed by a qualified lawyer before Aether 1.0.

## Public Release Gates

- Concrete local-first OCR loader.
- Linux and Windows archive packaging; signed installers/update metadata deferred to public-release hardening.
- Security review and threat model refresh complete; Trust & Safety warnings available with remaining items tracked in [docs/security-review.md](docs/security-review.md).
- Windows system-wide hotkeys available; Linux deferred pending reliable compositor API.
