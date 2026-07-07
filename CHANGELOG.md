# Changelog

All notable changes to Aether will be documented in this file. Append newapp versions above the previous version (not the bottom of the doc or where ever feels right at the time).

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

## [Unreleased]

## [0.9.21-alpha] - 2026-07-07

This release closes out the v2.0 architecture-review roadmap
(docs/review/07-roadmap.md, "composition becomes the product"): workspace
activation, approval-gated agent command execution, an MCP tool surface, a
headless local API, and a native (opt-in) Kokoro-ONNX voice path all landed
in this pass. The one deliberately open thread is flipping the native voice
default, tracked as future work pending real-world parity testing. Remaining
work moves to the 3.0-horizon roadmap items for a future 0.9.30 pass.

### Added
- Workspace as the organizing concept (2.0 roadmap item 1, first slice): an in-repo `.aether/workspace.json` manifest records a workspace's preferred model, embedding model, linked RAG dataset, and instruction file list. `IWorkspaceActivationService` reads it (falling back to the existing app-side `WorkspaceProfile` when no manifest exists) and the Agent panel now activates the matching model/dataset automatically after "Explain Workspace" runs, instead of leaving them as stale independent selections. A new "Save as Workspace Defaults" button writes the current selection back to the manifest so it ships with the repo. Chat also gained an optional `ActiveWorkspaceRoot`/`ActivateWorkspaceCommand` so the same manifest can activate a preferred model outside the Agent panel; both are opt-in and no-op unless a workspace root is set.
- Agent: approval-gated command execution (2.0 roadmap item 2). The agent can now request `run_command`, but only for a recipe the workspace itself already declared safe (`dotnet build`, `dotnet test`, `npm test`, `cargo test`, `pytest`) via the same `.aether/workspace.json` manifest from item 1 (`WorkspaceManifest.AllowedCommands`, populated from "Explain Workspace"'s existing command-recipe detection). A command must match both a fixed, hardcoded allowlist (so a hand-edited manifest can never smuggle in an arbitrary shell command) and the workspace's own declared list; the model can only select a recipe by name, never construct a command line. Execution always requires human approval through the existing review queue, runs with a 5-minute hard timeout, and is confined to the workspace root, matching every other agent action's audit trail.
- MCP client as the tool surface (2.0 roadmap item 3): a new `Aether.Mcp` project speaks JSON-RPC 2.0 over the stdio of locally configured MCP servers (spawn, initialize handshake, `tools/list`, `tools/call`; stdio transport only, no remote HTTP/SSE servers in this pass). Servers are configured under a new Settings section (name, command, arguments, working directory, enabled) backed by `AppSettings.Mcp.Servers`. Each discovered tool is exposed to the agent as `mcp:{serverId}:{toolName}` through the existing `IAgentToolExecutor`/`IAgentSafetyGate` seam via a thin `IMcpToolBridge` interface in `Aether.Core`, so `Aether.Agent` keeps depending only on Core, never on `Aether.Mcp` directly (enforced by a new architecture test, mirroring the existing Rag/ONNX confinement rules). Every `mcp:` tool call always requires approval, regardless of what the server itself claims about the tool's safety.
- Headless core / local API (2.0 roadmap item 4): the non-UI half of the desktop app's DI graph was extracted into a new `Aether.Composition` project (`AetherServiceRegistration.AddAetherCoreServices`), shared by the desktop app and a new `Aether.LocalApi` host. `Aether.LocalApi` is an optional, off-by-default loopback-only (`127.0.0.1`) ASP.NET Core minimal API exposing exactly three endpoints per the "minimal read/action surface" scope decision: `POST /v1/chat/completions`, `GET /v1/memory/query`, and `POST /v1/rag/query`. Every request must present a token in `X-Aether-Token`, generated and saved from a new Settings "Local API" section; the host fails closed (503) rather than allowing unauthenticated access when no token has been configured yet.
- Voice convergence, native Kokoro-ONNX (2.0 roadmap item 5): a new `Aether.Voice` project adds `NativeKokoroVoiceProvider`, a fully in-process Kokoro text-to-speech path with no Python subprocess. It combines a small English-only phonemizer (a common-word dictionary plus letter-fallback rules, explicitly not a misaki port), a static phoneme-to-token tokenizer matching Kokoro's real ONNX vocabulary, and ONNX Runtime inference against the same lazy-load/SHA256-pinned-download pattern as the RAG reranker: the model and voice files are never downloaded on the synthesis path, only via an explicit Doctor install action. It is registered as a new, separate voice provider (`VoiceProvider.KokoroNative`) alongside the existing Python-based Kokoro provider rather than replacing it; the settings default stays Python-based Kokoro until native passes real-world parity testing. `IVoiceProvider.RequiredPythonVersion` is now nullable so providers that need no Python interpreter (native Kokoro, OpenAI) can say so directly instead of using a `(0, 0)` sentinel.

## [0.9.20-alpha] - 2026-07-07

This release closes out the v1.0 architecture review and roadmap
(docs/review/): all four 1.x structural debts are paid down, the 1.0
hardening checklist (retention policies, architecture tests, docs-truth,
Avalonia pin) is complete, and every Feature Audit Merge/Deprecate/
Over-engineered item has been either resolved or explicitly, honestly
deferred with reasoning recorded in the review docs. What remains is 2.0+
work (workspace-as-organizing-concept, approval-gated agent execution, MCP
client, headless core/local API, voice convergence completing) by design,
not leftover 1.x debt. The version jumps from 0.9.16 to 0.9.20 to reflect
the actual scope of this pass rather than a routine patch bump.

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

### Changed (audited, no code change)

- Workspace file browser panel (docs/review/05-feature-audit.md: rated
  "Merge", flagged as "a file manager inside a chat app is scope creep").
  Audited
  before cutting: the "Workspace Files" panel is a flat, search-only list
  feeding a preview pane, nested under "Draft Patch"; `SelectedWorkspaceFile`
  is the only way to choose a patch target, and `AgentWorkspaceTools.ListFiles`/
  `SearchFiles` are read-only, extension-filtered, path-bounded (no folder
  tree, no rename/move/delete/create). This is already scoped to exactly
  what patch review needs, not a general file manager; trimming it would
  remove the sole patch-target picker with no replacement, a regression
  rather than a simplification. Left unchanged.

### Removed

- Memory encryption toggle (docs/review/05-feature-audit.md: rated
  "Over-engineered"). `MemorySettings.EncryptMemoriesAtRest` and the
  Settings UI "Encrypt at rest" checkbox turned out to be a phantom
  control: the flag was persisted but no code anywhere actually performed
  AES encryption or decryption on memory content, so the setting implied a
  security property Aether didn't have. Removed the setting and the UI
  control rather than picking a default for it, since there was no real
  encryption to default on. `Memory.IsEncrypted` stays in the schema
  (always `false` now, harmless) rather than trigger a `memories.db` column
  migration for this cleanup.
- Local tasks/reminders/automations (docs/review/05-feature-audit.md: rated
  "Deprecate"). Deleted `TasksViewModel`, `AutomationScheduler`,
  `IAutomationScheduler`, `TasksView`, `TaskAutomationModels.cs`
  (`LocalTaskItem`, `ScheduledAutomation`, `AutomationRunHistory`), and the
  `Tasks`/`Automations` lists from `AppSettings`. Existing `settings.json`
  files with those fields simply ignore them on next load; no migration
  needed since nothing reads them anymore. The sidebar lost its "Tasks and
  automations" entry point. This is unrelated to the Agent Workbench's own
  task-state persistence (`IAgentTaskStateStore`/`FileAgentTaskStateStore`),
  which is untouched.
- Benchmarking suite scope trim (docs/review/05-feature-audit.md: rated
  "Over-engineered"). Deleted `ExportAllZipAsync` (a thin wrapper that re-ran
  `ExportAllAsync` and zipped the result, a duplicate bulk-export path next
  to the existing "Export All" folder export) and the "All / Latest per
  model / Last N" ranking-mode picker (`RankingFilterMode`, its three
  buttons, and the `NumericUpDown` for N). Rankings now always show the
  latest run per model, the only mode that actually answers "which model
  should I use," instead of a small analytics dashboard with three
  interchangeable views. Left alone, deliberately: the triple CSV/JSON/MD
  export per run, the run-info/case-info dialog split, and the 12 starter
  suites, since cutting those changes user-facing export formats and
  existing test expectations rather than removing pure surface duplication;
  see docs/review/05-feature-audit.md for the full trim list considered and
  what was scoped out.

