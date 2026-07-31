# 06. Roadmap and sequencing

## Version

Ships as **0.34.0-alpha** (`Directory.Build.props` only: VersionPrefix,
AssemblyVersion, FileVersion; currently 0.33.0). Minor bump: speculative
decoding, the speed check, the held message and per-model folders are all
user-visible. After merge the owner tags `v0.34.0-alpha`, which becomes the
release through the tag-driven workflow. Agents never push a tag.

## Progress table

**Update this table in the same commit as the work it describes.** This round
was planned during a period of frequent service interruptions. A fresh agent
with no memory of any conversation should be able to read this table plus
`git log --oneline main..HEAD` and know exactly where to resume.

| # | Item | Landed |
| --- | --- | --- |
| 1 | 2.1 Oversized dataset returns results | yes |
| 2 | 1.1 Post-setup chain stops serialising | yes |
| 3 | 1.2 Servers auto-start concurrently | yes |
| 4 | 1.6 Delete dead `IsLoading` | yes |
| 5 | 1.3 Chat warming state | yes |
| 6 | 1.4 Held message | yes |
| 7 | 1.5 Startup breakdown visible (with 5.3) | yes |
| 8 | 2.2 FTS5 candidate generation | yes |
| 9 | 2.3 Cache holds embeddings only | yes |
| 10 | 2.4 Bounded top-K scan | yes |
| 11 | 2.5 Content loaded for candidates only | yes |
| 12 | 2.6 Concurrent send-path injections | yes |
| 13 | 2.7 Index size visible in the RAG panel | yes |
| 14 | 3.1 Composable speculative-decoding section | yes |
| 15 | 3.2 Verified launch flags | yes |
| 16 | 3.3 Draft model validated before launch | yes |
| 17 | 3.4 Combined VRAM estimate | yes |
| 18 | 3.5 Speed check | yes |
| 19 | 3.6 Comparison | yes |
| 20 | 3.7 Doctor check | yes |
| 21 | 4.1 A model is a file set | yes |
| 22 | 4.2 Per-model destination folders | yes |
| 23 | 4.3 Organize stops flattening | yes |
| 24 | 4.4 Flat-folder migration | yes |
| 25 | 5.1 Conversation summary projection | yes |
| 26 | 5.2 README version and guard | yes |
| 27 | 5.4 Docs, changelog, deferred ledger | yes |
| 28 | Close-out: archive pack, PR | yes |

## Sequencing (strict)

1. **2.1 first, before anything else in the round.** Write the failing test
   first: build a dataset whose estimated cache size exceeds
   `MaxCacheBytes`, query it for a phrase that is certainly in it, assert a
   result comes back, and watch it fail. It is the round's one active
   data-correctness path and it must land before any of the scan rework
   touches the same file, so that the rest of doc 02 is optimising correct
   behaviour rather than rearranging a bug.

2. **Doc 01, in the order 1.1, 1.2, 1.6, 1.3, 1.4, 1.5.** The ordering
   changes (1.1, 1.2) are the whole daily benefit and are lower risk than
   they look, because the model-refresh-on-server-ready path already exists
   and already works (`ServicesViewModel.cs:1474-1486` into
   `MainWindowViewModel.cs:189`). Confirm that before writing anything: if
   `ServerAvailabilityChanged` does not fire on a `Status` transition to
   `Running`, then 1.1 has a dependency this pack says it does not, and the
   event is the first thing to fix. 1.6 is a deletion and lands early to
   keep it out of later diffs. 1.3 before 1.4 because the held message is
   defined in terms of the warming state.

3. **The rest of doc 02, in order.** 2.2 before 2.3, because removing
   content from the cache is only safe once BM25 no longer reads it. 2.5
   before or with 2.3 in the same breath: the moment the cache stops
   carrying content, every downstream consumer needs its new source. Run the
   RAG eval harness before 2.2 and after 2.5 and put both numbers in the PR.

4. **Doc 03, in order.** 3.1 is a settings-shape change and everything else
   depends on it. 3.2 is pure argument building. 3.3 before the owner is
   invited to point at a real draft model. Then 3.4, 3.5, 3.6, 3.7.

5. **Doc 04.** Last of the code, because it is the first thing to cut and
   because it moves the owner's real model files. Doc 03 does not depend on
   it: the MTP heads are already on disk in the hub cache and doc 03 is
   verifiable there today.

6. **Doc 05.** 5.1 can land any time and is independent of everything. 5.2
   and 5.4 are close-out. 5.3 lands with 1.5.

7. Housekeeping (below), then close-out: archive this pack to
   `docs/review/archived/r27/`, final build and test pass, PR per
   `docs/pull-requests.md`. No AI co-author trailer on commits.

## Test estimate

