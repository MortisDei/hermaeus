# Changelog

All notable changes to Aether will be documented in this file. Append new versions above the previous version.

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

## [0.9.9-alpha] - 2026-05-17

### Changed

- Project version metadata bumped to `0.9.9-alpha`.
- Runtime logs now redact persisted entries before they reach disk as well as
  UI log views.
- Conversation list and memory timestamps now store UTC values and render local
  display times consistently.
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

## [0.9.7-alpha] - 2026-05-17

### Changed

- Chat now truncates request history to the selected context window while
  preserving the full stored conversation, and stopped streaming responses are
  marked as incomplete instead of reloading as finished messages.
- Agent tools now execute the supported local read-only operations immediately
  and can apply draft patches only after approval; commands, network access,
  installs, commits, pushes, uploads, downloads, and history rewrites remain
  blocked.
- RAG query caching is now bounded by approximate memory size as well as dataset
  count, and cache warmups log their current footprint.
- Task and automation reminder checks now use UTC internally while preserving
  local date picker behaviour for older unspecified due dates.
- SQLite store initialization and settings save/load now use async gates to
  avoid concurrent initialization or settings writes.
- First-run startup now skips heavyweight panel loading until the setup wizard
  is completed, so the wizard appears sooner and initial launch feels snappier.
- Toast notifications now use a toast-shaped toolbar icon, and the stored
  notification history shows the newest items first.
- Settings now populate the embedding model dropdown from discovered local
  GGUF files and restart the embedding server after a model change is saved.
- Settings internals are now split into domain section view models and section
  views while preserving the single settings screen and save flow.
- Agent draft patch review now opens as an awaited modal dialog instead of
  routing through a full app panel.
- Top toolbar icons are now grouped more coherently so the main workspace,
  operations, logs, notifications, and settings read left to right more
  naturally.

### Fixed

- Memory injection now uses the full configured budget instead of stopping at
  half the memory count or half the character budget.
- XTTS API script generation now delegates to the shared generator instead of
  carrying a duplicated template in the setup service.
- ONNX reranker downloads are pinned to a Hugging Face commit and verified with
  SHA256 before loading.
- Backup restore now warns that encrypted local secrets need the original
  `secrets.local.key`, which is intentionally excluded from backups.
- Benchmark CSV export now normalizes embedded newlines inside fields.
- Extra local runtime arguments now handle escaped quotes inside quoted values.
- Voice preview skips audio players that are not present on `PATH`.
- Doctor embedding model detection no longer treats arbitrary `e5` substrings
  as embedding model evidence.
- The Ollama service now disposes its owned `HttpClient` instead of carrying a
  misleading static-client comment.
- Conversation export is available for Markdown and JSON from chat and the
  conversation details flyout.
- Setup wizard startup now returns early when the wizard is required, instead
  of loading the rest of the app before showing onboarding.
- Setup wizard step visibility no longer depends on runtime-mutated XAML
  converters, preventing a blank first-run wizard surface on launch.
- Local AI asset detection now prefers the existing `Models` directory when it
  contains GGUF files, so Doctor and reranker installs do not drift into a
  separate lowercase `models` folder.
- Doctor no longer treats arbitrary chat GGUF files as embedding models, skips
  backend health checks until a dedicated embedding model is installed, and
  does not report Linux global hotkeys as a problem.
- Toast history trimming now preserves newest-first ordering when the cap is
  exceeded.
- Project version metadata bumped to `0.9.7-alpha`.

## [0.9.6-alpha] - 2026-05-17

### Fixed

- Memories now shows the empty state only when there are no memories.
- Removed the placeholder Phi-4 SHA256 entry so setup no longer fails every
  default model download against a known-wrong hash.
- Local fallback secrets now use a stable per-data-root key file and
  `Rfc2898DeriveBytes` instead of hostname-derived hand-rolled PBKDF2.
- TTS settings now unsubscribes process status events during shutdown.
- Chat regeneration now uses stored original message text and attachment paths
  instead of parsing the display-only attachment summary.
- Background panel load failures now surface a warning toast as well as a
  runtime log entry.
