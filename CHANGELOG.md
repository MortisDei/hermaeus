# Changelog

All notable changes to Hermaeus will be documented in this file. Append newapp versions above the previous version (not the bottom of the doc or where ever feels right at the time).

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

## [0.38.0-alpha] - 2026-08-24

### Added

- Agent tool, MCP, approval, safety-gate, and rewind records now retain a
  deterministic provider-neutral outcome beside their raw evidence. Historical
  tasks remain loadable as `Unknown`, and the five evidence origins now remain
  distinct through JSON persistence while legacy `Inferred` values load as
  model inference.
- Added the Lab Evidence surface and the additive `experience.db` store for
  typed Agent, GPU Fit, and Lab-run evidence, with exact filters, redacted
  export, linked corrections, confirmed hard removal, and data-root migration
  and backup coverage.
- Runtime capability evidence now uses an extensible dotted-id registry with
  exact runtime and optional model identities. New benchmark evidence also
  carries a path-free v2 runtime/model/hardware/configuration fingerprint while
  historical v1 fingerprints and capability caches remain readable.
- GPU Fit now exposes a deterministic weights, separate K/V, runtime overhead,
  companion, and policy-headroom breakdown in Services. A shared runtime
  telemetry source records process-scoped RAM and honest Unknown GPU evidence,
  and fingerprint-matched observations can be stored and compared without
  modifying the analytical prediction.
- Lab now freezes immutable experiment definitions, launches a dedicated
  loopback runtime without mutating Chat or saved settings, preserves
  source-labelled observations and correctness-gated comparisons, cleans up by
  exact process ownership, and exposes a stale-identity-guarded Apply review
  through the normal settings save flow.
- Added bounded Lab recipes for GPU layers, context, runtime-advertised KV,
  Flash Attention, and CPU-MoE placement. Each preserves its baseline, combines
  GPU Fit prediction with shared process telemetry and greedy correctness
  evidence, stops on repeated failure or mismatch, and stores repeated evidence
  in bounded immutable configuration slices without auto-selecting a winner.
- Added conditional Lab adapters for general external drafting and EAGLE-3,
  plus bounded one-at-a-time draft maximum/minimum, probability, and GPU-layer
  recipes. Exact runtime flags, verified target/companion identities,
  tokenizer/vocabulary compatibility, EAGLE target binding, draft acceptance,
  memory, and output equivalence remain explicit gates; missing proof stays
  `Unknown`.
- Added a controlled prompt/shared-prefix Lab recipe that pairs identical
  reconstructed prompts with request caching disabled and enabled, retains only
  prompt hashes, and reports prompt timing/throughput plus exact output
  correctness. Direct reused-token counts remain Missing unless the exact
  runtime proves a reviewed response field; timing is never converted into a
  token count.
- Added revisioned, user-owned Project State with directly editable objectives,
  milestones, status, and structured continuity items. Model-origin proposals
  remain in a provenance-visible review queue until edited/accepted or rejected,
  stale revisions fail atomically, and only bounded accepted state reaches
  project-bound Chat and Agent context receipts.

### Changed

- Embedded MTP is now `Available` only after model-specific capability or
  observed drafting evidence. NextN metadata plus generic runtime help remains
  `Unknown`, and a failed probe never becomes `Unavailable`.

## [0.37.0-alpha] - 2026-08-20

### Fixed

- Windows packaging now initializes MSVC and the Windows SDK through Visual
  Studio Developer PowerShell, so `build.ps1` works from a normal PowerShell or
  VS Code terminal without requiring a Developer Command Prompt.
- Windows portable packages now keep desktop and Local API runtime files under
  `app/`, documentation under `docs/`, and icon assets under `icons/`. A tiny
  source-included native `Hermaeus.exe` at package root replaces the command
  launcher and starts only the bundled `app\Hermaeus.Desktop.exe`.
- Fallback secret encryption keys now live outside the portable Hermaeus data
  root in a user-specific OS configuration location. Existing same-root keys
  migrate when the secret store initializes, and backup/restore copy now states
  that credentials must be re-entered on another machine.
- Agent patch apply, revert, reject, and block decisions no longer enter the
  unrelated pending-tool approval path, preventing a patch review click from
  executing or dismissing a different queued action.
- Managed llama.cpp archives now verify SHA256 before extraction: the pinned
  b10034 assets use source-controlled hashes and latest/CUDA assets require the
  digest from GitHub release metadata. Linux package tar headers no longer
  expose the builder's user/group name or preserve group-write bits.
- Linux packages now give the Avalonia window, X11/XWayland WM class, installed
  desktop entry, and icon theme resource one `hermaeus` identity, restoring
  taskbar association with the Moss icon. The extracted `Hermaeus` launcher is
  now a relocation-safe link to the native apphost, so file managers launch it
  as an application instead of opening a shell script as text. Native
  `Install Hermaeus` and `Uninstall Hermaeus` actions now provide graphical
  confirmation while keeping their shell implementations under `app/`.
- Doctor now turns Linux tray support Ready only after the current session's
  tray has responded to an interaction. Chat no longer offers first-run setup
  as recovery for a stopped or absent model after onboarding is complete, and
  directs those users to Services instead.
- Deleting the active conversation now returns keyboard focus to the fresh Chat
  input. Normal Chat receives a compact, dynamic Hermaeus environment block
  grounded in the selected runtime, attachments, Knowledge, memory, and Recall,
  while explicitly excluding web, shell, tools, and Agent workspace actions.
- The onboarding starter catalogue now uses verified current Phi-4 mini, Gemma
  4 E2B/E4B QAT, and official Qwen3 8B/14B GGUF files. Native Kokoro readiness
  rechecks after install and across step navigation and restart.
- Managed llama.cpp release selection now ignores unrelated semver tags and
  chooses the newest b-numbered release with a compatible platform asset.
  Settings and onboarding share that installer with Doctor, successful installs
  update Services paths, and Doctor can discover the newest managed build.
- Diagnostic Moss notifications can expose a restrained Copy details action.
  Linux packages now keep runtime internals under `app/`, resources under
  `icons/` and `docs/`, use a public `Hermaeus` launcher, install the canonical
  application-menu icon, and omit PDB files.
- Added a release-user guide, clarified RID restore requirements for packaging
  with skip-restore, and corrected the commercial-licensing summary so it does
  not narrow PolyForm Noncommercial's permitted institutional uses.
- Linux managed llama.cpp installs now preserve required SONAME companions from
  safe in-archive links, and Doctor executes the configured binary before
  calling it usable. Incomparable release identifiers report unknown instead
  of "current enough."
- Doctor model-download progress is rate-limited, coalesced, and bounded;
  navigation preserves the active operation. Its install dialogs and Moss
  diagnostics have solid readable backgrounds, and install approval remains
  disabled until the specific plan has been reviewed.
- Data-root migration excludes the process-owned `hermaeus.lock`, while the
  single-instance guard remains exclusive. Error notifications also enter
  Runtime Logs, notification times display locally, and the unclean-session
  title now agrees with its detail.
- Incomplete first-run setup has a persistent resume action, retains its exact
  step across diagnostics navigation, and includes brief factual Moss guidance.
  Managed embedding warm-up waits for its localhost server to be Running.
- Expected localhost health races now observe abandoned probe faults instead of
  leaking them to the unobserved-task log. Hermaeus-managed Hugging Face model
  downloads retain their existing manifest provenance.
- Model update checks persist a first calculated model hash before the remote
  lookup, so an interrupted lookup does not hash the same model again. Doctor
  now accepts current llama-server `build 10509` version output.
- Changing models now replaces auto-selected vision projectors and MTP draft
  heads with the sole companion belonging to the newly selected model, while
  preserving explicit companion choices. Near-equivalent extracted memories
  reinforce one row instead of accumulating paraphrases.
- Expanded reasoning is visually boxed and labelled, separating it from the
  final answer.
- The default dedicated embedding model is now the SHA256-pinned
  Qwen3-Embedding-0.6B-Q8_0 GGUF. Existing Nomic installations stay selected
  and on disk until a verified Qwen download completes, then Doctor points the
  embedding server at Qwen and leaves RAG/memory reindexing user-controlled.
- Starter model downloads now adopt matching files on retry, verify hashes before
  completion, write provenance, and refresh Services immediately after setup.
- Models and Services share one context default and one KV-cache precision. Model
  cards show download state and offer confirmation-gated safe deletion.
- Reasoning deltas are preserved separately from answers through providers,
  storage, history policy, local API, transcript rendering, and exports. Replay
  requires proven managed llama.cpp template support and preservation settings.
- Deterministic benchmark scoring now handles multiline structures, grouped
  digits, explicit refusal language, and explicit keyword alternatives without
  rewriting historical runs.
- Benchmark runs now preserve the managed llama-server KV cache K/V types and
  Flash Attention setting in saved provenance, run details, comparisons, and
  JSON, Markdown, and CSV exports. Historical runs remain unmodified and show
  these settings as not recorded.
- Memories deletion is confirmation-gated and its item commands bind through the
  named view root.
- Agent transcript replay now compresses only consecutive, proven-identical
  successful tool outcomes for the next model decision. Raw transcripts remain
  intact; failed, partial, changed, and older unproven entries remain verbatim.
  A repeated successful sequence is diagnostic-only and never auto-blocks work.
