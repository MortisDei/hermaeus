# 06. Roadmap and sequencing

## Version

Ships as **0.32.0-alpha** (`Directory.Build.props` only: VersionPrefix,
AssemblyVersion, FileVersion; currently 0.31.0). Minor bump: branching and
Whisper are both user-visible capabilities. After merge the owner tags
`v0.32.0-alpha`, which becomes the release through the tag-driven workflow.
Agents never push a tag.

## Sequencing (strict)

1. **02, the context receipt, first.** It is the smallest doc, it fixes a
   bug the owner is looking at right now, and it rebuilds the same message
   footer that doc 01 then adds a branch switcher to. Doing that region
   twice is waste; doing doc 01's switcher into a footer that already has
   its receipt is not. Write the collapsed-means-collapsed regression test
   first and watch it fail before changing anything.

2. **01, branching.** The centerpiece and the largest behaviour change.
   Land 1.1 and 1.2 (the tree, the backfill, the pure path functions) with
   their tests **before any UI reads them**, for the same reason r24
   landed Projects before anything consumed a project id. Then 1.3
   regenerate, 1.4 edit, 1.5 the switcher, 1.6 the subsystem decisions.
   1.3 before 1.4 because regenerate is the existing data-loss path and
   closing it is the item's whole justification.

3. **04, benchmarks.** Self-contained inside `BenchmarkInsightsMath`, a
   pure static class nothing else in this round touches. Good recovery
   work if doc 01 runs long.

4. **03, Whisper.** Largest volume of genuinely new code, and the only doc
   whose loss would not gut the round. Build 3.2 (log-Mel and FFT) and
   3.3 (decode and detokenize) as pure, fully tested units before wiring
   either into a session, exactly as `GreedyCtcDecode` and
   `NormalizeZeroMeanUnitVariance` are pure today.

5. **05, docs.** Last on purpose: the coverage guard can only pass once
   this round's features exist to be documented. Write `deferred.md` early
   if convenient, but run the guard at the end.

6. Housekeeping (below), then all docs, then close-out: archive this pack
   to `docs/review/archived/r25/`, final build and test pass, PR per
   `docs/pull-requests.md`. No AI co-author trailer on commits.

## Test estimate

Roughly **92 to 118 new tests** on top of the 1316 green at `1bd3f2d`, so
expect to land somewhere around 1408 to 1434.

| Doc | Tests |
| --- | --- |
| 01 Conversation branching | 22 to 26 |
| 02 One context receipt | 14 to 18 |
| 03 Speech that punctuates | 20 to 24 |
| 04 Benchmarks you can trust | 12 to 16 |
| 05 Docs that match the app | 4 to 6 |

All new harness-style methods register in `XunitHarnessTests.HarnessCases`
or the reflection guard fails the suite. Tests stay sequential.

Nothing in this round needs a live server, a network call, a microphone or
a GPU. If a test appears to need one, the abstraction is wrong. An unusually
large share of this round is pure functions over arrays and lists
(`ConversationTree`, `ChatContextReceipt`, the FFT and mel front end, the
decode loop, `BenchmarkInsightsMath`), which is where the coverage should
concentrate.

Watch suite runtime: 1m31s locally at 1316 tests. The FFT and mel tests
must use small fixtures, not sweeps over sample rates.

## Descope order (if the round overruns)

Cut from the bottom, never from the middle:

1. **3.5, language selection.** Auto-detection reported honestly is enough;
   the picker is polish.
2. **The rest of 03 down to 3.1 through 3.4.** Whisper with punctuation,
   casing and bounded long audio is the entire win. Do not ship a new model
   with the old unbounded forward pass.
3. **4.2, the drill-down expander.** Not 4.1. 4.1 is the honesty fix and
   4.2 is the convenience; cutting the honesty fix and keeping the
   expander would be exactly backwards.
4. **1.6's full-tree export option**, keeping active-path export.
5. **5.1's `docs/features.md` half**, keeping the README check.

Do not descope 1.1 through 1.3, or 2.1 and 2.2. Half a tree is a corrupt
tree, and a receipt that collapses two source kinds out of three is the bug
the owner reported wearing a different hat.

**The file cap in 3.0 is not descopable at any budget.** A file picker the
app itself offers can currently OOM-kill the application on an ordinary
podcast-length recording, because `MaxFileBytes` permits about 1.7 hours of
audio into a single quadratic-attention forward pass. If doc 03 is cut
entirely, the cap still drops to what the shipped model can actually
survive, as a housekeeping item, with the message stated as a duration.

## Practical warnings for the implementer

- **Re-verify every file:line before editing.** This pack was exact against
  `1bd3f2d` (v0.31.0-alpha, 1316 tests green, zero warnings).
  `ChatViewModel.cs`, `AgentViewModel.cs`, `AgentService.cs` and
  `ServicesViewModel.cs` are named hot spots in CLAUDE.md and move often.
  Doc 01 and doc 02 both land in `ChatViewModel.cs`.