- Conversation FTS rebuilds now run only when the FTS table is missing or a
  schema migration actually changes columns.
- Conversation auto-summary throttling now caches the last summary timestamp
  per conversation to avoid a DB read on every assistant response.
- Routine settings saves now skip data-root migration unless a previous data
  root is explicitly provided, preventing startup crashes when the selected
  data root already contains existing databases.
- RAG ingest embedding failures now return actionable guidance when llama.cpp
  responds with 501/404, including a hint to enable `--embeddings` and point
  `LlamaCppBaseUrl` at an embeddings-capable server.
- Embeddings-mode managed server launches now default to `--pooling mean`
  (unless explicitly overridden) to keep llama.cpp OpenAI-compatible
  embeddings requests from failing with pooling type `none`.
- RAG ingest now clamps per-chunk embedding input size before calling
  `/v1/embeddings`, preventing single oversized chunks from failing a full
  ingest run on servers with smaller embedding token limits.
- Doctor now detects missing embedding models and offers one-click download
  from Hugging Face (nomic-embed-text-v1.5-Q4_K_M by default), automatically
  configuring the embedding server model path.
- RAG ingest now pauses non-embedding LLM servers and TTS services during
  document indexing to reduce memory pressure, then restores them after
  ingest completion (or cancellation/failure).
- First-run startup now skips heavyweight panel loading until the setup wizard
  is completed, so the wizard appears sooner and initial launch feels snappier.
- Toast notifications now use a toast-shaped toolbar icon, and the stored
  notification history shows the newest items first.

### Changed

- Draft patch preview decisions now use async callbacks rather than
  fire-and-forget `Action` callbacks.
- Backups now exclude the local fallback secret key file, and restore supports
  an explicit overwrite path for future UI use.
- Starter benchmark checks now use more specific expected keywords.
- Project version metadata bumped to `0.9.6-alpha`.

## [0.9.5-alpha] - 2026-05-16

### Added

- Chat context inspector and trace viewer for transparent model calls.
- Compare Models workflow for side-by-side prompt checks across selected models.
- RAG dataset manager with lifecycle, embedding, stale/missing source, and
  reindex status details.
- Privacy audit dashboard for remote-provider, local-server, secret, log, and
  backup visibility.

### Changed

- Project version metadata bumped to `0.9.5-alpha`.

## [0.9.4-alpha] - 2026-05-16

### Added

- Structure-aware RAG chunking for markdown headings, code symbols, PDF pages,
  log events, and web pages, with metadata carried through storage and traces.
- Query planning now emits multiple rewritten variants and records them in RAG
  traces for later inspection.
- Context packing now respects a configurable token budget and records packing
  summaries plus refusal reasons in traces.
- Eval harness results now report Recall@K, MRR, citation hit rate,
  unsupported answer rate, refusal accuracy, and reranker rank delta.
- Benchmarks now capture richer run metadata, record repeated cold and warm
  attempts where possible, and surface median, p95, and failure summaries for
  more useful comparisons.
- Voice setup now documents the consolidated provider architecture, isolated
  venv requirement for F5-TTS and XTTS v2, and Apple Silicon `mps` hardware
  backend support.
- Agent workspace now includes an Explain Workspace flow with workspace profile
  persistence, project instruction detection, safe command recipes, risk notes,
  suggested `AGENTS.md`, and a RAG ingest plan saved to workspace memory.

- Tools: added a lightweight CLI `TraceValidator` tool for validating
  `agent.trace.jsonl` event lines. It now uses `JsonSchema.Net` to validate
  each trace line against `docs/schemas/agent_trace.schema.json`, and ships
  with convenience runner scripts at `scripts/validate_trace.sh` and
  `scripts/validate_trace.ps1`.
- Schemas: added example JSON Schema at `docs/schemas/agent_trace.schema.json`
  to document the fields emitted in agent traces and to guide future
  validation tooling.
- Tests: added an acceptance test that exercises trace schema validation and
  a view-model style UI acceptance test asserting patch-queue metadata is
  surfaced by the `AgentViewModel`.

### Changed