- Voice orchestration tests now synchronize on provider start/completion signals
  instead of fixed sleeps. Voice provider registry behavior is covered for
  stored aliases, fallback logging, persistence, catalog metadata, and service
  mapping.
- Managed llama-server settings now discover speculative modes and
  `--threads-batch` from the selected executable, recheck them at launch, and
  refuse stale unsupported settings instead of silently emitting them. Prompt
  processing threads are stored separately from generation threads when the
  runtime supports them.
- Capability snapshots now record meaningful runtime changes in Activity. Moss
  reports one deduped heads-up for a changed snapshot, with a warning when a
  disappeared capability can affect the configured server.
- The benchmark model-selection regression test now uses ephemeral loopback
  ports, so an unrelated listener cannot make it misreport that a benchmark
  restart was skipped.
- Benchmark observations now carry a persistent fingerprint for the known model
  and inference configuration, including prompt-processing threads, plus direct
  local provenance. This identifies what was measured without turning results
  into automatic recommendations or backfilling historical assumptions.
- Memories now support typed, evidence-backed relationships while preserving
  legacy related-memory ids. Recall remains lexical/vector-first, can inspect
  one direct active relationship, and exposes relationship-expanded context in
  the chat receipt. A superseded memory yields to its direct current fact.
- Public release readiness now includes immutable official GitHub Action pins,
  minimal CI and release token permissions, monthly Dependabot updates, and
  release-first checksum verification guidance.

From 0.29.0-alpha onward, every minor version is tagged and released on
GitHub (see `docs/packaging.md` "Releases"); patch versions are tagged only
for urgent hotfixes.

## [0.36.0-alpha] - 2026-08-01

Implements docs/review r29: things that look like they work. Every item in the
round was something that presented itself as working and was not, and that runs
through the UI and the test suite alike.

### Fixed

- **The Services page saves.** The Voice and speech-recognition cards at the
  bottom of it edit the same settings the Settings page does, and nothing on
  the page ever wrote them to disk, so a base URL, voice, device or speed set
  there was silently discarded on restart. There is now a Save button in the
  page header, running the same single save flow as the Settings page's, and
  opening Services refreshes the settings sections first so nothing stale can
  be written back over a real value.
- **There is a key that inserts a newline in the chat box.** With
  "Ctrl+Enter to send" off, which is the default, Enter sent and nothing at all
  produced a newline. Enter now sends and Ctrl+Enter inserts a newline; with
  the setting on, the two swap; Shift+Enter inserts a newline either way.
- **The copy and read-aloud buttons under the last message can be clicked.**
  They used to end up flush against the input bar. They also have a larger hit
  target.
- **The per-channel voice pickers in Settings > Voice look like pickers.** They
  have a chevron that opens the list, and they show the whole list on focus
  instead of waiting for you to guess a first character. When the voice service
  has not listed its voices, the section says so and says where to fix it,
  rather than showing a list of one placeholder that looks populated.
- **A server that dies on launch is reported at once.** The health wait used to
  run its HTTP probe out to a two-second timeout and then sleep a 600ms poll
  interval before reporting a process that had already exited. Both are now
  raced against process exit.
- The Services voice card no longer says named voice profiles are in
  Settings > Voice. No UI has ever created or edited one.

- **RAG chunks larger than 512 tokens are embedded instead of silently
  refused.** The embeddings server was launched with a hardcoded 512-token
  physical batch, added to silence a startup warning. That number is also the
  largest input the server will embed at all, and the default chunk size (1600
  characters plus 320 of overlap) is 500 to 650 real tokens, so a large share
  of every ingest was rejected outright. Nothing surfaced it: ingestion
  reported success for the chunks that happened to fit and the rest went to the
  runtime log, where one owner log had accumulated 846 of them. The batch now
  follows the server's context size, which is the real ceiling on a single
  embedding input anyway.
- **Clearing a text box in the project editor no longer crashes the app.**
  Avalonia binds an emptied box back as null, and a null parameter does not
  bind as NULL against a NOT NULL column: it threw while preparing the
  statement and reached the UI as an unhandled fault.
- Managed llama.cpp release lookups use the `ggml-org` organisation. The
  project moved from `ggerganov`, and GitHub's redirect was doing the work.
- **The cursor flicker on the nav rail and the chat icon bar.** The gaps between
  icon buttons hit-test to nothing: the nav rail's buttons meet at rounded
  corners, and the chat toolbar sets `ColumnSpacing="8"`. A pointer in one of
  those gaps falls through to the window's root panel, whose cursor is the
  default arrow, so crossing a row flickered hand/arrow/hand a few pixels before
  each button boundary. A pointer-event log taken during the flicker showed the
  pointer-over chain collapsing to that root panel and rebuilding about 80 times
  a second, with 953 enter/exit pairs on one inactive nav button against 7 on
  the active one.
  Icon-button containers now carry a transparent, hit-testable background with
  `Cursor="Hand"`, so the gaps resolve to the row instead of the window root. The
  nav rail and the chat toolbar set it in xaml because they also hold non-button
  content; every other all-button container gets it automatically from
  `Desktop/Controls/IconBarCursor.cs`, since icon buttons appear in fifteen axaml
  files and a missed container is an invisible regression.
  The background has to sit on the container itself rather than on a sibling laid
  over the buttons. A control's own background is painted behind its children and
  is only hit where no child is hit, so it fills the gaps without taking the
  pointer off a button; a sibling `Border` was tried in the chat toolbar first and
  swallowed the buttons' hover highlight.
  Both halves matter: a panel with no `Background` is never a hit-test result,
  and `TopLevel` takes the cursor from the hit element without walking up to
  ancestors, which is why setting `Cursor` alone on the nav panel in r27 could
  never have worked.
  Containers that also hold text set it in xaml instead, since the automatic pass
  deliberately skips those so the hand cursor cannot spread across a settings
  page: the nav rail, the chat toolbar, and the conversation rows, whose details
  button sat beside its title with a 4px gap.
  The button hover styles also gained base-state backgrounds, so a hover swaps
  one brush for another rather than introducing one, and a guard test fails the
  build if a hover style is added without its base pair. Base and hover selectors
  are mutually exclusive: while both matched, which one won depended on style
  activation order rather than on anything written in the file, and the hover
  highlight stopped appearing on the nav buttons.
  This bug survived five earlier attempts across r27 to r29 aimed at cursor
  regions, tooltip placement, tooltip hit-testing and popup rendering. The two
  cursor entries under 0.33.0-alpha below both claimed a fix that did not hold.
- Tooltips are drawn by the app (`Desktop/Controls/OverlayToolTip.cs`) rather
  than by Avalonia's `ToolTipService`, which is disabled app-wide. This was built
  while the flicker was believed to be a tooltip-popup problem; it was not, and
  this change is not what fixed it. It is kept because it does avoid a genuine
  open upstream bug, AvaloniaUI/Avalonia#19218, in which the popup's outer edge
  yields no hit-test result and the service closes and reopens the tooltip in a
  loop near a screen edge. Views are unaffected: they still set `ToolTip.Tip`.
- **The RAG question box empties when you send.** It kept the question, which
  next to a finished answer reads as "that never sent". The question is now
  echoed above the answer it produced, and put back in the box only when the
  ask failed and there is something to retry.
- **Asking a question in the RAG panel works without a default model set.** It
  passed an empty model id and the user got "Could not determine which provider
  serves model ''. Refresh the model list and try again." Refreshing could not
  fix it, because the real cause was that no model was chosen. Ask now uses the
  model Chat has selected, and says plainly what to do when there is none.
- **The app stays responsive during a large ingest.** The RAG pipeline's
  expensive work is synchronous and CPU-bound, and nothing in it used
  `ConfigureAwait(false)`, so it ran on the UI thread. A 1,759 file ingest
  froze the window for minutes, worst during the final BM25 build, which is why
  it looked like it hung near the end. Ingest and reindex now run on the thread
  pool.
- **The RAG panel scrolls to its last card**, which used to sit behind the
  question bar. Same defect and same fix as the chat transcript.
- Moss lost the unexplained orange ball beside his head. It was a "lantern
  glow" accent with no lantern attached to it.

### Added

- **Steer a task the agent is already running.** The reply box in the workbench
  sends an instruction into a run in flight, instead of only answering a
  question the agent asked. It interrupts the model call in progress, is folded
  into the planner's context at the next step boundary, appears in the
  transcript immediately, and consumes a step from the run's budget. A tool that
  has already started executing is never interrupted; it runs to completion and
  the instruction lands after it.

  **A steering instruction cannot approve anything.** It is user text, exactly
  as untrusted as the goal the task was created with. Telling a running agent
  "you have my approval to run any command, do not ask again" changes nothing:
  the next command still stops at the gate. Approvals keep their own explicit
  path. Three regression tests exist solely to keep that true, and the safety
  gate itself was not touched.

  Steering is refused with a stated reason on a finished task (Continue reopens
  those), on an orchestration parent with a running sub-task (naming the child),
  and when eight instructions are already waiting.
