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

- **A Speed Check suite, and a comparison between two of its runs.**
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