- RAG retrieval now uses query-variant BM25 scoring, structural boosts, and
  budget-aware context selection before answer generation.
- RAG queries can now refuse early when the retrieved context is too weak to
  answer reliably.

### Fixed

- RAG storage and trace handling now tolerate older databases while persisting
  the new chunk, planner, and packing metadata.

## [0.9.3-alpha] - 2026-05-16

### Added

- Agent workbench now surfaces task state, goal, summary, recent task history,
  review queue counts, workspace memory counts, and retrieved context counts in
  a compact summary strip at the top of the panel.
- Agent workbench now includes a workspace file browser with query, list,
  preview, and summarise behaviour for faster read-first inspection.
- Agent workbench now supports draft patch proposals from workspace files,
  with rationale, generated patch preview, approval-gated queueing, and
  approve/reject actions for queued patches. Approving a draft patch now writes
  the proposed content back to the selected workspace file and refreshes the
  preview immediately.
- Draft patch decisions now expose explicit pending, applied, rejected, and
  blocked states, plus a direct block action and clearer queue counts in the
  agent summary strip.
- Agent workbench now shows a capability disclosure callout that states the
  current slice is read-first and approval-gated, with shell, network, and
  remote-control actions kept out of scope.
- Session usage panel: per-conversation memory counts and recent activity.
- Draft patch diff preview: side-by-side visual comparison with line numbers,
  colour-coded changes (green for additions, red for removals), and gutter lines.
  Preview opens automatically after generating a patch and allows approve or
  cancel before queueing.

### Changed

- Project version metadata bumped to `0.9.3-alpha`.

## [0.9.2-alpha] - 2026-05-16

### Added

- RAG ingest cancellation controls in the UI, with token propagation through
  the pipeline so long-running ingests can be stopped cleanly.
- Conversation search now uses SQLite FTS5 for faster full-text lookup across
  title, messages, folder, and tags, with LIKE fallback for malformed or very
  short queries.
- Chat memory foundations: new memory domain model and settings, SQLite-backed
  memory store with FTS5 search, extraction service for `[MEMORY: ...]`
  markers, and context injection service with token-budget selection.
- New Memories panel in the main toolbar for reviewing, searching, pinning,
  archiving, and deleting saved memories.
- Settings now include chat memory controls for enable/disable, prompt
  injection toggle, auto-summary threshold, injection token budget, encryption
  toggle, and auto-archive days.
- Phase 5 memory automation: conversations are now auto-analysed in the
  background after assistant replies, and durable memories are extracted and
  persisted only when conversation importance exceeds the configured threshold.
- Memory test coverage now includes store CRUD/search behaviour, extraction
  marker parsing/cleanup, and injection token-budget prioritisation.
- Chat now shows a live memory status line (enabled state and recent counts)
  in the header area for clearer memory visibility.

### Changed

- Project version metadata bumped to `0.9.2-alpha`.
- RAG query prompt generation now applies per-dataset `PromptTemplate`
  configuration, honoring `{context}` and `{question}` placeholders.
- Benchmark starter suite seeding is now ID-aware, so missing default suites
  are inserted even when the database already contains other suites.
- SQL connection string construction in `SqliteRagStore` is now cached and only
  rebuilt when the resolved database path changes.
- Kokoro and F5-TTS Python scripts are now shipped as embedded resources rather
  than multi-hundred-line C# string literals.
- Local AI setup scripting and action definitions are now split into dedicated
  helper classes so the service file is easier to navigate and maintain.
- Chat context usage estimation is now debounced to reduce repeated full
  recalculations while typing or when messages/attachments change rapidly.

### Fixed

- HttpClient pooling: Converted instance `HttpClient` creation to static class
  fields across 11 service classes (OpenAiService, LlamaCppService,
  OllamaService, RuntimeProfileService, ModelDownloadService,
  LlamaCppEmbeddingService, KokoroVoiceProvider, XttsV2VoiceProvider,
  OpenAiVoiceProvider, RagPipeline, OnnxCrossEncoderReranker) to prevent socket
  exhaustion under concurrent connections.
