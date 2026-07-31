# 04. Tests that actually run

## Why

This project has never audited its test suite as a whole. It has 1754 tests
across 198 files and a coverage ratchet, and nobody has ever asked whether
the suite is honest, whether it is fast for the right reasons, or where its
gaps are.

r28 doc 04 added a TRX logger to both CI legs and deliberately stopped
there, refusing to optimise a guess. The measurements are now in. This doc
reads them, adjudicates r28's three stated guesses, and fixes what the
evidence names.

## The measurements

All CI figures are from run **30668536832** (the r28 merge to `main`,
v0.35.0-alpha), TRX artifacts `test-results-windows-latest` and
`test-results-ubuntu-latest`. Local figures are from this machine
(Windows 11, Release, `--no-build`) on the same tree.

**Before reproducing any of this:** send test output outside the repository.
A `.trx` header contains `runUser="MACHINE\user"`, and a coverage report
contains absolute local paths; both are personal identifiers on a repository
that is going public. `dotnet test` defaults to
`src/Hermaeus.Tests/TestResults/` whenever `--logger` or `--collect` is
passed without `--results-directory`. Pass one. `.gitignore:6-13` catches the
mistake, but do not rely on it.

| | ubuntu-latest | windows-latest |
| --- | --- | --- |
| Job wall time | 2m57s | 13m06s |
| Summed test durations | **59.9s** | **501.4s** |
| Tests | 1754 | 1754 |
| Passed / Skipped | 1754 / 0 | 1754 / 0 |
| Median test | ~2ms | ~2ms |

Where Windows's 501 seconds go:

| Duration band | Tests | Sum | Share |
| --- | --- | --- | --- |
| > 10s | 3 | 101.7s | 20.3% |
| 5-10s | 3 | 17.8s | 3.5% |
| 1-5s | 129 | 243.5s | 48.6% |
| 100ms-1s | 327 | 132.6s | 26.4% |
| 10-100ms | 120 | 4.1s | 0.8% |
| < 10ms | **1172** | **1.9s** | 0.4% |

The five worst Windows-minus-Linux gaps:

| Test | Windows | Linux | Ratio |
| --- | --- | --- | --- |
| `TraceStoreTests.Retention_prunes_per_kind_without_touching_other_kinds` | 49,334ms | 961ms | 51x |
| `EvalStoreTests.Retention_keeps_only_the_newest_window` | 27,652ms | 841ms | 33x |
| `ServiceHarnessTests` (model usage rollup survives trace pruning) | 24,686ms | 1,384ms | 18x |
| `AgentHarnessTests` (inspect git diff handles large status output) | 6,665ms | 502ms | 13x |
| `ProjectViewModelTests.OpenNewProjectFromWorkspace_offers_adoption_...` | 5,832ms | 78ms | 75x |

## Adjudicating r28's three guesses

r28 doc 04 named three hypotheses and forbade acting on any of them before
measuring. All three are now answered.

**"The cost is spread evenly across the tests." Wrong, and r28 correctly
predicted this was the guess most likely to be wrong.** 1172 of 1754 tests
finish in under 10ms on Windows and account for 0.4% of the time. The cost
lives in the 135 tests that take over a second.

**"Process creation dominates." Partly right, and precisely bounded.**
Exactly four tests are Windows-only work:
`ServerProcessManagerTests.StartAsync_*` spend 13.1s on Windows and 0.6ms
on Linux. That is real process-spawn cost and it is genuinely fixable. It
is 2.6% of the gap, not the explanation for it.

**"Windows Defender / hosted-runner I/O dominates." Right, and this is the
finding.** The decisive comparison is not Windows against Linux, it is
**CI Windows against local Windows**:

| Test | CI Windows | Local Windows | CI Linux |
| --- | --- | --- | --- |
| `MemoryScopeTests.Existing_v1_database_gains_scope_columns_on_initialize` | 4,409ms | **164ms** | 27ms |
| `TraceStoreTests.Retention_prunes_per_kind...` | 49,334ms | **3,588ms** | 961ms |
| `VoiceTempFileCleanupTests.GenerateSpeechAsync_deletes_the_temp_wav...` | 3,638ms | **1,660ms** | 23ms |
| `ServerProcessManagerTests.StartAsync_disposes_the_previous_monitor_cts` | 5,258ms | **5,271ms** | 0.6ms |

Same OS, same code, same test: 27x slower on the hosted runner for a test
that creates a temp directory and a small SQLite file. The one test whose
local and CI numbers match to within 0.3% is the one that spawns a process
rather than touching the filesystem. Filesystem work on the hosted Windows
runner is the dominant term, and it is not something the test code caused.

Corroborating: a full local run in **Debug, with coverage instrumentation
attached**, completed 1754 tests in **5m29s**. CI Windows takes 8m11s in
Release with no instrumentation.

### One hypothesis this data kills

Test durations do not drift upward over the run. Grouping both legs' results
into deciles by start time shows medians bouncing between 0.3ms and 170ms
with no monotonic trend, on either platform. So the accumulation of
undeleted temp roots in `%TEMP%` (`Helpers.cs:232`, `_deferred`) is **not**
progressively slowing the suite. Do not spend a future round on it.

## Work items

### 4.1 Exclude the test working set from Defender on the Windows CI leg

The measurement says hosted-runner filesystem cost dominates and that our
code is not the cause. The one cheap, reversible lever is to stop the
runner's real-time scanner from inspecting every file the suite creates.

Add to `.github/workflows/ci.yml`, on the Windows leg only, before the test
step:

```yaml
      - name: Exclude test working set from Defender (windows only)
        if: runner.os == 'Windows'
        shell: pwsh
        run: |
          Add-MpPreference -ExclusionPath $env:RUNNER_TEMP
          Add-MpPreference -ExclusionPath $env:GITHUB_WORKSPACE
```

This is a CI-only change. It alters no product code and no test.

**It is an experiment with a stated success condition.** Compare the next
run's Windows summed test duration against 501.4s using the TRX artifact
r28 already uploads. If it does not fall materially, **revert this step**
and record in `docs/review/deferred.md` that hosted Windows disk I/O is the
floor and no test-level change will move it. Do not keep a CI step that
does nothing on the theory that it might be helping.

### 4.2 Point test temp roots at the runner's own temp directory

`TempDir` builds its root from `Path.GetTempPath()` (`Helpers.cs:242`). On
a hosted runner that is not necessarily `RUNNER_TEMP`, which is the path
4.1 excludes and the path the runner itself cleans between jobs.

Make the root honour `RUNNER_TEMP` when set, falling back to
`Path.GetTempPath()` otherwise, so 4.1's exclusion covers the files the
suite actually writes and a developer machine is unaffected.

This must land **with** 4.1 or 4.1's measurement is meaningless.

### 4.3 Sixteen tests that report Passed without running

Sixteen test methods begin with:

```csharp
if (!OperatingSystem.IsWindows()) return;
```

They are in `ServerProcessManagerTests` (7), `PythonHealthValidatorTests`
(3), `LlamaServerInstallTests` (2), `LocalAiSetupXttsPythonGateTests` (2),
`LocalApiProcessManagerJobObjectTests` (1) and `ModelManifestStoreTests`
(1). On Linux each one returns immediately and xunit records
**Passed**. Both legs report `Skipped: 0`.

This is the accuracy defect in the suite. A green Linux leg is currently
claiming to have verified llama-server installation, Python health
validation, job-object assignment and manifest behaviour that it did not
execute.

Note the three sites that are **not** this defect and must be left alone:
`BackupMigrationTests.cs:173`, `ServiceTests.cs:1873` and
`ServiceTests.cs:1073` branch to platform-appropriate assertions inside a
test that asserts on both platforms. That is correct.

