# 06. Roadmap and sequencing

## Version

Ships as **0.33.0-alpha** (`Directory.Build.props` only: VersionPrefix,
AssemblyVersion, FileVersion; currently 0.32.0). Minor bump: the workbench
restructure and the cross-suite ranking are both user-visible. After merge
the owner tags `v0.33.0-alpha`, which becomes the release through the
tag-driven workflow. Agents never push a tag.

## Sequencing (strict)

1. **01, the review queue, first, and 1.2 before anything else in it.**
   Write the failing test first: approve a `Complete` task with no pending
   action, assert its status is still `Complete` afterwards, and watch it
   fail. That is the round's one active data-corruption path and it is
   twelve lines to close. Then 1.1, 1.3, 1.4, 1.5. Every part of doc 01 is
   independent of doc 02 and must land before it, so that doc 02 is moving
   correct controls rather than relocating a bug.

2. **03, but only 3.1.** The capability derivation is a pure function with
   no dependency on the layout, and doc 02 moves the control that renders
   it. Landing 3.1 first means doc 02 moves the final version once instead
   of moving a placeholder and then editing it in place. Same reasoning r25
   used to put its context receipt before its branch switcher.

3. **02, the restructure.** The largest change and the one with the least
   test coverage per line, so it goes when the two things it relocates are
   already settled. Order inside it: build the four-tab shell and the status
   line and decision strip first, verify the app runs, then move sections in
   the order of the 2.1 inventory table, checking each row off as it lands.
   Do not move everything and then run it once.

4. **3.2, the run outcome block.** Needs doc 02's Run tab to exist. It is
   composition over the existing ledger, so it is short once there is
   somewhere to put it.

5. **04, benchmarks.** Entirely inside `BenchmarkInsightsMath`, the report
   record, and one card on an existing tab. Nothing else in this round
   touches any of them, so this is the recovery work if doc 02 runs long.

6. **05.** 5.2 (the flaky test) can land any time and should land early if
   the suite goes red for that reason during the round. 5.1 last of the
   code. 5.3 docs after the features exist, per r25's rule, and run the
   doc-drift guard at the end.

7. Housekeeping (below), then close-out: archive this pack to
   `docs/review/archived/r26/`, final build and test pass, PR per
   `docs/pull-requests.md`. No AI co-author trailer on commits.

## Test estimate

Roughly **60 to 80 new tests**. The suite stands at roughly 1,400 cases at
`35045fc`; expect to land somewhere around 1,460 to 1,480.

| Doc | Tests |
| --- | --- |
| 01 The review queue is a queue | 14 to 18 |
| 02 A workbench you can read | 6 to 10 |
| 03 What it can do, and whether it worked | 16 to 20 |
| 04 Best across every suite | 16 to 20 |
| 05 Small open items | 8 to 12 |

Doc 02's number is deliberately the lowest in the round despite being the
largest change. A view restructure is verified by review, by the axaml
guards, and by running the app; padding it with assertions about control
trees would buy confidence the tests do not actually have. **Doc 02 is the
one place in this round where "run the app and look at it" is the primary
verification**, and it is not optional. See the warnings below.

All new harness-style methods register in `XunitHarnessTests.HarnessCases`
or the reflection guard fails the suite. Tests stay sequential.

Nothing in this round needs a live server, a network call, a microphone or a
GPU. If a test appears to need one, the abstraction is wrong. Doc 03's
capability text, doc 04's whole aggregation, and doc 01's queue predicate
are all pure functions over records, and that is where the coverage should
concentrate.

## Descope order (if the round overruns)

Cut from the bottom, never from the middle:

1. **5.1, the capabilities endpoint.** Deferred since r1 and it survives
   another round. It is in this round because it is small, not because it is
   urgent.
2. **4.4, Doctor.** Only if Doctor makes no cross-suite claim today, in
   which case there is nothing to keep consistent yet.
3. **2.4, the Changes tab badge**, and **2.6's empty-state pass.** Polish on
   top of the restructure.
