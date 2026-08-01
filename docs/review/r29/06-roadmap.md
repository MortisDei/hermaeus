# 06. Roadmap

Ships as **0.36.0-alpha**. Version bump in `Directory.Build.props` only
(`VersionPrefix`, `AssemblyVersion`, `FileVersion`). The owner pushes the
tag; agents never do.

Branch `r29/round` from `main`. One PR, per `docs/pull-requests.md`.

## Sequencing

Strict order. The reason for each position is given; do not reorder without
a reason that survives the same test.

| # | Item | Why here | Landed |
| --- | --- | --- | --- |
| 1 | 4.1 + 4.2 Defender exclusion and `RUNNER_TEMP` | The experiment needs CI runs to observe. Landing it first gives it the whole round to report. Must land together or the measurement is meaningless. | Yes |
| 2 | 1.1 Save on Services | The most costly defect in daily use: settings the owner sets are being silently discarded. | Yes |
| 3 | 1.4 Ctrl+Enter both ways | Second most costly: no newline in the chat box in the default configuration. | Yes |
| 4 | 1.3 Chat action row reachable | Small, self-contained, visual verification needed. | Yes (structural fix; pixel result still wants a human eye) |
| 5 | 1.2 Voice channel picker | Depends on nothing; grouped after the two keyboard/mouse fixes because it is the largest of the five. | Yes |
| 6 | 1.5 Cursor flicker: observe, then maybe fix | Deliberately after the other four. Its first deliverable is an observation, not a change. | No, by the doc's own rule: the observation needs a human at the pointer and no third unverified fix shipped. Finding recorded in deferred.md |
| 7 | 4.3 Platform skips report Skipped | Independent of everything. Changes CI's reported numbers, so land it before doc 03 adds tests and the numbers get harder to read. | Yes |
| 8 | 4.4 The four slow `ServerProcessManagerTests` | May turn into a product fix in the health wait; wants its own commit and its own scrutiny. | Yes, as a product fix in the health wait (5.3s/2.6s/2.6s/2.6s to 63ms/29ms/22ms/117ms) |
| 9 | 4.5 Injectable timeouts | Closes a deferred row. | Partly: the three component timeouts done, VoiceOrchestratorTests left per descope order 1 |
| 10 | Doc 02 Models as cards | Largest pure-UI change. After the bug fixes because it is the least urgent and the most reviewable. | Yes |
| 11 | Doc 03 Agent steering | Largest and riskiest. Last of the feature work so an interrupted round loses this rather than a bug fix. Its 3.6 tests land in the same commit as the feature, not after. | Yes, including all of 3.6 |
| 12 | 4.6 + 4.7 Coverage floor and the test-suite doc | Reads the state of the suite after this round's tests exist, not before. | Yes (floor 60, docs/testing.md) |
| 13 | Doc 05 small items, docs, CHANGELOG, version bump | Close-out. | Yes |
| 14 | Read 4.1's result and decide | The last thing before the PR merges: compare the Windows TRX against 501.4s. Keep the step or revert it, and record which in `deferred.md`. | Yes. Run 30681255153: Windows summed test duration **72.3s** against the 501.4s baseline. Condition met by a wide margin; the step is KEPT and the result is recorded in `deferred.md`. |

Commit after each row. An interrupted session should cost at most one row.

## Test budget

Roughly **30 new tests**, plus changes to existing ones.

| Source | New tests |
| --- | --- |
| 1.1 Services save | 3 |
| 1.2 Voice channel options | 3 |
| 1.3 Trailing spacer guard | 1 |
| 1.4 `ChatInputKeyAction` | 8 |
| Doc 02 source kind | 3 |
| Doc 03 steering | 8 (3 of them the safety pins in 3.6) |
| 4.3 Early-return guard | 1 |
| 4.4 Health wait observes exit | 1-2 |

**Expect Linux's reported counts to change.** After 4.3, the Linux leg
reports 16 Skipped where it previously reported 16 Passed. The discovered
count is unchanged. This is the point of the item, not a regression; say so
in the commit message so the next reader of a CI summary is not alarmed.

## Practical warnings

- **`dotnet build` fails with locked DLLs while a dev-run instance is
  alive.** Docs 01 and 02 both want the app running for visual checks. Close
  it cleanly before building. Never `taskkill /F`.

- **The dev run shares the owner's real `settings.json`.** 1.1 adds a Save
  button that writes the whole settings object. Testing it in a dev run
  writes the owner's real settings. Look, do not resave casually, and do not
  leave the app in a state where a stale section VM could be persisted.

