---
name: build-and-verify
description: The current Hermaeus build, sequential test, coverage, packaging, process-audit, and owner GUI verification workflow.
---

# Build and verify Hermaeus

Use the environment that can satisfy the command. The restricted runner may
not access NuGet configuration, `%LOCALAPPDATA%`, Avalonia BuildServices, or
MSBuild/VSTest IPC. If that limitation is known, run the required command on
the approved Windows host instead of making a doomed restricted attempt.

## Normal verification

```powershell
dotnet build Hermaeus.sln
dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj `
  --results-directory "$env:TEMP\hermaeus-tests"
dotnet build Hermaeus.sln -c Release
```

The solution treats warnings as errors. Tests are standard xunit and are
deliberately sequential because they share temporary data roots and SQLite
connection pools. Do not re-enable parallelization. Use `--no-restore` only
after restore/build assets are known to exist. New harness-style tests must be
registered in `XunitHarnessTests.HarnessCases`; the reflection guard fails
otherwise. Platform-specific tests use `WindowsOnlyFact`, never an early
return that reports Passed.

Keep test results and coverage outside the repository. Never use
`git add -A`; inspect untracked files before staging. A runner that reports
completion while a VSTest host remains alive needs a process/result-directory
check before the run is considered complete. A persistent C# Dev Kit or
MSBuild/VSTest service host can be legitimate reusable infrastructure and
must not be force-killed merely because it contains vstest paths.

## Coverage gate

Coverage is the final automated precommit gate and runs once, after focused
tests, the full sequential suite, Debug and Release builds, and the diff check
needed before it. Use the repository script:

```powershell
pwsh ./scripts/coverage.ps1
```

The current line-coverage floor is 60%, matching `AGENTS.md`,
`docs/testing.md`, `scripts/coverage.ps1`, and `scripts/coverage.sh`. The
script uses `--no-restore` and writes reports beneath the operating system
temporary directory. Do not chase the percentage with low-value tests. After
coverage, inspect `git status --untracked-files=all` again and prove that no
report or test output entered the checkout.

## Manual desktop validation

For AXAML, binding, lifecycle, tooltip, theme, or interaction changes, source
tests are not GUI proof. Launch the desktop app only when the owner has placed
that real session in scope. It reads and writes the owner's real
`%LOCALAPPDATA%\Hermaeus\settings.json` on Windows or
`~/.local/share/Hermaeus` on Linux, so look before changing settings and close
the app cleanly. Never use `taskkill /F`. Exercise the changed view, inspect
console binding warnings, and check both themes when relevant. Report owner
live validation as a separate gate. A screenshot or unavailable computer-use
surface is evidence, not a live pass.

## Packaging and release boundaries

Packaging is `pwsh ./build.ps1 -SkipRestore -Runtime win-x64` on Windows or
`./build.sh --skip-restore` on Linux only after the exact-RID restore assets
exist. Releases are tag-driven and owner-controlled. Do not create or push
version tags, releases, or repository settings changes. Commit only after
explicit owner authorization, with one coherent Conventional Commit and no
AI co-author trailer.