- **The tree backfill is the highest-risk change in the round.** Every
  existing conversation has its parent chain inferred at load. Get it wrong
  and the owner's real chat history renders scrambled or truncated. Test
  against a seeded 0.31.0 `messages_json` blob, not only a round-trip of
  data this round wrote. This is the same discipline r24 applied to its
  four migrations and it matters more here, because the blob is the
  content rather than an index that can be rebuilt.

- **Back up the data root before running the app.** `dotnet run` shares the
  owner's real `settings.json` **and** the real `conversations.db`, and
  this round changes how conversations load and save. `BackupService`
  exists; use it. Never force-kill the process; close it cleanly.

- **Regenerate currently deletes user data.** Do not preserve any part of
  that behaviour for compatibility, and do not put it behind a setting.

- **The cycle guard is not paranoia.** `messages_json` is a text blob in a
  file the user owns, syncs and can edit. A cycle in the parent chain
  walked on the UI thread is a hang with no cancel.

- **Verify both themes by actually running the app.** The branch switcher
  and the receipt expander are new chrome on every message in the app's
  most-used panel. The tooltip guard scans axaml and will bite on the
  switcher arrows.

- **`Hermaeus.Voice` keeps ONNX Runtime types to itself.** CLAUDE.md states
  this rule for `Hermaeus.Rag`; Voice holds itself to the same line today
  and doc 03 must not be the change that breaks it.
  `ISpeechRecognitionService` stays free of `Microsoft.ML.OnnxRuntime`.

- **Never delete a downloaded model.** Doc 03 retires wav2vec2 from the
  code, not from the user's disk. Report it, offer removal, do not act.

- **Nothing new on the chat send path.** Doc 02's receipt is built from
  source references that already exist in memory after the pre-stream
  phase; it performs no retrieval. The existing pre-stream timing breakdown
  (`docs/features.md:21-37`) will show a regression, so check it before
  opening the PR.

- **When moving or rewriting doc sections, verify with a full-file read.**
  A truncated check has already let a dropped heading reach a live release
  once in this repository, and doc 05 is a round of doc rewriting.

## Housekeeping

- **README "Major Features"** gains r24's six capabilities plus branching.
  This is doc 05's motivating item and it must not itself be the thing that
  gets skipped at close-out.
- **`docs/review/deferred.md`** seeded from 5.3 and linked from the
  README's documentation list.
- **`CHANGELOG.md`** per the existing FIFO with `docs/changelog-archive.md`,
  ten versions maximum in the live file.
- **Doctor's leftover-model note** if 3.1 retires wav2vec2: size, path,
  explicit remove action, no automatic deletion.

## Explicit rejections (do not do these)

Considered and declined, with reasons. Engage with these rather than
re-proposing them.

- **Branch merging, diffing, or three-way reconciliation.** A tree with a
  switcher is the feature. Merging two model answers into one is a research
  problem with no correct answer, and any heuristic would silently
  fabricate a message the model never produced.

- **Auto-branching on anything other than regenerate and explicit
  edit-and-resend.** Branches come from two deliberate actions. Not from
  typing, not from navigating away, not from a timer.

- **Branch names, colours, a tree view, or a conversation minimap.**
  `‹ 2/3 ›` on messages that have siblings. A chat app with a graph view
  has lost the plot, and this round exists partly because the app already
  shows more than it explains.

- **Editing assistant messages.** The transcript records what the model
  actually said. A transcript you can rewrite is not a transcript, and
  every downstream consumer (memory extraction, Recall, export, traces)
  quietly becomes untrustworthy.

- **LLM-based punctuation restoration on top of wav2vec2.** The cheap
  alternative to doc 03, and wrong: it puts a second model on the
  transcription path, makes the output non-deterministic, and **invents**
  punctuation rather than reporting it. Whisper emits punctuation because
  it heard it.

- **Streaming or word-by-word live transcription.** Whisper decodes a
  30 second window at a time. Live partial display would need a different
  model and a different interaction design. r24's hands-free state machine
  already shows the user what it heard before anything sends.

- **Keeping both wav2vec2 and Whisper as selectable local engines.** Two
  local STT backends doubles the install surface, the Doctor checks and the
  support burden so that a user can choose the worse one.

- **A benchmark suite designer or case editor.** 4.1 changes how existing
  results are ranked. It does not add a way to author cases, and a round
  that fixes a ranking should not also grow the thing being ranked.

- **Generating documentation from code.** 5.1 asserts that a name appears
  in a file. It does not write prose. A generated README is a README nobody
  reads and nobody trusts.

- **A new nav panel.** Nothing in this round earns one. The receipt is
  in-message, the branch switcher is in-message, the benchmark drill-down
  is an expander in an existing tab.

- **New NuGet packages.** Standing rule, and doc 03 is the temptation: no
  audio library, no DSP library, no FFT package, no tokenizer package. The
  FFT is roughly sixty lines of radix-2 and the detokenizer is a dictionary
  lookup, both pure and both more testable written in-tree than pulled in.
