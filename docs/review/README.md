# Review round 27: Fast, and honest about it

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 06 is the roadmap and sequencing contract.

## Why this round exists

r26 made the Agent workbench legible. r27 is about the two things the owner
asked for by name: performance, and being able to tell that performance
changed.

The app is not slow because anything in it is badly written. It is slow in
four specific places, and each one has the same shape: a decision that was
correct when the code was small, left in place after the code stopped being
small.

**Chat is unusable at launch until a 64k-context Gemma finishes loading.**
`CompletePostSetupInitializationAsync` (`MainWindowViewModel.cs:322-340`)
awaits six steps in strict sequence. Step four is
`Services.AutoStartAllAsync()`, which loops managed servers one at a time
(`ServicesViewModel.cs:1394-1398`), and each one awaits `WaitForHealthAsync`
against a five-minute deadline polled every 600 ms
(`ServerProcessManager.cs:95`, `:695-719`). Step six is
`Chat.LoadModelsAsync()`. The owner's install auto-starts two servers, so the
model dropdown stays empty until a 4.2 GB Gemma at 64512 context and then a
separate embedding server have both reported healthy, in order, even though
listing chat models needs only the first. The window itself is responsive;
`IsLoading` (`MainWindowViewModel.cs:289`) is set, bound to nothing, and has
never gated a pixel. The panel you opened the app to use is the one that
waits.

**A RAG dataset above the cache ceiling silently answers nothing.**
`StoreCache` (`RagQueryService.cs:86-107`) drops a dataset whose estimated
size exceeds 128 MiB *without caching it* and returns. `RetrieveAsync`
(`:226-233`) calls `WarmCacheAsync`, reads the cache back, and gets an empty
list. `CosineScan` scores nothing and BM25 scores nothing, so the query
returns no results, forever, while re-reading every chunk and every embedding
out of SQLite on every single attempt. It is a correctness bug with a
performance cause, and it arrives exactly when a corpus becomes big enough to
be worth having.

**Downloading a model gets you part of a model.** `DownloadHfFileAsync`
(`ModelManagementViewModel.cs:798-820`) downloads the one file that was
clicked. A model's companions are not a concept anywhere in the download
path, so a multimodal model arrives without its `mmproj` and quietly cannot
see, a model with an MTP head arrives without the head, and a sharded model
arrives as one shard that will not load. `PlanDestination`
(`HuggingFaceBrowserSupport.cs:19-24`) then discards the repository path and
writes to `<Models>\LLM\<filename>`, which is why the owner's seven files
named `mmproj-F16.gguf` cannot coexist and why a flat folder offers every
model every other model's projector (`ServicesViewModel.cs:231` finds
projectors by scanning the sibling directory).

**And the round's headline feature is already sitting on the owner's disk.**
Draft-model speculative decoding has been deferred since r18 4.4 because it
needed a second model file, a second VRAM budget, and a picker whose wrong
answer costs performance silently. Two of those three objections are gone.
`unsloth`'s Gemma 4 repositories ship a Multi-Token Prediction head beside
the model, the owner already has `mtp-gemma-4-E4B-it-BF16.gguf` (171 MB)
and `mtp-gemma-4-E4B-it.gguf` (59 MB) downloaded and unused, and the
installed `llama-server` (b10195) exposes `draft-mtp` as a first-class
`--spec-type`. An MTP head shares the base model's vocabulary by
construction, so the compatibility question that made this hard does not
arise, and 59 MB drafting for 4.2 GB is the size ratio speculative decoding
wants rather than the marginal one.

So: doc 01 stops startup waiting on a model and lets a message typed during
that wait actually send. Doc 02 fixes the retrieval bug and makes the scan
scale. Doc 03 wires up MTP drafting and gives the owner a way to measure
whether it helped. Doc 04 makes a downloaded model a whole model. Doc 05 is
small items, including a README that has been nine releases out of date.

## Documents

| Doc | Theme |
| --- | --- |
| `01-startup-that-never-waits.md` | Post-setup init runs concurrently, server auto-start leaves the critical path, chat models list on a server-ready event, and a message typed before the server is up is queued rather than swallowed |
| `02-retrieval-that-scales.md` | The oversized-dataset silent-empty bug, a contiguous embedding block with lazy content fetch, a bounded top-K instead of sorting every chunk, and concurrent send-path injections |
| `03-drafting-and-proof.md` | One composable speculative-decoding section replacing the lone `NgramSpeculative` bool, MTP drafting wired to verified flags, and a speed check that records real tok/s so the knob is not faith-based |
| `04-models-arrive-complete.md` | Companion-aware download (projector, MTP head, shard sets), per-model destination folders, and the sibling scan that depends on them |
| `05-small-open-items.md` | Conversation list metadata projection, the dead `IsLoading`, a README version guard, docs |
| `06-roadmap.md` | Ships as 0.34.0-alpha; sequencing, test budget, descope order, housekeeping, explicit rejections |

