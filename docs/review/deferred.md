# Deferred items

Every item a review round deferred rather than rejected, with the round that
deferred it, the reason, and its current status.

Created in r25 (doc 05 5.2). Before this file existed, that history lived
scattered across ten roadmap documents in `docs/review/archived/` and was only
findable by grepping for the word "deferred", which is how an item goes missing
for twenty rounds without anyone actually deciding to drop it.

**Deferred is not rejected.** A round's explicit rejection is a decision with
reasons, not work waiting in the feature backlog. This file separates selected
round work, parked feature work, operational validation watches, and concise
rejection tombstones so those categories cannot be misread as one product
queue. Every round's close-out updates the relevant row.

## Selected for R32

| Item | Deferred by | R32 disposition |
| --- | --- | --- |
| Whole-active-workload GPU Fit | R31 final dogfood closure | Selected as the broader whole-workload resource inventory, planning, reservation, admission, and evidence contract. This includes CPU, RAM, VRAM by device, owned and external consumers, in-process ONNX work, and explicit Unknowns rather than only GPU Fit. See `docs/review/r32/01-whole-workload-resource-intelligence.md`. |
| Hugging Face model artwork | R31 final dogfood closure | Selected for repository/model download cards with publisher-controlled `thumbnail` metadata, a strict Hugging Face-only redirect and content policy, bounded cache, offline fallback, and no secret-bearing requests. See `docs/review/r32/05-hugging-face-artwork.md`. |
| Context checkpoints and cache RAM | r30 Batch #3 | Selected as separate capability-gated, resource-accounted, measured investigations beside adaptive inference. Each control ships only if current runtime support, correctness, and reproducible single-user benefit are established. See `docs/review/r32/02-adaptive-local-inference.md`. |
| Multi-device placement | r30 Batch #3 | Selected for the consumer/allocation/per-device representation and capability gates. Execution/UI ships only with suitable hardware evidence; otherwise it remains an explicit Unknown and the unverified remainder stays parked. Experimental tensor split is never a default. See `docs/review/r32/01-whole-workload-resource-intelligence.md` and `02-adaptive-local-inference.md`. |
| Automated contradiction resolution and temporal knowledge engine | r30 Batch #4 | Narrowed and selected as evidence-grounded assertion/source revisions, explicit human review, current/as-of/history retrieval, exact revision citations, and atomic publication. Automatic truth selection, newer-means-true behavior, and model-authored resolution remain rejected. See `docs/review/r32/04-temporal-knowledge-evolution.md`. |
| Same-repository CI run de-duplication | R32 reconciliation | Selected as a bounded workflow fix: distinct non-required pre-PR branch checks, required PR merge-context checks, main/fork coverage, and authority-scoped superseded-run cancellation. No repository-setting change. See `docs/review/r32/09-adversarial-reconciliation.md`. |

## Parked feature work

| Item | Deferred by | Revisit gate |
| --- | --- | --- |
| Agent run/step endpoints on the local API | r1, restated r2; contracts landed r31 | Desktop and the separate Local API process still need one serialized per-task mutation owner. Atomic task files do not resolve run, steering, cancellation, or approval races. This is a distinct authority/lifecycle project, not an R32 resource or recommendation add-on. |
| MCP HTTP and SSE transport | r2 Phase 3 | Revisit when a concrete non-stdio server or interoperability requirement exists. Local stdio servers remain the demonstrated use case. |
| llama.cpp backend sampling and internal performance instrumentation | r30 Batch #3 | Keep out of normal launches until a selected runtime exposes stable machine-readable diagnostics and an isolated Speed Check proves the value. R32 may consume ordinary observed resource and request metrics, but that does not silently select this item. |
| Knowledge-graph expansion and multi-hop retrieval | r30 Batch #4 | Revisit only for a demonstrated retrieval or inspection failure that bounded temporal revisions and one-hop evidence relationships cannot solve. GraphRAG, arbitrary multi-hop traversal, graph UI, AST mapping, and a universal workspace graph remain out. |
| Automatic model/profile selection and workload routing | r30 Batch #4 | R32 recommendations stay explicit and reviewable. Revisit automatic routing only with task-specific quality evidence, an understandable decision contract, rollback, and a demonstrated workflow need. |
| TLB behavioural evidence interchange | r30 Batch #4 | Revisit when both projects need a versioned experiment-summary interchange. Do not reference TLB assemblies or import simulation internals. |

