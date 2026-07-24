# 06. Roadmap and sequencing

## Version

Ships as **0.30.0-alpha** (`Directory.Build.props` only: VersionPrefix,
AssemblyVersion, FileVersion; currently 0.29.1). Minor bump: new
user-visible capability across the Agent workbench. After merge the owner
tags `v0.30.0-alpha`, which becomes the release via the tag-driven
workflow.

## Sequencing (strict)

1. **4.1 approval fingerprint** first. It is the one item where today's
   behaviour is arguably a defect (approval not bound to the displayed
   action); everything else is enhancement. Land it with its tests before
   building more surface on the approval path.
2. **01 Run Ledger, then Task Rewind** (1.1 builder, 1.2 UI, 1.3 service,
   1.4 UI). The round's centerpiece; do it while the budget is freshest.
3. **03 workspace policy** (3.1 manifest, 3.2 enforcement, 3.3
   visibility). Rewind must already exist so its policy interaction
   (doc 03 3.2 last bullet) is implemented once, not retrofitted.
4. **4.2 lesson filter**, then **4.3-4.5 scenarios** (they test 4.1, 4.2,
   and 03, so they come after those exist), then **4.6 bookkeeping**.
5. **02 plan checkpoint and completion honesty** (2.1-2.5). Deliberately
   after the security work: it is the most descopable doc (see below).
6. **05.1 security docs split**, folding in r23's own deltas, then all
   remaining docs (`docs/agent.md`, `docs/features.md`, `CHANGELOG.md`).
7. Close-out: archive this pack to `docs/review/archived/r23/`, final
   build/test pass, PR per 05.2. No AI co-author trailer on commits.

## Test estimate

Roughly 24 to 34 new tests from the current suite (1124+ as of r22):

- Fingerprint binding: 5-6 (stability, mismatch refusal, match executes,
  rejection path, legacy state).
- Ledger builder: 4-5 (created vs edited, multi-patch per file, reverted
  status, parent/child folding, empty run).
- RevertTaskAsync: 5-6 (full revert, created-file deletion, per-file
  conflict skip with partial success report, refusal while
  running/pending, parent with children).
- Policy: 6-8 (precedence, never-beats-allow, malformed whole-rejection,
  read cap persistence, blocked write classification, containment-first).
- Lesson filter: 2-3 (rejection, trace event, non-stated sources
  untouched).
- New scenario check `forbid_active_lesson_matching` plus manifest
  validation: 2-3. Scenario manifests themselves are exercised by the
  existing suite plumbing, not unit-per-scenario.
- Plan checkpoint and reservations plumbing: 3-4.

All new harness-style methods register in `XunitHarnessTests.HarnessCases`
(the reflection guard fails otherwise). Tests stay sequential; nothing here
needs a live server or network. Scenario fixtures with provocative
filenames must remain valid paths on both Windows and Linux CI.

## Descope order (if the round overruns)

Cut from the bottom, never from the middle: first **2.4** (context receipt
expander), then **2.1** (plan checkpoint). Both move to r24 intact. Do not
descope 4.x or 03 partially; a half-shipped security control is worse than
a deferred one. Doc 01 does not get descoped; it is the round.

## Practical warnings for the implementer

- Re-verify every file:line in this pack before editing (spec tree
  74c0c00); AgentService.cs in particular moves often.
- `AgentService.AppendApprovalAsync` is on the workbench UI path and the
  review-queue path; test both callers with the fingerprint change, and
  keep the wizard-singleton lesson in mind for any ViewModel that caches
  state across navigation.
- Rewind's confirmation dialog and the ledger empty state are new UI
  copy: no em dashes, tooltips on icon-only controls (the guard test
  scans), Moss copy per `docs/mascot.md` voice rules, and verify both
  themes by running the app. `dotnet run` shares the owner's real
  settings.json: look, do not resave settings, never taskkill /F.
- The policy glob matcher must reuse the existing `glob_files` semantics,
  not a new engine; divergence between "what glob_files matches" and
  "what policy matches" would be a security bug.
- When moving history sections in 05.1, verify the move with a full-file
  read, not a truncated head check (a dropped heading shipped to a live
  release once already).

## Explicit rejections (do not do these)

External suggestions reviewed and declined, with reasons; engage with
these rather than re-proposing:

- **Numeric confidence scores on completion.** A self-reported "78%" is
  not calibrated and dresses a vibe as measurement. Reservations (2.3)
  carry the same information honestly.
- **Full Evidence Explorer UI** (per-answer evidence trees, ignored-source
  lists with reasons). The receipt expander (2.4) exposes the real,
  deterministic data that exists; a model-narrated evidence story is
  chain-of-thought with better typography. Revisit only if the receipt
  proves insufficient in daily use.
- **Dry Run mode.** Every mutation is already individually previewed and
  approval-gated, and Rewind now makes the aftermath recoverable; a
  whole-run simulation layer (overlay filesystem, pretend tool results)
  is a large new surface for marginal added safety. Deferred, not
  refused forever; a future round could revisit it as a shadow-workspace
  changeset review.
- **Workspace Health Score.** An opinionated repo linter is a different
  product. `WorkspaceAnalysisService` already profiles workspaces for the
  agent's own needs.
- **Memory Diff.** Lessons already persist evidence counts, confidence,
  source kind, and counter-evidence; the Lessons panel shows them. A
  diff ceremony on top adds UI, not information.
- **Scenario 16 as a scenario.** Shipped as code hardening (4.1); the
  suite grades model behaviour and cannot tamper with task state.
- **No policy editor UI** this round; the manifest is hand-edited like
  `allowedCommands`.
- **No new task states.** "Completed with reservations" and the plan
  checkpoint reuse existing states plus persisted flags.
- **No new NuGet packages** (standing rule; the glob and SHA256 needs are
  covered in-tree).
