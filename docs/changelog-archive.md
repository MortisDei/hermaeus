# Changelog archive

The CHANGELOG.md in root only contains the current 10 versions of changelogs. The rest are archived here in line with the 10 version limit in the main changelog.

## [0.26.1-alpha] - 2026-07-23

### Fixed

- **Repointing `DataManagement.DataRootDirectory` at a folder that already
  held a full, real copy of the data threw "already exists" and left the
  setting permanently stuck reverted to blank.** `SettingsService.MigrateDataRoot`
  treated any existing file at the destination as a blocking conflict, with
  no way to just repoint without a move - so once the data root setting was
  blanked by any means (this shipped alongside a real field incident: the
  app fell back to the default root, created fresh stray database files
  there, and every subsequent attempt to point back at the real root failed
  the same way, silently re-saving blank each retry). A conflict on *every*
  migratable file now means "the target already has its own copy" and is
  treated as a plain repoint (nothing moved, nothing deleted on either
  side); a conflict on *some but not all* files stays genuinely ambiguous
  and still refuses, matching the original safety intent. Both
  `SettingsViewModel.SaveAsync` (Settings page) and
  `SetupWizardViewModel.ApplyDataRootStepAsync` (wizard) share this fix
  through `PreviewDataRootMigration`/`MigrateDataRoot`. Two new regression
  tests cover the repoint case at both layers; one existing test per layer
  was updated to a genuine partial-conflict scenario since a single-file
  full conflict is no longer a refusal case.

### Changed

- **App icon switched from Tree Ring to the "Archivist's Seal" mark**
  (Option 1 of 4 on `docs/hermaeus-icons.png`: a gold "H" monogram grown
  through with a tree and open book, in a circular medallion) - Tree Ring
  (shipped in 0.26.0-alpha) read worse at tray/taskbar size. `hermaeus.ico`,
  `hermaeus-app.png`, `hermaeus-tray.png`, and the tray-dark/light fallbacks
  were regenerated from the new artwork with the same contrast-boosted
  small-size treatment. Neither option is fully clean at 16x16 - fine
  medallion detail is tight at that size regardless of which mark is used;
  32px and up read clearly.

## [0.26.0-alpha] - 2026-07-23

### Changed

- **Real Hermaeus branding replaces the placeholder art shipped in the r20
  rename.** `docs/hermaeus-branding.png` and the new `docs/hermaeus-icons.png`
  are the first illustrated brand sheets for the product; this release wires
  their choices into the app instead of leaving them as reference-only mockups.
- **App icon, taskbar icon, and system tray icon now use the "Tree Ring"
  mark** (Option 4 of 4 on `docs/hermaeus-icons.png`: a gold "H" monogram with
  a leaf sprout, set in a wood-grain medallion) instead of the placeholder
  goggle-eye glyph: `hermaeus.ico` (16/32/48/256px), `hermaeus-app.png`, and
  `hermaeus-tray.png` are all cropped and resized from the same source
  artwork. Sizes at or below 32px use a contrast-boosted crop so the "H"
  stays legible once the fine wood-grain texture anti-aliases into mud. The
  unused `hermaeus-tray-dark.png`/`hermaeus-tray-light.png` fallback assets
  were refreshed the same way and normalized to 256x256 (previously an
  inconsistent 1254x1254 left over from the r20 rename).
- **`Controls/MossIcon.axaml` redesigned** to match the illustrated Moss
  character (round face, pointed ears, mushroom/leaf tuft, big eyes) instead
  of the retired mechanical-tinkerer goggle design. Still plain Avalonia
  shapes at 16x16 icon scale, no new rendering dependency.
- **`docs/mascot.md` rewritten** to match the actual illustrated character
  and personality ("Keeper of Knowledge": curious, diligent, loyal) instead
  of the earlier "mechanical tinkerer" placeholder concept that predated any
  real art. Documents the formal brand colour palette, typography, and why
  Tree Ring (not the full Moss face) was chosen for the app icon.
- **Brand colour palette and typography wired into the UI theme**
  (`App.axaml`, `Styles/AppStyles.axaml`): FluentTheme's accent colors now
  use the brand Forest green instead of Avalonia's default blue; the primary
  send button uses Forest fill with Parchment text, the sidebar new-chat
  button uses a Forest outline. Three brand typefaces are embedded under
  `Assets/Fonts/` (Cinzel for headings, Source Sans 3 for body text,
  JetBrains Mono for code) and applied app-wide - every hardcoded
  `Consolas`/`Courier New`/`Cascadia Code` font-family reference across the
  Desktop views was normalized to the embedded JetBrains Mono with the same
  fallback chain. See `NOTICE.md` for font licensing (SIL OFL 1.1).

## [0.25.3-alpha] - 2026-07-22

### Fixed

- **The "changing status messages while thinking" feature (r19 6.4) never
  actually changed in practice.** The rotating whimsy words only activated
  after the server's first stream event arrived, with "Reading prompt" held
  separately as fixed text before that. For llama.cpp specifically, no
  event of any kind arrives during prompt eval - the first SSE line to show
  up already carries the first visible token - so the rotation gate never
  opened and the whole wait (which can run 15+ seconds on a long prompt)
  showed only a static "Reading prompt... Ns". "Reading prompt" is now just
  one word in the same rotating pool as the rest, so the label actually
  varies through the entire wait, not just an occasionally-reached tail end
  of it.
- **The rotation showed the identical word sequence on every send.** Each
  send now starts from a random point in the word list (still advancing
  deterministically from there within that send, so it doesn't flicker).

## [0.25.2-alpha] - 2026-07-22

Field-report fixes from the owner's first real dogfooding session against a
local Gemma model.

### Fixed

- **Saved code-block artifacts could double up their extension**
  (`calculator.cs.cs`): when the reply's markdown heading already named the
  file (e.g. "# calculator.cs"), `DeriveArtifactStem` handed that back
  verbatim and the language extension got appended on top. The stem now
  strips a trailing extension-shaped suffix first.
- **Long syntax-highlighted code blocks could render as an empty box with
  the Save button scrolled out of view**: the AvaloniaEdit code viewer had
  an unbounded `MinHeight` (scaling with line count) fighting a fixed
  `MaxHeight="420"` with internal scrolling disabled, so a block taller than
  420px was either stretched far past the visible area or clipped with no
  way to reach the rest. Capped to the same bound and enabled the scrollbar.
- **Attaching an image required manually switching the file picker's filter
  dropdown**: the picker's first filter entry (which the OS dialog opens to
  by default) only listed text/code extensions, so images were invisible
  until the user noticed and switched it themselves. A combined "All
  supported files" filter is now first.
