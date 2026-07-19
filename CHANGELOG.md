# Changelog

All notable changes to Aether will be documented in this file. Append newapp versions above the previous version (not the bottom of the doc or where ever feels right at the time).

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

## [0.22.0-alpha] - 2026-07-19

Implements docs/review r17 in full: hardware-aware context fit and benchmark
truth. One theme across both fronts: numbers the app shows about models and
hardware must be measured or derived, not guessed. A new internal GGUF
header-metadata reader (layer count, KV head count, head dims, training
context, quantization - never tensor data) makes both honest.

### GGUF metadata and KV-cache math

- **`GgufMetadataReader`** (`Aether.Services`): a small internal parser for
  the GGUF header's metadata key/value section only. Bounds-checked against
  untrusted downloaded files (64 KiB string cap, 1,000,000 array-element cap,
  100,000 metadata-key cap, every read checked against the actual file
  length); malformed or truncated input returns null rather than throwing.
  Process-lifetime cached by (path, size, mtime).
- **`KvCacheMath`** (`Aether.Services`): deterministic
  `block_count * head_count_kv * (key_length + value_length) * bytesPerElement`
  KV-cache estimate, with `--cache-type-k`/`--cache-type-v` overrides and
  offload-fraction scaling for partial GPU layers. A known, documented
  overestimate for sliding-window-attention models (the gemma family).
- **`ModelFitEstimator`** gains a KV-cache-aware overload (null GGUF info
  falls back byte-identically to the existing size-only estimate); the
  Models page's fit chips now state the weights/KV split at the model's
  configured or default context for local files.
- **Services page context-fit warning** replaces the flat "above 16384
  tokens" rule with hardware-aware VRAM/RAM math (weights + KV cache vs. the
  detected GPU or system RAM) whenever a local GGUF header and a hardware
  profile are both available, falling back to the old flat rule otherwise so
  the warning never silently disappears. A training-context advisory is
  appended independently when the configured context exceeds what the model
  was trained at. Both are informational only - nothing here edits a value or
  blocks Start.
- **Auto Tune learns about context**: when the configured context does not
  fit the GPU at full offload, `AutoTuneAsync` probes one additional
  candidate (all layers at the largest context from a fixed ladder that
  still fits, capped at the model's training context) before its usual
  layer-by-layer descent at the originally configured context. This is the
  only place in the app that changes a context size automatically, and it
  only runs from the explicitly user-clicked Auto Tune action.

### Benchmark truth

- **Real server timings**: `RunCaseAsync` now streams via `StreamChatAsync`
  events instead of the text-only stream, capturing llama-server's own
  prompt/decode timings object. When a provider reports them, tokens/sec is
  `predicted_n / predicted_ms * 1000` (a measurement) and prompt speed is
  shown alongside it; each result is labeled `server-timings` or
  `chars-approx` so measured and estimated numbers are never confused.
- **Honest fallback math**: the chars/4 estimate (used when a provider
  reports no timings) now divides by the decode window (total time minus
  first-token latency) instead of total elapsed time, so a long prompt is no
  longer counted as slow decode twice.
- **Neutral resource score**: `ResourceScore` used to be Aether's own process
  RSS delta - noise for a model running in `llama-server` (a different
  process) or on a remote endpoint. It is now always neutral (1.0); the
  before/after memory and VRAM snapshots stay for display.
- **Honest run metadata**: runs against a managed local GGUF model now carry
  the real context size/GPU layers/thread count/model path from the managed
  server actually configured to serve that model, and a real quantization
  label from the GGUF header, instead of stamping app-process values
  (`Environment.ProcessorCount` threads, empty quantization) on every run
  regardless of the model. `RuntimeKind` is derived from the model's own
  provider tag instead of a hardcoded `"dotnet"`; Insights normalizes legacy
  `"dotnet"` runs at load time (in memory only) so old and new runs of the
  same model keep aggregating together.
- **Rerun fidelity**: `RerunAsync` now resolves the live model instance from
  the provider first (falling back to a thin reconstruction only if the
  model no longer exists), so a rerun's metadata is not hollowed out by
  losing `DefaultContextSize`/tags/profile linkage.
- **Cold means cold**: since r14 made `cache_prompt: true` unconditional,
  llama-server could retain a previous request's KV across benchmark runs,
  letting a "Cold" run's first case get a warm prefill. A new
  `LlmChatOptions.DisablePromptCache` (honored only by `LlamaCppService`,
  benchmark-only in practice) is set for each case's iteration 0 and left
  false for warm iterations; the chat path is untouched and keeps
  `cache_prompt: true` as its default.
- **No more dropdown restarts**: selecting a model in the Benchmarks dropdown
  no longer stops and restarts the live managed chat server (previously a
  1-2 minute operation on large models) - it only updates a status hint.
  `RunAsync` already switches the server when actually needed via
  `PrepareSelectedModelAsync`.

57 new tests (884 -> 941). `docs/security-review.md` gained an r17 subsection
for the GGUF header parser as untrusted-input surface. `docs/review/`
archived to `docs/review/archived/r17/`.

## [0.21.0-alpha] - 2026-07-19

Implements docs/review r16 in full: orchestration hardening, memory
integrity, and workbench truth. An independent audit of r15's sub-task
orchestration (the first since it shipped) found a stuck-forever failure
mode and a workspace-identity gap; a full audit of the memory subsystem
(never previously reviewed on its own) found that "forget" did not actually
forget and the `[MEMORY: ...]` save-marker feature was a phantom; a desktop
audit found the recent-tasks list r15 deferred, plus several truth/hygiene
gaps.

### Fixed

- **Orchestration could wedge permanently on a child completed outside the
  loop.** A child task reaching `Complete`/`Failed` other than through its
  parent's own orchestration loop (opened directly and stepped to
  completion, or resumed via the review queue while the child itself was
  open) left the parent's sub-task spec stuck `Running` forever; every
  later run of the parent then threw "Agent task is already finished."
  `RunOrchestrationAsync` now reconciles each non-terminal spec against its
  child's actual persisted status at the top of every iteration before
  choosing what to run next. The review queue's child entries now carry
  `ParentTaskId`, and approving a child's pending action resumes the
  parent's orchestration by that id directly, instead of inferring
  parenthood from whichever task happens to be open in the workbench.