- `ReaderWriterLockSlim` disposal: `CompositeLlmService` now implements
  `IDisposable` to properly dispose the shared model cache lock.
- Async-over-sync anti-pattern: `ServiceTests.SystemInfoSafeFallback()` now
  uses proper async/await instead of `GetAwaiter().GetResult()`.
- Port validation: `ServerProcessManager.NormalizeConfig()` now validates port
  range (1-65535) before process launch to catch invalid configurations early.
- AutoTune debugging: `ServerProcessManager.AutoTuneAsync()` now tracks and
  includes process exit code in timeout exception for better diagnostics.
- Event lifecycle: `ServicesView` now properly unsubscribes from
  `ObservableCollection.CollectionChanged` on view unload to prevent memory
  leaks.
- Symlink security hardening: `AgentWorkspaceTools.ResolveSafePath()` now
  rejects symbolic links to prevent path traversal attacks.
- Secret storage encryption: `SecretStore` now uses AES-256-CBC encryption
  with PBKDF2 key derivation (using machine identifier and 10,000 iterations)
  instead of Base64 encoding, with backward compatibility fallback.
- Model download integrity: `LocalAiSetupService` now supports SHA256 hash
  verification when trusted hash metadata is available. Hash mismatches fail the
  setup action with a clear error message.
- Settings voice sample import compile break: `SettingsView` now correctly wires
  `ImportTtsVoiceSampleCommand` through `TtsSettingsViewModel`.
- Reranker directory resolution parity between Doctor and runtime reranker:
  both now use the same path resolution strategy.
- `MainWindowViewModel` search debounce now disposes prior
  `CancellationTokenSource` instances to avoid CTS leaks while typing.
- `KokoroVoiceProvider` and `F5TtsVoiceProvider` now serialise synthesis calls
  with a semaphore to avoid concurrent Python process stampedes.
- WAV playback fallback chain restored in `VoiceProviderProcessRunner`:
  `paplay` → `pw-play` → `aplay` → `ffplay`.
- `ServicesView` now unsubscribes old collection-changed handlers when
  DataContext changes, preventing duplicate subscriptions.
- `AutomationScheduler` settings saves are now guarded and failures are logged
  to runtime logs instead of being silently swallowed.
- Chat attachment paths are now persisted in `Message` and round-tripped
  through `ConversationStore`, so regenerate can reattach after app restart.
- RAG ingest health reporting now uses `IngestReport.Health` directly;
  `__health__` sentinel rows are no longer emitted by web ingest.
- Legacy unused services removed: `XttsService` and `OghmaRagService`.
- `CompositeLlmService` shared model cache state is now lock-protected, and
  provider model refresh now runs concurrently instead of sequential timeouts.
- `RagQueryService` dataset chunk cache and LRU metadata are now synchronised
  for concurrent query/warm/clear access.
- Model Management UI no longer shows the misleading non-destructive
  "Cleanup" action.
- Data root migration now carries all local SQLite families used by chat and
  memory features (`conversations.db*`, `memories.db*`, `benchmarks.db*`).
- Feature documentation now includes a dedicated Memory section describing the
  UI, settings controls, and background auto-summary behaviour.
- Chat auto-summary failures are now captured in runtime logs as warnings
  instead of being silently swallowed.



## [0.9.1-alpha] - 2026-05-15

### Changed

- Voice provider configuration now uses a dropdown selector to clearly indicate
  the active provider, with conditional display of provider-specific settings
  below the selector (Kokoro voice/device/speed controls, F5-TTS options, etc.).
- Test project `Aether.Tests` is now configured as a proper xUnit test project
  (`Microsoft.NET.Test.Sdk` + xUnit runner), replacing the custom executable
  harness so `dotnet test` provides discovered, passed, and failed test counts.
- xUnit harness cases are now grouped into per-domain fixtures (Backup,
  Services, RAG, TTS, Agent) for clearer CI output while preserving the same
  underlying coverage set.