- **Pasting an image into chat did nothing**: `TextBox` only ever knew how
  to paste text. Ctrl+V (or right-click Paste) with an image on the
  clipboard now attaches it the same way a dragged-in file would; plain
  text paste is unaffected.
- **A failed send only ever showed a bare HTTP status** ("Response status
  code does not indicate success: 500"), discarding whatever llama.cpp
  actually said about why - often the one clue that matters (e.g. a
  `--mmproj` mismatched to the loaded model). The response body is now read
  and included, bounded to 500 characters.
- **The last chat message's action row (copy/speak buttons) sat right
  against the input box** with no breathing room. Added bottom padding to
  the message list.

### Changed

- **Chat artifact folders are now named after the conversation title**
  (sanitized, deduped against same-titled conversations), not the raw
  conversation GUID, so `{DataRoot}/chat-artifacts/` means something when
  browsed in a file manager. A hidden per-folder marker file keeps the
  folder stable if the conversation is renamed later; folders created
  before this change (bare GUID names) are still found via the same lookup,
  so no existing artifacts are orphaned.

## [0.25.1-alpha] - 2026-07-22

### Changed

- **App icon, taskbar icon, and system tray icon now show Moss** instead of
  the old Aether "A" mark: `hermaeus.ico` (window/taskbar, 16/32/48/256px),
  `hermaeus-app.png` (Linux desktop icon), and `hermaeus-tray.png` render the
  same shapes and colors as the in-app `Controls/MossIcon.axaml`, generated
  programmatically so every size stays pixel-consistent with it. Sizes at or
  below 32px drop the dark lens-housing ring for legibility (the full
  four-ring goggle stack anti-aliases into mud that small), so the eye reads
  as one clean glowing circle instead. The unused `hermaeus-tray-dark.png`/
  `hermaeus-tray-light.png` fallback assets were refreshed the same way for
  consistency, though nothing currently references them.
- `docs/hermaeus-branding.png` (an illustrated marketing mockup sheet, not
  referenced by any code or the README) still shows the old mark and
  wordmark text baked into the pixels - flagged in docs/mascot.md as a
  follow-up needing real illustration work, not a programmatic redraw.

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


## [0.10.0-alpha] - 2026-07-12

Implements docs/review r5 in full: voice stops being "TTS for chat" and
becomes a shared service for the whole app, and the benchmark store grows a
deterministic recommendation engine on top of data it already persists.

### Voice orchestration

- **`IVoiceOrchestrator`.** A single background worker drains a priority
  queue (Low/Normal/Critical) one utterance at a time, so two consumers can
  never overlap audio without a mixer. Critical utterances preempt the
  current playback and clear queued Low items; same-key `DedupeKey`
  utterances collapse; channels default to disabled except Chat, which
  preserves the pre-r5 manual speak button.
- **Voice profiles and per-channel settings.** `TtsSettings.Profiles`
  (named voice/speed combinations) and `TtsSettings.Channels` (per-channel
  enable + profile) are new, additive settings fields; a Settings > Voice
  card exposes a mute toggle, auto-speak, streaming speech, and the
  channel/profile editors.
- **Six consumers.** Chat can auto-speak replies (`Tts.AutoSpeakChatReplies`,
  off by default) and, experimentally, speak them sentence-by-sentence while
  the model is still streaming (`Tts.StreamingChatSpeech`, via a new
  `SentenceChunker`). The agent narrates milestones only (task started,
  waiting for approval/reply at Critical priority, terminal Complete/Failed)
  and can use a per-workspace voice profile stored in `.aether/workspace.json`.
  Doctor speaks one Critical summary per scan when it finds Errors.
  Benchmarks announce run completion. A new `VoiceNotificationBridge`
  forwards Warning/Error toasts onto the Notification channel.
- All spoken chat text passes through `ChatSpeechSanitizer`, which replaces
  fenced/inline code with a spoken placeholder so TTS never reads code
  character by character.

### Benchmark insights

- **Tag propagation.** `BenchmarkCase.Tags` now copies onto
  `BenchmarkResult.Tags` at scoring time; `BenchmarkInsightsService`
  suite-joins tags back onto older runs whose originating suite still
  exists, so historical runs still feed per-tag leaderboards.
- **`BenchmarkInsightsService` + `BenchmarkInsightsMath`.** Deterministic,
  no LLM: groups runs by model + quantization + runtime, filters to
  hardware comparable to the current machine (GPU name + VRAM within 10%,
  or both CPU-only), and ranks by `QualityPerSecond = quality *
  log2(1 + tokens/sec)`. Requires at least 2 runs and 10 cases before a
  model appears in any leaderboard; flags stale results (>60 days old or a
  different Aether minor version). Produces pairwise comparison sentences
  ("X scores 4% lower than Y but runs 2.3x faster").
- **Insights tab** on the Benchmarks page: best-overall card, per-tag
  leaderboards with comparison sentences, caveats, and a re-run shortcut.
  Loaded on demand, not on page open.
- **Doctor advisory check.** Info-only: when the selected chat model ranks
  more than 10 points behind the best comparable model, Doctor names both.
  Never Warning/Error, never switches anything automatically.

55 new tests (327->382), coverage floor raised 45->47. Ships as 0.10.0-alpha because
two new user-facing capabilities landed in the same round. Archived at
docs/review/archived/r5/.

## [0.9.44-alpha] - 2026-07-12

Implements docs/review r4 in full: an audit of the r3 agent loop and lesson
store found the loop's feedback channels were half-wired and the lesson
store's contradiction mechanic was structurally unreachable. r4 fixes both,
then lands r3's deferred item (automatic lesson capture from task-terminal
states) on top.

### Interaction and failure semantics

