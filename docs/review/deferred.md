# Deferred items

Every item a review round deferred rather than rejected, with the round that
deferred it, the reason, and its current status.

Created in r25 (doc 05 5.2). Before this file existed, that history lived
scattered across ten roadmap documents in `docs/review/archived/` and was only
findable by grepping for the word "deferred", which is how an item goes missing
for twenty rounds without anyone actually deciding to drop it.

**Deferred is not rejected.** A round's "Explicit rejections" section is a
decision with reasons and belongs in that round's roadmap. This file is for work
that was agreed to be worth doing and then postponed. Every round's close-out
updates the status column.

## Open

| Item | Deferred by | Why it is still open |
| --- | --- | --- |
| Draft-model speculative decoding | r18 4.4 | Real speedup, but it needs a second model file, a second VRAM budget, and a draft-model picker whose wrong answer silently costs performance instead of failing visibly. That is a doc of its own. **Strongest current candidate.** |
| Agent run/step endpoints on the local API | r1, restated r2 | Not an effort problem: there is no design for how a non-interactive caller satisfies the agent's approval gate. The gate is the product, so an endpoint that bypasses it is not a smaller version of the feature. Needs its own design pass. |
| Settings and capabilities probe endpoint on the local API | r1 | Small and still open. `LocalApiEndpoints.cs` serves `/health`, `/v1/chat/completions`, `/v1/memory/query`, `/v1/rag/query`, `/v1/models` and `/v1/embeddings`, and nothing that reports what the instance can do. Good filler, too small to lead a round. |
| Workflow composition and task orchestration | r1 Opportunities #9 | The stated condition for revisiting was observed sequencing pain from real `run_command` use. That evidence still does not exist. r1's own words stand: "Building an orchestrator before the primitives are proven is how projects acquire their worst code." |
| MCP HTTP and SSE transport | r2 Phase 3 | `McpClient` is stdio only. No demand observed; local servers are the use case. |
| Remaining provenance convergence | r1 | Narrower than r1 described. r2 already replaced the `__RAG_SOURCES__` sentinel strings with a typed `RagStreamEvent`; what remains is that it carries `RagTraceChunk` rather than `SourceReference`. Chat never uses that path (it builds `SourceReference` directly from packed chunks), so the divergence only affects the RAG panel's own trace view, where the richer shape is earning its keep. |
| Multi-machine sync of the data root | r1, r2 | Still no cloud, by design. User-owned file sync of the data root remains the answer and needs no code. |
| Deterministic timing for clock-dependent tests | r25 5.4 | Three cases. `MainWindowViewModelStartupTests` asserts a debounce has *not* fired at 150ms and `McpTests` asserts a call fails within 5000ms rather than waiting out a 30s timeout; both are races in principle and neither has been observed to fail. `ServicesViewModelTests.Removing_a_managed_server_disposes_its_view_model` **has** been observed to fail intermittently under full-suite load: it waits on a `Rebuild` that `SettingsChanged` posts through `RunOnUi`, which is fire-and-forget by design in production. Consolidating the wait helpers made this visible rather than causing it, because two of the old copies returned silently on timeout. The real fix is to drain the posted work deterministically (`Helpers.QueueingSynchronizationContext.DrainAll` exists for exactly this, from r12) rather than to widen a timeout. |
| Loading the Whisper ONNX graphs under test | r25 doc 03 | Every pure component (front end, decode policy, detokenizer, tensor-name pairing) is covered, but the graphs themselves are a 291 MB approval-gated download, so `WhisperDecoderSession`'s tensor plumbing (empty-cache shapes, `use_cache_branch`, cross-attention reuse) is exercised only by real use. The download and hash-verification path **is** confirmed against a real install: all five assets fetched and every SHA256 matched its pin. Creating the inference sessions and decoding are still unconfirmed. |

## Closed

| Item | Deferred by | Closed by | Evidence |
| --- | --- | --- | --- |
| Conversation branching and message-edit forks | r24 | r25 doc 01 | `Message.ParentId`, `ConversationTree`, non-destructive regenerate |
| In-process Whisper | r24 (rejected as too large beside three other features) | r25 doc 03 | `WhisperOnnxModel`, `LogMelSpectrogram`, `WhisperGreedyDecoder` |
| Per-app tokens for the local API | r1 | r2 | `LocalApiSettings.Tokens` |
| Embeddings endpoint | r1 | r2 | `POST /v1/embeddings` |
| Structured source reference on memories | r1 | later round | `MemoryStore` writes a `SourceReference`; round-trip and backfill both tested |
| Chat consuming RAG and memory citations | r1 | later round, presentation rebuilt in r25 doc 02 | `ChatContextReceipt` |
| Per-feature model-usage counters | r5 | r6 | `UsageInsight` |
| Task-terminal lesson capture | r3 | r4 | `AgentLessonText` goal fingerprinting |
| Recent-tasks list | r15 (data layer only) | r16 | Recent-tasks UI in the Agent panel |
| N-gram speculative decoding | r18 4.4 | shipped | `ServerConfig.NgramSpeculative` |
