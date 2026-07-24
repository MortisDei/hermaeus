# Changelog

All notable changes to Hermaeus will be documented in this file. Append newapp versions above the previous version (not the bottom of the doc or where ever feels right at the time).

The project follows semantic versioning once public release candidates begin.
Pre-1.0 versions may still change internal APIs and storage details.

FIFO for changelog entries, 10 versions in this file max. Remove older entries
and append them to `docs/changelog-archive.md` to maintain the 10 version
limit.

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