- **User-reply channel.** `IAgentService.AppendUserReplyAsync` answers a
  task's `ask_user` question: appends the reply to the transcript (new
  `"user"` role) and resumes the task. Refuses when a tool approval is
  pending - a reply is never a substitute for an approval decision. The
  workbench shows a reply box that resumes the autonomous loop after
  sending, the same approve-and-continue shape as a gated-action approval
  (both now share `AgentViewModel.ResumeAgentLoopIfRunnableAsync`).
- **Real failure semantics.** `AgentTaskState.ConsecutiveStepErrors` tracks
  unparseable model responses; three in a row fails the task (recorded on
  `Decisions`, every bad step still kept in the transcript) instead of
  looping forever in `WaitingForUser`. Any step that parses successfully
  resets the counter. An unhandled model-call or tool-execution error now
  hands the task to `WaitingForUser` before rethrowing, instead of leaving
  it stranded in `Running`. Hitting `Agent.MaxAutoSteps` while still
  `Running` now also lands in `WaitingForUser` with a logged/transcripted
  note, rather than stopping silently.
- **Approved-tool transcript entries.** A gated action's result now reaches
  the transcript once approved, not just `ToolResults`' last-five window -
  previously the results of the most consequential actions could age out of
  the model's view. Removed the unused, transcript-and-lesson-bypassing
  `AgentService.ExecuteApprovedToolAsync`.
- **Native tool-call fidelity.** A tool-calling model's own prose now becomes
  the recorded thought summary instead of a synthetic "Calling X."
  placeholder; a turn requesting more than one tool call notes the dropped
  ones so the model sees next step that only the first ran.
- Small hygiene: `AgentContextBuilder`'s one-word workspace search heuristic
  now only runs on a task's first step (later steps have transcript history
  and navigation tools); `PendingSteps` drops entries once they appear in
  `CompletedSteps`; stale `KnownRisks` text about commands being blocked
  fixed to match actual policy.

### Lessons v2

- **Signature redesign.** Command/patch/approval dedupe signatures no longer
  bake in the outcome (was `command:{cmd}:{ok|fail}:{token}`, now
  `command:{cmd}`) - previously a command that failed then succeeded created
  two permanently-separate rows and the store's contradiction logic was
  unreachable for these kinds. Schema bumped to v2 with a migration that
  collapses existing outcome-suffixed rows (keeping the one with the most
  evidence) on next start.
- **Approval counter-evidence.** Approving a gated action now records
  counter-evidence against a prior rejection lesson for the same tool (via
  new `AgentLessonEvidence.CounterOnly`, which only ever weakens an
  existing lesson, never originates one), so a single early rejection can no
  longer become a standing lesson the user's own later approvals can't
  soften.
- **Structured command outcomes.** `AgentToolResult` gained `ExitCode` and
  `TimedOut`; lesson capture uses the real exit code instead of string-
  sniffing `"Exit code 0"` from the summary text, and skips capture entirely
  on a timeout (which says nothing about whether the command itself works).
- **Task-terminal capture** (the item r3 deferred). On `Complete` or
  `Failed`, a lesson keyed by a deterministic goal fingerprint
  (`AgentLessonText`: tokenize, sort, hash - no LLM) records whether goals
  like it tend to work out here; an uneventful success records nothing.
  Separately, every lesson actually shown to the model during a task that
  completes successfully gets its evidence confirmed via new
  `ILessonStore.ConfirmAsync` - the compounding half of the self-learning
  loop.
- **Relevance-aware injection.** Lesson candidates are now ranked by pinned
  status, confidence, and shared terms with the current goal/recent tools
  before packing, instead of confidence alone, so an unrelated
  high-confidence lesson can no longer crowd out one that actually bears on
  the current step.
- Polish: `SqliteLessonStore` timestamps now round-trip with
  `DateTimeStyles.RoundtripKind` instead of applying local-time conversion;
  the lesson error-token regex is now static/compiled.

### Chat-side lesson consumption (optional)

- New `Memory.ConsumeAgentLessonsInChat` setting (Settings > Memory; off by
  default). When on, `ChatViewModel` folds Global-scope agent lessons into
  the system prompt as their own read-only markdown block, alongside stored
  memories. Deliberately kept out of `BuildMemoryInjectionAsync`'s
  `InjectedMemoryIds` list, so a `[MEMORY_UPDATE]`/`[MEMORY_FORGET]` marker
  can never target a lesson - the Agent workbench's Lessons panel remains
  the only write path.

23 new tests (327 total), zero warnings. Full details in
docs/review/archived/r4/.

## [0.9.43-alpha] - 2026-07-12

Implements docs/review r3 (agent capability, memory, self-learning) in full.
r2's roadmap was fully actioned; this round targets the Agent workbench's gap
against Claude Code/Codex, upgrades memory recall and lifecycle, and adds a
new per-machine agent self-learning (lesson) store.

### Agent capability

- **Persisted step transcript.** Each task keeps `transcript.jsonl` (one line
  per assistant thought / tool result), replayed budgeted (most-recent-first)
  into every step's context (`Agent.TranscriptTokenBudget`, default 12,000
  tokens) instead of only the last five, 4000-char-truncated tool results.
  Fixed a latent bug found while building this: `AgentJson.Options` was
  indented, so anything written through it into a JSONL file (including the
  pre-existing `agent.trace.jsonl`) spread one entry's JSON across multiple
  physical lines, corrupting line-oriented replay; both now use a new
  `AgentJson.CompactOptions`.
- **Autonomous runs.** `IAgentService.RunAsync` loops `RunStepAsync` until a
  final answer, a question for the user, a gated action needing approval, a
  blocked task, or `Agent.MaxAutoSteps` (default 20). Start and
  approve-and-continue both drive the loop; a manual single-step advance
  remains available. Never auto-approves anything.
- **Native tool calling.** `LlmChatOptions.Tools` / `LlmStreamEvent.ToolCalls`
  added to the core LLM contract; implemented in `OpenAiService`,
  `LlamaCppService` (shared OpenAI-compatible wire format via the new
  `OpenAiCompatibleToolWire` helper, including streamed tool-call-delta
  accumulation), and `OllamaService` (whole, non-fragmented tool calls). The
  agent declares its fixed tool set natively and falls back to the JSON
  prompt protocol automatically when unsupported.