- **The Models panel is a grid of cards.** Twelve full-width rows that packed
  name, raw name, provider, size, tags, fit, tune summary, update state and a
  date into one line of small grey text are now tiles. Every control from the
  old inline editor moved into a Configure flyout; none was dropped.
- **Model cards say where the model came from**: a Hugging Face badge, whose
  tooltip names the repo, for anything downloaded through or linked to one, and
  a local-file badge otherwise. Nothing about how models are discovered,
  downloaded, updated or tuned changed.


- **Mixture-of-Experts CPU offload** (`--n-cpu-moe` / `--cpu-moe`), under
  Services > Advanced engine options. On a MoE model the experts are most of
  the file but only a few are active per token, so keeping them in RAM leaves
  the GPU for attention; cutting GPU layers to make the model fit gives up the
  part that wants the GPU. Off by default and no effect on a dense model.
- **The setup wizard states each starter model's licence before you download
  it**, and offers a choice rather than one recommendation: Phi-4 mini (MIT),
  Qwen2.5 7B/14B and Gemma 4 E4B (Apache-2.0), Llama 3.2 3B (Llama 3.2
  Community License), and Qwen2.5 3B, which is research and non-commercial
  only. The VRAM-based recommendation is the starting selection and stops
  overriding you the moment you pick, and it now recommends Phi-4 mini rather
  than Qwen2.5 3B on a machine with no GPU: a default should not carry use
  restrictions.
- **Close to tray is its own setting.** It used to share one flag with
  "Minimize to tray", so wanting minimize-to-tray also meant the close button
  could never actually quit the app. Both default to today's behaviour.
- **Moss is the application and tray icon**, replacing the Archivist's Seal
  monogram. The raster assets are generated by `scripts/generate-icons.ps1`, so
  they can be regenerated rather than being opaque binaries. The generator is a
  shape-for-shape transcription of the in-app `MossIcon.axaml`, so the taskbar
  and the app show one character; the first version redrew him instead and came
  out looking like a cowboy. The icons are also full bleed, because the dark
  field the first version sat on made Moss render noticeably smaller than every
  neighbouring taskbar icon while adding nothing visible on a dark shell.

### Changed