The fix, with no new package. xunit 2.9.2 supports a `Skip` reason on
`FactAttribute`, evaluated at discovery, so a subclassed attribute can
decide per platform:

```csharp
/// <summary>A fact that runs only on Windows, and reports Skipped rather
/// than Passed elsewhere. A test that silently returns on the platform it
/// does not support is a green tick for work that never happened.</summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only behaviour.";
    }
}
```

Replace `[Fact]` with `[WindowsOnlyFact]` on those sixteen methods and
delete the early-return line from each. Keep every assertion exactly as it
is; this item changes reporting, not coverage.

Then add the guard that stops it recurring: a test that scans
`src/Hermaeus.Tests/*.cs` and fails on a `[Fact]`-attributed method whose
body opens with an `OperatingSystem.Is*` early return. Model it on the
existing axaml-scanning guards.

### 4.4 The four tests that genuinely cost Windows 13 seconds

`ServerProcessManagerTests.StartAsync_disposes_the_previous_monitor_cts_on_restart`
(5.3s), `StartAsync_does_not_mutate_the_callers_ServerConfig` (2.6s),
`StartAsync_reports_error_with_exit_code_and_log_tail_when_the_process_exits_immediately`
(2.6s) and `StartAsync_does_not_block_launch_when_job_object_assignment_fails`
(2.6s). Local and CI timings agree to within 0.3%, so this is our code, not
the runner.

Each launches a real `ImmediateExitExecutable` and waits. Find the wait.
The likely shape is a health-check poll with a fixed interval that always
runs to its full duration when the process has already exited.

**Fix the wait, not the test.** If `ServerProcessManager` polls for health
on a schedule that ignores the process having already exited, that is a
product defect worth fixing on its own merit: a user whose server dies on
launch waits the same seconds for a result the app already had. Make the
health wait observe process exit, and the tests get fast as a consequence.
If the wait turns out to be genuinely necessary, make its interval
injectable and have the tests pass a short one. Do not shorten a production
timeout to suit a test.

### 4.5 Nine seconds of real sleeping, on both legs

These wait on wall-clock time and pay it on every platform:

| Test | Linux | Windows |
| --- | --- | --- |
| `MemoryEmbeddingBackfillTests.SearchAsync_falls_back_to_FTS_ranking_quickly_when_query_embedding_hangs` | 3,044ms | 4,168ms |
| `MemoryEmbeddingBackfillTests.SaveAsync_returns_promptly_when_the_embedder_hangs...` | 3,034ms | 3,643ms |
| `RecallServiceTests.A_source_that_exceeds_its_timeout_is_omitted_and_named...` | 3,005ms | 3,017ms |

`VoiceOrchestratorTests` adds 4.0s on Linux across 12 tests through fifteen
`Task.Delay` calls of 10ms to 300ms (`VoiceOrchestratorTests.cs:18-280`).
The suite has 60 `Task.Delay`/`Thread.Sleep` sites in total.

The three tests above are the honest targets: each asserts that a component
gives up on a hanging dependency within a timeout, and each proves it by
waiting out the real timeout. Make the timeout injectable on the component
under test (constructor parameter defaulting to today's value, so
production behaviour is unchanged) and pass a short one from the test. This
is the same shape as the fix `deferred.md` has been carrying since r25 for
clock-dependent tests, and it closes that row.

`VoiceOrchestratorTests` is sleep-then-assert throughout and is the most
likely place in this suite for a future flake. Convert it to signalled
waits (a `TaskCompletionSource` the fake playback completes) rather than
timed ones. If that turns out to be a larger rewrite than it looks, do the
three tests above, leave `VoiceOrchestratorTests` alone, and say so.

### 4.6 Read the coverage, and fix the ratchet that stopped ratcheting

Measured on this machine at `f03e7c1`, `dotnet test --collect:"XPlat Code
Coverage"`, 1754 tests, all passing:

**Overall line coverage: 61.6%** (29,438 / 47,807).