4. **3.2, the run outcome block**, keeping 3.1. 3.1 has a drift guard
   attached to it and stops the capability text rotting again; 3.2 is a
   better presentation of information the Changes tab already carries.
5. **4.3's per-suite drill-down**, keeping 4.1 and 4.2. The column the owner
   asked for is the answer plus its basis; the click-through is convenience.

**Do not descope doc 01.** Not any part of it, and especially not 1.2. An
approval path that mutates a task with nothing pending is the one thing in
this round that damages data the user cannot get back.

**Do not descope doc 02 into "just fix the header".** 2.2 alone would stop
the header eating the window and would leave the panel exactly as
incomprehensible as the owner reported it. If doc 02 cannot be finished, the
right move is to finish the 2.1 inventory for the Run and Changes tabs and
leave Workspace and History as a single third tab, not to abandon the shell.

## Practical warnings for the implementer

- **Re-verify every file:line before editing.** This pack was exact against
  `35045fc` (v0.32.0-alpha, the r25 merge). `AgentViewModel.cs`,
  `AgentService.cs` and `BenchmarkService.cs` are named hot spots in
  CLAUDE.md and move often. Docs 01, 02 and 03 all land in
  `AgentViewModel.cs`.

- **The build fails while the app is running.** `dotnet build` cannot copy
  `Hermaeus.ViewModels.dll` or `Hermaeus.Voice.dll` into the Desktop output
  while a Hermaeus process holds them, and it fails with `MSB3027` after ten
  retries rather than anything that reads like the real cause. Close the app
  cleanly before building. Never force-kill it.

- **`dotnet run` shares the owner's real settings and data root.** This
  round changes the Agent panel, which reads and writes real
  `task_state.json` files and the real `agent/task_index.db`. Back up the
  data root with `BackupService` before running the app against it, and
  never resave settings casually.

- **Doc 01 changes what the review queue lists. Existing tasks are the test
  data.** The owner's install has real tasks with real approval history, and
  after 1.1 most of them will correctly disappear from the queue. That is
  the fix, not a data loss, and the run ledger still holds every approval.
  Verify against a real task index before assuming a seeded one is
  representative: `task_index.db` is rebuildable, so a stale index is a
  plausible confusion during this work.

- **Verify doc 02 by running the app, in both themes, at a small window
  size.** The current layout's worst failure (2.2) only appears when content
  grows, so check it with a task that has a long plan, sub-tasks and new
  lessons all at once. The tooltip guard scans axaml and will bite on moved
  buttons.

- **Check the 2.1 inventory table off row by row.** The failure mode of a
  restructure this size is a panel that quietly does not exist afterwards,
  and it is invisible in a diff that moves a thousand lines.

- **Do not let doc 03 grow a service.** 3.1 is a pure function over the tool
  set and the workspace policy. 3.2 is composition over `AgentRunLedger`. If
  either starts needing a constructor dependency it did not have, stop and
  re-read the doc.

- **Doc 04's aggregation must share r25's shared-case-set logic, not copy
  it.** Two implementations of "which cases did they both sit" is the exact
  divergence r25 doc 04 was written to remove.

- **When moving or rewriting doc sections, verify with a full-file read.**
  `docs/agent.md` is 704 lines with 26 headings and doc 05 asks for real
  edits in several of them. A truncated check has already let a dropped
  heading reach a live release once in this repository.

## Housekeeping

- **`docs/review/deferred.md`.** Four rows move from Open to Closed:
  Benchmarks "Best Overall" column (doc 04), Settings and capabilities probe
  endpoint on the local API (5.1), and the closable third of Deterministic
  timing for clock-dependent tests (5.2, with the row rewritten to describe
  the two cases that remain open rather than deleted). Fill the Closed
  table's Evidence column with the type or test that proves it, as the
  existing rows do.