- Settings: add Kokoro managed service controls and health probe at startup; TTS speed persisted in settings.
- Kokoro: Start/Stop managed service, venv-aware startup, and GPU-friendly install notes (supports CUDA, AMD, Intel via environment guidance).
- Kokoro: Start/Stop managed service, venv-aware startup, and GPU-friendly venv setup. The Local AI setup now detects NVIDIA (CUDA) or ROCm and suggests the appropriate device; settings persist TTS device and speed. Manual override available in Settings.
- Local AI setup now attempts to install a matching PyTorch wheel for the selected backend (CUDA/ROCm/CPU) before installing XTTS packages.
- Local AI setup now explicitly warns when Intel or AMD GPUs are detected without a supported wheel path and falls back to CPU for XTTS setup instead of silently masking the hardware.
- Chat: avoid inserting internal runtime/TTS error messages into chat history; surface as runtime logs and toasts instead.
- RAG Embeddings section expanded with clarification that embeddings convert
  document text to vectors for semantic search and are cached after first
  compute, plus notes that reranker is optional and improves search relevance.
- Tooltips for Local AI Setup Plan and Approve buttons now include more
  detailed guidance on what each action accomplishes (preview vs. execute).
- Toast notifications repositioned from top-right to top-left below the menu
  bar for improved visibility and less overlap with active content areas.

### Fixed

- Chat message list no longer shows a selected-row grey slab when clicked;
  Chat view now uses a non-selectable `ItemsControl` inside a `ScrollViewer`
  instead of a selectable `ListBox`.
- Window title now updates correctly when switching between non-Chat panels
  (Settings, Agent, Models, RAG, Services, Tasks, etc.).
- Agent view Grid layout: fixed TextBox/Browse button field overlap by adding
  a third column definition for proper button sizing.
- Services view: eliminated duplicate items appearing on scroll or after
  collection changes by preventing multiple CollectionChanged subscriptions on
  the same handler.
- Python version validation now uses per-provider requirements; Kokoro requires
  Python 3.12 while F5-TTS and XTTS v2 require Python 3.11, with appropriate
  health check messages for each provider.
- Reranker model path now resolves correctly against LocalAiAssetsRoot instead
  of the incorrect DataRootDirectory reference.
- Resource leak in MarkdownViewer: timer now properly stopped and unsubscribed
  from events when control is detached from visual tree; prevents timer tick
  events continuing after control disposal.
- Resource leak in ServerProcessManager.AutoTuneAsync: TaskCompletionSource
  now guaranteed to complete even on abnormal process exit; added defensive
  catch block and finally-block check to ensure completion never hangs.
- MainWindowViewModel panel commands now consistently use sync commands with
  background async loading; ShowChatPanelAsync and ShowAgentPanelAsync
  converted to ShowChatPanel and ShowAgentPanel using RunBackgroundTaskAsync
  pattern for consistency with other panel switching commands.
- CompositeLlmService timeouts tuned: per-provider timeout increased from 2s
  to 5s to accommodate slower model providers; model cache duration increased
  from 30s to 300s (5 minutes) for more stable model discovery; added explicit
  error classification for timeout vs other exceptions.
- RagQueryService LRU cache simplified: replaced complex Queue-based TouchCache
  with LinkedList for O(n) instead of O(n^2) eviction; cleaner implementation
  with direct node removal and addition to end.
- ChatViewModel regenerate now uses proper attachment storage: AttachedFilePaths
  collection added to MessageViewModel to store file paths; regeneration
  retrieves paths from message model instead of fragile marker-based parsing.
- AgentService JSON extraction improved: added brace-matching logic to properly
  handle nested structures and escaped quotes; validates extracted JSON with
  JsonDocument.Parse before returning; fallback behavior now attempts multiple
  extraction strategies instead of failing on first mismatch.
- SqliteRagStore connection pooling: added Pooling=true and Max Pool Size=5
  to SQLite connection string for automatic connection reuse across operations.
- SettingsView.axaml binding consistency: added missing Tts. prefix to
  IsLegacyVoiceBackend bindings for UI visibility conditions.

- Window title now updates correctly when switching between non-Chat panels
  (Settings, Agent, Models, RAG, Services, Tasks, etc.).
### Changed

