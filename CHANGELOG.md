# Changelog

All notable changes to Aether will be documented in this file. Append newapp versions above the previous version (not the bottom of the doc or where ever feels right at the time).

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

## [0.9.17-alpha] - Unreleased

### Added

- Architecture and strategy review under `docs/review/` (architecture,
  dependencies, opportunities, vision, feature audit, technical debt,
  roadmap, critique, system map, Evaluation System design), plus a rewritten
  `AGENTS.md` and project skills under `.claude/skills/`.
- Provider capability model: each chat provider declares a
  `ProviderDescriptor` (tag, managed-local/local-api/remote-api kind, and
  capabilities such as streaming, usage reporting, and model pull/delete).
  `CompositeLlmService` exposes the registry and routes through it.
- Retention policies: runtime log rotation keeps the newest 10 archives per
  log file, and benchmark history keeps the newest 200 saved runs.
- Unified trace model (Context System step 1): one `TraceRecord` envelope and
  `ITraceStore` (SQLite `traces.db`, capped at 500 rows per kind) now backs
  chat, RAG, and agent traces as projections of a single schema. Chat traces
  persist across restarts (previously in-memory only), RAG traces write their
  full detail as the record payload instead of a bespoke write-only table,
  and agent steps are indexed in the shared store alongside the
  schema-validated per-task JSONL artifact.
- Shared `ContextPackBuilder` (Context System step 2): chat context snapshots,
  RAG chunk selection, and agent memory/RAG packing all use one
  budget-and-truncate algorithm in `Aether.Core` (`ContextPart` in,
  `PackedContext` out) instead of three divergent implementations.
- Memory scopes (Context System step 3): memories carry
  `Scope`/`ScopeId`/`Title` (`Global`, `Conversation`, `Workspace`) with an
  additive `memories.db` schema migration. Agent workspace notes now live in
  the shared memory store as Workspace-scoped rows via a new
  `WorkspaceMemoryStore`; legacy per-workspace `memory.json` files are
  imported once on startup and renamed to `memory.json.migrated`.
- Check/fix registry: a shared `IInspectionEngine` aggregates checks from
  `IInspectionCheckProvider` implementations, tagged by view (`doctor`,
  `trust`, `privacy`). Doctor and Trust now contribute their existing checks
  as providers instead of standalone report types with no shared shape, and
  Privacy Audit is extracted out of `SystemOverviewViewModel` into a real,
  testable `PrivacyAuditService`. New inspection areas register a provider
  instead of editing an existing service.
- Privacy Audit now derives "remote providers" from `ProviderDescriptor.IsRemote`
  and `VoiceCapability.Remote` instead of string-matching provider names
  ("OpenAI"). A remote voice provider is now flagged even when no remote chat
  provider is enabled, and adding a new remote chat provider to
  `CompositeLlmService.Providers` needs no Privacy Audit changes.
- Evaluation System step 4 (final): Benchmarks and RAG eval export now share
  one atomic write-then-rename helper (`Aether.Core.Services.AtomicFile`)
  instead of each carrying its own copy; RAG eval's report writes are now
  crash-safe (previously a plain, non-atomic write). Ranking already had a
  single implementation, so there was nothing to retire there. Fully
  deleting the two systems' own stores is deferred: nothing yet reads
  `EvalRun`s back out of `IEvalStore`, so their panels still read their own
  richer, system-specific data; see docs/review/10-evaluation-system.md.
- Evaluation System step 3: the RAG eval harness now projects each retrieval
  run onto the shared eval shape and writes it through `IEvalStore`, alongside
  its existing `run.jsonl`/`report.md` export. Retrieval metrics (Recall@K,
  MRR, citation hit, unsupported answer, refusal accuracy, grounding,
  reranker delta) become `CaseResult.Scores` entries rather than engine
  features, keeping the shared engine agnostic to how retrieval is scored.
- Evaluation System step 2: Chat's "Compare Models" now runs through the
  shared `IEvalEngine.RunQuickCompareAsync`, which executes one case against
  each selected target and returns one `EvalRun` per target, instead of a
  private streaming loop inside `ChatViewModel`. Behavior is unchanged
  (sequential execution, per-model latency/usage/error capture); the compare
  loop is no longer duplicated code.