- **Surgical edits.** `edit_file` (unique `old_string`/`new_string` replace)
  and `create_file` (new files only, refuses to overwrite), both
  approval-gated with the same path containment as every other tool.
- **Navigation tools.** `glob_files`, regex + context-line support in
  `search_files`, line-ranged `read_file`, subdirectory/depth-scoped
  `list_files`.
- **`set_plan`** tool: a visible, agent-maintained plan checklist; executes
  immediately since it only mutates task state.
- **`run_command` template families.** Replaces the fixed verbatim-string
  dictionary with families that accept an optional, containment-checked
  path/script argument (`dotnet build`/`test [project]`, `npm test`,
  `npm run <script>` limited to scripts the workspace's own `package.json`
  declares, `cargo build`/`test`, `pytest [path]`); the safety gate now
  matches on family, not exact string, so a workspace declaring bare
  `dotnet test` also covers `dotnet test tests/Foo.csproj`. Command output
  bounded to the last 200 lines instead of dumped unbounded then hard-cut
  mid-line. Per-task remembered approvals: an identical repeat of an
  already-approved command string may auto-execute for the rest of that
  task; a different command, even in the same family, still requires its own
  approval, and the memory never survives past the task.

### Agent self-learning (new)

- New per-machine lesson store (`agent/lessons.db`, `ILessonStore` /
  `SqliteLessonStore`) capturing deterministic, evidence-backed observations
  from `run_command` outcomes, `edit_file`/`create_file`/`apply_draft_patch`
  outcomes, and user approval rejections, plus model-stated
  `[LESSON: ...]` observations. Repeated matching evidence for the same
  signature reinforces one row (confidence and evidence count rise, capped);
  a contradiction decays confidence and, below a floor, retires the lesson,
  flipping it to the new outcome so later confirming evidence can revive it.
  Pinning a lesson locks it against further automatic changes.
- Relevant lessons (global plus the current workspace) are injected into
  every step's context pack, most confident first.
- New Lessons panel in the Agent workbench: view, inline edit, pin, retire,
  reactivate, delete. Lessons only ever inform the model; the safety gate
  never reads the lesson store.

### Memory

- **Hybrid recall.** `MemoryStore.SearchAsync` blends FTS rank with cosine
  similarity against a query embedding when an embedding model is
  configured (additive schema v4, `embedding` BLOB column, lazy backfill on
  first hybrid search), falling back to the existing FTS/LIKE behaviour
  otherwise. Every result now carries a `RelevanceScore`.
- **Relevance-aware injection.** `MemoryInjectionService` selection now
  blends that relevance score with importance instead of sorting
  recency-first; unaffected for memories not retrieved via search.
- **Lifecycle.** New `RecallCount`/`LastRecalledAt` columns (schema v4);
  `MemoryLifecycle.ComputeEffectiveImportance` decays a memory's effective
  importance the longer it goes unrecalled (pinned memories never decay).
  `IMemoryStore.ArchiveStaleMemoriesAsync` auto-archives (never deletes)
  memories that decay below a floor and stay unrecalled long enough; the
  Memories panel runs it on open and now sorts by effective importance and
  shows recall stats.
- **Update/forget markers.** A model can correct or retire a memory shown to
  it this turn with `[MEMORY_UPDATE: <id> | <content>]` /
  `[MEMORY_FORGET: <id>]`; only ids actually injected into that turn are
  honored, everything else is ignored and logged
  (`ConversationMemoryService.ApplyInjectedMemoryMarkersAsync`).
- **Structured extraction.** Auto-summary now asks for JSON
  (content/category/importance/tags) instead of `[MEMORY: ...]` markers with
  keyword heuristics, giving model-supplied metadata directly; the marker
  format remains the fallback.
- **Unified workspace memory.** Deleted the legacy file-backed
  `FileAgentWorkspaceMemoryStore` (superseded by the SQLite-backed
  `WorkspaceMemoryStore` since r2); tests that exercised legacy-file import
  now write the legacy format directly instead of depending on the removed
  class.

### Housekeeping

- Moved `tests/Aether.Tests` to `src/Aether.Tests`, alongside every other
  project, updating the solution, project references, and every doc/skill
  that pointed at the old path.
- Added a CI workflow (`.github/workflows/ci.yml`): restore/build/test on
  ubuntu-latest and windows-latest.
- Added `scripts/coverage.ps1` / `scripts/coverage.sh`, a line-coverage
  ratchet via coverlet (floor recorded once measured; only ratchets up).
- Removed pointless `Task.Run` wrappers around synchronous work in
  `MemoryInjectionService`/`MemoryExtractionService`; the agent trace now
  logs the full context pack once per task plus a small delta per step
  instead of the full pack on every step.


## [0.9.42-alpha] - 2026-07-11

Implements docs/review/03-next-level-roadmap.md Phases 1 through 4 in full
(r2 review). Each phase is independently shippable; landed together here.

### Phase 1 - Provenance convergence

- **RAG citations are now a typed stream event, not a sentinel string.**
  `RagQueryService.StreamQueryAsync` used to interleave plain answer tokens
  with magic-prefixed `"__RAG_SOURCES__...__END_SOURCES__"` and
  `"__RAG_TRACE__...__END_TRACE__"` strings that every consumer had to
  independently detect and strip. It now yields a closed `RagStreamEvent`
  (`Token`/`Sources`/`Trace`) with a typed `RagTraceSummary` payload.
  `RagStreamProtocol` (the shared sentinel parser) is deleted; `RagViewModel`,
  `RagEvalService`, and `Aether.LocalApi` all switch on `Kind` instead. Fixed
  a real bug in passing: the trace event now actually carries
  `ExpandedQuery`/`QueryVariants`/`PlannerNotes`/`ContextPackingSummary`,
  which the persisted trace always computed but the old sentinel JSON never
  included, so `RagViewModel`'s Context Transparency fields for those were
  silently always empty.
- **Memory gained a structured `Source` (`SourceReference`)** alongside the
  existing `SourceConversationId`. Additive `MemoryStore` schema migration
  (v3, `source_json` column). Rows written before this migration backfill a
  `SourceReference` from `SourceConversationId` at read time; no data
  rewrite. `MemoryExtractionService` and `ConversationMemoryService` now
  populate it with a short content-derived title, the conversation locator,
  and a snippet.