- **1.4 changes how Enter behaves in the chat box.** Get it wrong and the
  app's primary input is broken. The pure-function extraction is there so
  the logic can be tested without a visual tree; write those tests before
  wiring the handler.

- **Doc 03 touches `AgentService.cs` (2121 lines), which CLAUDE.md names as
  a hot spot.** Minimal, focused changes. The interrupt handling in 3.4 sits
  inside an existing try/catch whose exception filter is load-bearing
  (`:349`); read the surrounding fifty lines before editing it.

- **4.1 is a CI change and CI changes are cheap to get wrong.**
  `Add-MpPreference` needs the Windows leg only; a missing `if:` condition
  fails the Linux leg with a command-not-found. Guard it on `runner.os`.

- **The TRX artifacts are how any of this is verified.** They are already
  uploaded (`ci.yml:52-56`). Do not remove that step while tidying.

- **Never let test output land in the working tree.** A `.trx` header carries
  `runUser="MACHINE\user"` and a run name of `user@machine`, and a coverage
  report carries absolute local paths. On a repository that is going public
  those are personal identifiers. `dotnet test` writes to
  `src/Hermaeus.Tests/TestResults/` by default the moment a `--logger` or
  `--collect` is passed without an explicit results directory, and it did
  exactly that during this pack's own measurement work. The ignore rules
  added alongside this pack (`.gitignore:6-13`) now catch it, but the habit
  is the real fix: pass an out-of-tree results directory when measuring, and
  check `git status --untracked-files=all` before every `git add`.

## Descope order

If the round runs long, drop from the bottom of this list first. Nothing
above the line is optional.

1. 4.5's `VoiceOrchestratorTests` conversion (do the three injectable
   timeouts, leave the twelve sleep-then-assert tests, say so, and leave the
   deferred row open).
2. 4.7's test-suite documentation section.
3. Doc 02 entirely. The model list works; it is ugly. It is the only item
   here that is purely aesthetic.
4. Doc 03's 3.4 interrupt, keeping 3.1-3.3 and 3.5. A steer that lands at
   the next step boundary is most of the value; the interrupt is the
   refinement. **3.6's safety tests are not descopable under any
   circumstance, including this one.**

--- do not descope below this line ---

5. Doc 01's five fixes. These are the round.
6. 4.3's Skipped reporting. A suite that reports Passed for work it did not
   do is the reason this round exists.

## Housekeeping

- `docs/review/deferred.md` per 5.3.
- Archive this pack to `docs/review/archived/r29/` at close-out.
- `CHANGELOG.md` entry under 0.36.0-alpha.
- Verify `README.md`'s version line; the guard test enforces it.

## Explicit rejections (do not do these)

- **Do not enable test parallelization.** r28's rule stands and 4.6's
  measurements removed the argument for it: 1172 of 1754 tests already
  finish in under 10ms, and the expensive ones are expensive because of
  shared filesystem and SQLite state. Do not delete
  `XunitHarnessTests.cs:4`.

- **Do not shard the CI matrix.** Not before 4.1 reports.

- **Do not link Activity rows to panels.** The owner considered it and chose
  to leave the design as it is: a row opens the specific thing it names, or
  it opens nothing. The panel comes alive as RAG, the agent and downloads
  get used. r28 doc 03's rule is unchanged.

- **Do not build named voice profiles.** 5.1 fixes the copy that promises
  them. The feature is not requested and 1.2 covers the need.

- **Do not edit `AgentSafetyGate.cs`.** Stated in doc 03 and in the README
  and repeated here because it is the one thing in this round that would be
  hard to notice in review and impossible to walk back safely.

- **Do not add a second settings save path.** 1.1 routes through the single
  existing flow. A Services-only serializer would be a second validation
  story and CLAUDE.md's one-save-flow rule exists to prevent exactly that.

- **Do not add tests to raise the coverage number.** 4.6 raises the floor to
  just under the measured value and records the gaps. Filling them is a
  future round's work, chosen deliberately, not padding.

- **Do not remove `TtsSettings.Profiles`.** Still read by the legacy
  migration at `TtsSettingsViewModel.cs:245`.

- **No new NuGet packages.** Every item here is buildable from what is
  already referenced, including 4.3's skip attribute (xunit 2.9.2's
  `FactAttribute.Skip`) and 4.1's Defender exclusion (a runner-provided
  PowerShell cmdlet, not a dependency).

- **Do not "fix" the Windows CI gap in the tests.** The measurement says the
  dominant term is hosted-runner filesystem cost, not test code. Rewriting
  tests to dodge it would trade real coverage for a faster number.