### Changed

- Voice convergence status audit (docs/review/02-dependency-review.md,
  docs/review/07-roadmap.md). No code change: confirmed the settings-default
  and UI-labeling half of "Kokoro-ONNX default, Python providers marked
  advanced/best-effort" was already in place (Kokoro is the settings
  default; `VoiceProviderCategory` already marks XTTS/F5 "Advanced" in the
  Settings UI). Documented, rather than silently left stale, that Kokoro
  itself still runs as a Python 3.12 subprocess (`KokoroProcessManager`),
  not native ONNX Runtime inference: the "ONNX" half of that roadmap line
  was aspirational, not fact. Making it real is a feature build (tokenizer,
  phonemizer, ONNX session, audio postprocessing), correctly scoped at 2.0
  ("voice convergence completes"), not folded into this pass.
- AvaloniaEdit usage audit (docs/review/02-dependency-review.md). Exactly one
  call site, read-only chat-markdown code-block rendering
  (`MarkdownViewer.cs`), no editing anywhere in the app. Deleted a dead,
  unused method (`ResolveHighlighting`) and de-duplicated shared style
  values between the two code-block rendering branches. Decision: keep the
  dependency rather than replace it with a bespoke highlighter, since that
  would mean reimplementing per-language tokenizers, a feature build, not a
  refactor, for a single well-scoped, actively-maintained package.