- Evaluation System step 1 (docs/review/10-evaluation-system.md): shared
  `EvalCase`/`EvalTarget`/`EvalRun`/`CaseResult` models in `Aether.Core` and
  an additive `IEvalStore`/`SqliteEvalStore` (`eval_runs.db`, capped at the
  newest 500 runs). `BenchmarkService` now projects each saved run onto the
  shared shape and writes it through the new store alongside its existing
  `benchmarks.db` persistence; the Benchmarks UI is unchanged. This is the
  first of four steps that converge Benchmarks, Compare Models, and the RAG
  eval harness onto one engine.
- Runtime capability discovery: `LlamaCppService` probes llama-server's
  `/props` endpoint and `OllamaService` probes `/api/show` per model, caching
  the result and populating `LlmModel.ProbedContextLength`.
  `ModelProfileService.ApplyProfiles` uses the probed value as the context
  size when no explicit per-model override is saved, so chat's context-budget
  math uses a real number instead of a guessed default. The Model Management
  context-size field now shows "Detected: N" as its placeholder when a probe
  succeeded.

### Changed

- `ILlmService` contract break ahead of 1.0: sampling parameters moved into
  an `LlmChatOptions` record, the interface has a single event-based
  `StreamChatAsync` (text-only callers use the `StreamChatTextAsync`
  extension), and the unused `PullModelAsync`/`DeleteModelAsync` methods were
  removed.
- `Aether.Core` no longer references CommunityToolkit.Mvvm (it was unused);
  Core is now BCL-only and an architecture test enforces it.
- Tests standardized on `dotnet test` (the previous `dotnet run` invocation
  was a silent no-op); xunit parallelization is disabled because harness
  cases share temp data roots and SQLite pools.

### Fixed

- Pinned `SQLitePCLRaw.bundle_e_sqlite3` 3.0.0 to replace the vulnerable
  native SQLite pulled transitively by Microsoft.Data.Sqlite
  (GHSA-2m69-gcr7-jv3q); bumped Microsoft.Data.Sqlite and
  System.Numerics.Tensors to 9.0.9. Fresh restores previously failed under
  warnings-as-errors.
- Local AI asset detection now prefers the actual on-disk casing of the
  models directory instead of a guessed `Models`/`models` variant, with a
  platform-appropriate comparer; fixes wrong reranker/model paths and 24
  Windows-only test failures.
- Temp-directory cleanup in tests clears SQLite connection pools and retries,
  so test databases delete reliably on Windows.

### Tests

- Architecture tests: ViewModels must not reference Avalonia, ONNX Runtime
  stays confined to `Aether.Rag`, and `Aether.Core` may not gain package
  references.
- `SqliteMigrationRunner` contract pinned (ordering, idempotence, target
  version, scope independence).
- Provider descriptor registry semantics pinned (unique tags, remote surface,
  model-management capabilities).


## [0.9.16-alpha] - 2026-05-18

### Added

- RAG dataset manager now includes a "Delete" button on each dataset to remove
  datasets with a confirmation dialog warning. Deletion cascades to all
  associated chunks and is permanent.

### Changed

- Project version metadata bumped to `0.9.16-alpha`.
- Single-iteration benchmark runs now report run mode as `Cold` instead of
  `ColdWarm` in saved runs and exports.
- Built-in benchmark starter suites now include code generation, structured
  output stress, multi-step reasoning, Aether workflows, and hallucination
  resistance suites.
- RAG settings now discover installed reranker folders under `Models/rerank`
  and expose them through a selector.
- Embedding GGUF discovery now uses the dedicated `Models/embed` folder, so
  chat/code GGUFs no longer appear as RAG embedding choices.
- Aether Doctor now runs in the background after launch and raises a startup
  notification when errors or warnings are found.
- Managed Services now normalize duplicate default Chat/Embeddings cards and
  stop a running peer on the same port before starting another managed server.
- RAG ingest progress now separates overall progress from current-stage
  progress and labels file batches alongside embedding batches.
 - Managed llama.cpp chat and embedding defaults now use separate high localhost
  ports so both servers can run at once without colliding with common scan
  targets.
- RAG now uses a dedicated embedding base URL, so embedding requests can point
  at a separate embeddings-capable server while chat keeps using the chat
  server endpoint.
- Managed Services now show the `--embeddings` toggle only on the Embeddings
  server card, which removes the confusing checkbox from the Chat card.
