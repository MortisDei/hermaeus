# Internal Competitive Research

Internal note: this file is private product research and release planning
context. Do not use this language in public product copy, README text,
marketing, release notes, license files, screenshots, or storefront material.

This matrix tracks capability gaps against comparable AI workspaces while
preserving Aether's native desktop, local-first direction.

## Status

| Area | Reference Capability | Aether Status | Notes |
| --- | --- | --- | --- |
| Native desktop | Desktop app | **Completed** | Native Avalonia shell, no WebView markdown, Linux/Windows target. |
| Chat history | Folders, tags, pins, archive, search | **Completed** | Rename/delete/search/folder/tag/pin/archive plus compact details flyout. |
| Message scale | Native browser virtualization | **Completed** | Virtualized transcript plus throttled markdown rendering during streams. |
| Notifications | Toasts/reminders | **Completed** | Toast service used by chat, services, RAG, XTTS, eval, tasks, backups. |
| Models | Model metadata and management | **Completed** | App-owned model profiles: display name, description, tags, visibility, defaults. |
| Runtime profiles | llama.cpp, Ollama, OpenAI-compatible | **Completed** | Managed llama.cpp process, Ollama `/api/tags` and `/api/chat`, OpenAI-compatible `/v1`. |
| XTTS | Voice selection/preview/clone workflows | **Completed** | Voice discovery, in-memory playback, preview, imported voice samples. |
| Quick chat | Desktop quick bar | **Partial** | In-app compact quick-chat surface, tray actions, and focused-window hotkeys are complete. System-wide hotkeys remain OS/compositor-specific. |
| Tasks/reminders | Calendar/tasks/automations | **Completed v1** | Local tasks and app-running scheduled automations with toast reminders. |
| RAG ingest | Oghma-grade corpus processing | **Completed v1** | Text/markdown ingest, title/source embedding injection, parent-child, reindex diffing, health warnings. |
| OCR/web ingest | OCR and web loaders | **Partial** | Provider config and placeholders exist; concrete local-first OCR and optional web loader execution deferred. |
| RAG retrieval | Hybrid, rerank, grounding | **Completed v1** | Wide semantic scan, full-corpus BM25, RRF fusion, ONNX cross-encoder reranker, grounding score. |
| Citations | Source chips, overflow, drawer | **Completed** | `[1] [2] [3] +N`, source inspector, copy source/path. |
| RAG traces | Retrieval diagnostics | **Completed** | Query traces persisted with chunks, scores, context, model, latency. |
| Eval harness | Oghma benchmark scripts | **Completed v1** | `gold.json`/`stress.json` loader, retrieval/full-answer eval, dashboard, JSONL/Markdown export. |
| Model benchmarks | Model rankings, leaderboards, analytics | **Completed v1** | Saved benchmark suites/runs, deterministic quality checks, speed/resource metrics, rerun/export, local rankings. |
| System overview | Admin/system dashboards | **Completed v1** | App/runtime storage, CPU/RAM, database footprint, components, and best-effort GPU/VRAM status. |
| Storage | Configurable data root, backup/restore | **Completed v1** | Data root migration, dry-run preview, conflict refusal, zip backup/restore. |
| Security | Secrets, path checks, log redaction | **Partial** | Local secret refs, user-only secret file on Unix, unsafe restore/path checks, log redaction, initial data-safety tests. OS keychain remains. |
| Performance | Stream/render stability | **Completed v1** | Model-list memoization, markdown render throttling, response timing logs. |

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

- System-wide quick-chat hotkey integration where OS/compositor APIs are reliable.
- Concrete local-first OCR and optional web loaders.
- OS credential-store integration beyond Aether's local secret reference store.
- Expanded automated tests for RAG eval, runtime validation, secret migration,
  and process argument safety.