- **Chat now actually injects memory, and shows a Sources panel.** Memory
  injection (`MemoryInjectionService`) existed since early memory work but
  nothing in `ChatViewModel` ever called it. `SendAsync` now searches
  relevant global memories (gated by the existing `Memory.Enabled` setting),
  builds a memory-context block appended to that turn's system prompt, and
  populates a new `MessageViewModel.Sources` collection with the
  `SourceReference`s actually used, rendered as a small chip row under the
  assistant's reply (tooltip shows the memory content).

### Phase 2 - Local API: from demo to substrate

- **Streaming.** `POST /v1/chat/completions` accepts `"stream": true` and
  responds with Server-Sent Events in the OpenAI `chat.completion.chunk`
  wire shape (deliberate wire compatibility with existing SSE clients, not a
  dependency on OpenAI). Buffered JSON stays the default.
- **Per-app tokens, replacing the single shared token.**
  `LocalApiSettings.Tokens` is now a list of named entries
  (`LocalApiTokenEntry`: Id, Name, secret-store reference, created-at);
  Settings > Local API manages the list (add-with-name, revoke individually,
  each action applies and saves immediately rather than waiting for the
  page's main Save button, since a pending revocation that silently reverted
  would be a real footgun for a credential list). `LocalApiTokenAuth`
  authenticates against every configured entry and records which one
  matched; `LocalApiEndpoints` now traces calls under that verified token
  name, with the caller-supplied `X-Aether-Client` header kept alongside it
  only as an unverified display hint. A load-time settings migration
  converts an existing single `ApiToken` into a "Default" named entry so
  upgrading users keep a working integration.
- **Embeddings endpoint.** `POST /v1/embeddings` wraps `IEmbeddingService`,
  returning one vector per input string plus the provider's dimensionality.

### Phase 3 - MCP hardening

- **Per-server tool allowlists.** `McpServerConfig.AllowedTools` restricts
  which of a server's declared tools are actually callable (empty means no
  restriction, matching prior behavior); configurable per server in
  Settings > MCP Servers. Also closes a real gap found while implementing
  this: `McpToolBridge.ExecuteAsync` previously forwarded any
  `mcp:{server}:{tool}` reference to the server without checking it was
  ever declared via `tools/list`; it now verifies the tool is both
  allowlisted and actually declared before calling it.

### Phase 4 - Local-only crash/lifecycle journal

- **`AppLifecycleJournalService`** (`Aether.Core.Services`, so Aether.Rag and
  Aether.Voice can both use it without a reference against the established
  Desktop/ViewModels to Services/Agent/Rag to Core dependency direction):
  one small atomic-write JSON file in the data root recording session start
  time, whether the session exited cleanly, and the last notable operation
  it was performing. Generalizes the ad-hoc preflight logging added for the
  0.9.38-0.9.40 native Kokoro ONNX crash into a reusable mechanism, now also
  wired into the RAG cross-encoder reranker's ONNX session loads. Doctor
  reports a new "Previous session exited cleanly" check, warning and naming
  the last recorded operation when the prior session never recorded a clean
  exit, purely local diagnosis with no telemetry or upload.

### Docs

