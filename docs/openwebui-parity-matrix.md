# Aether Best-in-Class Parity Matrix

This is the working map for beating Open WebUI feature-by-feature while keeping
Aether native, local-first, and faster for desktop use.

## Baseline

| Area | Open WebUI / Oghma Reference | Aether Status | Next Build Slice |
| --- | --- | --- | --- |
| Native desktop | Open WebUI now has a desktop app | Native Avalonia app exists | Polish shell, command palette, tray/quick chat |
| Chat history | Open WebUI folders/search/tags | Rename/delete/search basics | Folders, tags, pinned chats, archived chats |
| Streaming performance | Open WebUI batches rendering and avoids expensive markdown churn | Token batching exists | Message virtualization and incremental markdown diffing |
| Notifications | Open WebUI toasts/calendar reminders | Native toast service started | Route service/RAG/TTS/eval events through toasts |
| Models | Open WebUI model management | llama.cpp/OpenAI-compatible model list | Per-model defaults, avatars, tags, edit-from-selector |
| Local services | Oghma assumes Ollama; Aether manages llama-server | Direct llama-server process manager | Multi-runtime profiles: llama.cpp, Ollama, OpenAI-compatible |
| XTTS | Syrinx has speaker discovery and cloning workflows | Voice discovery + readback started | Voice preview, clone import, CPU/GPU service presets |
| RAG ingest | Oghma title-injects embeddings, BM25 stats, parent-child options | Title injection, BM25, parent upgrade | Source inspector, reindex diffing, corpus health checks |
| RAG retrieval | Oghma uses wide candidates, hybrid scoring, optional rerank/grounding | Wider hybrid retrieval | Reranker, grounding verification, feedback logging |
| Citations | Open WebUI source buttons and overflow indicator | RAG emits source metadata internally | Inline citations, source drawer, +N overflow indicator |
| Eval harness | Oghma has benchmark/eval scripts | Not app-native yet | Golden-set runner, latency/quality dashboard |
| Security | Open WebUI hardens routes/assets/access | Basic local-only host binding | Data path validation, secret storage, process arg audit |
| Storage | Open WebUI server DB; Aether local SQLite | Alternate data root + migration | Backup/restore, encrypted secrets, cleanup tools |

## Borrow From Oghma Infinium

- Title/source injection before embedding.
- Broad semantic candidates before final fusion.
- Full-corpus lexical scoring, not lexical scoring only over vector hits.
- Parent-child context expansion.
- Grounding checks and conservative "not enough context" behavior.
- Query/eval logging for retrieval tuning.
- Corpus health and index health checks.

## Borrow From Syrinx / Apocrypha

- XTTS service launch profiles.
- Speaker discovery endpoints: `studio_speakers`, `speakers`, `speaker_ids`, `/voices`.
- Voice clone registration and preview workflow.
- CPU/GPU device selection surfaced clearly in service settings.

## Immediate Priorities

1. Source inspector for RAG answers with citation chips.
2. Chat folders/tags/pins plus upgraded search filters.
3. Message virtualization for very long chats.
4. Eval harness using Oghma-style gold/stress question sets.
5. Security review checklist converted into automated tests where possible.