## Operational and validation watch

These are evidence or environment gates, not open product features. Close them
when the named observation is obtained, or promote a reproducible defect into a
round with its own scope.

| Item | Origin | Current watch |
| --- | --- | --- |
| Deterministic timing for clock-dependent tests | r25 5.4, partly closed r26 5.2 | Two tests still use real time: `MainWindowViewModelStartupTests` checks that a debounce has not fired at 150 ms, and `McpTests` checks failure within 5000 ms. Add a seam when one becomes flaky or its touched area already needs time control; do not create an R32 clock project without evidence. |
| Windows CI test time: Defender exclusion retained only for trusted pushes | r29 4.1, tightened r30 Batch #4 | Preserve the narrow trusted-push exclusion and its security boundary. Re-measure only when CI behavior changes; do not rewrite product tests around runner scanning. |
| Loading the Whisper ONNX graphs under test | r25 doc 03 | Pure plumbing and the pinned 291 MB install path are covered, but real session creation and decoding remain an owner live/download gate. R32 resource adapters must report this state honestly and must not claim graph execution coverage. |
| COSMIC folder-picker or portal observation | R31 final dogfood closure | Mounted drives disappearing was not reproduced as a Hermaeus defect. Await reproducible portal/file-picker evidence; keep it separate from R32 popup or resource work. |

## Rejected

These are explicit product-direction decisions, not work waiting for a later
round. Reconsideration requires a new owner-authorized premise and threat/
evidence review.

| Item | Rejected by | Rationale |
| --- | --- | --- |
| Continuous local fine-tuning and adapters | R32 planning audit, continuing R31 direction | Experience is evidence for explicit recommendations, not permission to train. Any future training proposal needs separately authorized provenance, consent, licensing, poisoning, storage, evaluation, privacy, and rollback design. |

## Closed