- **Draft-model speculative decoding** stays open and becomes the leading
  candidate for r27. Update its "Why it is still open" to say it was passed
  over in r26 because the round's weight went to the Agent workbench, not
  because the case against it changed. An item that is deferred twice with
  no reason recorded is how an item goes missing for twenty rounds, which is
  why this file exists.
- **README "Major Features"** gains the workbench restructure and the
  cross-suite benchmark ranking. r25 added the guard; this is the first
  round that has to satisfy it rather than write it.
- **`CHANGELOG.md`** per the FIFO. Adding 0.33.0 pushes 0.26.0 out of the
  live file and into `docs/changelog-archive.md`. See 5.3.

## Explicit rejections (do not do these)

Considered and declined, with reasons. Engage with these rather than
re-proposing them.

- **A separate Agent panel per tab in the main sidebar.** The Agent is one
  place you do one kind of work. Four sidebar entries would move the
  confusion up a level and make the sidebar the thing that needs a redesign.

- **A dockable, resizable, user-arrangeable workbench layout.** Solving "this
  panel is hard to read" by making the user lay it out themselves is not
  solving it. Four tabs with a fixed, considered arrangement is the product
  decision; if the arrangement is wrong, change the arrangement.

- **Persisting the selected tab across sessions.** The Agent panel opens on
  Run because Run is what it is for. A panel that opens on whatever you were
  last poking at is a panel with no opinion.

- **Auto-switching tabs when a run finishes or an approval arrives.** The
  decision strip exists precisely so that nothing has to move the page under
  the user. An app that jumps while you are reading is the problem this
  round is fixing.

- **A bulk "approve all" in the review queue.** Every approval is one
  decision about one named action with its own fingerprint. Bulk approval is
  a button whose only purpose is to stop reading, on the one screen in the
  app where reading is the whole point.

- **Remembering approvals per tool, or an "always allow" checkbox.**
  `RememberedCommandApprovals` already exists for the narrow, deliberate
  command-recipe case (`AgentModels.cs:149`) and that is the extent of it.
  Widening it is a change to the safety gate, and the safety gate is not
  what this round is about.

- **Loosening or reclassifying any risk level to reduce approval prompts.**
  If the workbench feels like it asks too often, that is a finding to record,
  not a thing to fix by making the gate quieter.

- **An approval-history panel.** Two renderings of approval history already
  exist (doc 01 1.1). A third would be the same mistake this round is
  correcting.

- **An LLM-written run summary in 3.2.** The model's own account is already
  on screen as `CurrentTaskSummaryLabel`. 3.2 is the deterministic
  counterpart, and blending them makes it impossible to tell which one is
  evidence.

- **A confidence score, grade, or percentage on a run outcome.** Settled by
  r23 2.3 and unchanged: the app reports what happened, it does not rate
  itself.

- **Pooling every case across suites for doc 04's cross-suite ranking.** A
  40 case suite would outvote a 5 case suite four to one, so "best across all
  suites" would silently mean "best on the largest suite". 4.2 states the
  method that does not do this.

- **User-tunable benchmark weighting.** A ranking anyone can retune is a
  ranking nobody can compare.

- **A benchmark suite designer or case editor.** Rejected in r25 for the
  same reason it is rejected here: a round that fixes a ranking should not
  also grow the thing being ranked.

- **Making the capabilities endpoint probe.** 5.1 reports settings and
  counts. An endpoint that loads a model to find out whether a model loads is
  a denial-of-service handle wearing a health check's name.

- **Widening a timeout to fix 5.2.** Drain the posted work. A wider timeout
  converts an occasional red into a slower occasional red.

- **Draft-model speculative decoding.** Not rejected, deferred, and it stays
  the leading candidate for r27. It needs a second model file, a second VRAM
  budget, and a picker whose wrong answer costs performance silently instead
  of failing visibly. That is a doc of its own and it does not belong beside
  a view restructure.

- **New NuGet packages.** Standing rule. Nothing here needs one, and doc 02
  in particular is built from Avalonia controls this repository already uses:
  `BenchmarkView.axaml:189` is the `TabControl` precedent.
