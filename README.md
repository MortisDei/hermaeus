# Hermaeus

[![CI](https://github.com/MortisDei/hermaeus/actions/workflows/ci.yml/badge.svg)](https://github.com/MortisDei/hermaeus/actions/workflows/ci.yml)

Hermaeus is a native, local-first AI workspace for developers and power users.

Rather than being another chat client, Hermaeus combines conversations, long-term memory, local retrieval, supervised agents, model management, benchmarking, diagnostics, and pluggable voice services into a single desktop application where every action is transparent, reviewable, and under the user's control.

Built with **Avalonia UI** and **.NET 10**. Tested on Windows and on Pop!_OS (Wayland); other Linux environments should work but are less exercised.

---

## Why Hermaeus?

Most AI desktop applications focus on chat.

Hermaeus treats chat as just one part of a larger local AI workspace.

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
- Context Inspector and a per-answer context receipt
- Conversation branching: regenerate and edit without losing the original
- Conversation search
- Long-term memory integration
- Token and context tracking

### Projects

A named container - folder root, default chat model, default RAG dataset,
default system prompt - that Chat, the Agent Workbench and RAG all read from.
Switching the active project sets that context everywhere at once.

### Recall and the command palette

One local search index over your own words in Hermaeus: past messages, agent
tasks, memories and document chunks, fused into a single ranked result. The
command palette (**Ctrl+K**) searches Recall alongside every registered app
command, so an empty query is a browsable index of what the app can do.

Recall ships with a visible switch, a real "Clear index" action, per-conversation
exclusion and honest size reporting.

### Activity

An outcome record for work that finishes somewhere you are not looking:
ingests, refreshes, model downloads, backups and restores, memory sweeps, the
voice backend, managed servers. Facts the app observed, not a model-written
summary. A row that names a specific artifact opens it; one that does not stays
inert rather than offering a link that goes nowhere.

### Constrained output

Where Hermaeus needs a reply in a particular shape, the shape is enforced by
the provider's sampler rather than requested in the prompt. The agent's action
protocol and memory auto-summary both use it, which is what makes a small local
model a viable driver for them. Providers that cannot enforce a shape say so at
the call site instead of silently sending an unconstrained request, and every
existing fallback stays for them.

### Memories

A reviewable store of durable facts, with hybrid retrieval, per-scope
organisation, and explicit control over what is kept and what is forgotten.

### Local AI Runtime Management

- Managed `llama.cpp` / `llama-server`
- Ollama support
- OpenAI-compatible providers
- Model profiles
- Runtime profiles
- GPU auto-tuning
- Runtime diagnostics
- Local AI setup wizard
- Speculative decoding, composable: n-gram drafting and draft-model drafting
  (including MTP heads) can be combined, and an incompatible draft model is
  refused before the server starts rather than after
- Complete model downloads: shards, vision projectors and MTP draft heads
  arrive together, each model in its own folder

### Local RAG

- Structure-aware chunking
- Markdown, code and PDF support
- Hybrid retrieval
- Multi-variant query planning
- ONNX reranking
- Budget-aware context packing
- Watched sources: datasets notice when their source files change instead of
  rotting silently
- Index size shown against the in-memory budget, so a corpus outgrowing it is
  visible while it grows rather than discovered later as a slow query
- Source citations
- Retrieval traces
- Native evaluation harness

### Agent Workbench

A supervised local agent designed around explicit user control.

The workbench is a status line, a pinned strip for whatever decision the agent
is waiting on, and four tabs: Run, Changes, Workspace, History. The decision is
never behind a tab, the panel opens on Run every time, and it never switches
tabs under you.

Features include:

- Read-first workflow
- A review queue that lists only what needs a decision now
- A finished run that says what it did: files, commands, approvals, and what it
  could not confirm
- Capability text derived from the real tool set and this workspace's own rules
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

Speech recognition runs in-process on a local Whisper model: dictation anywhere
text goes, transcription of an audio file, and an optional hands-free
conversation mode. Transcripts carry punctuation and casing, and the language is
detected rather than assumed. Captured audio is transient - transcribed, then
deleted, never persisted and never attached to a conversation.

### Benchmarking

Evaluate models using practical local benchmark suites.

Includes:

- First-token latency
- Throughput
- Deterministic quality checks
- Ranking profiles, compared only across cases every model actually ran
- Best across every suite, by mean per-suite standing, so a large suite cannot
  outvote a small one and there is no winner when there cannot honestly be one
- Historical comparisons
- Speed Check: a fixed suite for measuring tokens per second, and a comparison
  between two runs of it with the configuration difference that separates them.
  How much speculative decoding helps depends on the model pair and the content,
  so this measures it rather than assuming it
- CSV / JSON / Markdown export
- Managed model switching
- System overview

### Diagnostics

Built-in tooling for running local AI reliably.

Includes:

- Hermaeus Doctor
- Trust & Safety checks
- Runtime health and runtime Logs
- GPU detection
- Storage analysis
- Backup and restore
- Migration tools
- A Settings page for preferences, kept separate from the Services page that
  manages processes and files on disk

---

# Quick Start

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). A local model runtime (managed `llama.cpp`, Ollama, or an OpenAI-compatible endpoint) is configured later, in-app, via the setup wizard - nothing else needs to be installed up front.

```bash
git clone https://github.com/MortisDei/hermaeus.git
cd hermaeus
dotnet run --project src/Hermaeus.Desktop
```

Open **Services**, configure a runtime, select a model, then start the Chat service. If you have no runtime set up yet, the in-app setup wizard walks through downloading a starter model.

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

API keys are stored as secure Hermaeus secret references.

---

# Security

Hermaeus is designed around a local-first security model.

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

See **docs/security-review.md** for the complete engineering review, and **SECURITY.md** to report a vulnerability.

An automated regression suite of **1,176 tests** runs on every commit across Windows and Linux, with warnings treated as build errors.

---

# Known Issues / Alpha Status

Hermaeus is alpha software, developed and tested primarily on Windows. Linux (Pop!_OS, Wayland) has been run and works, but is less exercised than Windows and more likely to surface rough edges. Prebuilt binaries are not code-signed; Windows SmartScreen will warn on first run (verify the published SHA256 instead of dismissing it blindly). See `CHANGELOG.md` for the pace of fixes and new features.

Hermaeus has one maintainer. Issues get a best-effort response; pull requests must follow `CONTRIBUTING.md`.

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
- `docs/projects.md`
- `docs/recall.md`
- `docs/benchmarks.md`
- `docs/security-review.md` (current controls and threat model)
- `docs/security-history.md` (per-round security history)
- `docs/security-roadmap.md` (open security hardening work)
- `docs/review/deferred.md` (work a review round postponed, and why)

---

# Project Structure

```
src/
├── Hermaeus.Core/
├── Hermaeus.Agent/
├── Hermaeus.Rag/
├── Hermaeus.Services/
├── Hermaeus.ViewModels/
├── Hermaeus.Desktop/
└── Hermaeus.Tests/

docs/
```

---

# Building

```bash
dotnet build Hermaeus.sln

dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj

./build.sh --skip-restore

pwsh ./build.ps1 -SkipRestore
```

Packaging scripts create Linux `.tar.gz` and Windows `.zip` archives under `dist/`.

---

# License

Hermaeus is source-available.

Private and non-commercial use is licensed under the **PolyForm Noncommercial License 1.0.0**.

Commercial use requires a separate commercial licence.

See:

- LICENSE.md
- COMMERCIAL.md
- NOTICE.md

Hermaeus is **not** an OSI-approved open source project.

For commercial licensing requests, see the Contact section of COMMERCIAL.md.

---

# Current Status

Current version:

**0.35.0-alpha**

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

Hermaeus is alpha software. See [Known Issues](#known-issues--alpha-status) below and `CHANGELOG.md` for the current state and improvement cadence. Public release hardening continues in the areas of installer signing, OCR support, additional security tightening, and Linux global hotkeys.