| Project | Line coverage |
| --- | --- |
| `Hermaeus.Composition` | 100.0% (91/91) |
| `Hermaeus.Agent` | 91.3% (4,582/5,019) |
| `Hermaeus.Core` | 90.7% (2,110/2,327) |
| `Hermaeus.LocalApi` | 88.1% (332/377) |
| `Hermaeus.Rag` | 79.9% (2,716/3,400) |
| `Hermaeus.Services` | 71.2% (9,907/13,923) |
| `Hermaeus.Mcp` | 68.6% (197/287) |
| `Hermaeus.Voice` | 65.1% (2,450/3,765) |
| `Hermaeus.ViewModels` | 62.9% (6,982/11,103) |
| `Hermaeus.Desktop` | 0.9% (71/7,568) |

`Hermaeus.Desktop` is views and compiled XAML and is not meaningfully
unit-testable; excluding it, the suite covers **73.0%** of the code it can
reach.

**The ratchet is not ratcheting.** `scripts/coverage.ps1:2` defaults
`-Threshold 47`; CLAUDE.md says the floor is 45%. Actual is 61.6%. A floor
fourteen points below the current value cannot fail on any regression short
of deleting a quarter of the tests. Raise the default threshold to **60**,
update CLAUDE.md's stated floor to match, and reconcile the two numbers
(they have disagreed for some time; see 5.2).

Do not chase the percentage. Raising the floor to just under the current
value is the whole change; no new tests are owed to this item.

**The largest reachable gaps, for the record and for future rounds** (this
round fixes none of them; recording them is the deliverable):

| Class | Coverage | Uncovered lines |
| --- | --- | --- |
| `Services.VoiceProviderRegistry` | 0.0% | 112 |
| `Voice.WindowsAudioCapture/Session` | 0.0% | 145 |
| `Services.SystemInfoService` | 33.1% | 101 |
| `Services.LocalAiSetupService` | 43.5% | 109 |
| `Rag.Chunking.ParagraphChunker` | 58.0% | 133 |
| `ViewModels.MainWindowViewModel` | 58.5% | 168 |
| `ViewModels.ServerProcessViewModel` | 64.8% | 250 |
| `Services.LlamaServerSetupService` | 65.0% | 100 |
| `ViewModels.AgentViewModel` | 74.7% | 138 |
| `ViewModels.ChatViewModel` | 84.9% | 108 |

`Services.VoiceProviderRegistry` at 0% is worth a sentence: doc 01's voice
picker reads from it, `DoctorService` reads from it, and no test has ever
executed a line of it. It is the most surprising entry in this table and
the strongest candidate for the next round's coverage work.

Add this table to `docs/review/deferred.md` as a new open row ("Coverage
gaps named in r29 doc 04 4.6") so it does not have to be rediscovered.

### 4.7 Write down what the suite is

Add a short `## Test suite` section to `docs/features.md` (or a new
`docs/testing.md` if it runs long, linked from CLAUDE.md's build/test
section) recording: the test count, that the suite runs sequentially and
why, the platform-skip attribute from 4.3 and when to use it, the coverage
floor and how to run the ratchet, and the fact that Windows CI is slower
than Linux CI for runner-I/O reasons rather than code reasons.

This is the durable output of the audit. The next person to ask "is the
test suite healthy" should find an answer instead of repeating this work.

## What this doc explicitly does not do

- **No parallelization.** r28's standing rule holds: the suite stays
  sequential and `XunitHarnessTests.cs:4` is not deleted. The measurement
  removed the reason to consider it. 1172 tests already finish in under
  10ms; parallelism would win nothing on them, and the expensive tests are
  expensive because of shared filesystem and SQLite state, which is exactly
  what parallelism would break.
- **No sharding of the CI matrix.** Not until 4.1's experiment reports.
- **No new tests for coverage's sake.** 4.6 records gaps; it does not fill
  them.
