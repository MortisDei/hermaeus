# Changelog

All notable changes to Hermaeus will be documented in this file. Append newapp versions above the previous version (not the bottom of the doc or where ever feels right at the time).

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

## [0.25.0-alpha] - 2026-07-22

Implements docs/review r20 in full: the product is renamed from Aether to
Hermaeus (after Hermaeus Soter, the Indo-Greek king) ahead of going public.
The go-public trademark check found "Aether" carries real risk: multiple
live USPTO Class 9 registrations plus several existing local-AI desktop
projects already use the name.

This touches every namespace, project, and assembly (`Hermaeus.Core`,
`Hermaeus.Desktop`, etc.), the solution and csproj files, the default data
root (`{LocalApplicationData}/Hermaeus`), avares resource URIs, and every
doc. Moss the mascot is unchanged; only the product name around him changes.

### Breaking

- **Default data root moved** to `{LocalApplicationData}/Hermaeus` (was
  `.../Aether`). No automated migration; move the folder by hand while the
  app is closed (see the updated README).
- **Local API headers renamed**: `X-Aether-Token`/`X-Aether-Client` become
  `X-Hermaeus-Token`/`X-Hermaeus-Client`. Any external caller script needs
  updating.
- **Crash log filenames renamed**: `aether_unhandled.log`/
  `aether_unobserved.log` become `hermaeus_unhandled.log`/
  `hermaeus_unobserved.log`. Doctor no longer reads old-named crash logs.
- **OS secret store service name renamed** (Linux `secret-tool`, macOS
  Keychain): existing secrets stored under the old `Aether` service name on
  those platforms are orphaned, not migrated. Windows (DPAPI file under the
  data root) is unaffected beyond the data-root move above.
- **Single-instance lock file renamed** to `hermaeus.lock` under the new
  data root folder.

### Compatibility (kept on purpose)

- The SQLite schema-version bookkeeping table (`aether_schema_versions`) is
  renamed to `hermaeus_schema_versions` in place on first open of an
  existing database, so already-migrated data does not re-run every
  migration.
- The Agent workspace manifest continues to be read from the legacy
  `.aether/workspace.json` path if the new `.hermaeus/workspace.json` one
  is absent; it is always written to the new path.

### Added

- A permanent `NamingConsistencyTests` guard scans the repo for stray
  "Aether" references so a future edit or bad merge fails the build instead
  of shipping half-migrated.
- Kokoro voice lexicon gains a `hermaeus` pronunciation entry
  (`her-MEE-us`); the `aether` dictionary-word entry is kept since it is a
  real English word CMUdict may still miss.

## [0.24.1-alpha] - 2026-07-22

### Added

- **Moss, the workshop mascot.** `docs/mascot.md` defines the character
  (identity, personality, visual spec, animation ideas, icon rules) as the
  source of truth for future art. A flat-vector icon-scale rendering
  (`Controls/MossIcon.axaml`, plain Avalonia shapes, no new dependency) now
  appears next to the Services error banner and the RAG ingest-progress
  line, matching the "goggles, one glowing eye" icon spec.

## [0.24.0-alpha] - 2026-07-22

Implements docs/review r19 in full: the "daily-driver truth" pack covering
stability/honest failure reporting, services and model management friction,
agent task continuity, voice chunk-boundary quality, chat attachments
(.docx/.pdf/images) and artifacts, and a UI-truth/polish sweep.

### Stability and truthful failure

- **Markdown render crash containment** (`MarkdownViewer`): a malformed
  document now falls back to a plain selectable text block and logs the
  failure instead of taking the chat view down with it.
- **Truncated replies are visible, not silent**: a reply cut off by the
  configured token cap (`finish_reason: length`) now shows a "Continue"
  affordance instead of reading as a naturally finished answer.
- **Crash log surfaced on next launch**: an unhandled-exception log from the
  previous session (now written under `{DataRoot}/logs/`) is read back by
  Doctor's clean-shutdown check and shown with real detail instead of a
  generic "did not shut down cleanly" message.
- **Duplicate startup-window guard**: a double-fired `window.Opened` event
  could double-run app initialization; now guarded to run exactly once.

### Services and model management

- **Model path field is a real ComboBox again** for detected models, with
  out-of-root browsed/saved paths repaired back into the list instead of
  rendering blank.
- **LLM folder casing** (`llm` vs `LLM`) is now probed against what already
  exists on disk instead of hardcoded, for both browsing and organizing
  downloaded models.
- **llama.cpp update no longer fights a running server**: `Doctor`'s update
  flow now stops managed servers using the binary being updated before the
  update, and restarts them afterward (always, even on failure).
- **CUDA runtime reuse**: a llama.cpp update that only changes the CPU/GPU
  backend binary can now reuse an already-downloaded matching CUDA runtime
  instead of re-downloading it.

### Agent task continuity

