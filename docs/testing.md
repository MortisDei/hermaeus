# The test suite

This is the current test execution, coverage, and guard-test contract. Dated
timing and coverage measurements are retained as evidence, not as permanent
test-count or performance promises.

## Shape

At the r29 measurement, the suite had around 1790 tests across ~200 files, all
in `src/Hermaeus.Tests`. This is historical sizing information, not a permanent
test-count contract. Standard xunit; no separate integration project and no
test categories.

```bash
dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj
```

Most tests are fast. On Windows, at the r28 measurement, 1172 of 1754 tests
finished in under 10ms and together accounted for 0.4% of the run. The cost
lives in the ~135 tests that take over a second, and most of that is
filesystem and SQLite work rather than test logic.

## The suite runs sequentially, deliberately

`XunitHarnessTests.cs` disables xunit's parallelization and that is not an
oversight. Tests share temp data roots and SQLite connection pools, so
parallelism would break them rather than speed them up. The r29 measurement
also removed the performance argument for it: the cheap tests are already
free, and the expensive ones are expensive precisely because of the shared
filesystem and SQLite state that parallelism would corrupt.

Do not re-enable parallelization.

## Run the suite in the real terminal environment

The test harness deliberately uses shared temporary data roots and SQLite
connection pools, but a restricted command runner can also be unable to reach
the real Windows application-data root or NuGet's user configuration. Errors
such as `UnauthorizedAccessException` under `%LOCALAPPDATA%\Hermaeus` or
`SQLite Error 14: unable to open database file` are therefore not evidence of
test leakage or a product regression until the documented command has been
reproduced in a normal VS Code or PowerShell terminal.

Do not change `src/Hermaeus.Tests/Helpers.cs`, production data-root logic, or
SQLite setup merely to make a restricted runner pass. Run the same command in
the normal terminal, keep results under `%TEMP%`, and diagnose a genuine
failure only from that run. The canonical Windows verification is:

```powershell
dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj `
  --results-directory "$env:TEMP\hermaeus-tests"
```

## Never write test output into the working tree

A `.trx` header contains `runUser="MACHINE\user"` and a run name of
`user@machine`. A coverage report contains absolute local paths. Both are
personal identifiers, and this repository is going public.

`dotnet test` writes to `src/Hermaeus.Tests/TestResults/` by default the moment
`--logger` or `--collect` is passed without `--results-directory`. Pass one:

```bash
dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj \
  --logger "trx;LogFileName=test-results.trx" \
  --results-directory /tmp/hermaeus-test-results
