# Changelog

All notable changes to Hermaeus will be documented in this file. Append newapp versions above the previous version (not the bottom of the doc or where ever feels right at the time).

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

From 0.29.0-alpha onward, every minor version is tagged and released on
GitHub (see `docs/packaging.md` "Releases"); patch versions are tagged only
for urgent hotfixes.

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