- Collapsed two provider tag-switches into the capability model
  (docs/review/06-technical-debt.md item 4). `CompositeLlmService.StreamChatAsync`
  now routes through a delegate table keyed by `ProviderDescriptor.Tag`
  instead of a hand-written switch; `ServicesViewModel`'s duplicate
  provider-label switch now reads `CompositeLlmService.DescriptorFor(...)`.
  Adding a provider means one dictionary entry, not two separate switches to
  update in sync. Left alone, deliberately: the settings-flag-per-provider
  shape (`Llm.LlamaCppEnabled` etc.) and `PrivacyAuditService`'s tag switch,
  since collapsing those means a settings-schema migration, a bigger and
  riskier change than this cleanup.
- Decoupled `Aether.Agent` from `Aether.Rag` (docs/review/06-technical-debt.md
  item 11). `AgentContextBuilder` now depends on a new
  `Aether.Core.Services.IAgentRetrievalService` seam (dataset existence check
  + scored-chunk retrieval) instead of `RagQueryService`/`SqliteRagStore`
  directly; `Aether.Rag.AgentRetrievalService` implements it. Deleted two
  pieces of dead code found along the way: `AgentModels.cs`'s
  `AgentRagRetrievalResult` (never constructed anywhere) and Core's
  `IRagService`/`RagResult`/`RagSource` (never implemented or consumed).
  `Aether.Agent.csproj` no longer references `Aether.Rag.csproj`; a new
  architecture test enforces it stays that way.
- Folded the Session Usage panel into Memories (Feature Audit: Merge).
  `MemoriesViewModel` now carries a conversation filter (per-conversation
  memory counts, the old panel's whole purpose) with a "Clear" reset and a
  CSV export scoped to the selected conversation. `SessionUsageViewModel`,
  `SessionUsageDetailViewModel`, and their views are deleted; the sidebar has
  one memory entry point instead of two.
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


