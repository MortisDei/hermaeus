# 06. Roadmap and sequencing

## Version

Ships as **0.31.0-alpha** (`Directory.Build.props` only: VersionPrefix,
AssemblyVersion, FileVersion; currently 0.30.0). Minor bump: four new
user-visible capabilities. After merge the owner tags `v0.31.0-alpha`,
which becomes the release through the tag-driven workflow. Agents never
push a tag.

## Sequencing (strict)

1. **01 Projects, entirely** (1.1 through 1.6). The data model goes first
   because recall rows, activity rows and dataset bindings all carry a
   project id. Retrofitting scope into an index after it exists is exactly
   the mistake r23's roadmap called out for workspace policy and rewind.
   Land the four migrations with their seeded-previous-version tests before
   anything reads a project.
2. **04 4.1, the command registry, out of doc order.** Doc 02 2.5 consumes
   it and must not grow a temporary list of its own that then has to be
   deleted. Registry first, palette second.
3. **02 Recall**, starting with **2.0**, the switch, the clear action, the
   per-conversation exclusion and the size reporting. 2.0 is first inside
   the doc, not last, because it is the item that keeps a store holding
   copies of the user's own words from being invisible and permanent, and
   an item like that retrofitted after the index ships is an item that
   quietly does not ship. Then 2.1 index, 2.2 messages, 2.3 tasks, 2.4
   fusion, 2.5 palette, 2.6 chat recall. This is the round's centerpiece;
   build it while the budget is freshest. 2.6 is last within the doc because
   it touches the send path and should be built against a Recall that is
   already known-good.
4. **04, the rest** (4.2 Activity, 4.3 outcomes, 4.4 per-panel discovery,
   4.5 settings-field search).
5. **03 Living knowledge.** After Activity exists, so a watched refresh
   records its outcome through the real recorder rather than a placeholder
   that gets rewritten a day later.
6. **05 Voice input** (5.1 through 5.7). Last: the largest new surface, the
   only one needing hardware the owner may not have, and the only one whose
   loss would not gut the round.
7. Housekeeping (below), then all docs, then close-out: archive this pack to
   `docs/review/archived/r24/`, final build and test pass, PR per
   `docs/pull-requests.md`. No AI co-author trailer on commits.

## Test estimate

Roughly **78 to 98 new tests** on top of the 1185 green at `c398e43`, so
expect to land somewhere around 1265 to 1285.

| Doc | Tests |
| --- | --- |
| 01 Projects | 14 to 18 |
| 02 Recall and palette | 26 to 32 |
| 03 Living knowledge | 10 to 13 |
| 04 Activity and legibility | 14 to 17 |
| 05 Voice input | 14 to 18 |

All new harness-style methods register in `XunitHarnessTests.HarnessCases`
or the reflection guard fails the suite. Tests stay sequential; do not
re-enable parallelization for the new stores.

Nothing in this round needs a live server, a network call, or a
microphone. If a test appears to need one, the abstraction is wrong: the
per-platform asset selection in `LlamaServerSetupService` is the model to
copy, unit-testable for every platform without downloading anything
(`LlamaServerSetupService.cs:13`).

Watch suite runtime. The local suite is 1m25s and CI is roughly 7m40s per
leg. Recall and Activity both invite tests that loop over batches; use fake
embedding services and seeded rows, never a real embedding call in a test.

## Descope order (if the round overruns)

Cut from the bottom, never from the middle:

1. **5.5 hands-free mode.** The most descopable thing in the round.
2. **5.4 dictation everywhere**, narrowed to the chat input only.
3. **The rest of 05 down to 5.1 to 5.3.** A managed STT backend plus
   "Transcribe audio file..." in Services is a coherent, honest, shippable
   thing on its own. Do not ship the backend with no surface at all.
4. **3.4 automatic refresh**, keeping manual "Refresh now".
5. **4.5 settings-field search.**

Do not partially ship 01 or 02. Projects half-bound across two of four
subsystems is worse than no Projects, and 02 is the round.

**2.0 is not descopable at any budget.** Shipping a store that holds copies
of the user's own messages with no switch, no clear action and no
exclusion is not a smaller version of Recall; it is a different and worse
product. If 2.0 cannot be afforded, Recall does not ship this round.

## Practical warnings for the implementer

- **Re-verify every file:line before editing.** This pack was exact against
  `c398e43`. `ChatViewModel.cs` (1930 lines), `AgentViewModel.cs` (1991),
  `AgentService.cs` (1731) and `ServicesViewModel.cs` (1496) all move
  often. They are named hot spots in CLAUDE.md: make minimal, focused
  changes and put new subsystem knowledge with the subsystem.
- **The wizard-singleton lesson applies to the project switcher.** DI
  singleton ViewModels load their state once. A switcher that saves before
  its list has loaded can write an empty active project over a real one.
  This exact bug has shipped in this repo before.
- **`dotnet run` shares the owner's real settings.json.** Look, do not
  resave settings casually, and never force-kill the process. Close it
  cleanly. This round adds a lot of new settings fields, so the temptation
  to poke at the running app is higher than usual.
- **Verify UI work in both themes by actually running the app.** The
  palette, the Activity feed and the mic button are three new surfaces with
  new colours (project colour dots, outcome states, a recording indicator).
  Project colours come from the brand palette, not free hex, precisely so
  both themes stay readable.