- Runtime log entry buffer capacity reduced from 2000 to 1000 entries to reduce
  in-memory overhead during long-running sessions while maintaining adequate
  debugging history.
- Setup Wizard toolbar icon removed to reduce visual clutter; the wizard
  remains accessible via the Settings panel.

## [0.9.0-alpha] - 2026-05-14

### Added

- Local AI setup now offers approval-gated downloads for a default Phi-4 mini
  reasoning GGUF file when no GGUF models are present, plus a platform-specific
  `llama-server` binary download when the runtime is missing.
- Added resumable model download support and binary installation helpers for
  large local assets.
- The existing Settings scan flow now surfaces the new download actions without
  requiring a separate setup screen.
- The first-run Setup Wizard now surfaces Kokoro onboarding directly in the
  voice step, including the provider install plan and risk notes.
- Added regression coverage for resumable downloads and llama-server release
  data validation.
- Voice provider abstraction with capabilities, health checks, and install
  plans for Kokoro, F5-TTS, XTTS v2, and optional OpenAI voice.
- Local AI setup install plan previews that must be reviewed before approval.
- Runtime log viewer with filters, copy actions, and redacted diagnostics export.
- Aether Doctor screen with environment checks for storage, runtimes, voice,
  RAG, GPU visibility, and secrets, plus diagnostics copy and navigation.
- Python health validator that rejects broken or non-relocatable Python
  installs and surfaces actionable diagnostics.
- First-run Setup Wizard: a 6-step guided onboarding that configures data
  roots, chat backend, model paths, voice provider, and runs the Aether
  Doctor before starting. The wizard sets `SetupWizardCompleted` to skip on
  subsequent launches after finish or skip.
- RAG ingest dry-run, duplicate-source reporting, and per-document ingest
  summaries surfaced in the RAG panel before writing changes.
- Agent review queue with approve/reject actions for waiting tasks, plus a
  file-backed workspace memory panel for reusable workspace notes.

### Fixed

- Refactor LLM provider routing in CompositeLlmService from hardcoded model
  name prefix checks to metadata-driven ProviderTag routing; adds canonical
  provider tagging to LlmModel discovery for OpenAI, Ollama, and llama.cpp.
- Extract shared ExtraArgsParser utility for quote-aware command-line argument
  tokenization; eliminates duplication between ServerProcessManager and
  TrustService and centralizes parsing logic.
- Guard SetupWizardViewModel runtime synchronisation loop to prevent infinite
  re-entrancy via PropertyChanged events; replace event-driven sync with
  guarded partial methods.
- Fix MarkdownViewer timer lifecycle: move subscription to OnDetachedFromVisualTree,
  add IDisposable implementation, and properly dispose timer on control detach
  to prevent resource leaks.
- Add logging for fire-and-forget background tasks in MainWindowViewModel;
  capture exceptions in RunBackgroundTaskAsync helper and route them to
  RuntimeLogService instead of silently discarding them.
- Bound RagQueryService cache to 5 datasets maximum with LRU eviction policy;
  prevent unbounded memory growth during long-running query sessions and
  corpus re-rankings.
- Harden OllamaService.ParseId to detect and reject model names containing
  extra colons that would cause silent truncation; add ParseIdGuarded variant
  for safer model name extraction.
- Fix test harness and CI-only fakes to match new voice provider APIs and
- Restructure AppSettings into hierarchical configuration objects: TtsSettings
  (voice provider, TTS configuration), RagSettings (RAG service, reranking),
  LlmSettings (LLM providers, model defaults), UiSettings (theme, hotkeys, tray),
  and DataManagementSettings (storage paths). This improves maintainability and
  reduces cognitive load by grouping related settings under semantic namespaces
  throughout Core, Services, ViewModels, and tests.
  Python validator constructor; repaired several setup & log view bindings.
- Refactor: extract TTS settings and commands into `TtsSettingsViewModel`; clean
  `SettingsViewModel`, update Settings view bindings, and move XTTS status handling
  into the nested viewmodel to improve separation of concerns and testability.
