# Changelog

All notable changes to Aether will be documented in this file. Append newapp versions above the previous version (not the bottom of the doc or where ever feels right at the time).

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

## [Unreleased]

## [0.19.0-alpha] - 2026-07-18

Implements docs/review r14 in full: "Fast by default." A field log showed an
8-minute chat send reading a 9,744-token prompt at 51 tokens/sec, because
every Windows install shipped the CPU-only llama.cpp build and launched it
with zero GPU offload and four parallel slots (quartering the context). r13
taught the app what hardware it runs on; r14 makes the runtime use it and
keeps the latency story honest.

### Added

- **GPU-aware llama.cpp builds.** A new runtime-variant setting (Auto by
  default) resolves against the detected GPU: NVIDIA picks the CUDA build
  (with its `cudart` companion runtime), any other real GPU picks Vulkan, and
  no GPU picks CPU. Asset selection matches the release by os/arch plus the
  variant token, verified against the live GitHub asset list, and prefers the
  lowest CUDA version for driver compatibility. After install the binary is
  launch-verified; a GPU build that cannot start falls back to CPU once.
- **A Doctor advisory** that fires when a real GPU is present but inference is
  still configured for the CPU (CPU-only build or zero offload), naming the
  measured consequence and deep-linking the fix.
- **Superseded-version pruning.** After a successful update the flow offers to
  remove old `bNNNNN` version directories under the install root (single
  confirm, current and previous kept, locked directories skipped).

### Changed

- **GPU offload now uses the GPU.** `GpuLayers = -1` means "all layers"
  (rendered as `--n-gpu-layers 999`) and is the default for new managed
  servers when a GPU is detected; `0` remains explicit CPU; `N > 0` offloads
  exactly N.
- **One request slot by default.** Managed servers launch with `--parallel 1`
  so the whole `--ctx-size` belongs to one conversation and every send reuses
  the same KV cache, instead of silently getting a quarter of the context.
  Chat requests now send `cache_prompt: true` explicitly and launch with
  `--cache-reuse 256`; embeddings servers pin `-b 512 -ub 512` to stop the
  batch-clamp warning. Context-limit math uses the per-slot ceiling.
- **Updates install to the resolved install root**, not the current binary's
  own version directory, so they no longer nest one tag deeper each time
  (`b10064\b10066\...`), and preserve the configured runtime variant.
- **Latency truth.** Chat timing separates the first streamed event of any
  kind from the first visible content token, so a long non-content stream
  prefix is no longer hidden inside "before first token"; the slow-send
  warning names where the time went and, on a GPU machine configured for CPU
  inference, appends a "prompt was read at CPU speed" hint.

### Fixed

- **"Failed to fetch models" log spam.** A stopped managed server no longer
  logs an error per model-fetch call; the line is emitted at most once per
  up-to-down transition, and skipped entirely when the server is known
  stopped.
- **Triple stop-logging per shutdown.** Stop is now idempotent at the logging
  level: an already-stopped server logs nothing, so the runtime log shows one
  Stopping/Stopped pair per actual shutdown.

## [0.18.0-alpha] - 2026-07-18

Implements docs/review r13 in full: "usability is what is going to decide
whether this app sinks or swims." The System page now tells the truth about
Windows hardware, the Models page grows from a metadata editor into an
actual library (compact cards, a real scroll fix, auto-tune from the page,
fits-on-your-hardware chips, a flat-folder organizer), local models can be
linked to their Hugging Face origin and checked/updated in place, and the
orphan chat Temp spinner becomes a full sampling flyout.

### Fixed

- **Windows System Overview lied about RAM, OS version, CPU name, and GPU.**
  `SystemInfoService` returned 0 for available RAM on Windows, reported the
  raw `10.0.NNNNN` kernel string as the OS name (Windows 11 self-describes as
  Windows 10), returned the process architecture as the CPU *name*, and
  probed GPUs via `nvidia-smi`/Linux DRM only, so a Windows machine without
  the NVIDIA CLI showed no GPU and no VRAM at all. RAM now comes from a
  `GlobalMemoryStatusEx` P/Invoke, the OS name from a pure build-number
  mapper (`OsNameFormatter`) plus the registry `DisplayVersion`, the CPU name
  from `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0`, and GPU/VRAM
  from a registry fallback over the display adapter class key when
  `nvidia-smi` finds nothing. A new cached `ISystemInfoService.GetHardwareProfileAsync`
  gives the fits-check and HF browser cheap repeated access without
  re-spawning processes per row. `StarterModelCatalog.Recommend` now sees
  real VRAM on non-NVIDIA-CLI Windows machines instead of always falling
  back to the smallest tier.
- **The Models page could not be scrolled to the bottom.** Each of 32+ model
  cards rendered 8 always-expanded `NumericUpDown`s that captured every
  mouse wheel notch before it reached the outer `ScrollViewer`. Cards are
  now collapsed by default (one line: name, running badge, provider, size,
  tags, fits/update chips, tune summary, modified date) with a filter box
  above the list; the wheel-capture fix mirrors the same tunnel-phase
  handler ServicesView/SettingsView already use to force wheel scroll onto
  the page regardless of what is under the pointer.
- **Auto-tune only existed on the Services page**, even though the Models
  page lists every GGUF on disk. The tune-profile upsert logic is lifted out
  of `ServicesViewModel` into a shared `LlamaTuneProfileStore` (both pages
  now read/write the same store); the Models page gained a per-model
  "Auto tune" button and a sequential "Auto-tune all" that skips
  already-fresh profiles via a pure staleness predicate (missing, size
  drift, mtime drift, or llama-server build drift).

### Added

- **Fits-on-your-hardware chips.** `ModelFitEstimator` estimates FitsGpu /
  FitsPartial / TooLarge / Unknown from a file size and the cached hardware
  profile (deliberate rough headroom constants, documented as such); shown
  on Models-page cards, the HF browser's file list, and next to the wizard's
  recommended starter model.
- **"Organize folder..."** flattens the Hugging Face hub-cache maze
  (`hub\models--org--repo\snapshots\<sha>\*.gguf`) into
  `<ModelsDirectory>\LLM\<file>.gguf`. Plan -> preview dialog (every
  "from -> to" move, collision skips, provenance-record count) -> confirm ->
  execute: same-volume rename or copy+verify+delete across volumes, never
  renames a file (the name is the HF update-matching identity), skips name
  collisions instead of overwriting, moves multi-part sets atomically,
  rewrites every stored reference (`ServerConfig.ModelPath`,
  `LlamaTuneProfile.ModelPath`, `ModelProfile` keys) in one settings save,
  and offers a separately-confirmed empty-directory cleanup that only
  removes directories still empty at removal time.