- docs/features.md, docs/agent.md, and docs/security-review.md updated for
  all of the above (Local API endpoints/tokens, chat Sources panel, MCP
  allowlists, Doctor's clean-shutdown check). docs/security-review.md's
  Local API and MCP rows are refreshed for `0.9.42-alpha`; the rest of that
  document is unchanged from its `0.9.14-alpha` baseline and is flagged as
  such rather than implicitly re-certified.

## [0.9.41-alpha] - 2026-07-11

Second architecture/security review pass (docs/review/, r2). Closes every
Phase 0 item in docs/review/01-code-audit.md: two P1s, eight P2s, and the
resolvable P3s.

### Fixed
- **`LocalApi.Enabled` was a phantom toggle**: nothing ever launched the
  `Aether.LocalApi` host process, so the Settings checkbox did nothing, and
  a manually-launched host ignored the setting entirely. `LocalApiProcessManager`
  now actually starts/stops the host (packaged sibling install, or the
  dev build output when running via `dotnet run`/F5), wired into app startup,
  settings save, and shutdown; `Aether.LocalApi/Program.cs` refuses to serve
  (exit 1) when `Enabled` is false even if launched directly. `build.ps1`/`build.sh`
  now also publish `Aether.LocalApi` into a `LocalApi/` subfolder of the
  release package. A new unauthenticated `/health` endpoint backs the
  process manager's startup health-poll.
- Local API call tracing built `DetailJson` by string-interpolating the
  caller-controlled `X-Aether-Client` header, so a crafted header could
  inject malformed/extra JSON into stored trace records; it's now built
  with `JsonSerializer.Serialize`, and the caller name is stripped of
  control characters and capped at 64 characters.
- `McpClient.CallToolAsync` stringified every tool argument via `ToString()`,
  so a server expecting a JSON number/boolean/object for a declared
  parameter received a string instead; arguments now map by runtime type.
- `McpClient` never drained a spawned server's stderr; a server that logged
  more than the OS pipe buffer would block on its next stderr write, hanging
  every in-flight call until the 30-second timeout with no diagnostic. Stderr
  is now drained continuously into a bounded tail, included in failure
  messages.
- `McpClient` requests made after the server process closed its stdout used
  to hang for the full 30-second per-call timeout; the client now faults all
  outstanding and future calls immediately when the connection closes.
- The agent's `inspect_git_diff` tool read stdout/stderr only after
  `WaitForExit`, which deadlocks against git blocking on a full pipe for a
  large working tree, previously surfacing as a false "git status timed
  out." Streams are now read concurrently with the wait, matching the
  pattern `run_command` already used.
- `apply_draft_patch` wrote the user's approved file changes with a plain
  `File.WriteAllText`; a crash mid-write could truncate their source file.
  It now goes through the same temp-plus-move `AtomicFileWriter` already
  used for state files (`IAgentWorkspaceTools.ApplyDraftPatch` is now
  `ApplyDraftPatchAsync`).
- The native Kokoro model download buffered the full response (a few
  hundred MB) into memory before writing to disk; it now streams via
  `HttpCompletionOption.ResponseHeadersRead`.
- Native Kokoro speech synthesis (phonemize/tokenize/ONNX inference) ran on
  the caller's thread, which is the UI thread for `SpeakAsync`/`PreviewVoiceAsync`,
  freezing the app for the duration; it's now offloaded to a background thread.
- `secrets.local.key` (the AES key protecting every fallback-stored secret)
  was written with a bare `File.WriteAllText`, restricting permissions only
  after the file was already visible, and non-atomically. It's now written
  temp-then-move with permissions restricted before the move, matching
  `secrets.local.json`'s own write path.
- A secret that fails to decrypt under both the current and legacy format
  (e.g. a corrupted or replaced `secrets.local.key`) used to silently
  resolve to an empty string; `SecretStore` now logs a warning through
  `IRuntimeLogService` so the failure is diagnosable instead of surfacing
  only as a downstream provider auth failure.
- The local API's chat completion endpoint only forwarded
  Temperature/MaxTokens, ignoring the five other sampling parameters added
  in 0.9.39 and any saved per-model profile defaults, so API callers got
  different output than the desktop app for the same model. It now applies
  the same explicit-value / model-profile-default / global-setting
  precedence the desktop `ChatViewModel` uses.
- The local API's RAG source parsing kept a third private copy of the
  `__RAG_SOURCES__` sentinel regex/JSON-shape logic; it now calls the
  shared `RagStreamProtocol.ParseSources` like every other consumer.

### Added
- `LocalApiSettingsViewModel.ProcessStatusLabel`, shown in Settings > Local
  API, so the Enable checkbox has a visible, honest status ("Running",
  "Stopped", "Stopped (no token configured)", etc.) instead of an assumed
  effect.

### Docs
- docs/review/ now holds a second review round (r2): a code audit, an
  architecture assessment of the post-r1 v2.0/v3.0 code, and a next-level
  roadmap. r1 moved to docs/review/archived/r1/.

## [0.9.40-alpha] - 2026-07-07

### Fixed
- `NativeKokoroVoiceProvider`/`KokoroOnnxModel` resolved `LocalAiAssetsRoot` once at DI-singleton construction time and cached it forever; changing the setting later in the same running session silently kept reading/writing the old location. The assets root is now re-resolved from current settings on every access, and a loaded ONNX session/voice-style cache is dropped and reloaded if the root changes underneath it.
- Diagnosed a real crash: the Kokoro Native install actually completed successfully (model + all 28 voices downloaded, SHA256 verified) but a subsequent model load via `new InferenceSession(...)` crashed the whole process natively (bypassing all managed exception handling) on at least one machine. `InferenceSession` construction now uses explicit, conservative `SessionOptions` (basic graph optimization, single-threaded, sequential execution) instead of the all-optimizations default, trading a little inference speed for avoiding whatever fused/parallel kernel path was crashing.

## [0.9.39-alpha] - 2026-07-07

### Added
- Full LLM sampling parameters: top P, top K, min P, repeat penalty, frequency penalty, and presence penalty, alongside the existing temperature. Available as global defaults (Settings > LLM), per-model overrides (Model Management), and live per-conversation editing (`ChatViewModel`), mirroring the existing temperature pattern throughout. Each is optional; leaving a field blank sends nothing for it and the provider uses its own default. llama.cpp and Ollama receive all six; OpenAI-compatible endpoints only receive the three the real OpenAI API supports (top_p, frequency_penalty, presence_penalty), since top_k/min_p/repeat_penalty are llama.cpp/Ollama-only extensions.
- Services panel: the Stopped status now reads "Stopped, click Start to launch" instead of just "Stopped", plus a tooltip on the status dot explaining that it's independent of Doctor's "found on disk" check.

### Fixed
- Settings > Voice: Python-path/venv fields no longer stay enabled when a non-Python-backed voice provider (Kokoro Native, F5-TTS, OpenAI) is selected; they're now gated the same way the existing XTTS-only fields already were.

## [0.9.38-alpha] - 2026-07-07

### Fixed
- `SettingsViewModel.Shutdown()` stopped `XttsProcessManager` but never `KokoroProcessManager`, so the Python Kokoro process could outlive an app shutdown that didn't go through DI container disposal.

### Added
- `KokoroOnnxModel` now writes a `kokoro_native_install.log` line (with the on-disk model file size) immediately before each `InferenceSession` load, both during install and lazy-load. A native ONNX Runtime fault during session creation bypasses managed exception handling and kills the process with no other trace; this at least pins down whether a crash happens at session load versus during download/verification.

## [0.9.37-alpha] - 2026-07-07

ViewModel orchestration extraction, part 6: the three large orchestrators previously deliberately deferred (`ChatViewModel.SendAsync`, `ChatViewModel.CompareSelectedModelsAsync`, `RagViewModel.IngestAsync`).

### Added
- `Aether.Core.Services.ChatSendOrchestrator`: drives one streamed chat completion (usage/timing/cancellation/error classification), extracted from `ChatViewModel.SendAsync`. The ViewModel now only owns UI-facing message-state mutation; the streaming call itself is testable against a fake `ILlmService` with no UI plumbing.
- `Aether.Core.Services.ChatStreamAccumulator`: the render-batching throttle (flush on a time/size threshold) previously a local closure inside `SendAsync`.
- `Aether.ViewModels.ModelCompareOrchestrator`: target selection (explicit selection vs. fallback to the active model, capped at 4) and `EvalRun` to `ModelCompareResultViewModel` mapping, extracted from `ChatViewModel.CompareSelectedModelsAsync`.
- `Aether.ViewModels.RagIngestServiceSuspension`: the suspend-competing-services/restore sequence (managed embedding server, XTTS, Kokoro) extracted from `RagViewModel.IngestAsync`.
- `Aether.Rag.RagIngestRequestBuilder`: builds/updates the target `RagDataset` for an ingest run and renders the ingest health summary line, extracted from `RagViewModel.IngestAsync`.

### Fixed
- RAG ingest's service-restore failures (embedding server, XTTS, Kokoro) are now actually logged; previously the per-service error list built during restore was discarded without ever being surfaced.

## [0.9.36-alpha] - 2026-07-07

ViewModel orchestration extraction, part 5.

### Added
- `Aether.Core.Services.ChatConversationBuilder.AutoTitleFrom`: derives a conversation title from its first user message, extracted from `ChatViewModel.PersistAsync`.

## [0.9.35-alpha] - 2026-07-07

ViewModel orchestration extraction, part 4.

### Added
- `Aether.Agent.Services.AgentPatchReviewService`: the apply/reject/block status-transition, persist, and audit sequence shared by `AgentViewModel`'s `ApprovePatchAsync`/`RejectPatchAsync`/`BlockPatchAsync`. The ViewModel still owns the approval-preview UI flow (`RequestDraftPatchPreview`); this owns what happens once a decision is made.

## [0.9.34-alpha] - 2026-07-07

ViewModel orchestration extraction, part 3.

### Added
- `Aether.Services.ChatTraceService`: persists and reloads chat traces through the shared `ITraceStore`, extracted from `ChatViewModel`'s `AddChatTrace`/`PersistChatTraceAsync`/`LoadPersistedChatTracesAsync`/`TryParseChatTraceDetail` group. `ChatViewModel`'s constructor now takes `ChatTraceService?` instead of `ITraceStore?`; the ViewModel only builds/binds the UI-facing `ChatTraceViewModel`, the service owns the trace-store read/write and DetailJson mapping.

## [0.9.33-alpha] - 2026-07-07

ViewModel orchestration extraction, part 2.

### Added
- `Aether.Rag.RagDatasetHealthService`: computes a dataset's source/duplicate/missing/stale-file counts from its chunks, extracted from `RagViewModel.RefreshDatasetManagerAsync`. Testable against a temp directory instead of only through the full ViewModel.

## [0.9.32-alpha] - 2026-07-07

ViewModel orchestration extraction, part 1 (docs/review/01-architecture-review.md
item 5): pulls pure, testable logic out of `ChatViewModel`/`AgentViewModel`/
`RagViewModel` into plain services, leaving the ViewModels thinner. First of
several slices.

### Added
- `Aether.Core.Services.ChatContextUsageCalculator`: resolves the effective context window and computes usage label/percent/warning-level display values, extracted from `ChatViewModel.UpdateContextUsage`/`ResolveContextWindowLimit`. `ChatViewModel.TruncateHistoryToContextWindow` now delegates to the calculator's version, keeping its existing public signature for the one test call site that depends on it.
- `Aether.Rag.RagStreamProtocol`: parses the `__RAG_SOURCES__`/`__RAG_TRACE__` sentinel blocks `RagQueryService` interleaves into its answer stream. `RagViewModel` and `Aether.Rag.Eval.RagEvalService` each had their own independent copy of this exact parsing logic; both now share one implementation.
- `Aether.Agent.Models.WorkspaceActivationSelection`: resolves a `WorkspaceActivation`'s preferred model/dataset ids against a ViewModel's loaded candidate list. `ChatViewModel` and `AgentViewModel` each duplicated this id-to-object lookup inline.
- Unit tests for all three new pure-logic classes (`ChatContextUsageCalculatorTests`, `RagStreamProtocolTests`, `WorkspaceActivationSelectionTests`), testable without any UI plumbing for the first time.

## [0.9.31-alpha] - 2026-07-07

Closes the remaining half of docs/review/06-technical-debt.md item 4
(provider knowledge smeared across layers) without a settings schema
migration.

### Changed
- `PrivacyAuditService.IsChatProviderEnabled` and `CompositeLlmService.GetModelsAsync` independently matched a provider tag to the same `Llm.LlamaCppEnabled`/`Llm.OpenAiEnabled`/`RuntimeProfiles` flags; both now call one new `CompositeLlmService.IsProviderEnabled(tag, settings)`. Deliberately not done: replacing the two named boolean fields with a tag-keyed dictionary, which would need a `settings.json` migration for a benefit (adding a 4th provider) that hasn't materialized yet.

## [0.9.30-alpha] - 2026-07-07

Closes docs/review/01-architecture-review.md item 6 (settings monolith) with
a reasoned reject rather than a mass rewrite.

### Changed
- Docs only: evaluated giving each service only its own settings section instead of full `ISettingsService` access. Rejected as a retrofit: `ISettingsService` is referenced from 123 files, and settings persist as one atomic file/save/migrate/change-notification, so a per-section wrapper would still need the whole object for the operations that matter, making a mechanical rewrite mostly cosmetic. No bug traces back to this. New services should still prefer taking only the section they need going forward.

## [0.9.29-alpha] - 2026-07-07

Interface ceremony cleanup (docs/review/06-technical-debt.md item 3):
collapses single-implementation interfaces that had no test double and no
cross-project seam requirement to concrete classes. Internal refactor, no
behavior change.

### Changed
- 13 interfaces collapsed to concrete classes: `IBackupService`, `IBenchmarkService`, `IConversationExportService`, `IEvalEngine`, `IInspectionEngine`, `ILocalAiSetupService`, `IMemoryExtractionService`, `IMemoryInjectionService`, `IModelProfileService`, `IPrivacyAuditService`, `IRedactionService`, `IRuntimeProfileService`, `ITrustService`. Consumers now depend on the class directly; DI registers the concrete type instead of an interface mapping.
- Small data types that lived alongside a deleted interface file (`BackupResult`, `ConversationExportFormat`, `RuntimeHealth`) moved to their own file in `Aether.Core.Services`.
- The remaining single-implementation interfaces were audited and kept deliberately: about a dozen have real test-double implementations in the test suite (a genuine seam), and `ITraceStore`/`IAgentRetrievalService`/`IMcpToolBridge` are cross-project boundaries `Aether.Agent`/`Aether.Rag` are architecturally forbidden from crossing directly.

## [0.9.28-alpha] - 2026-07-07

Closes docs/review/02-dependency-review.md's staged item 4 (evaluate an
out-of-band ONNX Runtime download) with a reasoned reject rather than an
implementation.