- RAG settings now expose the embedding server base URL next to the embedding
  model selector.
- Benchmark view startup continues to load benchmark data before display, so
  the panel no longer opens as an empty black canvas.
  Chat and embeddings now use separate localhost ports by default so both servers can run at once.
- RAG now uses a dedicated embedding base URL instead of sharing the chat endpoint.
- The embeddings toggle only appears on the Embeddings managed server card.
- RAG settings now expose the embedding server base URL.
- Legacy localhost defaults are migrated away from 8080 and 8081 on load.   

### Fixed

- Benchmark rankings now collapse multiple runs for the same model to the best
  scoring run as intended.
- Directory RAG ingest now chunks, embeds, and stores local files in bounded
  file batches instead of holding the full corpus and all embeddings in memory
  until the end.
- RAG settings now select the installed Doctor-managed reranker when no explicit
  reranker path has been saved.
- Doctor now installs the pinned nomic embedding model under `Models/embed` and
  migrates a verified root-level copy into that folder.
- RAG ingest now starts the managed embedding server after suspending competing
  LLM services, so a shared chat/embedding port works when only one process is
  active.
- RAG ingest now retries oversized embedding inputs with smaller clamps instead
  of failing the full run on one chunk.
- Benchmark view now loads on open even if the startup path missed it, so it no longer shows a blank canvas.
- Auto-tune settings now persist correctly instead of snapping GPU layers back to 999 when starting the server.
- Delete dataset dialog warning warning no longer triggers the Avalonia XAML warning, because the dialog now has a public parameterless constructor.

### Tests

- Added regression coverage for expanded starter-suite seeding and
  single-iteration benchmark export run-mode labeling.
- Added regression coverage for persisted directory-ingest file batches after
  cancellation.
- Added regression coverage for reranker folder discovery and settings
  selection.
- Added regression coverage for dedicated embedding model discovery and Doctor
  embedding-model migration.
- Added regression coverage for Doctor startup warning notifications.
- Added regression coverage for duplicate managed service card cleanup.
- Added regression coverage for non-resetting RAG ingest progress and oversized
  embedding-input retries.
- Verified with `dotnet build` and `dotnet test` after the split-endpoint and
  UI changes.

## [0.9.15-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.15-alpha`.
- Desktop startup now initializes independent SQLite stores in parallel.
- Session usage detail view models are created per detail navigation instead of
  being reused as singleton state.
- Benchmarks now include a confirmed clear-history action for saved run
  history.
- Benchmark view model dropdown now persists selected model when navigating
  away and returning to the view instead of resetting to the first entry.
- Benchmark iterations reduced from 3 runs per test case to 1 run for faster
  feedback.
- Benchmark rankings now group saved runs by model so the list shows the best
  run per model instead of a flat duplicate run history, and the tabular view
  includes the number of runs behind each ranking.
- Benchmark Rankings display restructured with column headers (Model, Runs,
  Score, Pass Rate, Speed) for clarity, and split into two tabs: Rankings
  (best-per-model tabular view) and All Results (full saved run list).
- Chat conversation management now provides dual access: existing three-dots
  flyout menu plus new right-click context menu on conversation list items.

### Added

- Benchmark test info modal dialog displaying test case details including
  prompt, expected results (keywords, regex patterns, refusal requirement),
  and system prompt with selectable/copyable text.
- Benchmark results view now shows info button (ℹ) next to each test case to
  open the test details modal, and benchmark ranking rows expose the same test
  details action for the best run in each model group.
- Benchmark exports now support exporting all saved benchmark runs into one
  timestamped folder containing per-run exports plus a generated index.
  - Added option to create a single zip archive of all exported runs.
  - The export UI offers both per-run folder export and a single ZIP export.
  - Replaced the lightweight toast run-info with a full run-info dialog accessible
    from the run list and rankings. The dialog displays run summary, metrics,
    and per-result summaries and provides an export action.
  - Added ranking timeframe filters: `All`, `Latest per model`, and `Last N runs`.
    Use the new UI controls on the Rankings tab to switch modes and set `N`.
- Chat conversation context menu with options: Pin, Archive, Export Markdown,
  Export JSON, and Delete.

### Fixed

- Chat input now honors the `Ctrl+Enter to send` setting.
- Chat and RAG views now unsubscribe old scroll handlers when their data
  context changes.