- **Hugging Face integration**, anonymous/HTTPS/huggingface.co-only, every
  call manual-button-triggered (never on startup or a timer): a
  `model-manifest.json` provenance store records which local file came from
  which repo (written by the folder organizer's migration path, the HF
  browser, and a new manual "Link to Hugging Face repo..." card action that
  validates against the model-card API before saving); "Check for updates"
  batches by repo (one tree call each) and compares the tree's `lfs.oid`
  against the stored hash, hashing migration-linked files once on first
  check; a per-card "Update" button downloads to `<file>.update.tmp`,
  verifies the hash, then atomically swaps (move-to-`.previous`,
  move-into-place, delete `.previous` only after both moves succeed;
  restores the original on any failure) and flags the card "re-tune
  recommended" afterward. A new collapsed "Get models from Hugging Face"
  expander on the Models page searches GGUF repos, lists a selected repo's
  single-file GGUFs (multi-part sets are hidden this round, not
  half-supported) with a fits chip each, and downloads straight into
  `Models\LLM\`. The Privacy Audit's outbound-destination count and item
  list now disclose this surface whenever any model is repo-linked.
- **Chat header sampling flyout.** The orphan "Temp" spinner (temperature
  editable, nothing else was) is now a compact "T 0.7" button opening a
  flyout with all eight sampling parameters (temperature, top-p, top-k,
  min-p, repeat/frequency/presence penalty, max tokens) using the same
  ranges/tooltips as the Models page editor, plus "Reset to model defaults"
  sharing the exact fallback chain `OnSelectedModelChanged` already used
  (extracted into `ApplyModelProfileDefaults`, called by both). Still
  VM-local only - never written to `ISettingsService.Settings`.

### Docs

- `docs/security-review.md` gains an r13 subsection: new outbound surface
  (huggingface.co, manual-only, disclosed in the Privacy Audit), download
  integrity posture (origin-integrity via the tree API's `lfs.oid`, same
  stance as the starter-model catalog), the organizer/updater's
  data-mutation safety properties, and the read-only registry queries added
  for system truth.

99 new tests (688->787), zero warnings.

## [0.17.0-alpha] - 2026-07-18

Implements docs/review r12 in full: the first dedicated audit of
Aether.ViewModels (37 files, ~10k lines). Two systemic patterns anchor the
round - the live `ISettingsService.Settings` object is a shared mutable
global written outside the apply/save path, and fire-and-forget async work
around UI-bound state races its own later completion.

### Fixed

- **Finishing (or skipping) the setup wizard on first run left the app on a
  dead chat panel.** `MainWindowViewModel.InitializeAsync` returned early to
  show the wizard; the `WizardCompleted` handler only navigated to chat, so
  no servers auto-started and no models/RAG/agent/benchmark data loaded
  until a restart. The post-wizard sequence is now a named, reusable step
  (`CompletePostSetupInitializationAsync`) called from both the normal init
  path and the wizard-completed handler, guarded against double-running.
  Each step is isolated so one failing store cannot silently skip the rest.
- **Re-running the wizard and changing the data root bypassed migration.**
  `SetupWizardViewModel`'s data-root step called a plain `SaveAsync()`,
  which never migrates - the same "conversations lost" symptom the r11
  wizard-singleton fix addressed, through a second door. It now previews
  conflicts (same message the Settings page shows) and migrates through the
  same path Settings uses, with the same toast.
- **`SettingsViewModel.SaveAsync` mutated the live settings object before
  validation, and rolled back only the data root on failure.** Every tab's
  edits now apply onto a deep copy; a new `ISettingsService.SaveAsync(AppSettings, ...)`
  overload only swaps the copy in once the save (including migration)
  actually succeeds, so a failed save leaves every other in-flight edit
  exactly as it was, not persisted by some later, unrelated save.
- **Selecting a chat model overwrote `Settings.Llm.MaxTokens` globally**,
  changing what Benchmark/Agent/RAG sends saw as the cap. `ChatViewModel`
  now keeps a local `MaxTokens` field like `Temperature`/`TopP`.
- **A background model-list refresh reset user-tuned sampling parameters.**
  Model instances are recreated on every fetch, so re-matching by id still
  reassigned `SelectedModel` to a different object, re-applying profile
  defaults over a temperature the user had just changed. Refreshes that
  resolve to the same logical model now update the reference without
  re-applying defaults; a genuine model switch resets every non-profiled
  parameter to the settings default instead of leaking the previous model's
  tuning forward. `AgentViewModel.LoadAsync` had the same stale-reference gap
  for `SelectedModel`/`SelectedDataset` (a `??=` never re-matched after a
  refresh) and is fixed the same way.
- **Every settings save triggered a Services rebuild storm.** `SettingsChanged`
  fires after every save; `ServicesViewModel.Rebuild` cleared and re-added
  every server row regardless of what changed, force-invalidating the model
  cache and refetching models over HTTP, plus a synchronous orphan port scan
  on the UI thread - on saving anything, including unrelated tabs. `Rebuild`
  now diffs by config id (reusing unchanged rows, disposing dropped ones)
  and only fires `ServerAvailabilityChanged`/runs orphan detection when the
  server set, ports, or paths actually changed; orphan detection moved off
  the UI thread.
- **Trust rescans and the Local AI setup scan wrote unsaved edit-box values
  into live settings.** A trust rescan never even saved, so the mutation
  lingered until an unrelated save persisted it. Both now build a
  scan-scoped copy instead of touching the live settings object; the setup
  flow's genuine persist-before-running-an-action need goes through the
  real full apply/save, not a partial side-channel write.
- **Toast history could resurrect cleared/dismissed toasts, or lose the
  newest one.** `RunOnUi` posted unconditionally even from the UI thread, so
  code after it ran before the posted mutation - clearing history then
  immediately serializing it wrote the pre-clear list to disk. `RunOnUi`/
  `RunOnUiAsync` now execute inline when already on the captured context;
  toast-history mutation and its save are further bundled into one posted
  unit so a background-thread toast can never race its own save.
- **`ChatViewModel.SendAsync` had no exception handling.** Any throw after
  streaming started (a locked conversation database, an unexpected
  memory-marker error) left the assistant bubble stuck at `IsStreaming = true`
  forever with no visible error. A catch now marks the message as failed (or
  removes it if empty), logs, and toasts.
- **Per-keystroke searches had no debounce or cancellation.** Memories'
  search box and the Agent workspace file query each fired a fresh
  DB/filesystem query per character, with unordered completion interleaving
  `Clear`/`Add` on the bound list; both now reuse the existing 300 ms + CTS
  debounce shape. The Agent workspace-file selection loader gained a
  generation counter so a slower, older read/summarize can no longer
  overwrite a newer selection's preview.
- **`LogsViewModel` rebuilt its entire visible list on every log line**,
  O(n) work per line during a llama-server startup burst. Entries are now
  appended incrementally when they pass the current filter, with bursts
  coalesced behind a pending-refresh flag so one post handles many lines;
  full rebuilds remain for filter changes and Clear.
- **Concurrent `LoadModelsAsync`/`LoadAsync` calls duplicated models.**
  `ChatViewModel`, `AgentViewModel`, and `BenchmarkViewModel` now share one
  in-flight load task instead of each running its own Clear/re-add pass.
- **The agent's default workspace was the whole user profile**, enumerated
  and analyzed at every startup, writing a "Workspace profile" workspace
  memory entry for a folder the user never chose. `WorkspaceRoot` now
  defaults to empty; the existing empty-state UI handles it.
- **RAG "Add to dataset" ignored a renamed dataset name.** Editing the
  dataset-name box after clicking "Add to dataset" silently still ingested
  into the original target. The target now clears as soon as the box no
  longer matches it, so the next ingest creates a new dataset under the
  edited name.
- **Reindex flipped the dataset's recorded embedding model before the
  pipeline committed anything.** A cancelled or failed reindex left the
  live, UI-bound dataset instance claiming the new model while vectors were
  still old, defeating the r10 mismatch guard. Reindex now works on a clone;
  the live instance is only ever refreshed from what the pipeline actually
  persisted.
- **Benchmark `RerunAsync` had no `IsRunning` guard**; a second click during
  a run overwrote the run's `CancellationTokenSource`, leaking the first.
  `RerunFromInsightsAsync` also bypassed `CanExecute` by calling `RunAsync()`
  directly. Both now route through the guarded `RunCommand`/share `CanRun`.
- Small fixes: `ModelCompareOrchestrator.ToResult` no longer throws on an
  empty `CaseResults` list (returns an error row); a dead
  `!string.IsNullOrWhiteSpace(item.Folder)` check right after setting
  `Folder = string.Empty` is removed; `ChatViewModel.ClearChat` resets
  `SystemPrompt` like `NewConversation` does; `RemoveContextAttachment`
  recomputes the "N files ready" status instead of leaving it stale;
  `DoctorViewModel.RunFix`'s four copy-pasted install blocks are one shared
  helper that also disposes its `CancellationTokenSource`; embedding-download
  progress logging is serialized instead of one fire-and-forget file append
  per line; the setup wizard's model-folder step surfaces a toast when no
  managed server exists instead of silently no-op-ing;
  `McpServerConfigViewModel.ToConfig` now honors quoted arguments containing
  spaces instead of splitting on every space; a dead `IsReference` guard on
  `Tts.PythonPath` (paths are never stored as secrets) is removed;
  `SettingsViewModel.Reload()` now clears stale Trust/LocalAiSetup error
  text; `SaveAsync` no longer keeps the save command "executing" for an
  artificial 2 s tail.

37 new tests (688 total). docs/security-review.md gains an r12 subsection
(the agent no longer treats the user profile as an implicit workspace;
trust scans are read-only with respect to settings). Archived at
docs/review/archived/r12/.

## [0.16.0-alpha] - 2026-07-16

Implements docs/review r11 in full: the first dedicated audit of
Aether.Services (73 files, ~14k lines - providers, process management,
stores, setup/download, Doctor, voice glue). Reads every .cs file in the
project the way r10 read every file in Aether.Rag, and finds the same
pattern: features that look done and have never worked end to end, sitting
next to real field-impacting defects.

### Fixed

- **The built-in llama-server installer has never worked.** The pinned
  download URLs named release assets that do not exist (verified against
  the live GitHub API); the install-latest path filtered assets by a
  substring no real asset name contains, so it always threw "no asset
  matched"; and even with correct names, both paths moved a raw archive
  into place as the executable instead of extracting it. `ArchiveExtractor`
  (zip and tar.gz, zip-slip guarded, no new NuGet) now backs both the pinned
  path (re-pinned to a current release tag) and the latest-release path,
  shared by `LlamaServerSetupService`. Doctor's "Download llama.cpp" fix
  action rides the same code, so it's fixed too.
- **Windows executable resolution never tried `.exe`.** Four independent
  copies of PATH/directory resolution in `ServerProcessManager`,
  `DoctorService`, `TrustService`, and `LocalAiSetupService` probed only for
  the bare name `llama-server`, which cannot resolve on Windows; the default
  settings ship `ExecutablePath = "llama-server"`, so a fresh Windows
  install's managed servers were unstartable out of the box. This is the
  root cause of the r10 field finding that the owner's Embeddings server
  never launched. One shared `ExecutableResolver` (PATHEXT-aware, matching
  `VoiceProviderProcessRunner`'s already-correct logic) now backs all four
  call sites plus `OrphanServerDetector`; an architecture test bans any
  other `FindOnPath` reimplementation in Aether.Services.
- **Ollama chat did not stream.** `StreamChatAsync` used `PostAsJsonAsync`,
  which buffers the full response before returning, so first-token latency
  equaled total latency and Stop/cancel could not interrupt mid-generation.
  Now uses `HttpRequestMessage` + `ResponseHeadersRead` like the other two
  providers, and an unreachable endpoint yields an in-stream error event
  instead of throwing out of the async iterator.
- **Moving the data root silently lost secrets, traces, evals, logs, and the
  voice lexicon.** Migration only ever moved `conversations.db*`,
  `memories.db*`, `benchmarks.db*`, and `agent/`; everything else the app
  writes to the data root stayed behind, including the fallback secrets
  vault. A single `DataRootManifest` (walks the whole data root) now backs
  migration, its preview, and `BackupService`, so the three can never
  disagree again; secrets keep their restrictive permissions across the
  move.
- **The benchmark LLM judge was a phantom feature.** `UseJudge`/
  `JudgeModelId` were editable in the UI and persisted with every suite and
  run, and no code anywhere executed a judge. The UI controls and the
  copy-into-every-run wiring are removed; the model properties themselves
  stay so previously stored suite/run JSON still deserializes.
- OpenAI chat/model-list requests mutated `Authorization` on the shared
  static `HttpClient`'s `DefaultRequestHeaders`, racy under concurrent chat
  + model refresh; auth is now set per request.
- The "OpenAI-compatible" model list filtered to `gpt`/`o1`/`o3`/`o4`-
  prefixed ids, so pointing it at LM Studio/Groq/OpenRouter/vLLM etc.
  returned zero models. The prefix allow-list is dropped; a deny-list of
  known non-chat ids (embeddings/tts/whisper/dall-e) applies only when the
  host is `api.openai.com`.
- A model id whose provider tag was learned in an earlier scan, then not
  seen again in a later scan (a provider going temporarily unreachable),
  could silently route to llama.cpp - the id-to-tag memory is now durable
  (upserted, never cleared) across scans, and a genuinely never-seen id
  yields an explicit routing error instead of a silent guess. An
  all-providers-down scan no longer turns every `GetModelsAsync` call into a
  fresh probe storm (bounded negative-cache TTL).
- `LlamaCppService`'s probed context-length cache was keyed by base URL
  alone, so restarting the managed server with a different model or
  `--ctx-size` fed the previous model's window into token-budget math
  forever; now keyed by (base URL, model id), so a model swap is itself a
  cache miss.
- A runtime profile's health check sent a stored `secret:<name>` reference
  verbatim as the bearer token instead of resolving it, so any profile whose
  key went through the secret store failed health checks.
- Rerun rebuilt cases from stored results but dropped `Tags`, so reruns fell
  out of per-tag benchmark insights.
- Memory saves awaited the embedding call with no timeout on the
  post-response path, so a hung embedding endpoint stalled every memory
  write for up to the full HTTP timeout; now bounded by the same 3 s class
  the query path already used (existing backfill/COALESCE semantics pick up
  the null-embedding row later).
- Memory full-text search ordered candidates by `is_pinned`/
  `importance_score`/`updated_at` instead of FTS5's own bm25 rank, so the
  lexical half of hybrid scoring measured importance, not match quality.
  Pinned/importance influence still applies downstream, where it belonged
  all along.
- `MemoryStore`/`ConversationStore` parsed stored UTC timestamps with plain
  `DateTime.Parse`, which silently converts a "...Z" string to Local-kind on
  read; every store's date parsing now goes through one
  `DateTimeStyles.RoundtripKind` helper.
- Archiving a stale memory ran the full `SaveAsync`, re-embedding unchanged
  content once per archived row purely to flip a status flag; archiving now
  does a narrow `UPDATE` of `is_archived`/`updated_at` only.
- Backup zipped live SQLite files directly, risking an internally
  inconsistent copy if taken mid-write; each database is now snapshotted
  through SQLite's own online-backup API first.
- `BenchmarkService`'s first-call initialization lacked the `SemaphoreSlim`
  gate every other store uses, so concurrent first calls could race the
  starter-suite seed into a double-insert.
- `Aether.LocalApi`'s child process never joined the app's Windows job
  object (unlike every other managed process), so an app crash could orphan
  it holding its port and per-app tokens in memory.
- Subprocess/remote voice providers (Kokoro Python, F5-TTS, XTTS, OpenAI
  voice) could only play audio through Linux players (paplay/pw-play/
  aplay/ffplay), so every non-default voice provider was synthesize-only on
  a stock Windows machine; `XttsV2VoiceProvider` separately hardcoded
  ffplay with no Windows fallback at all. One `Aether.Voice.AudioPlayback`
  helper (PowerShell `Media.SoundPlayer` on Windows, native players
  elsewhere) now backs all four providers.
- Every spoken chat reply, notification, and agent narration left a `%TEMP%`
  wav on disk forever - `GenerateSpeechAsync` never cleaned up when the
  caller (always `VoiceOrchestrator`) didn't request a persisted output
  path. Now deleted after playback in that case.
- `ServerProcessManager`'s process-exit handler read `_process?.ExitCode`
  while `Stop()`/`Dispose()` could be disposing the same `Process` on
  another thread, risking an `ObjectDisposedException` on a threadpool
  thread; it now reads from the event's own `sender` and swallows disposed-
  object access. A restart also now disposes the previous monitor
  `CancellationTokenSource` instead of leaking it.
- `NormalizeConfig` wrote resolved executable/model paths back onto the
  caller's `ServerConfig` - typically the settings object itself - silently
  rewriting a directory or bare-name configuration in memory, later
  persisted by an unrelated `SaveAsync`. It now returns a copy.
- A voice provider's failure toast never reset, so after one toast a later,
  unrelated failure for that provider stayed silent for the app's lifetime;
  a subsequent successful utterance now resets it.
- `ExtraArgsParser` treated every backslash as an escape character, so any
  Windows path in a managed server's extra args (`--mmproj C:\models\
  proj.gguf`) was silently corrupted before reaching llama-server. A
  backslash now only escapes an immediately following quote or backslash.
- Auto-tune started GPU-layer probe candidates and waited for `/health` on
  the configured port without the same preflight `StartAsync` performs, so
  a port already held by another process made every candidate look like it
  worked. Auto-tune now fails fast and names the port owner.
- The setup wizard's Phi-4 model download was never hash-verified
  (`ModelHashes` was an empty map, so the "verify if available" branch was
  dead code); now pinned via the Hugging Face LFS oid, with a failed
  verification deleting the file, matching the Doctor embedding-model
  install pattern. The advertised size ("~9GB") is corrected to ~2.8 GB.
- XTTS's "Python 3.9-3.11" requirement was never actually enforced: the
  setup wizard tested `py -3.11` candidates by validating plain `py` (their
  `PrefixArgs` were dropped before the subprocess call), the validation
  script reported a version but nothing compared it to the supported range,
  and `PythonHealthValidator` accepted any minor at or above the required
  one with no ceiling, so Doctor's XTTS check passed on Python 3.13, which
  coqui TTS does not support. `IVoiceProvider` gained an optional
  `MaxExclusivePythonVersion` so Doctor and the setup wizard's own
  validation now agree; found and fixed a second, related bug in the same
  method while adding coverage - the check-parsing loop split the captured
  log on `'\n'` but the log was built with `StringBuilder.AppendLine`
  (`\r\n` on Windows), leaving a trailing `\r` that made every "PASS" check
  compare false regardless of the interpreter.
- `rocm5.8` has never been a published PyTorch index (confirmed: HTTP 403);
  `cu118` was years stale. Both re-pinned to current indices verified
  against download.pytorch.org, and the per-backend argument construction
  is now a pure, independently testable function.

### Docs

- docs/features.md updated for Ollama streaming, the fixed llama-server
  installer, the corrected XTTS Python range label, the full-manifest
  data-root migration, and SQLite-online-backup-based snapshots.
- docs/security-review.md gains an r11 subsection covering the rebuilt
  installer's provenance decision, the closed unverified-download gap, the
  secrets-inclusive data-root migration, and the runtime-profile
  bearer-token information-leak fix.
- Services view's Extra Args tooltip documents the backslash/quoting rule.

### Fixed (field testing on the r11 build, same day)

- The setup wizard's chat-backend picker rendered every runtime profile as
  the literal string `Aether.ViewModels.RuntimeProfileViewModel` (no
  `DisplayMemberPath`/`ItemTemplate`, so the `ComboBox` fell back to
  `ToString()`). It now shows each profile's `Name`.
- llama.cpp update-in-place failed whenever a chat and an embeddings server
  shared the same binary and either was running: extraction overwrote the
  live exe/DLLs, which Windows refuses while they're memory-mapped.
  `InstallLatestAsync` now extracts into a tag-versioned subdirectory
  instead of the existing install directory, so a running server is never
  touched; it picks up the new binary on its next restart.
- The embeddings server's model picker (both the detected-models dropdown
  and the file-browse dialog) searched/opened the general models folder,
  which `FindGgufModels` explicitly excludes embed/embedding/embeddings
  subdirectories from - so it could never list or default into an actual
  embedding model. The Services view now uses `FindEmbeddingModels` and a
  new `LocalAiAssetLocator.GetPreferredEmbeddingsDirectory` for the
  embeddings row.
- Doctor's "nomic embedding model version" check offered a "Download
  embedding model" fix - re-downloading over a model that was already
  installed and working - any time the on-disk file wasn't byte-identical
  to the pinned reference build, including when the hash check passed. Both
  branches are now informational only (`canFix: false`); the primary
  "embedding model availability" check still offers a real download when a
  model is genuinely missing.
- Reopening the setup wizard after initial setup (Settings' "re-run setup
  wizard", or the chat empty-state's "Open setup wizard") showed blank Data
  root / AI assets fields instead of the current values: `SetupWizardViewModel`
  is a DI singleton whose fields are only populated once, at construction,
  which can race `ISettingsService.LoadAsync()`. Advancing past that blank
  step then saved the blanks over the real `DataRootDirectory`/
  `LocalAiAssetsRoot`, which is very likely what actually caused the wizard
  to reappear and "lose" conversation history in the first place - the data
  was never deleted, just pointed at an empty folder. `MainWindowViewModel`
  now reloads the wizard from current settings every time its panel becomes
  active.

## [0.15.0-alpha] - 2026-07-16

Implements docs/review r10 in full: the first dedicated RAG deep-dive (a
broken parent-child retrieval mode, a dataset-delete leak, a stale query
cache, an embedding-model mismatch guard, an embedding-input clamp that cut
off the back half of default-sized chunks, an inconsistent boost scale, a
3x-per-query BM25 re-tokenization cost, and eval harness gaps), plus the
three residual field-report issues from 0.14.0-alpha's first real use.

### Fixed

- **Shutdown crash with an MCP session open.** `App.axaml.cs` disposed the DI
  container synchronously; `McpToolBridge` is `IAsyncDisposable`-only, so
  `ServiceProvider.Dispose()` threw an unhandled `InvalidOperationException`
  from the window-close path. Shutdown now awaits `DisposeAsync()` with a
  bounded 5 s wait, and a guard test enumerates every singleton registration
  to catch the next async-only service before it reintroduces this.
- **Parent-child retrieval returned nothing.** `GetChunksAsync` filtered on
  `parent_id IS NULL`, which excludes every embedded child chunk instead of
  the unembedded parent bodies; with `UseParentChild` enabled, semantic scan
  saw zero candidates and BM25 scored only parent bodies. An explicit
  `is_parent` column (additive migration, backfilled from existing
  `parent_id` references) replaces the inverted filter.
- **Deleting a dataset leaked every chunk row.** `DeleteDatasetAsync` relied
  on `ON DELETE CASCADE`, but no connection ever enabled SQLite foreign-key
  enforcement, so chunks and BM25 stats for a deleted dataset stayed in
  `conversations.db` forever. Deletes are now explicit and transactional;
  store initialization also does a one-time sweep for rows already orphaned
  by the old behavior.
- **Re-ingest served stale results until restart.** Adding documents to an
  already-queried dataset never cleared `RagQueryService`'s in-memory chunk
  cache. Ingest, reindex, and remove-missing-sources now all clear it.
- **Embedding-model mismatch surfaced as a raw exception or silent garbage
  rankings.** Querying a dataset with a different current embedding model now
  skips semantic search and falls back to BM25-only with a planner note;
  ingest refuses to mix models into one dataset. `CosineScan` also filters to
  matching embedding lengths as a belt-and-braces guard.
- **A file removed from the ingest folder stayed in the dataset forever.**
  There was no way to act on the `MissingFiles` health signal. A user-clicked
  "Remove missing sources" action (never automatic) now exists.
- **Dry-run ingest wrote to the database.** The initial `SaveDatasetAsync`
  call ran regardless of `DryRun`, so a dry run created a zero-chunk dataset
  row. It's now skipped for dry runs.
- **`LastIngestPath`/`LastIngestUtc` were never persisted.** They only lived
  in memory, so the Add-to-dataset folder pre-fill worked only within one
  session. Both are now columns, set on first ingest as well as re-ingest.
- **Embeddings saw only the first ~192 tokens (roughly half) of a default
  1600-char chunk.** The metadata header (title/path/heading) was prepended
  inside the same tiny budget with no cap of its own. The clamp is raised to
  512 tokens (retry ladder 512/256/128) and the header is capped at 48
  tokens, truncating an oversized source path (keeping its distinctive tail)
  rather than the chunk content.
- **The refusal preflight scored the wrong thing.** It measured
  question-vs-context token overlap, so a question phrased in different
  vocabulary than the corpus got refused even when semantic retrieval found
  the right chunks with a strong cosine score. It now refuses only when
  retrieval itself found nothing (best semantic score below threshold AND no
  BM25 term match). A refusal now also emits the closest sources it
  considered instead of a bare sentence. The dead `SemanticPlaceholder`
  grounding mode is deleted (post-answer grounding stays token overlap).
- **Structural boosts could outrank a clear semantic winner.** The same
  small constants were added directly to both raw BM25 scores (1-10, making
  them a no-op there) and RRF fusion scores (~0.01, where they dominated).
  The metadata boost inside `Bm25Scorer` is deleted; `HybridRetriever`'s
  fusion boost is now a capped proportional multiplier that can only break
  ties and lift near-ties.
- **BM25 re-tokenized the whole corpus up to 3x per query.** Each of up to 3
  query variants re-tokenized every candidate chunk's full content. TF is now
  computed once per query and reused across variants.
- **Dataset health loaded every chunk's full content on every refresh.**
  `RefreshDatasetManagerAsync` runs after every ingest, delete, and app load;
  it now loads only the source path/chunk index/modified-timestamp columns
  health actually needs.
- **Retrieval-only eval mode could never pass a `should_refuse` case.**
  `Passed` required both a retrieval hit and `!ShouldRefuse`, which is never
  true when a case expects a refusal. It now evaluates the same
  retrieval-strength gate the live query path uses.
- **Evals were uncancellable from the UI.** `RunEvalAsync` passed
  `CancellationToken.None`; a Stop button and wired cancellation token now
  match the pattern ingest already had. A cancelled eval never reaches the
  export step, so no partial run is written.
- **Voice: a dictionary miss gained a spoken trailing "e".** The
  letter-fallback's Magic-E rule checked the wrong side of the trailing "e"
  (silent only when the *preceding* character was a vowel, backwards from
  English's actual vowel-consonant-e pattern), so any out-of-dictionary word
  like "joke" spoke as "jok-e". Typographic characters LLM output produces
  constantly (curly quotes, em/en dashes, ellipsis) were not stripped either,
  which is why capitalized/quoted words missed the dictionary in the first
  place. Both are fixed; every word that reaches the fallback is now logged
  once per session for the next pronunciation report.

### Added

- **Reindex action.** Re-embeds every stored chunk of a dataset with the
  current embedding model, from stored content only (no source files
  required), then rebuilds BM25 stats and the query cache.
- **llama-server prompt-processing timings on the chat trace.** When
  llama.cpp reports its own `timings` object, the send trace now shows
  `server prompt N tok / X ms` alongside the existing pre-stream breakdown,
  decomposing a large first-token wait into request-queue time versus actual
  prompt evaluation. A send whose pre-first-token wait exceeds 10 s logs a
  runtime warning with the full breakdown.

### Docs

- docs/rag.md, docs/voice.md, docs/features.md, and docs/security-review.md
  updated for all of the above.

## [0.14.0-alpha] - 2026-07-15

Implements docs/review r9 in full: the send-path latency and orphaned-server
issues that shipped as documentation-only alongside the 0.13.1-alpha crash
fix are now implemented, plus the full UI-thread-safety sweep the crash fix
started.

### Fixed

- **HTTP timeout misreported as a user cancel.** `ServerProcessManager`'s
  health-check poll used a 2 s `HttpClient` timeout; that timeout throws
  `OperationCanceledException` too, indistinguishable by type from a real
  caller cancellation, so a slow (not dead) health check could silently
  overwrite an already-diagnosed Error status with an unexplained Stopped.
  Only a genuinely cancelled token now escapes the retry loop.
- **Untuned `Task.Run` wrapper caused a benchmark-history race.**
  `BenchmarkViewModel`'s suite-change handler wrapped an already-`await`-based
  reload in `Task.Run`, mutating UI-bound collections off the UI thread.

### Added

- **Send-path instrumentation** (`ChatSendTiming`). Every send now measures
  memory recall, injection selection, lesson context, prompt build, and
  first-token wait, surfaced in `PerformanceLog` and persisted on the chat
  trace.
- **Embedding backfill moved off the send path** (`MemoryStore.
  RunEmbeddingBackfillAsync`). `SearchAsync` embeds only the query now;
  backfill runs from a background pass at startup and after writes, with a
  per-row cooldown and attempt cap so a down embedding endpoint cannot tax
  every send.
- **Fast-fail query embedding.** A 3 s timeout on the query embed falls back
  to FTS-ranked recall instead of inheriting the embedding HTTP client's 60 s
  timeout; the fallback logs once per process.
- **Embedding endpoint fallback visibility.** A blank `Rag.EmbeddingBaseUrl`
  falling back to the chat server now logs once and raises a Doctor advisory.
- **Oversized-context advisory.** Services view and Doctor both flag managed
  servers configured with `ContextSize` above 16384.
- **Job-object process containment** (`ProcessJobObject`). Managed servers,
  auto-tune probes, and voice-engine (XTTS/Kokoro) children join one Windows
  job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, so they die with the
  app however it dies.
- **Port preflight with a named owner** (`PortOwnerLookup`). A conflicting
  port fails instantly, naming the port and (best-effort) its owning process,
  instead of launching a doomed process.
- **Orphan detection with an explicit Stop** (`OrphanServerDetector`). A
  leftover server from a previous session is identified by an exact
  executable-path match and offered a Stop button; anything else is reported
  only. Stopping re-verifies the PID and executable immediately before
  killing it.
- **`ViewModelBase`** (`Aether.ViewModels`). One `RunOnUi`/`RunOnUiAsync`
  implementation, replacing three private copies plus `TtsSettingsViewModel`'s
  bespoke `SynchronizationContext` usage.
- **Architecture test**: any public `ObservableCollection<T>` property on a
  ViewModel now fails the build; every ViewModel collection is
  `UiBoundCollection<T>`.

## [0.13.1-alpha] - 2026-07-15

Emergency stability fix for a crash found in first sustained field use of
0.13.0-alpha, plus the r9 review pack (`docs/review/`) covering the two
remaining field issues (send-path latency, orphaned server processes).

### Fixed

- **Cross-thread UI collection crash.** The app could hard-crash with
  Avalonia "Collection was modified; enumeration operation may not execute"
  in `PanelContainerGenerator` during chat use. Root cause: UI-bound
  `ObservableCollection`s mutated from worker threads. Marshaled the
  offending writers through the UI thread's `SynchronizationContext`:
  `ChatViewModel`'s debounced context-usage refresh (ran entirely on a
  `Task.Run` thread), persisted chat-trace loading, and memory status
  refresh; `LogsViewModel`'s `LogAdded` handler (rebuilt the visible log
  list on whichever background thread wrote the log entry).

### Added

- **`UiThreadGuard` / `UiBoundCollection<T>`** (`Aether.ViewModels`).
  ItemsControl-bound collections in `ChatViewModel` and `LogsViewModel` now
  throw immediately, with the offending call in the stack, if mutated off
  the UI thread. Armed once by the Desktop app at framework init; inert in
  headless tests (xunit's `SynchronizationContext` legitimately hops
  threads, so the guard deliberately does not arm on context presence).
  The r9 pack extends this to every ViewModel.
- **r9 review pack** (`docs/review/`): send-path latency instrumentation
  and embedding backfill relocation, job-object child-process containment,
  port preflight and orphan detection, full UI-thread-safety sweep with an
  architecture test.

## [0.13.0-alpha] - 2026-07-15

Implements docs/review r8: voice pronunciation, onboarding, performance, and
technical debt. Theme: polish what exists rather than add new capability.
Also reviews and corrects two optimization commits (`ad618da`, `aea2326`)
that landed between r6 and r7 without a review pass.

### Voice pronunciation

- **Text normalization** (`Aether.Voice/KokoroTextNormalizer.cs`). Numbers,
  currency, percentages, ordinals, clock times, and a handful of standalone
  symbols (`&`, `+`, `/`, `@`, `=`) are expanded to plain English words before
  phonemization. Fixes the bug where digits were silently dropped entirely
  (`MapLetter` mapped non-letters to empty, and the tokenizer skipped
  unrecognized characters) - "You have 3 errors" previously spoke as "You
  have errors".
- **CMU Pronouncing Dictionary** (`Aether.Voice/CmuPronouncingDictionary.cs`,
  `ArpabetIpaMap.cs`). The ~250-word inline dictionary and its letter-by-letter
  rule fallback are replaced as the primary pronunciation source by the
  ~126,000-word CMUdict (BSD-style CMU license; see `THIRD-PARTY-NOTICES.md`),
  embedded gzip-compressed. ARPABET phones map to Kokoro's IPA vocabulary with
  stress marks; unstressed vs. stressed AH (schwa vs. wedge) is handled
  specially. The rule-based fallback still exists for genuinely unknown words,
  fixed to no longer emit the vocabulary-unsupported "ɝ" symbol and to read
  word-initial "gh" as a hard /g/ (ghost) rather than /f/ (a rule meant only
  for word-final "gh", as in laugh).
- **User pronunciation lexicon** (`Aether.Voice/KokoroUserLexicon.cs`) at
  `{DataRoot}/voice/lexicon.txt`, `word = ipa` per line, reloaded when the
  file changes. Seeded with defaults for words no dictionary would know,
  including the app's own name ("Aether" was mispronounced before this).
  Settings > Voice gained an "Open pronunciation lexicon" button.
- **Suffix morphology**: possessives and common suffixes (`'s`, `s`, `es`,
  `ed`, `ing`) retry against the lexicon chain with correct voicing rules
  (e.g. "aether's" resolves via the stem "aether" plus a voiced /z/).
