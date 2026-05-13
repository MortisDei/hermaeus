# Changelog

All notable changes to Aether will be documented in this file.

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

## [0.9.0-alpha] - Unreleased

### Added

- Voice provider abstraction with capabilities, health checks, and install
  plans for Kokoro, F5-TTS, XTTS v2, and optional OpenAI voice.
- Local AI setup install plan previews that must be reviewed before approval.
- Runtime log viewer with filters, copy actions, and redacted diagnostics export.
- Aether Doctor screen with environment checks for storage, runtimes, voice,
  RAG, GPU visibility, and secrets, plus diagnostics copy and navigation.
- Python health validator that rejects broken or non-relocatable Python
  installs and surfaces actionable diagnostics.
- First-run Setup Wizard: a 6-step guided onboarding that configures data
  roots, chat backend, model paths, voice provider, and runs the Aether
  Doctor before starting. The wizard sets `SetupWizardCompleted` to skip on
  subsequent launches after finish or skip.
- RAG ingest dry-run, duplicate-source reporting, and per-document ingest
  summaries surfaced in the RAG panel before writing changes.

### Fixed

- Fix test harness and CI-only fakes to match new voice provider APIs and
  Python validator constructor; repaired several setup & log view bindings.




## [0.8.7-alpha] - 2026-05-13

### Added

- Voice provider groundwork for a pluggable local voice layer, with Kokoro as
  the recommended default readback engine, F5-TTS as the advanced cloning
  option, and XTTS v2 retained as the legacy compatibility backend.
- Voice setup terminology updates from XTTS-only wording to voice-provider
  wording in preparation for the pluggable layer.
- XTTS validation and repair improvements that require Python 3.11 and detect
  broken or incompatible venvs before install.

### Changed

- XTTS setup actions are now framed as voice backend setup rather than a boxed-
  in single-provider workflow.

## [0.8.6-alpha] - 2026-05-12

### Added

- Settings Trust & Safety scan for configured local executables, scripts,
  models, XTTS paths, runtime endpoints, hashes, and AI-root scope warnings.
- Advisory trust warnings for network-facing `llama-server` extra args such as
  `--host 0.0.0.0`.
- Direct chat file context injection for selected local text/code files, with
  attach/drop UI, bounded reads, skipped-file status, and history summaries.
- Digital PDF support in local RAG directory ingest through managed PdfPig text
  extraction, with image-only PDFs skipped as health warnings.
- Syntax-highlighted fenced Markdown code blocks in chat and RAG answers through
  AvaloniaEdit.
- Chat context usage indicator with provider-reported token usage when
  available and local estimates while drafting.

## [0.8.5-alpha] - 2026-05-12

### Added

- Optional RAG web URL loader that stays disabled by default and only ingests
  explicitly listed HTTP(S) pages when enabled for a dataset.
- Web ingest HTML text extraction with script/style stripping, small page
  limits, URL deduplication, and regression coverage for the default-disabled
  posture.
- Experimental Aether Agent workbench with explicit task state, compact context
  packs, local task logs/traces, read-only workspace tools, safety gating, and
  optional RAG-backed retrieval.
- Agent regression coverage for task-state serialization, workspace path
  safety, context packing, tool policy, and the fake-model agent loop.
- Approval-gated Local AI Setup assistant for scanning an AI folder, detecting
  models, venvs, XTTS v2 assets, voices, output folders, and rerankers.
- Structured setup actions for creating a venv, creating XTTS support folders,
  installing XTTS packages, and generating an XTTS v2 API script after explicit
  user approval.

## [0.8.4-alpha] - 2026-05-12

### Added

- Repeatable Linux and Windows archive packaging scripts.
- Linux package layout with desktop launcher metadata, user-local desktop
  install/uninstall scripts, icon asset, license notices, archive, and SHA256.
- Windows package layout with launch helper, license notices, ZIP archive, and
  SHA256.
- App and tray icon assets derived from the Aether branding sheet.
- Packaging documentation covering runtime requirements and self-contained
  builds.
- Refreshed security review and threat model for secrets, local runtimes,
  backup/restore, RAG ingest, tray behavior, and packaging.
- Expanded tests for RAG BM25/hybrid scoring, runtime profile validation,
  secret reference migration, and shell-free process argument construction.
- Opt-in Windows system-wide hotkeys for Quick Chat, New Chat, and Services,
  with Linux reported as unavailable until reliable compositor support exists.

## [0.8.3-alpha]

### Added

- OS credential-store integration for secrets via Linux Secret Service,
  macOS Keychain, and Windows Credential Manager.
- Local fallback secret vault remains available when no OS store is present.

## [0.8.2-alpha]

### Added

- Tray icon with show, quick chat, new chat, services, stop services, and quit
  actions.
- Local hotkeys for quick chat, new chat, services, and closing quick chat.
- Settings toggles for tray icon, minimize-to-tray, and local hotkeys.
- Documentation for tray behavior, local hotkeys, and close-button shutdown
  semantics.

## [0.8.1-alpha]

### Added

- Configurable local AI assets root for models, XTTS, venvs, and encoders.
- Detected-path application for XTTS script/python/voices/output and ONNX
  reranker assets.

### Changed

- Removed machine-specific XTTS script defaults; Aether now asks for a path
  when XTTS is started without one.

## [0.8.0-alpha]

### Added

- Central repo version metadata for all assemblies.
- Source-available private/noncommercial licensing posture.
- Commercial licensing documentation.
- Contribution terms for dual noncommercial and commercial distribution.
- Public-release notice and third-party component notice.

### Current Product State

- Native Avalonia desktop shell for Linux and Windows.
- Local-first chat history with folders, tags, pins, archive, search, rename,
  and delete.
- Runtime profiles for `llama.cpp`, Ollama, and OpenAI-compatible APIs.
- Managed `llama-server` start/stop, logging, auto-start, and GPU auto-tune.
- RAG ingest, citations, source inspector, traces, eval harness, and ONNX
  reranking.
- XTTS v2 launch, voice discovery, preview, import, and memory-only playback.
- Configurable data root, migration preview, backup, restore, and data-safety
  tests.

### 1.0 Release Gates

- Concrete local-first OCR ingestion.
- Optional web loader that is disabled by default.
- Linux and Windows packaging.
- Security review and threat model refresh.
- Expanded tests for RAG scoring, runtime validation, backup/restore, secret
  migration, and process argument safety.