- Markdown rendering now parses off the UI thread, avoids heavyweight editors
  for short code blocks, and handles malformed ordered-list starts safely.
- Crash logs now write under the app base directory instead of the launch
  working directory.
- Tray setup now survives missing tray assets and responds to tray setting
  changes at runtime.
- Logs folder opening now handles missing shell openers without crashing.
- Draft patch preview now uses the injected patch diff service.
- Restore backup confirmation now uses a dedicated AXAML dialog instead of an
  inline C# control tree.

### Tests

- Added regression coverage for clearing saved benchmark run history without
  removing benchmark suites.

## [0.9.14-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.14-alpha`.
- Benchmarks now discover local GGUF models from the AI assets root, run all
  built-in suites by default, and auto-switch the managed chat `llama-server`
  when a discovered model is selected.
- Services auto-tune now probes descending GPU layer candidates and keeps the
  highest candidate that reaches `/health`, with CPU fallback.
- Successful Services auto-tune results are now saved as per-GGUF profiles and
  automatically reapplied before managed server start.
- Doctor now checks the configured `llama-server` version and compares it with
  the latest `llama.cpp` release when GitHub releases are reachable.
- Doctor now warns for untuned local GGUF models, offers a `llama.cpp` update
  install action, and verifies pinned `nomic-embed-text-v1.5` files by hash.
- Doctor embedding model install now downloads the default
  `nomic-embed-text-v1.5` GGUF from a pinned Hugging Face commit and verifies
  its SHA256 before configuring RAG.
- Doctor embedding install now updates the embedding server model path to the
  downloaded model, replacing stale or wrong paths instead of only filling an
  empty value.

### Fixed

- Failed embedding model hash verification now removes the downloaded file and
  reports the verification failure through Doctor progress.
- Chat readback and voice preview now skip empty text instead of sending a
  blank request to Kokoro.

### Tests

- Added regression coverage for successful verified Doctor embedding installs,
  stale embedding server path replacement, and hash mismatch rejection.
- Added regression coverage for GGUF discovery, auto-tune candidate planning,
  GPU-layer log parsing, and llama.cpp build parsing.
- Added regression coverage for Doctor untuned-GGUF reporting and current
  platform llama-server asset selection.

## [0.9.13-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.13-alpha`.
- Agent task-index initialization now reconciles existing `task_state.json`
  files on each first initialization per process, so JSON state remains the
  source of truth if a previous save wrote JSON before the SQLite index update.
- Data-root migration now moves Agent state under `agent/`, including
  `task_index.db`, task JSON, logs, traces, and workspace memory files.

### Fixed

- Backup restore containment checks now use case-sensitive path comparison on
  Linux and macOS while preserving case-insensitive checks on Windows, blocking
  case-variant sibling directory escapes.

### Tests

- Added regression coverage for case-sensitive backup restore containment,
  Agent state data-root migration, and Agent task-index reconciliation from
  JSON source files.

## [0.9.12-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.12-alpha`.
- Conversation, memory, and RAG SQLite stores now maintain an
  `aether_schema_versions` table and run additive schema changes through a
  versioned migration runner instead of ad hoc initialization-only column
  checks.
- Agent task listing and review queue data now use a SQLite-backed
  `agent/task_index.db` catalog with indexed status and update-time columns,
  while `task_state.json` remains the durable per-task source of truth.
- Existing Agent task JSON files are backfilled into the task index on first
  initialization when the index is empty.

### Tests

- Added coverage that Agent recent-task and review-queue lists are served from
  the SQLite index and that the Agent index records its schema version.

## [0.9.11-alpha] - 2026-05-18

### Changed

- Project version metadata bumped to `0.9.11-alpha`.
- Local fallback secrets now derive encryption keys with a fresh random salt per
  stored ciphertext while still reading older fallback vault entries.
- RAG query caching now uses a tighter byte ceiling and refuses to retain a
  single oversized dataset cache entry.
- Conversation auto-summary now checks recent summary memories through a
  targeted conversation query instead of loading a broad recent-memory page.
- Benchmark starter suites are seeded once per process and data root.
- Ollama HTTP access now uses a shared client owned by the service type instead
  of creating a disposable client per service instance.

### Fixed

- Agent workspace path checks now use case-sensitive comparisons on Linux and
  macOS while preserving case-insensitive checks on Windows.
