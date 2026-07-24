---
name: build-and-verify
description: How to build, test, and run Hermaeus correctly, including the custom test runner, zero-warning policy, coverage ratchet, and safe manual app runs. Use before completing any code change in this repo.
---

# Build and verify Hermaeus

## Commands

```bash
dotnet build Hermaeus.sln
dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj
./scripts/coverage.sh          # or: pwsh ./scripts/coverage.ps1  (line-coverage ratchet, floor 45%)
dotnet run --project src/Hermaeus.Desktop        # manual/visual verification; see warning below
```

## Critical facts

- **Use `dotnet test`.** The suite is xunit (`XunitHarnessTests.cs` wraps the
  harness case lists). Parallelization is disabled at the assembly level
  because cases share temp data roots and SQLite pools; do not re-enable it.
  `dotnet run` on the test project is a silent no-op (there is no Main).
- **New harness-style test methods must be registered** in
  `XunitHarnessTests.HarnessCases`; the `HarnessRegistrationGuardTests`
  reflection guard fails the suite otherwise.
- **`TreatWarningsAsErrors` is on solution-wide** (`Directory.Build.props`).
  Any warning fails the build. Fix root causes; never blanket-suppress.
- Tests are integration-flavoured and touch temp data roots; they must not
  require a running llama-server or network access. If a test you add needs a
  model or server, gate it or fake the service.
- Packaging: `./build.sh` (Linux) or `pwsh ./build.ps1` (Windows), output
  under `dist/`. Releases are tag-driven: pushing a `vX.Y.Z-alpha` tag runs
  `.github/workflows/release.yml`, which packages and publishes a GitHub
  Release with changelog-derived notes. Never push a version tag yourself;
  that is an owner action.

## Manual app runs share real state

`dotnet run --project src/Hermaeus.Desktop` reads and writes the SAME
`%LOCALAPPDATA%\Hermaeus\settings.json` (Linux: `~/.local/share/Hermaeus`)
as the owner's installed app. Look, do not resave settings casually, and
never kill the process with `taskkill /F`; close it cleanly.

## Verification expectations

For UI-affecting changes, launch the app and exercise the changed view; the
test harness does not cover AXAML bindings. Binding errors appear in the
console at runtime; check for `Avalonia` binding warnings there. Theme-
sensitive changes (tooltips, empty states, dim text) need a look in both
light and dark themes.

For storage/schema changes, run the data-safety tests
(`BackupMigrationTests`, migration-related cases in `ServiceTests`) and
verify a fresh data root initializes cleanly by launching with a temp
`%LOCALAPPDATA%\Hermaeus` / `~/.local/share/Hermaeus` moved aside.
