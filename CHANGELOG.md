# Changelog

All notable changes to Aether will be documented in this file.

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

## [0.8.3-alpha] - Unreleased

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