### Changed
- Docs only: evaluated shipping ONNX Runtime as a Doctor-installed component instead of a bundled package. Rejected: `Aether.Voice`'s native Kokoro provider (now the default voice provider) also depends on ONNX Runtime, so most installs need it regardless of RAG/reranker use, and dynamically loading a downloaded native runtime is a materially riskier operation than today's data-only Doctor downloads (model weights). Revisit only if base install size becomes an actual reported problem.

## [0.9.27-alpha] - 2026-07-07

Naming/vocabulary drift (docs/review/06-technical-debt.md item 7) closed as
documentation, not renamed code: `ModelProfile`/`RuntimeProfile`/
`WorkspaceProfile`/tune profiles are deliberately separate schemas sharing a
word, and are stable public types by now, so a cosmetic rename was judged
riskier than the naming friction it relieves.

### Changed
- `CONTRIBUTING.md` gained a "Vocabulary" section disambiguating "memory," "workspace," and "profile" for future contributors, and an "Explicit non-goals" section listing what Aether deliberately does not do (hosted services, accounts, telemetry, plugin API, provider failover, vector databases, an ORM, a web UI), per docs/review/07-roadmap.md's closing instruction to write these down so they survive contributor enthusiasm.

## [0.9.26-alpha] - 2026-07-07