- **Unknown acronyms** are spelled out by letter name (e.g. "GGUF" as "gee
  gee you eff") rather than mispronounced as a word; acronyms cmudict
  already knows (api, cpu, html, usa) use their real dictionary entries.
- ~20 golden regression sentences assert exact IPA output and zero dropped
  characters through tokenization.

### Onboarding and usability

- **Guided starter model download** (`Aether.Services/StarterModelCatalog.cs`).
  The setup wizard's model step offers a hardware-aware download (three
  VRAM tiers, Qwen2.5 3B/7B/14B Instruct Q4_K_M) alongside the existing
  manual path picker, with SHA256 verification via the existing
  `ModelDownloadService`.
- **Voice install from the wizard**: the Voice step gained an "Install now"
  button for Kokoro (native) that calls the same `IDoctorService` install
  path Settings/Doctor already use, so first-run setup can finish chat and
  voice in one pass instead of requiring a separate trip to Settings.
- **Markdown tables and clickable links** (`Aether.Desktop.Controls.MarkdownViewer`).
  Tables (parsed by Markdig but previously unrendered) now render as a grid
  with a bold header row. Links are clickable (embedded via
  `InlineUIContainer`, since Avalonia's text-flow `Inline`/`Run`/`Span` have
  no pointer events) with an https/http-only scheme gate; anything else
  (`file:`, `javascript:`, `data:`) renders as inert styled text.