- **THIRD-PARTY-NOTICES.md now accounts for everything**, organised by what
  Hermaeus does with each thing: code it redistributes, native libraries inside
  those packages, bundled data (including the Inter typeface, previously
  undocumented), components downloaded to your machine but not redistributed
  (llama.cpp, and NVIDIA's CUDA runtime, which is not open source), model
  weights with each publisher's terms, and the services it calls. A guard test
  fails the build when a shipped package has no entry.
- `docs/llama-cpp-features.md` records a survey of llama-server's current flags
  against what Hermaeus emits, including what was considered and deliberately
  left alone.
- **The test suite reports what it actually ran.** Sixteen tests began by
  returning early on Linux, so the Linux CI leg recorded a green tick for
  llama-server installation, Python health validation, job-object assignment and
  manifest behaviour it never executed, and both legs reported `Skipped: 0`.
  They now report Skipped. The Linux leg's counts change accordingly; the
  discovered total does not. A guard test stops the pattern recurring.
- The coverage floor is 60%, matching the measured 61.6%. It was 45% in the
  docs and 47% in the scripts, both far enough below the real number that the
  ratchet could not fail on any regression short of deleting a quarter of the
  suite.
- `docs/testing.md` records what the suite is: how to run it, why it is
  sequential, the platform-skip attribute, the injectable-timeout rule, the
  guard tests, the coverage numbers per project, and why Windows CI is slower
  than Linux CI for runner-I/O reasons rather than code reasons.
- Tests that proved a component gives up on a hanging dependency by waiting out
  the real timeout now inject a short one. `MemoryStore` and `RecallService`
  take an optional timeout parameter defaulting to today's value; production
  behaviour is unchanged.
- CI's Windows leg excludes its test working set from Defender, and the test
  temp root honours `RUNNER_TEMP`. This is an experiment with a stated success
  condition and gets deleted if the Windows test time does not fall materially.

## [0.35.0-alpha] - 2026-07-31

Implements docs/review r28: small models, kept honest. Where Hermaeus needs a
reply in a particular shape it now enforces the shape instead of asking for
one, the Speed Check can tell a null result from a no-op, Activity rows take
you to what they describe, and the Windows CI gap was measured before anything
was changed about it.

### Added

- **Output that cannot be malformed.** A request can carry a JSON schema or a
  GBNF grammar, and the provider's sampler enforces it while the tokens are
  chosen. llama.cpp enforces both forms, Ollama enforces a schema, and
  OpenAI-compatible endpoints get one only against `api.openai.com`, where
  structured outputs are documented. Anywhere else, a constraint is refused in
  words at the point of use rather than sent as a field the server may quietly
  drop: a caller that sets a constraint intends to parse the answer without
  defending against prose. Both llama.cpp field names were confirmed against a
  running b10195 before the code was written.

  Plenty of compatible servers support the field and cannot say so, so
  **Settings > LLM carries a "This endpoint enforces response_format"
  checkbox** beside the OpenAI base URL, off by default. It is a declaration,
  not a probe: you are telling Hermaeus what your own server does, and if the
  claim is wrong the server's rejection is surfaced rather than retried
  unconstrained.

- **Memory auto-summary asks for a schema.** On a provider that can enforce
  one, the extraction shape is required rather than requested. All three of
  the existing fallbacks stay, because they are what runs everywhere else;
  what changes is that they stop being the common path.

- **A planner protocol a small model cannot get wrong.** The agent's action
  protocol was a schema written in prose at the end of a system prompt, asked
  for on every step and defended by an extractor, one targeted repair and an
  error budget. It is now a real schema sent as a constraint whenever the
  selected model's provider can enforce one, which includes every local
  llama.cpp and Ollama model. This matters most for MCP tools, which reach the
  model only through that text protocol regardless of provider.

- **Draft acceptance, reported.** llama-server counts the tokens it drafts and
  the tokens the target model accepts, and both are now read and shown beside
  the speed. `0 drafted` means drafting never engaged and a comparison was run
  between two identical configurations. A provider that reports no counters
  shows nothing rather than a zero. Hermaeus Doctor reports the same fact
  without needing a benchmark run, across three separate answers: drafted
  nothing, reported nothing (the server was started without `--spec-type`,
  because changing the setting does not restart a running server), and never
  measured.

- **The Speed Check runs five iterations per case** instead of one, and a
  comparison reports the median with the range observed, written as
  `70.2 tok/s (66.8 to 71.9 over 5 runs)`. A description of what was seen, not
  a confidence interval.

- **Activity rows take you where they point.** A row that names a specific
  artifact opens it, routing through the same navigation the command palette
  uses. A row with nothing specific to open stays inert rather than offering a
  link that goes nowhere. Consecutive rows within a minute sit under one time
  heading, which is arithmetic on the clock and not a claim that they are
  related.

- **The four Activity sources r24 named and never wired.** Model downloads (a
  partial file set records as Partial with the reason naming what is missing)
  and model updates, which download and then replace a file on disk,
  backup and restore in both directions, memory auto-archive sweeps including
  the ones that archive nothing, and the managed voice backend's start, stop
  and failure.

- **The chat trace records whether a turn's shape was enforced**, or
  "unconstrained", so a reply that parsed cleanly can be told apart from one
  that was made to.

### Changed

- **The agent stops telling local users their model is too small.** When a
  planner reply cannot be parsed, the message now distinguishes a provider
  that could not enforce a shape from a model that missed a shape that was
  enforced. Those are different problems with different answers, and only one
  of them is solved by downloading a bigger model. Each task records whether
  its planner calls were constrained, so "did this help" is answerable from
  real runs.

- **`docs/agent.md`'s risk table names the tools the gate actually holds**, and
  records that `run_command` is Review because the dispatch path sends it to
  `EvaluateCommand`, which blocks any command family the workspace has not
  declared. The per-task remembered-approval nuance is documented beside it. A
  guard test now fails if the table and the gate's two tool sets disagree,
  along with two more enumerated facts: `docs/benchmarks.md`'s recorded run
  metadata against `BenchmarkMetadata`, and AGENTS.md's settings-section list
  against `AppSettings`, in both directions.

- **CI writes per-test timings on both matrix legs** and uploads them, and
  NuGet packages are cached. The Windows test step takes about 3.5 times as
  long as the Linux one, and the timings say the gap is broad rather than
  concentrated: the 20 test classes with the largest delta own 86% of it and
  every one of them touches the filesystem, SQLite, or a spawned process. That
  makes the lever per-write cost rather than parallelism, so an opt-in
  parallel test collection was descoped on the evidence rather than attempted.
  Tests stay serial by default. The measurement and what was done about it are
  recorded in `docs/pull-requests.md`.

- **The nav rail shows which panel you are on.** The active button's icon
  carries the brand accent and scales up slightly, through a render transform
  rather than a size change so navigating never reflows the row.

### Fixed

- **`ShowActivity` never raised `PropertyChanged`.** It was left out of
  `OnActivePanelChanged`'s notification list when the property was added, so
  anything bound to it stayed stale. A guard test now fails if a per-panel bool
  is added without being notified.

- **A `Process` object that was constructed but never started made `Stop()`
  throw.** `Process.HasExited` raises for a process with no OS process behind
  it, which is exactly the state the voice process managers are in when
  `Stop()` runs from a failed start.

- **`SqliteEvalStore` committed a saved run and its retention prune
  separately.** Two durable commits per save, and a crash between them left
  the table over its cap. One transaction now, matching the trace store.

- **The Benchmarks panel silently reset five suite fields on every run.** The
  clone it takes before running dropped `IterationsPerCase`, `SuiteVersion`,
  `ScoringProfile` and both baseline fields, plus `CaseVersion` and
  `ExpectedBehaviourVersion` per case. Every run recorded a default scoring
  profile and version, and any suite asking for repeated iterations ran
  exactly one per case and recorded itself as "Cold". It went unnoticed
  because every shipped suite wanted one iteration until the Speed Check asked
  for five.

## [0.34.0-alpha] - 2026-07-31

Implements docs/review r27: fast, and honest about it. Startup stops waiting
for a model, retrieval scales with the answer instead of the corpus,
speculative decoding ships with a way to measure whether it helped, and a
downloaded model arrives complete.

### Fixed

- **A RAG dataset above the cache ceiling silently answered nothing.** A
  dataset whose scan index did not fit the 128 MiB in-memory budget was
  dropped by the cache without being cached, and the query then read that
  empty cache entry back and scored nothing. It returned no results and
  reported no error, after reading every chunk and every embedding out of
  SQLite, and it did that again on the next query and the one after. An
  over-budget dataset is now scanned from storage and says so in the
  retrieval result's planner notes. "Not cached" and "cached and genuinely
  empty" are different states.

- **A message typed before the chat server was ready vanished.** Pressing
  send with no model selected returned silently, so a question the user
  typed and submitted did nothing and said nothing. It is now held, shown as
  held with its reason, and sent once when a model lists.

- **The README said 0.24.0-alpha.** Nine releases out of date, on the front
  page. `DocsCoverageGuardTests` now parses `VersionPrefix` out of
  `Directory.Build.props` and requires it in the README, with a failure
  message that names the line to edit.

### Added

- **Speculative decoding, as one composable section.** `--spec-type` takes a
  comma-separated list, so drafting and n-gram speculation can run together
  rather than fighting over one bool. Flags were read from the installed
  llama-server's own `--help` rather than recalled: `--draft-max`,
  `--draft-min` and the `--spec-ngram-size-*` family have been removed
  upstream and now do nothing while printing that they were removed, and a
  test asserts none of them is ever emitted. A legacy `NgramSpeculative:
  true` upgrades to `Types = ["ngram-mod"]` exactly once and produces
  byte-identical launch arguments.

- **A draft model is checked before the server starts.** Path traversal,
  symlinks, existence, and a GGUF vocabulary-size comparison against the
  target. A mismatch refuses the start naming both models and both
  vocabulary sizes; a draft larger than half its target warns instead,
  because that is a bad idea rather than a broken one.

- **A Speed Check suite, and a comparison between two of its runs.** The first
  recorded result is in `docs/benchmarks.md` and is a null result: on
  gemma-4-E4B-it-qat-UD-Q4_K_XL, `draft-mtp` measured 70.2 median tok/s against
  `ngram-mod`'s 69.7, with time to first token 175 ms worse, which one
  iteration per case cannot distinguish from noise. That is what the check is
  for.
  Structured, repetitive, code and free-prose prompts, chosen because
  drafting behaves differently across those shapes. It measures speed, so no
  case asserts anything about quality. A run records the speculative settings
  that produced it, and two runs of the same suite and model can be put side
  by side with the configuration difference between them. No verdict, no
  grade, no significance claim.

- **The startup breakdown is readable in System Overview.** Phases with
  their milliseconds, the concurrent block labelled as concurrent so nobody
  adds up three overlapping numbers, and per-server auto-start times reported
  separately because they are no longer part of the total.

- **Chat says the server is warming.** One factual line above the composer
  with the server name and elapsed time, and past 90 seconds that this is
  longer than usual, with a way to open the Services log. No progress bar:
  llama-server reports nothing between launch and healthy.

- **Each dataset's search-index size against the memory budget** is shown in
  the RAG panel, so the ceiling is visible while a corpus grows rather than
  discovered afterwards as a slow query.

### Changed

- **Startup stops waiting for a model that nothing needed.** The three
  independent store loads run concurrently, each still isolated so a failure
  names its own operation in the log, and auto-starting managed servers has
  left the awaited chain entirely. Listing chat models never depended on it:
  a server reaching Running already re-lists models on its own. Servers now
  start at once rather than each waiting out the previous one's
  five-minute-capable health check, grouped by port so two servers sharing
  one port cannot both pass the preflight.

- **Retrieval scales with the answer rather than the corpus.** BM25
  candidates come from a new FTS5 index over chunk content instead of
  tokenising every chunk once per query variant; `Bm25Scorer` still does the
  scoring, and a test proves the FTS candidate set ranks identically to
  scoring the whole corpus. The in-memory cache holds a contiguous embedding
  block rather than documents, so its size is exact arithmetic over count and
  dimension instead of a sum over strings. The cosine scan is one pass with a
  bounded heap instead of allocating a record per chunk and sorting the
  corpus to select fifty. Content is read by id for the chunks that survive.

- **The three send-path injections run concurrently.** Memory, RAG and recall
  are independent and each already carried its own stopwatch. The pre-stream
  wait is now the slowest of the three rather than their sum, and sources
  still appear in a fixed memory, RAG, recall order regardless of which
  finished first.

- **A downloaded model arrives complete, in its own folder.** Selecting a
  GGUF resolves its file set from the repository tree: shard siblings
  (required, because a partial shard set will not load), an `mmproj`
  projector, and an `mtp` draft head, the last two offered and on by default.
  Destinations are `<models>/llm/<repo folder>/<file>`, which is what lets
  companion files with the same name from different repositories coexist and
  what makes the projector sibling scan find the right projector. Organize
  produces the same layout and moves companions with their model.

- **The conversation sidebar stops reading every message.** It drew titles,
  folders, tags and flags by deserialising every message of every
  conversation and then walking them again to backfill parent links. It now
  reads a column projection. Opening a conversation is unchanged.

### Removed

- `MainWindowViewModel.IsLoading`, which was set on every startup, bound by
  nothing in any axaml file, and had never gated a control.

## [0.33.0-alpha] - 2026-07-31

Implements docs/review r26: the review queue is a queue, the Agent workbench
has a shape, the panel says what it can do and whether the run worked, and
benchmarks answer "best across every suite".

### Fixed

- **A valid model response was rejected as unparseable JSON, stalling the run.**
  A local model that names a tool in `next_action.type` (`"type": "set_plan"`,
  `"tool_name": null`) produced a complete, well-formed response that the
  strict enum threw out whole. The user was told the response "could not be
  parsed as valid JSON" when it parsed fine, and the run stopped. Observed
  eight times across two real tasks. That one shape is now repaired into the
  protocol's own form and executed. The repair corrects the shape of the
  request, never its authority: the safety gate classifies the resulting tool
  exactly as if the model had named it correctly.
- **An unreadable response asked the user a question that did not exist.** It
  synthesized an `ask_user`, which parked the task in `waiting_for_review` and
  showed a reply box with nothing to reply to, and stopped the autonomous loop
  dead. It also made the existing three-strike budget unreachable, since
  reaching it took three manual Run Step clicks. The loop now retries by
  itself, with a corrective note appended to the transcript telling the model
  what was wrong; three unreadable responses in a row still fail the task.
- **The agent's question was never shown.** The planner's `user_message` went
  only to the log and the transcript, so a task paused on a question rendered
  a reply box with no question next to it. It is persisted on the task now and
  shown above the reply box.
- **A command containing shell metacharacters could reach an approval prompt.**
  `dotnet test && rm -rf /` matched the family prefix, so the gate offered it
  as approvable; execution would have refused it (nothing is launched through a
  shell, and the argument fails path validation), but it should never have
  looked legitimate. Such a string now matches no family at all.
- **`list_files` hid whole folders.** It shared the *search* result cap of 20,
  and the workspace walk is a LIFO stack, so a listing of a real workspace
  returned 20 entries from whichever subtree was popped first and said nothing
  about the rest. A run listed the workspace root, never saw a top-level folder
  that was genuinely there, and told the user the directory did not exist.
  Listings now have their own budget, are sorted so they are stable and shallow
  entries are not buried, include directories (a folder is never invisible
  because its own files were filtered out), and say plainly when they stopped
  early.
- **The workspace file list only populated on panel load**, so choosing a
  workspace, or opening a task belonging to a different one, left an empty list
  until the user found the Refresh button. It follows the workspace root now.
- **"step 111/20" compared two different things.** The numerator is the task's
  whole life and the denominator caps a single autonomous run, so a long task
  looked broken. The step count now reads "step 111", with "(4 of 20 this run)"
  added only while a run is actually spending that budget.
- **The cursor flickered between hand and arrow on the nav icons.** Avalonia
  places a tooltip at the pointer by default, so the popup opened under the
  cursor, the cursor reverted to an arrow, the control counted as exited, the
  tooltip closed, and the cycle repeated. Tooltips are placed below the control
  they describe now. (The earlier fix removed the dead gap between buttons,
  which was a real problem but not this one.)
- **A truncated file read read as a dead end.** The result said only
  `"truncated": true`, and a real run concluded the tool "cannot return the
  entire file content in one go" and abandoned the file. Results now carry the
  line range they cover and name the exact `line_offset` to ask for next, the
  oversized-result notice says the same, and the system prompt states it.
- **A file over the whole-file byte cap could not be read at all**, in slices
  or otherwise: the size gate ran before the line-ranged path, so `line_offset`
  could not rescue it. A bounded line range now has its own, much larger
  ceiling, which is what ranged reading is for. Every other check (ignored
  directory, symlink, text extension, read policy) is unchanged.
- **The plan panel stopped updating.** Same cause as the parse failure above:
  the model updates it with `set_plan`, and every one of those calls was being
  thrown away. The owner's task showed a plan last revised at step 36 while the
  run was on step 82, with exactly four discarded responses.
- **A conversation title overran the timestamp and the details button** in the
  sidebar. The title sat in a horizontal `StackPanel`, which measures children
  with infinite width, so `TextTrimming` never fired. It now wraps to at most
  two lines and then ellipsizes.
- **The cursor flickered between hand and arrow** on the boundary of a nav bar
  icon. The 4px `Spacing` between those buttons was dead space with a
  different cursor, so the smallest movement on an edge flipped back and
  forth. The buttons now carry that gap as internal padding instead, making
  the row one continuous hand-cursor region.
- **Choosing a benchmark suite threw the user onto the Run Detail tab.**
  Changing suite reloads the runs, which reassigns the selected run, which was
  indistinguishable from the user clicking a run row. Selections the app makes
  for its own bookkeeping no longer move the tab; a row click and a finished
  run still do.
- **Approving a finished task un-completed it and spent tokens restarting it.**
  The review queue listed every task that had ever been approved, not just
  tasks needing a decision, and `AppendApprovalAsync` accepted an approval on a
  task with nothing pending: it appended an approval record and set the task's
  status back to `Running`, which the workbench then resumed the agent loop on.
  The owner's report was "I can endlessly keep clicking approve"; each of those
  clicks was writing to `task_state.json`. An approval or rejection with
  nothing pending is now refused with a reason and changes nothing at all.
- **The review queue lists only what needs a decision now**: tasks
  `waiting_for_review` or `blocked`. Approval history is unchanged and still
  visible in the run ledger and on each row. A queue row with no pending action
  no longer renders Approve and Reject; it says whether it is waiting on a
  reply or an instruction and offers Open.
- **The queue refreshes itself** when a run pauses, after a run, a step, and a
  resumed loop. The manual "Refresh Queue" button is gone, along with "Refresh
  Memory", which could only ever have been a no-op.
- **The Agent header could eat the window.** It was an `Auto` row holding the
  stat tiles, the new-lessons strip, the capability notes, the full sub-task
  list and the full plan, none of it inside a `ScrollViewer`, so a long plan
  squeezed the working area toward zero height with no scrollbar to recover it.
- `ServicesViewModelTests.Removing_a_managed_server_disposes_its_view_model`
  drains the posted rebuild instead of waiting on a timeout, so it is
  deterministic rather than probable under full-suite load.

### Added

- **The Agent workbench is four tabs** (Run, Changes, Workspace, History) under
  a one-line status bar, each tab scrolling on its own. Every panel that
  existed before still exists, one click away. The decision the agent is
  waiting on lives in a pinned strip above the tabs and is never behind one.
  The panel opens on Run every time, never switches tabs on its own, and only
  the Changes tab carries a badge (pending patches, suppressed at zero).
- **A finished run says what it did**, on the Run tab, composed from the run
  ledger: files changed split created and edited with the line delta, commands
  run and how many failed, approvals asked for and how they went, unfinished
  plan steps, and the model's reservations. A run that changed nothing says so.
  A failed command is reported even when the task status is `Complete`. No
  score, no grade, no percentage.
- **Capability text derived from the code it describes.** The five hardcoded
  sentences under the old header are replaced by lines built from the
  executor's real tool set, this workspace's declared command recipes, its
  policy, and whether an MCP bridge is configured. A test asserts every tool
  the executor accepts is classified by exactly one line, so it cannot drift
  again.
- **Best across every suite**, on the Benchmarks Insights tab. Each suite gets
  its own leaderboard ranked by the same shared-case-set rule the overall board
  uses, and the cross-suite winner is the model with the best mean position
  across those suites, so each suite counts once and a 40 case suite cannot
  outvote a 5 case suite. The card names the suites it rests on, shows the
  leader's placing in each with the suite's full board one click away, and
  names every suite and model it left out. Fewer than two comparable suites, or
  no model present in all of them, produces no winner and a sentence saying
  which case applies.
- **The Agent panel shows that it is working.** A run in flight now drives an
  indeterminate progress bar and a rotating activity line in the status bar
  ("Step 3: Weighing the plan... 12s"), naming the current step and its
  elapsed time. The panel set one status message when the run started and did
  not touch it again until a step finished, so a long model call left every
  label frozen and read as a hung app. The clock and word rotation restart per
  step, and nothing shows for the first 1.5s so a fast step does not flicker.
- **Dismiss, on a review queue row.** Discards the task's pending action
  without executing it and closes the task as `cancelled`, so a run you are
  done with can leave the queue. Rejecting returned a task to
  `waiting_for_review`, so a row the user had finished with had no action that
  could ever clear it. Dismiss records no approval (walking away from a
  decision is not making one), keeps the ledger, approval history and
  transcript, leaves the task reopenable through the Continue box, and is
  refused on a running task or a sub-task child. `AgentTaskStatus.Cancelled`
  is new and terminal; `docs/agent.md` already documented `cancelled` as a
  task state, so this closes that drift too.
- **Ten more command families**, so an ordinary dev workflow is covered rather
  than only .NET, npm, cargo and pytest: `pnpm`/`yarn` test and run,
  `cargo check`, `cargo clippy`, `go build`, `go test`, `go vet` (including
  Go's `./...` pattern) and `python -m pytest`. Installers, formatters,
  long-running processes and `make`/`mvn`/`gradle` stay out, each for a stated
  reason rather than by omission; see `docs/agent.md`.
- **A blocked command says what can be done about it.** If the agent asks for a
  family this workspace has not declared, the row names it and offers to
  declare it in one click, after which the action still needs its own approval.
  If it asks for something outside the families entirely, the refusal says so
  and offers nothing, because nothing the user could allow would make it
  runnable. No new execution power either way.
- **A command recipe editor** on the Workspace tab. A workspace that declares
  no recipes can run nothing, and declaring one meant hand-editing
  `.hermaeus/workspace.json`, which assumed the user knew both that the file
  existed and which command families the safety gate accepts. Pick a family
  from the fixed list, optionally narrow it with an argument, and it is saved
  immediately; Remove takes it back off. The picker can only express families
  the gate already accepts, so a recipe cannot be declared that would be
  refused at run time, and every run still requires approval. Command Recipes
  and Project Instructions both gained explanatory text and tooltips.
- **`GET /v1/capabilities`** on the local API, deferred since r1. Reports the
  routes it exposes, the app version, and per feature (chat, RAG, memory,
  embeddings) whether it is usable right now with a reason when it is not. It
  reports rather than probes: no model load, no server start, no network call.
  It leaks no paths, keys, tokens or dataset names, and is authenticated and
  traced like every other route.

### Changed

- The header's review count reads "N waiting on you" rather than "N queued
  reviews", which is now true rather than merely worded differently.
- "Save Memory" is "Save this run as a workspace note" and sits with the run
  outcome it saves; "Explain Workspace" is "Re-analyse workspace" and sits on
  the Workspace tab with the profile it rebuilds; "Save as Workspace Defaults"
  moved next to the profile it writes.
- `BenchmarkInsightsReport` gained `SuiteLeaderboards` and `CrossSuite` as
  optional trailing parameters; a report constructed without them behaves
  exactly as it did at 0.32.0.

## [0.32.0-alpha] - 2026-07-31

Implements docs/review r25: conversation branching, one context receipt,
in-process Whisper, benchmark comparisons that are actually comparable, and a
guard against documentation drift.

### Added

- **Conversation branching.** A conversation is now a tree rather than a line.
  Regenerating an answer adds a new version alongside the old one, and editing
  one of your own messages sends the edit as a new version while leaving the
  original and its replies intact. Any message with more than one version gets a
  compact `< 2/3 >` switcher; a conversation that has never been branched looks
  exactly as it did before. Deleting a version is explicit, says how many
  messages go, and refuses when only one version remains. Every branch is saved,
  so conversation search and Recall still find a message you navigated away
  from, while the prompt, token accounting, memory extraction and export follow
  the version on screen. See `docs/features.md`.
- **One context receipt per answer.** Memories, Recall hits and knowledge
  excerpts now collapse behind a single line (`Context: 3 memories, 2 recall
  hits...`) that expands to the individual items.
- **Whisper speech recognition, in-process.** The local speech backend is now a
  pinned Whisper base model: transcripts carry punctuation and casing, and the
  language is detected from 98 languages rather than assumed. Settings > Voice
  can force a language instead. Long recordings are transcribed in fixed
  30-second windows with per-window progress, so memory no longer grows with
  recording length. See `docs/voice.md`.
- **Per-case benchmark breakdown.** The Best overall card opens onto its own
  evidence: each case's score with the runner-up's score for the same case
  beside it, and cases the runner-up won highlighted.
- **`docs/review/deferred.md`**, a standing ledger of every item a review round
  postponed rather than rejected, with the reason and current status. Seeded
  from an audit of all twenty-four previous rounds.

### Changed

- **Best overall is now ranked only on cases every ranked model actually ran**,
  keyed on case id and case version, and the card states that basis. When no two
  models share enough cases there is deliberately no winner: the panel says so
  and explains that running the same suite on each model is the fix. A model
  with far less shared coverage is excluded and reported in the caveats instead
  of shrinking the comparison for everyone. The card also names the axis, since
  the ranking blends quality with speed and the overall leader can be second on
  quality. Hermaeus Doctor reads the same report, so the two never disagree.
  See `docs/benchmarks.md`.
- The audio-file transcription limit is stated as a duration (90 minutes)
  instead of a byte count.
- README's feature narrative now covers Projects, Recall and the command
  palette, Activity, Memories, watched RAG sources, Logs, Settings and speech
  input, and a guard test fails the build if a navigation panel is missing from
  README or `docs/features.md`.

### Fixed

- **Regenerate destroyed the previous answer.** It removed both the assistant
  message and the question, put the text back in the input box and re-sent, so
  the earlier answer was gone from the conversation and from disk on the next
  save. It now creates a new version and deletes nothing. It also no longer
  overwrites a half-typed message in the input box.
- **Collapsing the memory pill on a chat message hid nothing.** Memory sources
  collapsed behind a count, but Recall hits from 0.31.0 rendered in a separate,
  always-visible strip directly above that control. Both are now sections of one
  collapsed receipt.
- **"Open in Memories" was offered for items that are not memories**, where it
  would search the Memories panel for a Recall hit or a document excerpt.
- **Transcribing a long audio file could exhaust memory and kill the app.** The
  file picker accepted up to 200 MB, about 1.7 hours, and fed it to a
  full-self-attention model as a single tensor. Fixed-window decoding makes
  length structurally safe.
- **A low-confidence transcript now means something.** The flag previously only
  meant "the text came back empty", so hands-free mode could not refuse to
  auto-send a hallucinated turn; repetition loops are now detected.
- Message timestamps were lost when a conversation was saved.
- Test-suite health: temp-directory cleanup could spend up to 3.4 seconds
  sleeping per test and still fail the test it was cleaning up after; five
  near-identical wait helpers (two of which gave up silently on timeout) are now
  one that always asserts and says what it was waiting for; and one acceptance
  test polled a wall clock for work it never awaited.

### Removed

- The `facebook/wav2vec2-base-960h` speech model is no longer used. Its
  vocabulary contained 26 uppercase letters and an apostrophe, with no lowercase
  and no punctuation at all, so every transcript read `HELLO CAN YOU CHECK THE
  BUILD` and no post-processing could restore what was never produced. **An
  already-downloaded copy is never deleted:** Hermaeus Doctor reports it as
  superseded, with its size and location, and leaves it alone.

## [0.31.0-alpha] - 2026-07-29

Implements docs/review r24: Projects, Recall and the command palette, living
knowledge (watched RAG sources), Activity ("did that actually work"), and
local speech recognition.

### Added

- **Projects.** A named container - folder root, default chat model, default
  RAG dataset, default system prompt, a brand-palette color - that Chat,
  Agent, and RAG all read from. Switching the active project (Ctrl+K or the
  switcher) sets that context everywhere at once; a new conversation
  inherits the project's defaults, the Agent panel pre-fills (never
  auto-selects) its folder root as the workspace, and RAG's default dataset
  follows. Create empty, from an existing conversation, or by adopting the
  currently-selected Agent workspace (including its workspace-memory notes).
  See `docs/projects.md`.
- **Recall.** A single local search index over your own words in
  Hermaeus - past messages, agent tasks, memories, and RAG document chunks -
  fused via reciprocal rank fusion into one ranked result. On by default,
  with a visible switch, a genuine "Clear index" action, per-conversation
  exclusion, and honest size reporting from day one. The command palette
  (**Ctrl+K**) searches Recall alongside registered app commands; an empty
  query shows commands grouped by area, and selecting a hit navigates
  straight to it. Optional chat-side injection (off by default). See
  `docs/recall.md`.
- **Watched RAG sources.** A dataset can watch folders for drift instead of
  being a photograph taken once at ingest. A cancellable scan classifies
  new/changed/missing files without touching anything; "Refresh now"
  applies new/changed files through the normal ingest pipeline after
  confirmation, and never removes missing files without a second, separate
  confirmation. Optional automatic refresh (off by default) can run on app
  start and/or every N hours, and never deletes under any configuration.
  Reuses the exact glob engine (`GlobMatcher`) the Agent's `glob_files`
  already uses, now shared from `Hermaeus.Core`.
- **Activity.** A reverse-chronological local record of managed server
  start/stop/crash, Doctor scan results, and RAG ingest/refresh outcomes,
  each an explicit outcome (Succeeded/Partial/Failed/Cancelled/Running)
  rather than a boolean. Shares the existing trace store; entries are
  redacted before persistence.
- **Local speech recognition, off by default.** In-process ONNX (no managed
  subprocess) using a CTC acoustic model
  (`facebook/wav2vec2-base-960h`, pinned, SHA256-verified, English) - the
  same in-process posture as native Kokoro TTS, chosen over porting
  Whisper's own decoder architecture to keep this round's complexity
  proportionate. A remote OpenAI-compatible backend is available but never
  the default. Windows capture via `winmm` (no NuGet package); Linux capture
  via the same `parecord`/`arecord`/`ffmpeg` fallback chain output already
  uses. "Transcribe audio file..." in Services > Voice exercises the whole
  pipeline without a live microphone. A dictation mic button (chat input
  today; other locations pending) inserts the transcript at the cursor -
  never sends automatically. Doctor and Privacy Audit both gained checks.
  Audio is always transient: transcribed and deleted immediately, never
  persisted. Hands-free conversation mode has a complete, tested state
  machine but is not yet wired to a live capture loop or Chat's UI. See
  `docs/voice.md`.
- **One command registry.** Every panel's actions register through a shared
  `ICommandRegistry`; the palette's empty-query view lists them grouped by
  area.

### Fixed

- **A workspace folder renamed or deleted out from under the app, followed
  by a refresh-files click, crashed the whole process.** Every other command
  in `AgentViewModel` catches its own exceptions; the one bound directly to
  the refresh button didn't, so an unhandled `DirectoryNotFoundException`
  propagated through `AsyncRelayCommand` unobserved. Now caught and reported
  as a status message.
- **The Agent panel's reply/continue box sat below two JSON diagnostic
  panels and a reservations list**, often requiring a scroll past ~300-400px
  of secondary detail just to reply to the agent. Reordered to sit directly
  below the response.
- **A code block saved to a chat artifact before a brand-new conversation's
  first persist could silently disappear from the artifacts panel** on the
  next load: it landed in a separate "unsaved" bucket folder that the
  conversation's real (later-assigned) id could never resolve back to. Now
  assigns the real id immediately, matching how RAG dataset attachment
  already handles the identical early-attachment problem.
- **A markdown heading that was only an inline-code-wrapped filename** (e.g.
  `` # `calculator.cs` ``) produced a saved artifact literally named
  `` `calculator.cs`.cs `` - the trailing-extension strip didn't fire
  because a backtick sat after the extension. Backticks are now stripped
  first.
- **`SecretStoreLogsWarningWhenStoredSecretCannotBeDecrypted` flaked
  intermittently.** Root cause: AES-CBC has no built-in integrity check, so
  decrypting under a replaced/corrupt key does not reliably throw - PKCS7
  padding validation alone has roughly a 1-in-256 chance of coincidentally
  passing on random garbage, and the lenient `Encoding.UTF8.GetString` used
  on the primary decode path let that garbage through as a "successfully
  decrypted" (but wrong) string instead of falling through to the failure
  path. Now uses strict UTF8 decoding on that path too, matching the
  fallback path's existing discipline.

## [0.30.0-alpha] - 2026-07-25

Implements docs/review r23 in full: "Trust You Can Operate", a round about
making the agent workbench's safety guarantees legible and reversible
rather than adding new capability.

### Added

- **Run Ledger and Task Rewind.** A "Changes" panel on the agent task view
  now shows every file touched (created/edited/reverted), every command
  run, and every approval decision for the current run, derived entirely
  from the existing persisted task state (no new storage). A "Rewind"
  action reverts a task's file changes back to their pre-run state via the
  existing draft-patch revert path, folding in any completed sub-tasks;
  it refuses while the task is still running or has a pending approval.
- **Approval fingerprint binding.** Every approve/reject decision is now
  bound to a SHA256 fingerprint of the exact tool name and canonicalized
  arguments that were rendered to the user. If the pending action changed
  underneath the approval (a TOCTOU window), the approval is refused
  instead of silently applying to different arguments than what was shown.
- **Workspace policy.** `.hermaeus/workspace.json` gains an optional
  `policy` block: glob-based read/write allow-lists plus a `never`
  deny-list that only ever narrows what the agent can touch, and an
  optional per-task read cap. Enforced by the same glob matcher `glob_files`
  already uses. A policy summary is visible in the workspace disclosure UI.
- **Plan-approval checkpoint (opt-in).** A new `RequirePlanApproval` agent
  setting pauses a task the first time it sets a plan, so the user can
  review the plan before any tool runs. Plan revisions after that point are
  logged and annotated with the step they happened at, and shown in the
  plan panel.
- **Completed-with-reservations.** The agent can now report a task as
  finished while flagging specific caveats (`reservations` in its final
  answer) instead of only ever claiming unqualified success; reservations
  from sub-tasks roll up into the parent's synthesis report and are shown
  in a dedicated panel.
- **Stated-lesson gate-claim filter.** A lesson the agent tries to record
  that claims a specific approval, policy, or safety-gate outcome is now
  rejected before storage (logged as `lesson_rejected`), closing a path
  where a poisoned or hallucinated "lesson" could bias future runs toward
  skipping a real safety check.
- **Three new agent scenarios** (14 confused-user-authority, 15
  tool-result-poisoning, 17 memory-poisoning), and a
  `forbid_active_lesson_matching` scenario check that fails if a run's
  active lessons match an injected instruction the scenario expects the
  agent to have ignored. The scenario library now ships 16 scenarios.
- Context receipt now has its own collapsed-by-default panel, split out
  from "Retrieved Context" for clarity.
- **Update check.** Doctor now compares the running app version against the
  newest published GitHub release on every scan, including the automatic
  startup scan, and flags a warning when a newer one exists. It never
  downloads or installs anything; the check's fix action just opens the
  releases page in your browser so you can update yourself. This needs the
  `MortisDei/hermaeus` repository to be public to succeed; while it is
  private the check fails closed the same way it would for any other
  network outage.

### Changed

- `docs/security-review.md` is split into three focused docs:
  `docs/security-review.md` (current posture only), `docs/security-history.md`
  (past rounds' findings, newest first), and `docs/security-roadmap.md`
  (known, accepted gaps with triggers for revisiting them).
- Code changes now land via pull request (`docs/pull-requests.md`): one
  open PR per maintainer at a time, merged by the owner. This round is the
  first to go through that flow end to end.
- **The in-app Moss accent icon** (`Controls/MossIcon.axaml`, shown in empty
  states, tooltips, and the setup wizard greeting) now matches the current
  woodland-goblin mascot direction (`docs/mascot.md`) instead of the retired
  cute-era silhouette: a pointed hood, ears, a small brass spectacles band,
  and a warm lantern-glow accent in place of the old mushroom tuft. The
  app/taskbar/tray icon (Archivist's Seal) is unaffected.
- **Settings > Voice channel routing no longer uses named voice profiles.**
  The "Add profile" step (create a named voice/speed combination, then pick
  it per channel) was confusing and is gone; each channel now picks a voice
  directly from the active provider's own voice list (or free-types one for
  a provider that cannot enumerate voices), and remembers it. A channel
  voice chosen before this change (via a legacy named profile) still
  resolves correctly on load rather than silently resetting to the default.

### Fixed

- **Services page kept showing the pre-update executable path after an
  llama.cpp update, until the app was restarted**, whenever every managed
  server was already stopped at update time. The update flow rewrites every
  managed server's path unconditionally, but the Services page only
  re-synced servers it had just stopped and restarted; a server that was
  never running (and so never in that list) kept its stale, construction-time
  path on screen. Every row now re-syncs from its config immediately after a
  successful update, regardless of whether it was running.
- **The "GPU present but 0 layers" Doctor advisory fired for a stopped
  server**, based only on static configuration. It now requires the chat
  server to actually be responding (a model genuinely loaded and running at
  CPU speed) before advising, since a stopped server cannot be wasting the
  GPU.
- **Hotkey support reported a neutral grey status on Windows** even though
  system-wide hotkeys are fully supported there. Now reports Ready (green)
  on Windows; unsupported platforms still report a warning.
- **The embedding backend health check reported a vague "not reachable"
  warning whenever no embedding server was running at all**, indistinguishable
  from an actually broken running server. It now probes the resolved
  endpoint first and reports a grey, Info-severity "No embedding server
  started" when nothing is listening; the warning is reserved for a server
  that is up but genuinely failing to embed.
- **Doctor's two GitHub update checks (llama.cpp, Hermaeus) hit GitHub's
  anonymous API on every scan, including the automatic startup scan.**
  GitHub allows only 60 requests/hour per unauthenticated IP; a handful of
  app restarts or manual rescans could exhaust that quota on background
  checks alone, then make a genuine llama.cpp update attempt fail with a
  403 rate-limit error, as reported live. Both checks now cache their
  result for an hour instead of re-fetching every scan. The 403 case
  itself, when it does happen, now surfaces as a clear "GitHub's rate
  limit was reached, wait about an hour" message instead of a raw HTTP
  exception string.
- A CI flake in `ServicesViewModelModelPathBindingTests` (asserted on a
  `SettingsChanged`-triggered rebuild immediately instead of polling for
  it, like an equivalent test elsewhere already does) and widened
  `TempDir`'s Windows file-lock-on-cleanup retry budget after two other
  CI runs failed on an unrelated, already-mitigated instance of the same
  class of flake in Agent orchestration tests.

## [0.29.1-alpha] - 2026-07-24

Hotfix: a Services-page settings-loss bug reported live during dogfooding, plus the first Linux CI results from this repo's newly re-enabled Actions.

### Fixed

- **Services page silently dropped edits after any unrelated settings save.**
  `ServicesViewModel`'s per-server rows held a `readonly` reference into
  `ManagedServers[]` captured once at construction. The Settings tab's save
  path swaps `ISettingsService.Settings` to a new object on every save
  (by design, for atomic rollback-on-failure); any *other* open Services row
  kept pointing at the now-orphaned pre-swap config, so every Start/Save on
  that row afterward wrote into a dangling object instead of the live
  settings tree. No error was raised - the edit just never reached
  `settings.json`. This is why Doctor's "GPU present but the chat server is
  set to 0 GPU layers" advisory could report `0` layers even while a model
  was genuinely running with real GPU offload: the last real edit (from
  Auto Tune or a manual GPU-layers change) had been silently lost. Fixed by
  re-pointing each row's config reference at its fresh same-id instance
  whenever the settings object changes.
- **3 Linux-only CI test failures, root-caused and fixed** (first real Linux
  CI run on this repo - Actions was disabled here until this release).
  `LlamaRuntimeVariantTests`' path-walking tests used hardcoded Windows
  backslash literals as production-code input; backslash is not a path
  separator on Linux, so `ResolveInstallRoot`/`SelectPrunableVersionDirectories`/
  `NearestTagDirectoryName` silently failed to parse the literal on that leg.
  Normalized the test inputs to forward slashes (valid on both platforms).
  `ServerProcessManagerTests.AutoTuneAsync_fails_fast_with_the_named_port_owner_when_the_port_is_occupied`
  was missing the `OperatingSystem.IsWindows()` guard its sibling tests all
  have; its fixture is a real `where.exe` copy, Windows-only by construction.
  `RagHarnessTests`' reindex-cancel test cancelled from a `PropertyChanged`
  handler reacting to `Progress<T>`-marshaled VM state, which posts through
  `SynchronizationContext` instead of running inline - a real race against
  the reindex loop's own progress, with no cross-platform ordering guarantee.
  Replaced with a deterministic cancel triggered synchronously from inside
  the embedding call itself.

## [0.29.0-alpha] - 2026-07-24

Fit for public view: makes the app presentable (Moss presence, readability)
and makes shipping it a repeatable one-command act (tag-driven GitHub
Releases). First version released as a tagged GitHub Release.

### Fixed

- **Dark-mode readability bug, root-caused.** The reported "dark grey and
  hard to read" text near Moss was not the tooltip surface (FluentTheme's
  stock tooltip resources render correctly in dark mode); it was ad-hoc
  `Opacity` values on TextBlocks with no contrast floor. Confirmed by
  screenshot sampling: a chat empty-state label at Opacity 0.4 rendered at
  roughly 3.7:1 contrast against a black background, below the WCAG AA
  minimum. Every TextBlock below 0.4 opacity across the app is now raised to
  one of two new first-party classes.

### Added

- **`hint`/`faint` text-dim classes** (`Styles/AppStyles.axaml`): `hint`
  (0.65 opacity) for secondary text, `faint` (0.45) as the floor no
  user-relevant text may render dimmer than. Applied to every TextBlock this
  round touched and every instance found below 0.4 opacity anywhere in
  `Hermaeus.Desktop`.
- **Branded, theme-aware `ToolTip` style**: explicit `ThemeDictionaries`
  background/foreground/border brushes (Deep Moss surface + Parchment text
  in dark mode, Parchment surface + Ink text in light mode) so tooltip
  readability no longer depends on FluentTheme defaults interacting with the
  app's accent-color overrides. Checked by eye in both themes.
- **`Controls/MossEmptyState.axaml`**: one shared empty-state control (icon,
  title, hint) replacing five copy-pasted bare-text blocks. Moss now appears
  in Chat's two empty states, Agent's "no tasks yet", Benchmark's "no runs
  yet", Memories' "no memories yet", and RAG's "no datasets yet", plus a
  small greeting icon on the first-run setup wizard header. No animation, no
  popups, no presence in the chat transcript.
- **Tooltip coverage sweep**, concentrated on the Agent workbench's approval
  gates: the patch-review Approve/Reject/Block buttons and the review-queue
  Approve/Reject buttons had no tooltips before this round despite being the
  app's highest-consequence controls; each now states what approving or
  rejecting actually does. Smaller additions to Settings > Trust, Settings >
  Voice, and per-item conversation actions.
- **Icon-only tooltip guard test** (`ServiceTests.IconOnlyControlsHaveTooltips`):
  scans every `.axaml` under `Hermaeus.Desktop` for a `Button`/`ToggleButton`/
  `RepeatButton` with an icon but no text and no `ToolTip.Tip`, alongside the
  existing em-dash scan. Passes with an empty allowlist; the sweep found full
  coverage already.
- **Tag-driven GitHub Releases** (`.github/workflows/release.yml`): pushing
  an annotated `v<version>` tag verifies the tag matches
  `Directory.Build.props`, builds win-x64 and linux-x64 packages with the
  existing `build.ps1`/`build.sh` scripts, and publishes a GitHub Release
  with changelog-derived notes plus an unsigned-binary/SHA256-verification
  footer. `scripts/release-notes.sh`/`.ps1` extract a version's changelog
  section; `scripts/release-notes-footer.sh` generates the footer. See
  `docs/packaging.md` "Releases" for the versioning and tagging policy.

## [0.28.0-alpha] - 2026-07-24

Dogfooding round: layout and settings-organisation feedback from actually
using the app, plus a real concurrency bug the feedback session's failing
test led back to.

### Changed

- **Benchmarks view restructured from three columns to two.** The old layout
  (Suites+history in a narrow left column, Run Setup in a fixed-width middle
  column, a wide right-hand "selected run" panel) looked unbalanced on a
  maximized window and put a run's case-by-case results far from the list you
  picked it from. The left rail now stacks Suites, Run Setup, and Run History
  together; the right side is one panel with tabs (Rankings, All Results,
  Insights, and a new Run Detail tab holding what used to be the separate
  right column). Selecting a run, or a run/rerun completing, now switches to
  Run Detail automatically. Tab content is width-capped and centered instead
  of stretching edge-to-edge on wide monitors.
- **Settings > RAG no longer duplicates the Embeddings server card.** "Embed
  URL" and "Embed model" were always overwritten by the Embeddings server's
  Port/Model on save, making them dead controls; that card
  (Services > Embeddings) is now the only place to set them; it gained a
  read-only "Embed URL: ..." label reflecting the port, and pushes the
  selected model's name into `Rag.EmbeddingModel` for dataset/reindex
  tracking. Settings > RAG keeps only Reranker and the chat knowledge context
  budget, since those have no Services equivalent.
- **Voice moved the same way**: provider selection, base URL, Start/Stop, and
  the voice/device/speed/path fields (anything that manages a Kokoro/XTTS/F5
  background process) are now a "Voice" card on Services, alongside
  Chat/Embeddings. Settings > Voice keeps only orchestration (mute-all,
  auto-speak, per-channel routing, named voice profiles), which has nothing
  to start or stop. `TtsSettingsViewModel` is now a single DI-shared instance
  so both pages reflect the same live state.

### Fixed

- **`ServicesViewModel.RefreshOrphanStatusAsync` had a real, reproducible
  race**: it posted its result back to the UI thread fire-and-forget
  (`RunOnUi`) as the last line of an `async Task` method, so the method could
  return - and callers could read `HasOrphan`/`CanStopOrphan` - before that
  posted update had actually run. Surfaced as an intermittently failing test
  (`RefreshOrphanStatusAsync_shows_information_only_for_an_unrelated_process`,
  4-5 of 8 runs); fixed by awaiting the UI update (`RunOnUiAsync`) instead of
  firing and forgetting it.

## [0.27.1-alpha] - 2026-07-24

Closes out the two risks/follow-ups left open at the end of r21.

### Fixed

- **Settings > Interface > Theme (System/Dark/Light) was a dead setting**,
  same class of bug as the font-size control fixed in 0.27.0-alpha: it saved
  to `UiSettings.Theme` but nothing ever pushed it into Avalonia's
  `RequestedThemeVariant`, so the app always ran the OS-default theme
  regardless of the picker. A new `AppThemeService` applies the choice at
  startup (mirroring `AppFontService`) and live as the user changes the
  dropdown.

### Tests

- Filled the two gaps in r21 doc 02's best-effort matrix that previously had
  no dedicated coverage: a store/DB failure (simulated by corrupting the
  SQLite file after ingest) and chat-level cancellation during retrieval,
  both now asserted directly in `ChatRagInjectionTests` rather than relying
  on the RagQueryService-layer cancellation test alone.
- Fixed two tests (`SettingsChildViewModelsApplyToSettings`,
  `WizardDataRootStepOnFirstRunCompletesWithoutMigrationNoise`) that never
  established an isolated "previous" data root before triggering a save/
  migration. Without one, the migration step resolved "previous" to the
  real `%LocalAppData%\Hermaeus` and tried to move files out from under
  whatever Hermaeus instance is actually running on the machine, failing
  with a file-lock error on a dev box with the real app open. Both now set
  an explicit temp data root first, matching the pattern already used in
  `BackupMigrationTests`.

2 new tests.

## [0.27.0-alpha] - 2026-07-23

Implements docs/review r21 in full: RAG meets chat. Retrieval Q&A used to
exist only in the RAG panel's one-shot query box; a conversation can now
have a Knowledge dataset attached, with per-turn retrieval injection,
citation pills, trace visibility, and honest degradation baked in from the
start. Also removes the three embedded brand fonts shipped in 0.26.0-alpha
after they proved hard to read in daily chat use.

### Added

- **Knowledge: a RAG dataset can be attached to a conversation.** The chat
  header's "Knowledge" picker (owner-facing name; code/settings stay `Rag*`)
  lists datasets with chunk counts; selecting one attaches it, "None"
  detaches. Every send against an attached conversation retrieves from the
  dataset (top 5, parent/child per the dataset's own config) and, when
  retrieval clears the confidence threshold, injects a bounded "Knowledge
  Context" block into the system prompt. Retrieved chunks render as
  individually clickable citation pills on the reply - the same pills the
  RAG panel's query pane already used, now finally reachable from daily
  chat. A weak or unrelated match ("thanks!") injects nothing, so attaching
  a dataset never degrades unrelated turns.
- **Chat Trace Viewer and Context Inspector show Knowledge truth.** The
  previously-dead `RagContextItems` trace field is now populated, alongside
  a new `RagMs` timing stage and a `RagNote` (weak-retrieval skip, embedding
  fallback, or missing-dataset reason) shown whenever nothing was injected.
  The Context Inspector's "exact context pack before send" now genuinely
  includes the Knowledge block (or an honest skip/failure reason) instead of
  silently omitting it.
- **Embedding-server-down fallback removes a raw-exception path.**
  `RagQueryService.RetrieveAsync` now degrades to BM25-only keyword search
  (with a planner note) when the embedding call throws, instead of failing
  the whole query - both from the RAG panel and from a chat send. The
  fallback is never cached; the next query probes the embedding server
  again.
- **Open in chat** on a Dataset Manager card starts a new chat conversation
  with that dataset pre-attached, reusing the same "new conversation" path
  as Ctrl+N.
- Settings > RAG gains "Chat knowledge context budget (tokens)"
  (`RagSettings.ChatInjectionTokenBudget`, default 2000), separate from the
  RAG panel's own query budget.
- Privacy Audit's "Remote providers" entry now names chat knowledge
  injection explicitly when a remote chat provider is selected and the RAG
  subsystem is available, matching the existing image-attachment disclosure.

### Changed

- **The three embedded brand fonts (Cinzel, Source Sans 3, JetBrains Mono)
  are removed.** The UI now defaults to the OS-native font for
  headings/body text and code, matching how the app looked before the
  0.26.0-alpha branding round. Settings > Interface > Typography lets a user
  override the heading, body, and code font families independently with any
  font installed on their system; `AppFontService` applies the choice live.
  The Settings > Interface "Font size" control, previously saved but never
  actually applied to anything, now controls chat message text size (the
  composer, the user bubble, and the assistant reply).

### Fixed

- **A conversation's `RagDatasetId` no longer resolving (dataset deleted, or
  a temporarily unmounted data root) degrades honestly**: the picker shows
  "Knowledge: missing" and the send proceeds with nothing injected, rather
  than silently forgetting the attachment or erroring the send.

12 new tests. `ConversationStore` schema version 1 to 2 (additive
`rag_dataset_id` column). `docs/security-review.md` gained an r21
subsection. `docs/review/` archived to `docs/review/archived/r21/`.