## Standing rules for the implementing agent

- **Verify before implementing.** Every file:line reference in this pack was
  exact against tree `ee2c592` (v0.33.0-alpha, the r26 merge). Re-verify
  before editing. `ChatViewModel.cs`, `ServicesViewModel.cs`,
  `MainWindowViewModel.cs` and `ModelManagementViewModel.cs` all move often,
  and CLAUDE.md names several of them as hot spots.
- **Re-verify the llama-server flags against the installed binary.** Doc 03
  is written against `C:\AI\llama-server\b10195\llama-server.EXE --help`,
  read directly rather than recalled. That surface is being actively renamed
  upstream: `--draft-max` and `--draft-min` **have been removed** and now
  print "the argument has been removed" while doing nothing. An
  implementation written from r18-era notes would emit dead flags, change
  nothing measurable, and look like it worked. Run `--help` and read it.
- No em dashes anywhere. Zero warning build. All tests pass. Register any
  new harness-style test methods in `XunitHarnessTests.HarnessCases`; the
  `HarnessRegistrationGuardTests` reflection guard fails otherwise.
- **No new NuGet packages.** Nothing in this round needs one. Doc 02's
  contiguous scan uses `System.Numerics.Tensors`, which
  `HybridRetriever.cs:1` already imports.
- **Doc 01 changes ordering, never gating.** Making startup concurrent must
  not make a failure silent. Each step keeps its own isolation and its own
  logged failure (r12 3.2), and a step that fails must still name itself in
  the runtime log exactly as it does today.
- **The queued send in 1.4 never sends anything the user did not send.** It
  holds a message the user explicitly submitted, shows it as held, and
  releases it when the model lists. It does not retry, does not resend on
  error, and does not survive an app restart. Anything beyond that is out of
  scope.
- **Doc 02's bug fix and doc 02's optimisation are separable and land in
  that order.** 2.1 is data correctness and must be provable on its own
  before any of the scan rework touches the same file.
- **Doc 03 changes how a managed server is launched.** The safety rules in
  `docs/security-review.md` apply unchanged: no shell-string launches, the
  draft model path goes through the same validation as `ModelPath`, and
  `ProcessStartInfo.ArgumentList` stays the only way an argument reaches the
  process.
- **Doc 04 moves the owner's real model files.** It is the one part of this
  round that can destroy something not reproducible by a rebuild. Plan is
  pure and previewed; execution is confirmed. Read `ModelFolderOrganizer`'s
  existing structure before changing it, and keep the existing property that
  a plan can be shown to the user before anything moves.
- Schema changes are additive and go through `SqliteMigrationRunner`. Doc 05
  adds a read projection, not a table.
- Update `README.md`, `docs/features.md`, `docs/rag.md`,
  `docs/benchmarks.md` and `CHANGELOG.md`. Run r25's doc-drift guard. Do not
  document planned behaviour as existing behaviour.
- Moss-attributed copy follows `docs/mascot.md` "Voice in UI copy".
  Icon-only controls need tooltips; the guard test scans axaml and fails
  without one.
- `docs/review/deferred.md` is updated at close-out. One row moves to
  Closed (draft-model speculative decoding, r18 4.4). Every other Open row
  keeps its status; do not quietly mark anything else closed. See 06's
  housekeeping.
- This round lands via pull request per `docs/pull-requests.md`: branch
  `r27/round` from `main`, commit there, open the PR with the template,
  merge after CI is green on both matrix legs. One open PR at a time. No AI
  co-author trailer on commits.

## If this session was interrupted

This round was planned during a period of frequent service interruptions and
is written to be resumable by an agent that has lost all prior context.

- The pack is the contract. Nothing in it depends on remembering a
  conversation.
- Doc 06 carries a sequencing table with an explicit "landed / not landed"
  column. Update it as work lands, in the same commit as the work. That
  table, plus `git log --oneline main..HEAD`, is the whole recovery
  procedure.
- Commit after each numbered item rather than each document. An interrupted
  session should cost one item, not one document.
