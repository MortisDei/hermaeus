# Aether Open WebUI Parity Matrix

This matrix tracks Aether against Open WebUI v0.9.x while preserving Aether's
native desktop, local-first direction.

## Status

| Area | Open WebUI / Oghma Reference | Aether Status | Notes |
| --- | --- | --- | --- |
| Native desktop | Open WebUI desktop app | **Completed** | Native Avalonia shell, no WebView markdown, Linux/Windows target. |
| Chat history | Folders, tags, pins, archive, search | **Completed** | Rename/delete/search/folder/tag/pin/archive plus compact details flyout. |
| Message scale | Native browser virtualization | **Completed** | Virtualized transcript plus throttled markdown rendering during streams. |
| Notifications | Toasts/reminders | **Completed** | Toast service used by chat, services, RAG, XTTS, eval, tasks, backups. |
| Models | Model metadata and management | **Completed** | App-owned model profiles: display name, description, tags, visibility, defaults. |
| Runtime profiles | llama.cpp, Ollama, OpenAI-compatible | **Completed** | Managed llama.cpp process, Ollama `/api/tags` and `/api/chat`, OpenAI-compatible `/v1`. |
| XTTS | Voice selection/preview/clone workflows | **Completed** | Voice discovery, in-memory playback, preview, imported voice samples. |
| Quick chat | Desktop quick bar | **Partial** | In-app compact quick-chat surface; global hotkey/tray are deferred. |
| Tasks/reminders | Calendar/tasks/automations | **Completed v1** | Local tasks and app-running scheduled automations with toast reminders. |
| RAG ingest | Oghma-grade corpus processing | **Completed v1** | Text/markdown ingest, title/source embedding injection, parent-child, reindex diffing, health warnings. |
| OCR/web ingest | PaddleOCR/Firecrawl loaders | **Partial** | Provider config and placeholders exist; concrete OCR/Firecrawl execution deferred. |
| RAG retrieval | Hybrid, rerank, grounding | **Completed v1** | Wide semantic scan, full-corpus BM25, RRF fusion, no-op reranker slot, grounding score. |
| Citations | Source chips, overflow, drawer | **Completed** | `[1] [2] [3] +N`, source inspector, copy source/path. |
| RAG traces | Retrieval diagnostics | **Completed** | Query traces persisted with chunks, scores, context, model, latency. |
| Eval harness | Oghma benchmark scripts | **Completed v1** | `gold.json`/`stress.json` loader, retrieval/full-answer eval, dashboard, JSONL/Markdown export. |
| Storage | Configurable data root, backup/restore | **Completed v1** | Data root migration, dry-run preview, conflict refusal, zip backup/restore. |
| Security | Secrets, path checks, log redaction | **Partial** | Local secret refs, user-only secret file on Unix, unsafe restore/path checks, log redaction. OS keychain and broader automated tests remain. |
| Performance | Stream/render stability | **Completed v1** | Model-list memoization, markdown render throttling, response timing logs. |

## Better Than Open WebUI For This App

- Native Avalonia desktop app instead of a web UI wrapped for desktop.
- Direct local `llama-server` management with GPU-layer auto-tune support.
- Oghma-style RAG retrieval: title/source embedding injection, full-corpus BM25,
  wide semantic candidates, and parent-child context.
- XTTS integration with memory-only generated audio playback.
- Local-first data root migration and explicit backup/restore.

## Deferred

- System-wide quick-chat hotkey and tray integration.
- Concrete OCR/PaddleOCR-vl and Firecrawl v2 loaders.
- Local cross-encoder reranker implementation.
- OS credential-store integration beyond Aether's local secret reference store.
- Full automated regression test project for migration/security/RAG eval.