Project-level AI configuration (docs/review/03-architectural-opportunities.md
item 10): finishes the "selecting a workspace activates its full
configuration" thought. `.aether/workspace.json`'s instruction paths already
round-tripped, but nothing ever read the files back into the agent's context.

### Added
- `AgentContextPack.ProjectInstructions`: the Agent now reads each activated workspace's declared instruction files (`AGENTS.md`, `CLAUDE.md`, etc., whichever were saved via "Save as Workspace Defaults") and folds their content into the context pack sent to the model, budget-capped like every other context section. Previously these paths were stored and shown in the UI but never actually reached the model.
- The Agent panel's Retrieved Context list now also shows activated project instructions, alongside retrieved memory and files.

## [0.9.25-alpha] - 2026-07-07

Aether as the machine's AI substrate, phase 1 (docs/review/07-roadmap.md,
3.0 long-term vision): deepens `Aether.LocalApi` and adds per-app data-flow
visibility to Privacy Audit.

### Added
- `GET /v1/models` on `Aether.LocalApi`: lists visible models (id, name, provider, context length) so a calling app can discover what's available instead of hardcoding a `modelId`.
- Every local API call is now logged to the shared `ITraceStore` as a new `TraceKind.LocalApi`, keyed by the caller's self-reported `X-Aether-Client` header (defaults to `"unknown"` if the caller doesn't send one).
- Privacy Audit gained a "Local API activity" item: reports distinct calling apps, per-app call counts, and last-seen time, sourced from that trace history. Reports "Disabled" when the host is off and "No calls yet" when enabled but unused.

### Changed
- `TraceKind` gained a fourth value, `LocalApi`, alongside `Chat`/`Rag`/`Agent`.

Explicitly deferred: per-app tokens (the client header is self-reported and advisory, not verified; today's auth is still one shared token), an embeddings endpoint, a settings/capabilities probe endpoint, and an agent run/step endpoint (needs its own design pass for how a non-interactive caller satisfies the agent's approval gate).

## [0.9.24-alpha] - 2026-07-07

Model landscape hedging audit (docs/review/07-roadmap.md, 3.0 long-term
vision): confirmed the LLM runtime layer is already thin and provider-agnostic,
and fixed the one real leak found.

### Fixed
- `RuntimeProfile.StartManagedLlamaServer`/`LinkedServerId` are llama.cpp-only concepts that were sitting on a record shared by all three `RuntimeKind`s (`LlamaCpp`, `Ollama`, `OpenAiCompatible`), meaning a future runtime kind could silently inherit meaningless llama-specific state. `RuntimeProfileService.NormalizeProfile` now forces both fields inert for any non-`LlamaCpp` profile, and the Services view's "Start managed llama.cpp" checkbox is only shown when that runtime kind is selected.

## [0.9.23-alpha] - 2026-07-07

Opens the 3.0-horizon "long-term vision" roadmap with the first slice of
"provenance everywhere" (docs/review/07-roadmap.md).

### Added
- `Aether.Core.Models.SourceReference` and `ProvenanceKind`: a small shared record for pointing back to where a piece of content came from (a RAG chunk, a memory, a workspace file, or an agent tool result), the seed of generalizing RAG's citations to the rest of the product.

### Fixed
- The agent's minimal RAG retrieval contract (`RetrievedChunk` in `Aether.Core.Services`) was silently dropping the source file/path for every chunk pulled into agent context. It now carries a `Locator`, threaded through `AgentRetrievedItem` into the Agent view's "Retrieved Context" panel, so RAG-sourced context in the Agent panel is traceable back to its file again.

### Changed
- `AgentToolResult` gained an optional `SourceReference? Source`, populated for tools with one clear locator (`read_file`, `summarize_file`, `draft_patch`, `apply_draft_patch`, `mcp:*`); left null where a single source doesn't make sense (`run_command`, `list_files`).

## [0.9.22-alpha] - 2026-07-07

Flips the native Kokoro-ONNX voice path from opt-in to the default, closing
the one deliberately open thread from the 0.9.21 v2.0 pass, and opens the
3.0-horizon roadmap (docs/review/07-roadmap.md, "Long-term vision").

### Changed
- **Kokoro (native) is now the default voice provider.** `TtsSettings.VoiceProvider` defaults to `"KokoroNative"` and `VoiceProviderRegistry.ParseProviderFromSettings` falls back to it for unrecognised settings values. The Python-based `KokoroVoiceProvider` moves to `VoiceProviderCategory.Advanced` alongside XTTS v2 and F5-TTS, staying available as a fallback rather than being removed.
- `LocalAiSetupService`'s readiness scan and setup actions no longer ask for a Python venv, Python health check, or voice packages when the active provider needs none (native Kokoro, OpenAI); it reports a plain "handled by Doctor" item for those instead. This removes a confusing "create a Python venv" prompt that would otherwise show up on a fresh install now that the default provider needs no Python at all.
- `SetupWizardViewModel` now matches the active voice provider by its stable `VoiceProvider` id instead of matching the display name against the raw settings string, fixing a pre-existing mismatch that made the wizard's provider preselection fall back to the first list entry rather than the actual configured provider.
- `NativeKokoroVoiceProvider` no longer reports the `Experimental` capability flag, reflecting its promotion to the default provider.

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