Roughly **95 to 115 new tests**. `XunitHarnessTests.HarnessCases` holds 394
registered harness cases at `ee2c592`; the full suite was around 1,470 after
r26. Record the actual before and after numbers in the PR rather than
repeating this estimate.

| Doc | Tests |
| --- | --- |
| 01 Startup that never waits | 18 to 22 |
| 02 Retrieval that scales | 24 to 30 |
| 03 Drafting and proof | 24 to 30 |
| 04 Models arrive complete | 22 to 26 |
| 05 Small open items | 8 to 12 |

All new harness-style methods register in `XunitHarnessTests.HarnessCases`
or the reflection guard fails the suite. Tests stay sequential.

**Nothing in this round needs a live server, a network call, a GPU or a
model file.** Doc 03 is argument building over `ServerConfig`. Doc 04's
`Plan` is pure. Doc 02's scan and doc 01's ordering are both testable with
fakes. If a test appears to need a real llama-server, the seam is wrong.

The two things tests cannot establish are whether drafting is faster on this
hardware (3.5 exists so the owner can find out) and whether doc 02 changed
retrieval quality (the eval harness answers that). Both are recorded in the
PR body as numbers, not asserted.

## Descope order (if the round overruns)

Cut from the bottom of the sequence, never from the middle:

1. **Doc 04 entirely.** The owner's MTP heads are already on disk, so doc 03
   ships and is verifiable without it. Companion downloading is what makes
   drafting work for the *next* model, which is a real cost but a deferrable
   one. If only part of doc 04 can land, land 4.2 (per-model destination for
   new downloads) and leave 4.3 and 4.4 alone: changing where new files go
   is safe, migrating existing files is the risky half.
2. **3.5 and 3.6, the speed check and comparison**, keeping 3.1 to 3.4. This
   is a real loss and should be resisted, because it turns drafting into a
   knob taken on faith on hardware where the answer genuinely varies. Cut it
   before cutting drafting itself only because a knob with no measurement is
   still better than no knob.
3. **2.2 through 2.7**, keeping 2.1. The bug fix is the part that matters;
   the scan rework raises a ceiling that 2.1 already stopped being silent
   about.
4. **1.4, the held message**, keeping 1.1 to 1.3. With the ordering fixed
   and a warming state on screen, the wait is short and explained. The hold
   is the delight, not the fix.

**Do not descope 2.1.** A dataset that silently answers nothing is the one
thing in this round that makes the app quietly wrong rather than slow.

**Do not descope 1.1 and 1.2.** They are the round's stated purpose, they
are small, and the event machinery they rely on already exists.

**Do not descope 5.2.** It is a version string and a test. The owner has
raised README accuracy in two consecutive rounds.

## Practical warnings for the implementer

- **Re-verify every file:line before editing.** This pack was exact against
  `ee2c592` (v0.33.0-alpha, the r26 merge). `ChatViewModel.cs`,
  `ServicesViewModel.cs`, `MainWindowViewModel.cs`,
  `ModelManagementViewModel.cs` and `RagQueryService.cs` all move often and
  several are named hot spots in CLAUDE.md.

- **Re-read `llama-server --help` before implementing doc 03.** The flags in
  3.2 were read from the owner's installed b10195 binary, not recalled.
  `--draft-max` and `--draft-min` have been **removed** upstream and now
  print "the argument has been removed" while doing nothing. An
  implementation written from prior knowledge will emit them, the server
  will start fine, nothing will change, and the feature will look like it
  works. This is the single most likely failure in this round.

- **The build fails while the app is running.** `dotnet build` cannot copy
  `Hermaeus.ViewModels.dll` or `Hermaeus.Voice.dll` into the Desktop output
  while a Hermaeus process holds them, and it fails with `MSB3027` after ten
  retries rather than anything that reads like the real cause. Close the app
  cleanly before building. Never force-kill it.

- **`dotnet run` shares the owner's real settings and data root.** This
  round changes `ManagedServers` settings shape (3.1) and moves model files
  (doc 04). Back up the data root with `BackupService` before running the
  app against it, and never resave settings casually. The settings migration
  in 3.1 runs against the owner's live file the first time the app starts.

- **Doc 04 is the one part of this round that can destroy something a
  rebuild cannot recreate.** Model files are large, slow to re-download, and
  in the owner's case some are QAT builds. Plan stays pure, the preview
  dialog stays, the running-server guard stays, and reference rewriting
  stays. Test against a temp root, never the real models directory.

- **Doc 02 changes the shape of `RagChunk` as it flows through retrieval.**
  Everything downstream of fusion reads `Chunk.Content`: parent upgrade, the
  reranker, the context packer, citations, the trace. Find all of them
  before changing the shape. `RagQueryService.cs` is 816 lines and the
  packing path is most of the second half.