- **Empty states**: Chat gets a distinct "no chat model configured" state
  with links to the setup wizard and Services (RAG and Memories already had
  empty states); Agent's workspace field and Benchmark's run history gained
  matching empty-state guidance.
- **Tooltip sweep** across the settings sections, Doctor, Memories, Model
  Management, Logs, and System Overview views that previously had none.

### Performance

- **Startup phase timing**: `App.axaml.cs` logs settings/store-init/voice-probe/
  viewmodel timings plus a total, so future startup work has a baseline.
- Embedding-model warm-up moved off the startup critical path (fire-and-forget
  after the UI is usable, logged separately when it completes) instead of
  blocking every launch behind an ONNX load.
- **Memory recall regression fix** (`Aether.Services/MemoryStore.cs`):
  `aea2326` had restricted hybrid-recall candidates to FTS hits plus a hard
  cosine > 0.7 threshold, and fetched each non-FTS candidate individually
  (N+1 queries). Every embedded row is scored again; only the plausible
  top-K non-FTS ids are hydrated, in one batched `WHERE id IN (...)` query.
- **Chat pagination**: conversations render only the newest 100 messages
  (`ChatViewModel.VisibleMessages`), with a "Show earlier messages" button
  to reveal more. Persistence, memory extraction, and prompt-history
  truncation still operate on the full message list.
