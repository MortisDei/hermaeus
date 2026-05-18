# Changelog

All notable changes to Aether will be documented in this file. Append new versions above the previous version.

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

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
- Benchmark Rankings display restructured with column headers (Model, Score,
  Pass Rate, Speed) for clarity, and split into two tabs: Rankings (tabular
  view) and All Results (detailed per-model metrics breakdown).
- Chat conversation management now provides dual access: existing three-dots
  flyout menu plus new right-click context menu on conversation list items.

### Added

- Benchmark test info modal dialog displaying test case details including
  prompt, expected results (keywords, regex patterns, refusal requirement),
  and system prompt with selectable/copyable text.
- Benchmark results view now shows info button (ℹ) next to each test case to
  open the test details modal.
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