- Refactor Rag ingest: decompose embedding and storage into cancellable,
  batched helpers (`EmbedChunksAsync`, `StoreChunksAsync`) to improve
  responsiveness and make cancellation/reporting deterministic during large
  ingests.
- Tests: add cancellation tests that verify RAG ingest cancels cleanly during
  embedding and storage phases to prevent long-running uninterruptible work.
- Tests: split the monolithic custom harness further by extracting backup and
  migration coverage into `BackupMigrationTests`, keeping `Program.cs` as a
  thinner runner and reducing the surface area for future test refactors.
- Tests: continue the harness split by extracting agent coverage into
  `AgentTests`, including task-state persistence, review queue behaviour,
  workspace memory, path safety, context packing, safety gating, and trace
  logging.
- Refactor AppSettings into dedicated configuration types for LLM, TTS, RAG,
  UI, and data management settings, so each domain is owned by a focused model
  instead of one oversized settings object.

## [0.8.7-alpha] - 2026-05-13

### Added

- Voice provider groundwork for a pluggable local voice layer, with Kokoro as
  the recommended default readback engine, F5-TTS as the advanced cloning
  option, and XTTS v2 retained as the legacy compatibility backend.
- Voice setup terminology updates from XTTS-only wording to voice-provider
  wording in preparation for the pluggable layer.
- XTTS validation and repair improvements that require Python 3.11 and detect
  broken or incompatible venvs before install.

### Changed

- XTTS setup actions are now framed as voice backend setup rather than a boxed-
  in single-provider workflow.

## [0.8.6-alpha] - 2026-05-12

### Added

- Settings Trust & Safety scan for configured local executables, scripts,
  models, XTTS paths, runtime endpoints, hashes, and AI-root scope warnings.
- Advisory trust warnings for network-facing `llama-server` extra args such as
  `--host 0.0.0.0`.
- Direct chat file context injection for selected local text/code files, with
  attach/drop UI, bounded reads, skipped-file status, and history summaries.
- Digital PDF support in local RAG directory ingest through managed PdfPig text
  extraction, with image-only PDFs skipped as health warnings.
- Syntax-highlighted fenced Markdown code blocks in chat and RAG answers through
  AvaloniaEdit.
- Chat context usage indicator with provider-reported token usage when
  available and local estimates while drafting.

## [0.8.5-alpha] - 2026-05-12

### Added

- Optional RAG web URL loader that stays disabled by default and only ingests
  explicitly listed HTTP(S) pages when enabled for a dataset.
- Web ingest HTML text extraction with script/style stripping, small page
  limits, URL deduplication, and regression coverage for the default-disabled
  posture.
- Experimental Aether Agent workbench with explicit task state, compact context
  packs, local task logs/traces, read-only workspace tools, safety gating, and
  optional RAG-backed retrieval.
- Agent regression coverage for task-state serialization, workspace path
  safety, context packing, tool policy, and the fake-model agent loop.
- Approval-gated Local AI Setup assistant for scanning an AI folder, detecting
  models, venvs, XTTS v2 assets, voices, output folders, and rerankers.
- Structured setup actions for creating a venv, creating XTTS support folders,
  installing XTTS packages, and generating an XTTS v2 API script after explicit
  user approval.

## [0.8.4-alpha] - 2026-05-12

### Added

- Repeatable Linux and Windows archive packaging scripts.
- Linux package layout with desktop launcher metadata, user-local desktop
  install/uninstall scripts, icon asset, license notices, archive, and SHA256.
- Windows package layout with launch helper, license notices, ZIP archive, and
  SHA256.
- App and tray icon assets derived from the Aether branding sheet.
- Packaging documentation covering runtime requirements and self-contained
  builds.
- Refreshed security review and threat model for secrets, local runtimes,
  backup/restore, RAG ingest, tray behavior, and packaging.
- Expanded tests for RAG BM25/hybrid scoring, runtime profile validation,
  secret reference migration, and shell-free process argument construction.
- Opt-in Windows system-wide hotkeys for Quick Chat, New Chat, and Services,
  with Linux reported as unavailable until reliable compositor support exists.

## [0.8.3-alpha]

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