| Item | Deferred by | Closed by | Evidence |
| --- | --- | --- | --- |
| General external draft-model workflow | r27 doc 03, narrowed r30 Batch #3 | R32 planning audit | R31 implemented the bounded external/EAGLE workflow, compatibility checks, target binding, tuning, counters, prediction/observation, and baseline comparison. Missing pairs and reduced-vocabulary uncertainty are capability/live-validation outcomes, not an unimplemented feature. Keep actual engagement and benefit in the owner live matrix. |
| Nav-icon cursor flicker: mechanism still unconfirmed | r29 1.5 | r29 close-out | Mechanism measured, not theorised, and it was none of the candidates this row listed. The gaps between icon buttons belong to no button (the notches where two rounded corners meet, plus container `Spacing` or `ColumnSpacing`), and a container with no `Background` is not a hit-test result, so the pointer fell through to the window's root panel, whose cursor is the default arrow. `TopLevel` takes the cursor from the hit element without walking up to ancestors, so crossing a row flickered hand/arrow/hand a few pixels before each boundary. A pointer-event log during the flicker showed the pointer-over chain collapsing to the root panel and rebuilding about 80 times a second: 953 enter/exit pairs on one inactive nav button against 7 on the active one. The experiment this row prescribed (remove `ToolTip.Tip` from one button) gave a false positive and sent two further rounds after tooltips; `ToolTip.ServiceEnabled=False` app-wide later proved the flicker persists with the tooltip service entirely off. The fix is a transparent, hit-testable background plus `Cursor="Hand"` on icon-button containers, on the container itself and never on a sibling laid over the buttons: `Desktop/Controls/IconBarCursor.cs` for all-button containers, xaml for the nav rail, chat toolbar and conversation rows. Guarded by `The_icon_bar_cursor_fix_stays_installed` and `Every_hover_content_presenter_style_has_a_base_state_pair`. |
| Draft-model speculative decoding | r18 4.4 | r27 doc 03 | `SpeculativeDecodingConfig` (composable `Types` list, replacing the `NgramSpeculative` bool), the verified `--spec-type` / `--spec-draft-model` / `--spec-draft-n-max` / `--spec-draft-n-min` / `--spec-draft-p-min` / `-ngld` argument builder with a test asserting no removed flag name is ever emitted, `SpeculativeDecodingValidator` (path, symlink and GGUF vocabulary-size checks), `SpeedCheck.Suite()` and `SpeedCheckComparer`. Closed via MTP heads rather than a general-purpose draft-model picker: an MTP head shares its base model's vocabulary by construction, which is what dissolved r18's compatibility objection. The general case, an arbitrary small model drafting for an arbitrary large one, is only partly addressed by 3.3's validation, which refuses on a vocabulary mismatch and warns on an oversized draft but cannot prove two unrelated models will draft well together. A future round wanting the general picker starts there. |
| Workflow composition and task orchestration | r1 Opportunities #9 | r15 and r16 | r1 deferred multi-step workflows, DAGs, pipelines, and task orchestration until approval-gated execution had real use. r15 implemented the bounded form the original item actually named: `plan_subtasks` creates 2-6 approved child tasks with fixed specialist profiles, sequential execution, inherited workspace context, per-child transcripts and approvals, an orchestration budget, and parent synthesis. r16 hardened depth limits, review-queue routing, self-healing terminal children, and duplicate-plan refusal. This closes task orchestration, not arbitrary reusable workflow composition or opaque routing; those remain future work only where separately recorded. |
| Remaining provenance convergence | r1 | r30 add-on | Re-audited the only remaining distinction: `RagStreamEvent` carries `RagTraceChunk` for the RAG panel's retrieval/selection trace, whose rank and score fields are its job; Chat builds `SourceReference` directly from its packed chunks for citations. There is no shared consumer with a broken or duplicate contract, so convergence would discard useful RAG trace data rather than repair a defect. |
| Multi-machine sync of the data root | r1, r2 | r30 add-on | Closed by design. Hermaeus has no cloud sync service; the data root is user-owned and portable, so user-chosen filesystem sync remains the supported multi-machine mechanism. No background synchronizer, account, conflict policy, or new endpoint is warranted. |
| Agent transcript compaction and successful-loop diagnostics | r30 TLB 1-2 | r30 add-on | `AgentTranscriptCompactor` leaves `transcript.jsonl` intact and compacts only model-facing replay groups that are consecutive, tool-identical, canonically argument-identical, result-identical, and replay-safe from existing source, timeout, and exit evidence. It preserves the first outcome plus count and step/entry range, never compacts old entries without provenance or failures, timeouts, denials, changed arguments, changed results, or separated calls. Three or more repeated outcomes appear as an informational context-receipt diagnostic; no tool is blocked and no task status or loop budget changes. |
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
| Coverage gaps named in r29 doc 04 4.6 | r29 4.6; selected for r31 remeasurement | R31 close-out | The targeted changed/error paths were remeasured and the canonical coverage ratchet passed at the configured 60% floor. See `docs/review/archived/r31/r31-final-dogfood-closure.md`. |
| Audio feedback and mute/accessibility policy | r30 G2 | R31 close-out | The bounded event policy, visual equivalents, volume/mute behavior, TTS arbitration, and argument-only playback path are implemented and passed the bounded Windows and Pop!_OS/COSMIC owner validation gates. |
| Normalized model-facing tool outcome vocabulary | r30 TLB 3, Batch #3 | R31 close-out | Deterministic normalized outcomes and distinct evidence origins are persisted beside raw evidence. See `docs/review/archived/r31/01-evidence-and-experience.md`. |
| Broader structured empirical experience learning | r30 TLB 4, narrowed r30 Batch #4 | R31 close-out | The bounded `experience.db` store, inspection, correction, removal, redacted export, and fingerprint-aware evidence flow are implemented without entering safety or authority decisions. See `docs/review/archived/r31/01-evidence-and-experience.md`. |
| Empirical engine-profile optimisation | r30 add-on, Batch #3, narrowed r30 Batch #4 | R31 close-out | Lab runs bounded isolated configurations, preserves prediction/observation/correctness separately, and requires stale-guarded Apply review through normal settings. See `docs/review/archived/r31/03-lab.md`. |
| Prompt reuse and shared-prefix prefill measurement | r30 Batch #3 | R31 close-out | The bounded controlled timing effect is implemented with hash-only prompt identities and exact output comparison. Direct reused-token counters and optional build-scoped log parsing remain explicitly deferred. See `docs/review/archived/r31/03-lab.md`. |
| Per-specialist or per-subtask model selection | r30 add-on | R31 close-out | Explicit model identity is persisted through approved subtasks, transcripts, receipts, reports, UI, and synthesis; unavailable selections pause without fallback. See `docs/review/archived/r31/04-project-state-agent-and-api.md`. |
