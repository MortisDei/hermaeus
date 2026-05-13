# Internal Competitive Research

Internal note: this file is private product research and release planning
context. Do not use this language in public product copy, README text,
marketing, release notes, license files, screenshots, or storefront material.

This matrix tracks capability gaps against comparable AI workspaces while
preserving Aether's native desktop, local-first direction.

## Status

| Area | Reference Capability | Aether Status | Notes |
| --- | --- | --- | --- |
| OCR/web ingest | OCR and web loaders | **Partial** | Optional web URL loader is implemented and disabled by default; concrete local-first OCR remains deferred. |
| Security | Secrets, path checks, log redaction | **Partial** | OS-backed secret refs with local fallback, unsafe restore/path checks, log redaction, initial data-safety tests. |

## Aether Advantages To Preserve

- Native Avalonia desktop app instead of a web UI wrapped for desktop.
- Direct local `llama-server` management with GPU-layer auto-tune support.
- Oghma-style RAG retrieval: title/source embedding injection, full-corpus BM25,
  wide semantic candidates, ONNX reranking, and parent-child context.
- XTTS integration with memory-only generated audio playback.
- Local benchmark history and system overview for choosing models on the user's
  own hardware.
- Local-first data root migration and explicit backup/restore.

## Deferred

- Linux system-wide quick-chat hotkey integration where compositor APIs are reliable.
- Concrete local-first OCR.