```

`.gitignore` catches the mistake, but the habit is the real fix. Check
`git status --untracked-files=all` before every `git add`, and never
`git add -A` from the repository root.

Some restricted command wrappers report after build output while a VSTest host
continues to run. Before treating that as a terminated suite, check for the
test-host process and the requested TRX outside the repository. This is a
runner-reporting boundary, not a product failure and not a reason to weaken
tests.

## Platform-specific tests report Skipped, not Passed

Use `[WindowsOnlyFact]` (`src/Hermaeus.Tests/WindowsOnlyFactAttribute.cs`) for
a test whose subject is genuinely Windows-only: a Windows API, an `.exe`, a job
object. It is a subclass of xunit's own `FactAttribute` that sets `Skip` at
discovery, so it needs no package.

Do **not** write this:

```csharp
[Fact]
public void Something()
{
    if (!OperatingSystem.IsWindows()) return;   // reports Passed on Linux
    ...
}
```

Sixteen tests did exactly that until r29, so the Linux CI leg recorded a green
tick for llama-server installation, Python health validation, job-object
assignment and manifest behaviour it never executed, and both legs reported
`Skipped: 0`. `PlatformSkipHonestyGuardTests` now fails the suite on that
shape.

Branching to platform-appropriate assertions **inside** a test that asserts on
both platforms is correct and is left alone; `BackupMigrationTests` and
`ServiceTests` do this.

## Do not prove a timeout by waiting out the timeout

If a test needs a component to give up on a hanging dependency, make the
timeout injectable (a constructor parameter defaulting to today's value, so
production behaviour is unchanged) and pass a short one. `MemoryStore`'s
`queryEmbedTimeout` and `RecallService`'s `sourceTimeout` are the pattern.

Three tests used to pay a real three seconds each, on every run, on both CI
legs, to assert something that takes 50ms to assert with an injected timeout.

Equally: if a test is slow because the code it tests waits, fix the wait, not
the test. r29's four slowest Windows tests were slow because
`ServerProcessManager`'s health poll ran its HTTP probe out to a 2 s timeout
and then slept a 600 ms interval before reporting a process that had already
exited. Racing both against process exit fixed a real product defect and took
13.1s off the Windows leg as a side effect.

For asynchronous queue ownership, use explicit test signals instead of sleeps:
the controlled provider in `VoiceOrchestratorTests` exposes start, completion,
failure, and cancellation boundaries, so assertions follow the event that
matters rather than elapsed time. It replaced fifteen `Task.Delay` calls in
r30; do not reintroduce polling or fixed delays for this behavior.

## Coverage

```bash
./scripts/coverage.sh          # or: pwsh ./scripts/coverage.ps1
```

The coverage scripts assume the solution has already been restored and built,
then run tests with `--no-restore`. Reports are written beneath the operating
system temporary directory so coverage artifacts never enter the checkout.
CI keeps the required restore and zero-warning build steps before its test
workflow. The Ubuntu CI leg runs that same test command under the runner's
Xvfb virtual display because the suite includes an Avalonia bitmap
render-boundary assertion; this exercises the Linux Avalonia path without
requiring a physical desktop session. The product still uses its normal
platform-detected renderer.

Floor: **60%** line coverage, set in `scripts/coverage.sh`,
`scripts/coverage.ps1` and stated in `AGENTS.md`. All four must agree.

Measured at r29: **61.6%** overall (29,438 / 47,807 lines). Excluding
`Hermaeus.Desktop`, which is views and compiled XAML and is not meaningfully
unit-testable, the suite covers **73.0%** of the code it can reach.

| Project | Line coverage |
| --- | --- |
| `Hermaeus.Composition` | 100.0% |
| `Hermaeus.Agent` | 91.3% |
| `Hermaeus.Core` | 90.7% |
| `Hermaeus.LocalApi` | 88.1% |
| `Hermaeus.Rag` | 79.9% |
| `Hermaeus.Services` | 71.2% |
| `Hermaeus.Mcp` | 68.6% |
| `Hermaeus.Voice` | 65.1% |
| `Hermaeus.ViewModels` | 62.9% |
| `Hermaeus.Desktop` | 0.9% |

The floor exists to catch a regression, not to be chased. Do not add tests to
raise the number. The known gaps are recorded in `docs/review/deferred.md`.

## Windows CI is slower than Linux CI, and it is not the tests

At r28's measurement the Linux leg summed 59.9s of test time and the Windows
leg 501.4s, for the same 1754 tests. The cause is hosted-runner filesystem
cost, not test code: the decisive comparison is CI Windows against **local**
Windows, where the same test that takes 4,409ms on the runner takes 164ms on a
developer machine. A full local run in Debug with coverage instrumentation
attached beat CI Windows in Release with none.

So: do not rewrite tests to make that number smaller. It would trade real
coverage for a faster clock.

**r29 acted on that and it worked.** Excluding the test working set from
Defender on the Windows leg, and pointing the test temp root at `RUNNER_TEMP`
so the exclusion actually covers it, took the Windows summed test duration from
**501.4s to 72.3s** (run 30681255153), and job wall time from 13m06s to 4m33s.
About 23s of that is r29's own test and product fixes; the rest, roughly 400
seconds, is the scanner. The runner's filesystem cost was the dominant term and
it was not a floor.

## Guard tests

Several tests exist only to stop a class of mistake recurring. They are
deliberately dumb, usually one scan over the source, because a guard that needs
maintenance is a guard that gets deleted the first time it is inconvenient.

| Guard | Stops |
| --- | --- |
| `HarnessRegistrationGuardTests` | A harness-style test method not registered in `XunitHarnessTests.HarnessCases`, so it never runs |
| `PlatformSkipHonestyGuardTests` | A `[Fact]` that returns early on the wrong platform and reports Passed |
| `BindingExpressionGuardTests` | An axaml binding that casts to a prefixed type, which crashes the app on the layout pass |
| `DocsCoverageGuardTests` | `docs/features.md` drifting from the real panel list, and `README.md` stating the wrong version |
| `ServiceTests.IconOnlyControlsHaveTooltips` | An icon-only control with no tooltip |
| Settings section guard | `AppSettings` and the documented settings-section list disagreeing in either direction |
| Naming guard | The pre-rename product name reappearing |
