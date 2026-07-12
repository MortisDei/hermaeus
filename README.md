# Aether

Aether is a native, local-first AI workspace for developers and power users.

Rather than being another chat client, Aether combines conversations, long-term memory, local retrieval, supervised agents, model management, benchmarking, diagnostics, and pluggable voice services into a single desktop application where every action is transparent, reviewable, and under the user's control.

Built with **Avalonia UI** and **.NET 10** for Linux (Wayland/X11) and Windows.

---

## Why Aether?

Most AI desktop applications focus on chat.

Aether treats chat as just one part of a larger local AI workspace.

Core design principles:

- **Local-first**. Your models, data, and workflows stay on your machine whenever possible.
- **Transparent**. Context, memory, citations, traces, and runtime behaviour are visible instead of hidden.
- **User-controlled**. Destructive actions require explicit approval. Services are managed rather than assumed.
- **Provider-agnostic**. Use `llama.cpp`, Ollama, OpenAI-compatible APIs, local RAG, and multiple voice providers through a consistent interface.
- **Native desktop experience**. Built with Avalonia instead of embedding a browser.

---

## Major Features

### Chat Workspace

- Native markdown rendering
- Virtualized conversations
- Syntax-highlighted code blocks
- File attachments
- Context Inspector
- Conversation search
- Long-term memory integration
- Token and context tracking

### Local AI Runtime Management

- Managed `llama.cpp` / `llama-server`
- Ollama support
- OpenAI-compatible providers
- Model profiles
- Runtime profiles
- GPU auto-tuning
- Runtime diagnostics
- Local AI setup wizard

### Local RAG

- Structure-aware chunking
- Markdown, code and PDF support
- Hybrid retrieval
- Multi-variant query planning
- ONNX reranking
- Budget-aware context packing
- Source citations
- Retrieval traces
- Native evaluation harness

### Agent Workbench

A supervised local agent designed around explicit user control.

Features include:

- Read-first workflow
- Workspace memory
- Context packs
- Safety gates
- Tool approval
- Local traces
- Lessons
- Task history
- Workspace profiles

### Voice

Pluggable voice providers including:

- Kokoro
- F5-TTS
- XTTS v2
- OpenAI Voice

Supports local playback, cloning workflows, provider-specific configuration, and managed runtime setup.

### Benchmarking

Evaluate models using practical local benchmark suites.

Includes:

- First-token latency
- Throughput
- Deterministic quality checks
- Ranking profiles
- Historical comparisons
- CSV / JSON / Markdown export
- Managed model switching
- System overview

### Diagnostics

Built-in tooling for running local AI reliably.

Includes:

- Aether Doctor
- Trust & Safety checks
- Runtime health
- GPU detection
- Storage analysis
- Backup and restore
- Migration tools

---

# Quick Start

```bash
git clone https://github.com/MortisDei/aether.git
cd Aether
dotnet run --project src/Aether.Desktop
```

Open **Services**, configure a runtime, select a model, then start the Chat service.

---

# Supported Runtimes

## llama.cpp

Managed locally through the Services workspace.

- OpenAI-compatible endpoints
- Automatic localhost binding
- GPU auto-tuning
- Per-model launch profiles

## Ollama

- Runtime profiles
- Local model discovery
- `/api/chat`
- `/api/tags`

## OpenAI-compatible APIs

Supports any provider exposing the standard OpenAI Chat Completions API.

API keys are stored as secure Aether secret references.

---

# Security

Aether is designed around a local-first security model.

Highlights include:

- Localhost-only managed services
- Shell-free process execution (`ProcessStartInfo.ArgumentList`)
- OS credential-store integration
- Encrypted local fallback secret vault
- SHA256 verification for managed downloads
- Trust & Safety scanning
- Backup and restore safeguards
- Versioned SQLite schema migrations
- Data migration validation
- Security review and documented threat model

See **docs/security-review.md** for the complete engineering review.

---

# Documentation

## User Features

- Chat & Context
- Model Management
- Local AI Setup
- System Integration

See:

`docs/features.md`

## Core Components

- `docs/rag.md`
- `docs/agent.md`
- `docs/voice.md`
- `docs/benchmarks.md`
- `docs/security-review.md`

---

# Project Structure

```
src/
├── Aether.Core/
├── Aether.Agent/
├── Aether.Rag/
├── Aether.Services/
├── Aether.ViewModels/
├── Aether.Desktop/
└── Aether.Tests/

docs/
```

---

# Building

```bash
dotnet build Aether.sln

dotnet test src/Aether.Tests/Aether.Tests.csproj

./build.sh --skip-restore

pwsh ./build.ps1 -SkipRestore
```

Packaging scripts create Linux `.tar.gz` and Windows `.zip` archives under `dist/`.

---

# License

Aether is source-available.

Private and non-commercial use is licensed under the **PolyForm Noncommercial License 1.0.0**.

Commercial use requires a separate commercial licence.

See:

- LICENSE.md
- COMMERCIAL.md
- NOTICE.md

Aether is **not** an OSI-approved open source project.

---

# Current Status

Current release target:

**0.10.0-alpha**

Major systems currently implemented include:

- Native desktop chat
- Managed local runtimes
- Local RAG
- Agent Workbench
- Long-term memory
- Voice providers
- Benchmark suites
- Local AI setup
- Doctor diagnostics
- Security review and threat model

Public release hardening continues in the areas of installer signing, OCR support, additional security tightening, and Linux global hotkeys.