- **Continue a finished task**: a task that reached a terminal state
  prematurely (e.g. the model stopped before finishing the plan) now shows a
  note explaining why, plus a "New Task" button and a Continue box that
  resumes the same task with added instructions instead of forcing a new one.
- **Pending step counts** are now tracked and displayed per task in the
  recent-tasks list (`agent_task_index` schema v3, additive migration).

### Voice

- **Chunk-boundary quality**: the phonemizer/tokenizer chunk text at real
  sentence/clause boundaries instead of blind length cuts, with silence
  inserted between chunks (120ms at a sentence boundary, 60ms otherwise) so
  stitched playback doesn't run words together.
- **Stop button and speaking state**: a global stop-speaking control and a
  per-message speak/stop icon swap driven by a new
  `IVoiceOrchestrator.IsSpeaking` / `UtteranceCompleted` event.
- **Voice id field**: replaced with an editable, suggestion-filtered field
  (Avalonia has no editable stock ComboBox) instead of a fixed dropdown.

### Chat attachments and artifacts

- **Attach files as direct chat context**: `.txt`/code files as before, plus
  new `.docx` (own minimal OOXML paragraph extractor) and `.pdf` (reuses the
  PdfPig-based extractor `Aether.Rag` already ships for ingest, rather than a
  second hand-rolled parser) support. Files that can't be read cleanly are
  Skipped with the reason shown, never silently dropped.
- **Image attachments for vision models**: `.png`/`.jpg`/`.jpeg`/`.webp` (up
  to 8 MB each, 4 per send) attach as OpenAI-style `image_url` content parts
  when the active chat server has a Vision projector (`--mmproj`) configured
  in Services, or when the selected model routes through the OpenAI provider
  (no local projector needed there); otherwise the image is Skipped with an
  honest reason instead of silently degrading to text-only. Services gets a
  "Vision projector" picker beside the model row that auto-suggests a lone
  `mmproj-*.gguf` found next to the selected model.
- **Chat artifacts**: every fenced code block in a rendered reply gets a Save
  button that writes it to a per-conversation artifacts folder
  (`{DataRoot}/chat-artifacts/{conversationId}/`); an Artifacts strip above
  the input bar lists saved files with Open/Reveal actions.

### UI truth and polish

- System Overview reorders GPU/Components above the Privacy Audit.
- Assistant chat bubbles get a visible border; the per-message memory pill is
  now a proper flyout.
- Chat auto-scroll only snaps to the bottom when already pinned there, so
  scrolling up to read history during a stream no longer gets yanked back
  down.
- The streaming "thinking" placeholder rotates through a small set of status
  words on a deterministic elapsed-time schedule.
- Benchmark Rankings tab shows a rank, a score bar, and a Details button per
  run instead of a bare table.
- A memory pill on an assistant message can now jump straight to that memory
  in the Memories view.

### Docs

- docs/features.md, docs/agent.md, docs/voice.md, docs/benchmarks.md, and
  docs/security-review.md updated for all of the above.

## [0.23.0-alpha] - 2026-07-21

Implements docs/review r18 in full: closes out nine files of uncommitted r17
follow-up work first (a real conversation-list UI regression, a keystroke-storm
save bug, a design-fork decision, and a test "fix" that did not fix the test),
then agent/model UX friction found in live use, then first-class llama-server
engine options.

### Finish the open work

- **Conversation details auto-save** (`MainWindowViewModel`,
  `ConversationItemViewModel`): the in-progress replacement for the manual
  Save button saved on every keystroke and reloaded the entire conversation
  list each time, replacing the `ConversationItemViewModel` instance backing
  an open details flyout mid-edit. Now debounces to one save 500ms after
  typing stops and updates the existing item in place instead of reloading;
  pin/archive still reload immediately since those change list membership/
  ordering. The dead `OnDetailsSaveClick` code-behind handler (button already
  removed) was deleted.
- **`TimeDisplay` regression**: restored the `OnUpdatedAtChanged` partial
  method dropped while rewriting the other change handlers, so conversation
  row timestamps ("12m ago") update again instead of freezing at first render.
- **`SuggestContextSize` design fork resolved**: the in-progress diff had
  quietly changed the function from "suggest a smaller context when the
  configured one does not fit" to "also suggest a larger context when there
  is headroom" without updating its doc comment or the Auto Tune status
  message, which still read "configured does not fit" even when raising
  context. Kept the upward-suggestion behavior (owner: users must always be
  free to set a larger context when the model supports it) and fixed both.
- **`GpuHeadroomBytes` duplication**: the in-progress diff corrected this
  constant from 1.5 GiB to 512 MiB in two independent copies
  (`KvCacheMath`, `ModelFitEstimator`). Collapsed to the single copy on
  `KvCacheMath`.