- **A manual "Run Step" click could bypass orchestration entirely.**
  `AgentService.RunStepAsync` now refuses (with a message pointing at
  `RunAsync`) to run a bare parent model step while any sub-task is
  unfinished, closing two corruptions: the parent answering "final" with
  children unrun, and the parent silently re-proposing `plan_subtasks`
  and discarding the in-flight plan. The workbench's Run Step button now
  advances the orchestration loop instead when the open task has unfinished
  sub-tasks. A task whose plan already exists is also blocked from
  accepting another `plan_subtasks` proposal at the safety-gate level and
  rejected again as defense in depth if a stale approval races in anyway.
- **Approvals executed against the wrong workspace.** The review queue lists
  tasks across every workspace, but approving a pending `edit_file`/
  `create_file`/`run_command` action executed it against whatever workspace
  the workbench currently had active, not the workspace the task was
  actually created in. `AgentTaskState` now persists its own
  `WorkspaceRoot` at creation (and children inherit their parent's), and
  approval execution uses it; a null-options approval with no stored root
  now throws instead of silently stranding the task `Running` with an
  unexecuted pending action. Review-queue rows show the task's own
  workspace folder name when it differs from the active one.
- **A paused child left its parent lying about its own status.** While a
  child sub-task waits on an approval or reply, the parent orchestration
  task now mirrors that pause (`WaitingForUser`/`Blocked`) and names which
  sub-task it's waiting on, instead of showing `Running` indefinitely with
  nothing actually happening.
- **Agent task index timestamps parsed as local time.** `FileAgentTaskStateStore`
  now parses with `DateTimeStyles.RoundtripKind`, matching the lesson
  store's existing correct parsing.
- **`SearchAsync` returned archived memories.** Neither the FTS branch nor
  the LIKE fallback filtered `is_archived`, so a memory retired via
  `[MEMORY_FORGET: id]` (or the stale-archival sweep) kept resurfacing in
  chat injection whenever the query matched lexically - "forget" did not
  forget. Both branches now exclude archived rows.
- **`Memory.AutoArchiveAfterDays` was a placebo.** `ExpirationDate` was
  written by auto-summary but never read anywhere. `ArchiveStaleMemoriesAsync`
  now archives any non-pinned row past its expiration date on top of its
  existing staleness rule, and `SearchAsync` excludes an expired-but-not-
  yet-swept row at read time as a second layer. A pinned memory with a past
  expiry still survives, consistent with every other lifecycle rule.
- **Memory injection scoring ignored lifecycle decay.** Selection blended
  relevance with raw `ImportanceScore` while the archiver already applied
  `MemoryLifecycle.ComputeEffectiveImportance` (30-day half-life), so a
  memory one day from stale-archival could still outrank a fresh one at
  injection time. Injection scoring now uses the same decayed importance.
- **Services nav "any running" dot went stale.** Its converter only
  re-evaluated when the whole `Servers` collection was replaced, not on a
  per-server Start/Stop/crash. Replaced with `ServicesViewModel.AnyServerRunning`,
  raised wherever per-server status transitions already flow through.
- **Ctrl+Q quit instantly with no confirmation**, including with focus
  inside a text box mid-thought and a generation or agent run in progress.
  Removed from the local hotkey set; Quit remains available via the tray
  menu and window close.

### Added

- **The `[MEMORY: ...]` save marker is wired up.** The instruction teaching
  the model this marker was already sent, but nothing in live chat ever
  extracted or saved it - a model that tried got the save silently deleted
  (when memories were injected, the cleanup step stripped it without
  saving) or shown raw to the user (when nothing was injected, nothing ran
  at all). The chat send path now teaches the marker whenever memory is
  enabled (regardless of recall hits), and every response now runs a single
  marker pipeline that applies `[MEMORY_UPDATE]`/`[MEMORY_FORGET]` for
  injected ids, extracts and dedupe-saves up to 3 new `[MEMORY: ...]` facts
  through the same merge path auto-summary uses, and always strips marker
  syntax from the persisted transcript.
- **Memory embedding-dimension mismatch detection and re-embed.** An
  embedding-model switch left every existing memory vector at the old
  dimensionality, silently zeroing its semantic recall score forever with
  no signal anywhere. Memories now carry an `embedding_dim` column; the
  Memories page shows a "N memories were embedded with a different model"
  banner with a user-clicked "Re-embed memories" button that clears the
  stale vectors and triggers a background re-embed. No automatic re-embed
  on a settings change, and no Doctor check - the Memories page is the
  surface.
- **Recent-tasks list.** `AgentViewModel.RecentTasks` was populated since
  r15 but nothing in the UI bound it, so a completed task's report, a
  failed task's blockers, or an orphaned-Running task were unreachable
  after an app restart except through the review queue. The Agent view now
  lists recent tasks (status chip, goal, relative time, sub-task rows
  indented with a tag) with an Open action; the review queue also gained an
  Open button. Opening a child directly now also shows its parent's goal.
- **Conversation delete confirmation.** Every other destructive action of
  this weight (RAG dataset delete, reindex, benchmark history clear, backup
  restore) was already confirm-gated; a conversation's context-menu delete
  was the one exception. It now shows the same `ConfirmActionDialog` used
  elsewhere.

### Changed

- Desktop hygiene: the tunnel-phase wheel-scroll hijack (ModelManagementView,
  ServicesView, SettingsView) is now one shared `WheelScrollHelper` instead
  of three copies; the four identically-shaped bool-to-"X.../X" label
  converters in `ModelManagementView.axaml.cs` are now one generic
  `BoolToTextConverter`; status-chip colors that were drawn from three
  different vocabularies (named `Brushes.X` values vs. two slightly
  different sets of material hex) now share one `StatusPalette`. No
  behavior change.

23 new tests (861 -> 884). Zero-warning build maintained throughout.

## [0.20.0-alpha] - 2026-07-19

Implements docs/review r15 in full: sub-task orchestration. A vague, broad
goal used to burn the whole transcript budget before the agent got anywhere;
`plan_subtasks` now lets it split such a goal into focused sub-tasks that
each run through the exact same loop, safety gate, and approval flow as
today, sequentially, with a consolidated report at the end.

### Added

- **`plan_subtasks`: approval-gated sub-task orchestration.** A new tool
  proposes splitting a broad, multi-domain goal into 2-6 focused sub-tasks,
  each with a goal, a specialist profile (`general`, `correctness`,
  `security`, `tests`, `performance`, `docs`), and success criteria. Always
  requires approval, with a full preview of the proposed plan. Once
  approved, children run sequentially as ordinary tasks (`ParentTaskId` set,
  own transcript, own lessons, own `RememberedCommandApprovals` - never
  shared with siblings or the parent) through `AgentService`'s existing
  loop. Depth is limited to one level in code: a child requesting
  `plan_subtasks` is blocked immediately, not by prompt instruction. The
  whole run is capped by `Agent.MaxOrchestrationSteps` (default 60,
  separate from each child's own `Agent.MaxAutoSteps`); hitting it marks
  remaining sub-tasks `Skipped` and synthesis says so honestly rather than
  pretending the run finished normally.
- **Consolidated synthesis report.** Once every sub-task is terminal, the
  parent runs one final model step to synthesize a report from each child's
  outcome and writes it to `report.md` in the task directory. A
  deterministic fallback (built from the sub-task specs themselves) takes
  over if that synthesis step fails or returns nothing usable, so a flaky
  last step never fails a run whose sub-task work already completed.
- **Workbench orchestration UI.** A sub-task strip shows live status
  (pending/running/complete/failed/skipped) per sub-task with an "Open
  report" affordance once synthesis has run. A child's pending approval
  surfaces in the shared review queue labeled with its parent's goal, and
  approving it resumes the parent's orchestration rather than stalling.
  Step/status text is labeled with the active child's sub-task position
  while an orchestrated run is in progress; the open task in the workbench
  stays pointed at the parent throughout.
- **Three new built-in scenarios** (`11-orchestration-gate`,
  `12-orchestration-depth`, `13-orchestration-budget`) plus two new
  deterministic scenario check types (`expect_subtask_statuses`,
  `expect_report_contains`) exercising the gate, the depth block, and
  budget truncation.

### Fixed

- **A gated action with no registered executor stranded the task.** When the
  safety gate required approval for a tool with no local executor (e.g. an
  `mcp:` tool the bridge isn't wired for), the task landed `WaitingForUser`
  with nothing to approve - `AppendUserReplyAsync` was the only way out,
  which a user had no reason to guess. It now lands `Blocked` with an
  explanatory result, matching the existing allowed-but-unexecutable case.
- **A model-reported blocker could silently vanish or flip status twice.**
  `state_update.blockers` set the task `Blocked`, but the blocker text was
  never recorded anywhere, and if the same response also requested an
  allowed tool, the later execution path silently overwrote the status back
  to `Running` with no trace the blocker ever happened. Every blocker is now
  recorded in `Decisions` regardless of outcome, and `Blocked` only wins
  when the step's tool did not go on to execute successfully this step
  (progress wins otherwise) - `ask_user`/`final` handling is unchanged.
- **Chat responses appeared all at once instead of streaming.** `MarkdownViewer`'s
  75ms render-debounce timer was stopped and restarted on every content
  change; since streamed tokens arrive faster than that, the timer kept
  getting pushed back and only ever fired once the stream paused, i.e. at the
  end. It now leaves an already-running timer alone, giving a steady render
  cadence during a stream instead of a debounce that never fires.
- **Sending a message scrolled the chat view back to the top.**
  `ChatViewModel.RefreshVisibleMessageWindow` did a full `Clear()` and rebuild
  of the windowed message list on every send, which collapses the
  `ScrollViewer`'s content to zero height and snaps it to the top before the
  explicit scroll-to-bottom call can catch up. It now appends just the new
  message(s) in place when the window's start position hasn't shifted.
- **"Ctrl+Enter to send" had no effect on plain Enter.** The chat input's
  `AcceptsReturn` was hardcoded `true` in `AppStyles.axaml` regardless of the
  `Ui.CtrlEnterToSend` setting, so Avalonia's own newline-insertion consumed
  Enter before the app's key handler ever saw it. `AcceptsReturn` is now set
  dynamically from the setting (and kept live via `SettingsChanged`).
- **Dragging over an assistant response only selected one block at a time.**
  `MarkdownViewer` renders each markdown block (paragraph, code fence, list
  item, table cell) as its own independent selectable control, so a drag
  couldn't span block boundaries. A drag that crosses from its starting
  block into another now selects every block in the response as a whole, and
  Ctrl+C copies the message's full raw markdown directly.
- **Nothing stopped a second Aether process from launching.** Two instances
  writing to the same SQLite data root with no cross-process coordination is
  a real corruption risk. A `SingleInstanceGuard` now holds an exclusive
  lock file (`%LocalAppData%/Aether/aether.lock`) for the process lifetime;
  a second launch attempt exits immediately instead of opening a second
  window. The OS releases the lock automatically on exit, crash, or kill, so
  there is never a stale lock to clean up.

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