- **Incremental markdown re-render**: `MarkdownViewer` now reuses each
  top-level block's existing control when its source text is unchanged
  between renders, rebuilding only from the first changed block onward,
  instead of rebuilding the entire document on every streaming debounce tick.
- Audited every model-restart call site; both existing guards
  (`ServicesViewModel.SelectModelAndRestartAsync`'s path comparison,
  `BenchmarkViewModel.PrepareModelAsync`'s model-ID fast-skip) were already
  correct and are now covered by tests. Chat model switching and
  `Aether.LocalApi` have no restart call sites.

### Technical debt

- **Harness registration guard** (`HarnessRegistrationGuardTests`): a
  reflection-based test asserts every public parameterless Task-returning
  method on the custom harness's test classes is registered in
  `HarnessCases` exactly once. Immediately caught six previously-dead tests
  across the codebase (beyond the two r6 already found) that existed but
  never ran: `AcceptanceTests.AgentUiAcceptance_PatchQueueMetadataIsRendered`,
  `AcceptanceTests.TraceSchema_FileIsValidAndSampleConforms` (also fixed a
  broken relative path in the same test), `RagTests.RagDirectoryIngestPersistsCompletedFileBatches`,
  `ServiceTests.BenchmarkExportAllCreatesBatchFolder`,
  `ServiceTests.MemoryStoreCountsByConversationWork`, and
  `TtsTests.VoicePreviewSkipsBlankText`. All six now run and pass.
- `SettingsSectionViewModels.cs` (881 lines, 11 classes) split one class per
  file; `DoctorService.cs` (1411 lines) split into partial-class files by
  domain (Startup, Storage, Runtime, Voice, Rag, System, Benchmarks). Both
  are pure moves with no logic changes.

### Fixed

- The chat "thinking" pulse animation (`aea2326`) never actually ran: it
  bound to `IsGenerating` on `MessageViewModel`, which has no such property,
  and set a `RenderTransform` via both a malformed attribute and a property
  element. Now bound to the message's own `IsStreaming`.

56 new tests (461 -> 517). Coverage floor: 49 -> 50 (narrative target).

## [0.12.0-alpha] - 2026-07-15

Implements docs/review r7: the Agent Scenario Suite, a library of small,
deterministic scenario workspaces that regression-test emergent agent
behaviour (retrieval, memory, the safety gate, approvals, and lessons
interacting) instead of testing any one feature in isolation. Every check is
a pure predicate over recorded artifacts (task status, safety-gate rows,
file hashes, answer substrings) - no LLM judge.

### Agent Scenario Suite

- **Engine** (`Aether.Agent`). `AgentScenarioManifest`/`AgentScenarioExpectations`
  model a scenario (goal, max steps, auto-approved tools, seeded memory/lessons,
  files placed outside the workspace for traversal tests, expectations).
  `AgentScenarioChecks` is a pure evaluator covering 11 deterministic check
  types (final status, approval-required/blocked/forbidden-execution per
  tool, files read/unread, files changed/unchanged, answer must/must-not
  mention, max new lessons, minimum gated risk, revertible-patch capture).
  `IAgentScenarioStore` loads built-in scenarios (shipped under
  `agent-scenarios/` next to the app) plus user scenarios from
  `{DataRoot}/agent-scenarios`, with user scenarios overriding built-ins by
  id. `IAgentScenarioRunner` executes a scenario against a real
  `AgentService` in a fully isolated sandbox: a copied workspace, a
  throwaway agent data root (its own task store and lesson store), and
  approvals that only ever grant (per-scenario `auto_approve` list) or are
  left pending - a scenario run can never deny (which would itself write a
  rejection lesson) and never runs an unapproved command. The user's real
  agent data root is untouched by any scenario run.
- **Library**: ten built-in scenarios, one behaviour each - conflicting
  documentation, prompt injection in a workspace file, secrets that must
  stay out of answers, a declared command that still needs approval, an
  undeclared command family that gets blocked outright, stale remembered
  context versus current code, a path-traversal read attempt, an
  approved-and-reverted edit, honest admission of undocumented behaviour,
  and lesson-store hygiene on a trivial task.
- **Workbench UI**: a "Scenario Evals" panel on the Agent view runs the
  suite (or one scenario) against the workbench's selected model with live
  progress, per-row pass/fail with failed-check detail, and a headline
  pointing at the exported report (`eval-runs/{suiteId}/report.md` +
  `run.jsonl`, alongside a new `EvalMode.AgentScenario` projection into the
  shared eval store).

### Other

- Doctor: llama-server-missing check now offers a "Download llama.cpp" fix
  action; build-number parsing handles the current `version: NNNN`
  llama-server output (no `b` prefix); tray-support check distinguishes
  confirmed Windows/macOS support from advisory-only Linux support.
- Model Management merges detected local GGUF files with runtime-reported
  models and shows a Running badge per model.
- Services: managed llama-server model path gets a detected-model dropdown;
  saving a managed server's port now syncs `Llm.LlamaCppBaseUrl` /
  `Rag.EmbeddingBaseUrl` and any linked runtime profile so chat/RAG follow
  the port instead of silently pointing at a stale one.
- Chat: forcing a model refresh now invalidates `CompositeLlmService`'s
  cached model list instead of returning stale data.
- Memory: `HybridRerankAsync` narrowed its SQLite scan to `id, embedding`
  and defers loading full high-relevance rows, cutting prompt-prep I/O.
  Embedding model warms up during app startup to avoid first-message
  latency. Kokoro phonemizer gained Magic-E vowel shifting, more digraph
  coverage, rhotic vowel rules, and dedicated affricate phoneme tokens.
- Fixed a shutdown crash by wrapping the exit handler's `ServiceProvider`
  disposal in try/catch/finally.

## [0.11.0-alpha] - 2026-07-12

Implements docs/review r6 in full: answerability. A first-time user can now
answer, from visible UI, where their data lives, whether anything leaves the
machine, which model answered, why files/chunks were selected, why a patch
was flagged risky, and whether they can undo it.

### Answerability surfaces

- **Local/remote badges.** Chat's model selector shows a "Local"/"Remote"
  badge (`ChatViewModel.IsSelectedModelRemote`, driven by the shared
  `CompositeLlmService.Providers` registry, the same source Privacy Audit
  already used).
- **Outbound-destinations summary.** System Overview's Privacy Audit is
  retitled "Privacy audit - what can leave this machine" and gains a
  one-line count (`PrivacyAuditService.CountOutboundDestinationsAsync`)
  across remote chat/voice providers, web-ingest-enabled RAG datasets, and
  MCP servers.
- **Data root affordances.** Settings > Data gets "Open folder" buttons for
  the data root and AI assets root (resolved live, not the raw text box),
  plus a "Your data, your machine" explainer and matching statement in
  System Overview.
- **Toolbar labels.** `Ui.ShowNavLabels` (default off) adds text captions
  next to the 13 previously icon-only toolbar buttons.
- **Per-message model attribution.** Audited and found already correct
  end-to-end (`Message.ModelId`/`DurationMs` round-trip through
  `messages_json`); added a regression test rather than new code.
- **RAG retrieval breakdown.** `RagTraceChunk` now carries per-signal vector/
  keyword/rerank scores and a deterministic plain-language summary ("Ranked
  2nd of 8: strong semantic match, term 'x' matched 3 times, reranker
  confirmed this ranking"), shown in the RAG source inspector.
- **Agent risk reasons + recipe transparency.** `AgentPendingToolAction`
  carries the safety gate's `Reason`; the review queue shows it plus, for a
  pending `run_command` approval, what it actually executes
  (`AgentApprovalPreview` - the exact npm script body from package.json, or
  a fixed provenance note for dotnet/cargo/pytest).
- **Agent context receipt.** Each step's context pack summarizes per
  section (Memory/RAG/Workspace files/Project instructions/Lessons) as item
  count + token estimate (`AgentContextReceiptBuilder`), omitting empty
  sections rather than showing them blank.
- **Applied-patch revert.** `AgentDraftPatch` captures a pre-image at apply
  time (both the manual draft-patch queue and direct `edit_file`/
  `create_file`/`apply_draft_patch` approvals); Revert restores it or
  deletes a created file, refusing if the file changed again since.
- **New-lesson review strip.** A task reaching a terminal state with newly
  *created* (not merely reinforced) lessons shows them once with Keep/Retire
  actions, tracked via `AgentTaskState.NewLessonIds`.

### Usage-aware benchmark insights

- **`model_usage` rollup.** `SqliteTraceStore` maintains a durable per-
  model/day/kind counter table alongside the existing trace log, unaffected
  by trace pruning. `IModelUsageService` exposes windowed summaries.
- **Usage insights.** `BenchmarkInsightsMath.BuildReport` takes optional
  per-kind usage; for any activity (Chat/RAG/Agent) with 20+ calls in 30
  days it names the dominant model, and recommends a switch only when a
  real leaderboard entry outranks it by the same 10-point gap threshold as
  the r5 Doctor advisory. A "Based on your usage" card appears in the
  Benchmarks Insights tab; the Doctor advisory gets at most one appended
  usage-aware sentence, Info severity only.

### Platform cleanup

- **Removed `InspectionEngine`/`IInspectionCheckProvider`.** Dead
  aggregation layer nothing consumed; Doctor/Trust/Privacy Audit already
  each own their checks and views directly.
- **Remote voice disclosure.** Settings > Voice shows an inline note per
  enabled channel when the active provider is remote; Privacy Audit gains a
  matching item.

Coverage floor raised 47 -> 48.