- **Voice temp-file cleanup**: the in-progress test "fix" (a bare
  `Task.Delay(100)`) did not actually fix the failing test. Root cause:
  every voice provider's `GenerateSpeechAsync` (OpenAI, Kokoro, F5-TTS, XTTS)
  reported a synthesis *failure* - discarding the caller's `OutputPath` - and
  never ran its own temp-file cleanup whenever playback itself failed after
  a successful synthesis. Restructured all four to separate the render step
  (still fails the whole call) from playback (best-effort; a playback error
  is now noted in the result message but the successful `OutputPath` and its
  cleanup are unaffected).

### Agent and model UX

- **Agent Scenario check correctness**: `expect_subtask_statuses` required an
  exact-length, exact-order match against the manifest's hardcoded sub-task
  list; a model that reasonably splits work into a different number or order
  of sub-tasks now only needs to have reached every distinct expected status
  at least once.
- **Agents view layout**: the agent's current response used to be squeezed
  into an 11px truncated header status label. It now gets its own
  always-expanded panel (wraps, scrolls past ~320px) near the top of the
  workbench, ahead of the reference panels (workspace profile, files,
  retrieved context) checked less often once a run is going.
- **Hugging Face browser scrolling**: the search results list and per-repo
  file list both clipped instead of scrolling when there were more entries
  than fit; both now scroll internally.
- **Local models list clutter**: verified against a real HF hub cache that
  the reported sub-500MB clutter was not sharded GGUF fragments (none were
  present) but `mmproj*.gguf` vision-projector and `mtp-*.gguf`
  multi-token-prediction draft-weight companion files - neither loadable as a
  standalone chat model in Aether today. Both are now excluded from the
  local models list.
- **Chat memory pill collapse**: `SourceReference` already carried a
  `ProvenanceKind` distinguishing memory recall from RAG citations; the chat
  view rendered both identically as always-visible pills. Memory-sourced
  entries now collapse behind a single "Memories used: N" pill that expands
  on click; RAG citation pills are unchanged.

### First-class llama-server engine options

- **`ServerConfig`** gains `KvCacheTypeK`/`KvCacheTypeV` (string, default
  `f16`), `FlashAttention` (tri-state, default `auto`), `ContextShift`,
  `MemoryLock`, `NoMemoryMap`, and `NgramSpeculative` (bools, default false).
  All additive JSON; every default emits nothing, so an older saved config
  produces a byte-identical launch. A value already present in `ExtraArgs`
  always wins over the equivalent first-class field, exactly like the
  existing `--parallel`/`--cache-reuse` precedent.
- **`KvCacheMath`** extends its bytes-per-element map to the full verified
  value set (f32 4.0, f16/bf16 2.0, q8_0 1.0625, q5_0/q5_1 0.6875,
  q4_0/q4_1/iq4_nl 0.5625) and gains a first-class-aware
  `ResolveBytesPerElement` overload plus `--swa-full` detection (skips the
  sliding-window KV discount when present, matching the engine's own
  full-size-cache behavior). Wired into the Services card's context-fit
  warning and `SuggestContextSize`'s ladder search, so switching the KV cache
  dropdown from f16 to q8_0 visibly raises how much context fits the same
  VRAM.
- **"Suggest engine settings"** button on the Services card fills Context
  Size, KV cache type, and Flash Attention with a hardware-tier
  recommendation (`EngineOptionPresets`, from the owner-supplied llama-server
  tuning guide's per-tier cheat sheet: 6 GB -> 8k ctx + q4_0; 8 GB -> 16k +
  q8_0; 16 GB -> 32k+ + q8_0, capped at the model's training context).
  Editable-form only, same contract as Auto Tune - nothing saves until Save
  Config.
- A quantized V cache combined with Flash Attention off shows an inline
  warning (llama.cpp needs flash attention for a quantized V cache) but still
  launches with exactly what was chosen; nothing is ever auto-changed.
- N-gram speculative decoding is exposed as one experimental "Advanced engine
  options" checkbox (`--spec-type ngram-mod`, zero additional VRAM). Verified
  the full flag surface (`--flash-attn`, `--cache-type-k/v`,
  `--context-shift`, `--spec-type`, DRY sampling, `--min-p`,
  `--cont-batching` defaults) against the bundled llama-server b10069
  `--help` output before implementing; every claim held.

## [0.22.0-alpha] - 2026-07-19

Implements docs/review r17 in full: hardware-aware context fit and benchmark
truth. One theme across both fronts: numbers the app shows about models and
hardware must be measured or derived, not guessed. A new internal GGUF
header-metadata reader (layer count, KV head count, head dims, training
context, quantization - never tensor data) makes both honest.

### GGUF metadata and KV-cache math

- **Sliding-window KV estimates**: GGUF parsing now reads
  `*.attention.sliding_window` and `*.attention.sliding_window_pattern`, and
  KV projections charge sliding-window layers at their window size instead of
  the full configured context. This keeps auto-tune and context-fit warnings
  from falsely capping long-context models that use interleaved sliding
  attention.
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