- **The tooltip guard test scans axaml.** Every icon-only control added by
  this round needs one: mic buttons, the palette trigger, per-panel
  discovery buttons, activity row actions, refresh buttons.
- **Every schema change is additive and tested against a seeded previous
  version**, not just tested for "the migration ran". Four stores change in
  doc 01 alone.
- **Nothing goes on the chat send path.** Recall indexing is a bounded
  background pass copied from `MemoryStore.cs:515-545`. Activity recording
  is fire-and-forget. If a send gets measurably slower, the round has a
  defect: the existing pre-stream timing breakdown
  (`docs/features.md:21-37`) will show it, so check it before the PR.
- **Redaction before persistence** for every new activity row, and the
  audio temp file must be deleted on the success, failure and cancellation
  paths alike.
- **Doc 03 touches the reindex/cancel area**, where r23 found a real race.
  Test scan cancellation explicitly rather than assuming the pipeline
  handles it.
- **One glob engine.** Watched-source globs reuse `glob_files` semantics.
  A divergence between what the agent's globs match and what a watched
  source matches is the same class of bug r23 rejected for workspace
  policy.
- **When moving or rewriting doc sections, verify with a full-file read.**
  A truncated check has already let a dropped heading reach a live release
  once in this repo.

## Housekeeping

Small, real, and previously recorded. Do these before close-out, not
instead of the round.

- **`MossIcon` is out of sync with the mascot spec.** `docs/mascot.md` was
  redesigned (the woodland-goblin direction) and
  `src/Hermaeus.Desktop/Controls/MossIcon.axaml` still renders the earlier
  design. Moss now appears in every empty state
  (`docs/features.md:840-849`), so the drift is visible everywhere. Bring
  the control to the current spec, or, if that is a real illustration job
  rather than a vector tweak, say so explicitly in the PR and leave it,
  rather than shipping a half-redraw.
- **Reconcile `docs/features.md:722-746`.** "Workbench Glue" describes
  Projects as though it exists and lists "Planned profile fields"
  (:739-740). Once doc 01 lands, rewrite that section to describe what is
  actually true and delete what Projects supersedes. Do not leave both the
  aspirational text and the real feature in the same document.
- **Root-cause the flaky secret-store test.**
  `ServiceTests.SecretStoreLogsWarningWhenStoredSecretCannotBeDecrypted`
  (`ServiceTests.cs:1883`, registered at `XunitHarnessTests.cs:121`) flakes
  intermittently. The house rule is that a red test is never "pre-existing
  and unrelated": find the actual cause and fix it. Do not add a retry.
- **New docs**: `docs/projects.md` and `docs/recall.md`, linked from the
  README's documentation list. Update `docs/rag.md` for watched sources and
  `docs/voice.md` for the input half (that file is currently titled and
  written as output-only). `CHANGELOG.md` per the existing FIFO with
  `docs/changelog-archive.md`.

## Explicit rejections (do not do these)

Considered and declined, with reasons. Engage with these rather than
re-proposing them.

- **`FileSystemWatcher` for watched sources.** Platform-divergent, lossy
  under load, multi-fires on a single save, and holds handles on user
  directories for the process lifetime. A deterministic scan gives the same
  outcome. Revisit only with evidence that polling is too slow in practice.
- **In-process Whisper via ONNX Runtime.** Attractive because
  `NativeKokoroVoiceProvider` proves the pattern, but an autoregressive
  encoder-decoder with KV cache management is a poor thing to write from
  scratch inside a four-feature round. Managed `whisper-server` first;
  in-process is a legitimate later round.
- **Wake word or always-on listening.** Every capture starts from an
  explicit action and shows an indicator for its whole duration. A hot mic
  with no visible state is not compatible with this app's privacy claims.
- **Per-project data roots, secrets, or sandboxes.** A project is a view
  over one data root: a label plus defaults. Anything else multiplies
  backup, migration and the data-root manifest by the number of projects.
- **Automatic project assignment.** No guessing which project an existing
  conversation or task "really" belongs to. Bindings are set explicitly or
  inherited at creation. Silently reclassifying a user's own records is
  unrecoverable trust damage for a convenience nobody asked for.
- **A model-written summary in the Activity feed.** Activity rows are facts
  the app observed. A narrated "here is what you did this week" is a
  plausible-sounding artifact with no verification path, in the one surface
  whose entire purpose is answering "did that actually work".
- **LLM reranking of recall results.** Non-deterministic, slow, and
  unnecessary: the deterministic ONNX cross-encoder reranker already exists
  in `Hermaeus.Rag` if reranking proves needed.
- **A new nav panel per feature.** Only Activity earns a panel. Projects
  gets a header switcher and a detail view; Recall gets the palette. Adding
  four panels to an app whose stated problem is "too many panels to keep
  track of" would be a direct own goal.
- **Conversation branching and message-edit forks.** A genuinely good
  feature and a real gap, but it is a schema change to the message tree
  plus a rendering change, and this round has no room. Recorded here as the
  leading r25 candidate rather than smuggled in.
- **Speaker diarization, or persisting recordings for later re-transcription.**
  The transcript is the artifact. The audio is not.
- **New NuGet packages.** Standing rule. Microphone capture on Windows is
  `winmm` P/Invoke; rank fusion reuses the RRF already in
  `HybridRetriever`; SHA256 and globbing are covered in-tree.