- Agent task IDs now use an explicit safe-character allowlist, reject path
  separators, reject Windows reserved device names, and enforce a length cap.
- Generated XTTS API scripts now escape configured paths as normal Python
  string literals, preventing quote or newline injection into the script body.
- Memory FTS tables now rebuild only when the FTS table is first created,
  avoiding a cold-start full reindex on every process start.
- Model management cache refreshes are now lock-protected so overlapping UI or
  background refreshes cannot race the cache list.
- Per-conversation memory override entries that merely duplicate the global
  setting are pruned during settings saves, and the remaining override map is
  capped.
- Voice provider installation checks now use the `IVoiceProvider` contract
  instead of concrete Kokoro/XTTS casts.
- Main-window folder filter refresh now restores its reload guard with
  `finally`, so exceptions cannot leave filter changes suppressed.
- RAG ingest service restoration is awaited and logged if restoration fails
  after an ingest attempt.
- Runtime log redaction now covers AWS-style access keys, Azure-style key
  assignments, and password query or assignment parameters.
- Runtime log archive collision handling now has a bounded suffix search with a
  GUID fallback.

### Tests

- Added coverage for salted local fallback secret ciphertexts, escaped XTTS
  script paths, stronger Agent task ID validation, settings memory override
  pruning, and expanded log redaction patterns.

## [0.9.10-alpha] - 2026-05-17

### Changed

- Project version metadata bumped to `0.9.10-alpha`.
- Settings, local fallback secrets, toast history, Agent JSON state,
  conversation exports, benchmark exports, and generated XTTS scripts now use
  atomic file replacement to reduce corruption risk during interrupted writes.
- Unreadable `settings.json` files are copied aside with a `.corrupt-*` suffix
  before defaults are loaded.
- Voice provider child processes are now killed on cancellation, and diagnostic
  command previews quote embedded quotes safely.

### Fixed

- OpenAI voice synthesis now resolves saved `secret:` API key references before
  sending requests to the configured endpoint.
- `llama-server` PATH lookup now ignores empty PATH segments so the current
  working directory is not treated as an implicit install location.
- Agent task state storage now rejects unsafe task IDs such as `..` instead of
  resolving them into parent directories.

## [0.9.9-alpha] - 2026-05-17

### Changed

- Runtime logs now redact persisted entries before they reach disk as well as UI log views.
- Conversation list and memory timestamps now store UTC values and render local
- Local AI setup now kills child processes when setup is cancelled and quotes
  command previews more safely.
- RAG metadata parsing now writes warning log entries when source or trace
  markers cannot be parsed.
- Toast history load/save failures now produce runtime warnings instead of
  disappearing silently.

### Fixed

- Backup restore path checks now reject path-prefix escapes and directory
  entries before extraction.
- Agent workspace tools now reject symlink ancestors and skip symlinked
  directories or files during local inspection.
- Workspace instruction discovery now uses path-boundary checks when deciding
  whether files are inside the selected root.
- OpenAI-compatible, llama.cpp, and Ollama streaming now tolerate malformed
  JSON event fragments without aborting the whole response.
- Model downloads now preserve the existing destination file until the completed
  temp file successfully replaces it.
- Runtime log rotation now avoids deleting an existing archive when two
  rotations happen in the same second.
- Trust & Safety now detects host override flags written as `--host=value` or
  equivalent `listen-host` forms.
- One-way Avalonia value converters now return unset values for back conversion
  instead of throwing in binding paths.
- Startup voice-provider probing now logs failures instead of swallowing them.
- Redaction now covers GitHub-style tokens and query-string secret parameters.

## [0.9.8-alpha] - 2026-05-17

### Changed

- Toast popups now use an opaque background so underlying page text does not
  bleed through them.
- Local AI setup scans are now voice-provider aware: Kokoro no longer triggers
  XTTS script or XTTS model actions, and package install plans are only shown
  when the selected provider's imports are missing.
- The Kokoro voice list now includes the full current English voice set from
  Kokoro-82M instead of the earlier small sample list.

### Fixed

- Doctor now labels the Python check with the selected voice provider's actual
  required Python version and no longer renders duplicate diagnostics inline.
- Agent summary counters now render in separate columns instead of overlapping
  in the first card.
- Local AI setup now detects a direct `llama-server` binary path as installed.
- Project version metadata bumped to `0.9.8-alpha`.


