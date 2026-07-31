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
| Agent run/step endpoints on the local API | r1, restated r2 | Not an effort problem: there is no design for how a non-interactive caller satisfies the agent's approval gate. The gate is the product, so an endpoint that bypasses it is not a smaller version of the feature. Needs its own design pass. |
| Workflow composition and task orchestration | r1 Opportunities #9 | The stated condition for revisiting was observed sequencing pain from real `run_command` use. That evidence still does not exist. r1's own words stand: "Building an orchestrator before the primitives are proven is how projects acquire their worst code." |
| MCP HTTP and SSE transport | r2 Phase 3 | `McpClient` is stdio only. No demand observed; local servers are the use case. |
| Remaining provenance convergence | r1 | Narrower than r1 described. r2 already replaced the `__RAG_SOURCES__` sentinel strings with a typed `RagStreamEvent`; what remains is that it carries `RagTraceChunk` rather than `SourceReference`. Chat never uses that path (it builds `SourceReference` directly from packed chunks), so the divergence only affects the RAG panel's own trace view, where the richer shape is earning its keep. |
| Multi-machine sync of the data root | r1, r2 | Still no cloud, by design. User-owned file sync of the data root remains the answer and needs no code. |
| Deterministic timing for clock-dependent tests | r25 5.4, partly closed r26 5.2 | Two cases remain, both races in principle that have never been observed to fail: `MainWindowViewModelStartupTests` asserts a debounce has *not* fired at 150ms, and `McpTests` asserts a call fails within 5000ms rather than waiting out a 30s timeout. Neither has a posted work item to drain, so the r26 fix does not apply to them and neither has cost the suite a red. The third case, `ServicesViewModelTests.Removing_a_managed_server_disposes_its_view_model`, is closed: it now installs `Helpers.QueueingSynchronizationContext` and drains the posted `Rebuild` instead of waiting on a timeout. |
| Loading the Whisper ONNX graphs under test | r25 doc 03 | Every pure component (front end, decode policy, detokenizer, tensor-name pairing) is covered, but the graphs themselves are a 291 MB approval-gated download, so `WhisperDecoderSession`'s tensor plumbing (empty-cache shapes, `use_cache_branch`, cross-attention reuse) is exercised only by real use. The download and hash-verification path **is** confirmed against a real install: all five assets fetched and every SHA256 matched its pin. Creating the inference sessions and decoding are still unconfirmed. |

## Closed

| Item | Deferred by | Closed by | Evidence |
| --- | --- | --- | --- |
| Draft-model speculative decoding | r18 4.4 | r27 doc 03 | `SpeculativeDecodingConfig` (composable `Types` list, replacing the `NgramSpeculative` bool), the verified `--spec-type` / `--spec-draft-model` / `--spec-draft-n-max` / `--spec-draft-n-min` / `--spec-draft-p-min` / `-ngld` argument builder with a test asserting no removed flag name is ever emitted, `SpeculativeDecodingValidator` (path, symlink and GGUF vocabulary-size checks), `SpeedCheck.Suite()` and `SpeedCheckComparer`. Closed via MTP heads rather than a general-purpose draft-model picker: an MTP head shares its base model's vocabulary by construction, which is what dissolved r18's compatibility objection. The general case, an arbitrary small model drafting for an arbitrary large one, is only partly addressed by 3.3's validation, which refuses on a vocabulary mismatch and warns on an oversized draft but cannot prove two unrelated models will draft well together. A future round wanting the general picker starts there. |
| Settings and capabilities probe endpoint on the local API | r1 | r26 doc 05 5.1 | `GET /v1/capabilities`, `CapabilitiesResponse`; reports settings and counts, never probes |
| Benchmarks "Best Overall" column, ranked across all suites | r25 follow-up (owner request) | r26 doc 04 | `SuiteLeaderboard`, `CrossSuiteRanking`, "Best across every suite" card; ranked by mean per-suite standing, not pooled cases |
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
