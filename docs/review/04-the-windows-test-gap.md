# 04. The Windows test gap

## Why

The owner noticed that Linux CI takes about two minutes and Windows about
twelve, and guessed something was up. Something is.

Per-step timings from run `30612541757` (the r27 merge to `main`), read from
the Actions API rather than estimated:

| Step | ubuntu-latest | windows-latest |
| --- | --- | --- |
| Set up job + checkout | 5s | 10s |
| Setup .NET | 1s | 3s |
| Restore | 15s | 36s |
| Build (zero warnings) | 68s | **65s** |
| **Test** | **72s** | **491s** |
| Job total | 2m46s | 10m11s |

Build is the same on both platforms, and Windows is marginally faster at it.
That single fact rules out most of the usual explanations: it is not
compiler throughput, not project count, not the Avalonia XAML compiler, not
`TreatWarningsAsErrors`. Restore costs 21 extra seconds and is worth fixing
(doc 06 6.3) but 21 seconds is not the problem.

The problem is one step. Windows spends **419 extra seconds inside
`dotnet test`**, running the same 1169 facts and theories that Linux
finishes in 72.

## What is known, and what is guessed

Known:

- `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
  (`XunitHarnessTests.cs:4`). Every test runs after the one before it.
- 123 of 188 test files reference `TempDir`, `SqliteConnection` or
  `NewSettings`. Roughly two thirds of the suite does real filesystem and
  SQLite work.
- 57 `Task.Delay` or `Thread.Sleep` calls exist in the suite. Their nominal
  total is on the order of ten to twenty seconds, so they are not 419
  seconds by themselves.
- CLAUDE.md says tests run sequentially because of shared temp data roots
  and SQLite pools, and says not to re-enable parallelization.

Guessed, and **not** to be acted on before 4.1:

- That Windows Defender real-time scanning of `obj`, `bin` and per-test temp
  directories is the dominant cost. Plausible, commonly true on hosted
  runners, unverified here.
- That process-creation cost (Windows `CreateProcess` against Linux
  `fork`/`exec`) dominates, via `McpTests` and anything spawning a child.
  Plausible, unverified.
- That the cost is spread evenly across 1169 tests. **This is the guess most
  likely to be wrong**, and it is the one that decides whether this is a
  parallelism problem or four slow tests.

A round that picks one of those and optimises it produces a CI job that is
differently slow and less trustworthy. So the first item measures.

## Work items

### 4.1 Per-test timing, on both legs, before anything changes

Add a TRX logger to the CI test step and upload it as an artifact:

```yaml
- name: Test
  run: dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj --no-restore -c Release -v normal --nologo --logger "trx;LogFileName=test-results.trx"

- name: Upload test timings
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: test-results-${{ matrix.os }}
    path: '**/test-results.trx'
```

TRX is VSTest's own logger, so this adds no package. `if: always()` so a red
run still yields timings, which is when they are most wanted.

Then run it, download both artifacts, and produce the list of the 25 slowest
tests per platform and the ratio between them. **Put that table in the PR
description.** It is the evidence every later item in this document depends
on, and a future round asking "why is Windows CI slow" should find the
answer in the PR rather than repeating the measurement.

This item lands and its output is read before 4.2 is written. Not the same
commit, not the same sitting if the run has not finished.

### 4.2 Fix what the measurement names

Deliberately unspecified, because specifying it would be guessing.

The plausible shapes, so the implementer recognises them:

- **Concentrated in a few tests.** Fix those tests. Most likely candidates
  are ones that wait on a real timeout rather than draining posted work,
  which is a known pattern here: `docs/review/deferred.md` still lists two
  clock-dependent cases, and r26 5.2 fixed a third by installing
  `Helpers.QueueingSynchronizationContext` and draining the posted work
  instead of waiting. That fix is the template.
- **Spread across all the SQLite-touching tests.** Then it is per-test setup
  cost, and the lever is `TempDir` and connection lifetime, not parallelism.
  Check whether temp directories are created under a path Defender scans and
  whether SQLite connections are pooled across tests or opened per test.
- **Concentrated in process-spawning tests.** Then it is `McpTests` and
  friends, and the lever is fewer spawns or a shared fixture.

Whatever the measurement says, fix that. Do not fix the others speculatively
in the same round.

### 4.3 An opt-in parallel collection, only if 4.1 justifies one

**Attempt this only if 4.1 shows the time is spread broadly rather than
concentrated.** If twenty tests own the 419 seconds, 4.2 is the whole fix
and this item is descoped, which is the expected outcome and not a failure.

If it is justified:

- `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
  **stays**. It is the default and the default stays safe.
- Introduce one named collection for tests that are provably pure: no
  `TempDir`, no SQLite, no process launch, no static or singleton state, no
  `SynchronizationContext`. A test joins it by being explicitly attributed.
- A new test is therefore serial unless someone deliberately marks it
  otherwise, which is the correct direction for the risk: forgetting the
  attribute costs a little speed, and forgetting to remove it costs a flaky
  suite.
- CLAUDE.md's rule gets updated to describe the new arrangement rather than
  deleted. The rule was right about why; it is being refined, not overruled.

Be honest about the ceiling here. The ~65 files that qualify as pure are
mostly parsers, comparers, validators and guards, which are already fast. If
4.1 shows the 419 seconds lives in the 123 I/O files, parallelising the pure
ones recovers very little of it, and saying so in the PR is a better outcome
than shipping a parallel collection that looks like progress.

### 4.4 A CI note in the repo

Whatever 4.1 finds, record it where the next person will look. A short
section in `docs/pull-requests.md` or a comment at the top of `ci.yml`
naming the measured split and what was done about it.

The failure this prevents is the one r25's docs guard was created for: a
fact that was expensive to establish, known to one person, and written down
nowhere.

## Deliberately out of scope

**Dropping the Windows leg, or running it only on `main`.** The app ships on
Windows and the owner develops on Windows. A matrix leg that runs less often
is a leg that catches things later.

**Running tests in parallel across the matrix by sharding.** Splitting 1169
tests across two Windows runners halves wall clock and doubles runner
minutes without learning anything about why the tests are slow. Consider it
after 4.1, never instead of it.

**Raising or removing any test timeout to make a slow test look fast.**
r26 rejected widening a timeout to fix a flake for the same reason: it
converts an occasional red into a slower occasional red.

**A CI time budget or regression gate.** r27 rejected one for startup and
the reasoning transfers: a wall-clock assertion on a shared runner is a
flaky test with a stopwatch. 4.1 reports the number.

**Touching `dotnet build`.** It is 65 seconds on Windows and 68 on Linux.
There is nothing there.
