# Changelog

All notable changes to Aether will be documented in this file.

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

## [0.9.2-alpha] - Unreleased

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
- Model download integrity: `LocalAiSetupService` now implements mandatory
  SHA256 hash verification for critical model downloads (Phi-4 mini reasoning).
  Hash mismatches now fail the setup action with a clear error message.
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



## [0.9.1-alpha] - Unreleased

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