- **Doc 01's concurrency must not swallow a failure.** Three steps in one
  `Task.WhenAll` share one exception unless each is individually wrapped.
  Wrap each in `RunBackgroundTaskCoreAsync` first, then `WhenAll` the
  wrapped tasks, so every failure still names its own operation in the log
  exactly as it does today (r12 3.2).

- **Verify doc 01 by running the app.** Close it, start it, and time how
  long until you can send a message. That is the number this round exists to
  change and no test measures it.

## Housekeeping

- **`docs/review/deferred.md`.** One row moves from Open to Closed:
  **Draft-model speculative decoding** (r18 4.4), closed by doc 03. Fill the
  Evidence column with the type or test that proves it, as the existing rows
  do. Note in the entry that it closed via MTP heads rather than a
  general-purpose draft-model picker, because that is the fact a future
  round needs: the general case (an arbitrary small model as draft for an
  arbitrary large one) is still only partly addressed by 3.3's validation.
- **Every other Open row keeps its status.** None of them are touched by
  this round. Do not quietly mark anything else closed.
- **README "Major Features"** gains speculative decoding and the speed
  check. And the version, per 5.2, which is the actual reason that section
  is being edited.
- **`CHANGELOG.md`** per the FIFO, ten versions maximum in the live file;
  adding 0.34.0 pushes the oldest into `docs/changelog-archive.md`.

## Explicit rejections (do not do these)

Considered and declined, with reasons. Engage with these rather than
re-proposing them.

- **A splash screen, or a progress bar for model loading.** llama-server
  reports nothing between launch and healthy. A progress bar over an unknown
  duration decorates the wait rather than shortening it, and implies
  knowledge the app does not have.

- **A send queue deeper than one message.** Depth one is a convenience.
  Depth N is a scheduler, with ordering, persistence and failure semantics
  that this round has no reason to design.

- **Persisting a held message across restarts.** A message the app sends
  because you launched it three days ago is a message you did not send.

- **Shortening `WaitForHealthAsync`'s five-minute deadline to make startup
  feel faster.** After doc 01 it is off the critical path. Shortening it
  converts a slow success into a failure on exactly the large models that
  need it most.

- **A startup-time budget or regression gate in CI.** 1.5 reports the
  number. A wall-clock assertion on shared runners is a flaky test with a
  stopwatch.

- **An approximate nearest neighbour index for RAG.** An exact SIMD scan
  over a contiguous block handles corpora far past anything this app has
  seen. ANN adds a build step, a tuning surface, a recall tradeoff to
  explain, and a second structure that can disagree with the database. The
  argument for revisiting is a measurement showing the exact scan is too
  slow on a real corpus, which does not exist.

- **A SQLite vector extension.** New native dependency, standing rule
  against new packages, and it would replace a scan that is not the
  bottleneck.

- **Replacing `Bm25Scorer` with FTS5's own `bm25()` ranking.** 2.2 uses FTS5
  for candidate generation specifically so that scoring does not move.
  Swapping the scoring function is a retrieval-quality change wearing a
  performance change's clothes.

- **Raising `MaxCacheBytes` as the fix for 2.1.** A bigger number moves the
  cliff. It does not remove it, and it hides the bug for exactly as long as
  it takes the corpus to grow again.

- **Re-tuning the RRF weights, boost factors, or `MaxBoostFactor`.** r10
  tuned those deliberately. This round makes retrieval faster, not
  different.

- **Automatic tuning, a settings sweep, or a "find the best configuration"
  button.** A sweep is a benchmark suite designer in disguise, rejected in
  r25 and again in r26 for the same reason: a round that measures a thing
  should not also grow the thing being measured.

- **A grade, score, confidence interval or recommendation on a speed-check
  result.** Settled by r23 2.3 and unchanged: the app reports what happened,
  it does not rate itself. A handful of runs on a desktop under unknown load
  does not support a significance claim.

- **Auto-selecting a draft model from a filename convention.** Doc 04 makes
  the file arrive beside its model; the user points at it once. Firing a
  runtime configuration change off the presence of an `MTP/` folder is magic
  that is hard to explain when it guesses wrong.

- **`eagle3`, `dflash` or `dspark` speculative types.** In the
  `--spec-type` list, out of scope. `Types` is a list of strings, so adding
  them later is data, not code.

- **Re-downloading or repairing models already on disk.** Doc 04 organises
  what is there and fetches complete sets going forward. Going looking for
  missing companions for models the app did not fetch is a different
  feature.

- **Model deletion, deduplication, or a "reclaim space" tool.** Moving files
  is already the riskiest operation in this round.

- **Renaming model files.** Folders change, filenames do not. A filename is
  how the user recognises a quantisation.

- **New NuGet packages.** Standing rule. Nothing here needs one: doc 02's
  scan uses `System.Numerics.Tensors`, already imported at
  `HybridRetriever.cs:1`, and FTS5 is SQLite's own, already used by
  `ConversationStore`.
