---
name: build-and-verify
description: How to build, test, and run Aether correctly, including the custom test runner and zero-warning policy. Use before completing any code change in this repo.
---

# Build and verify Aether

## Commands

```bash
dotnet build Aether.sln
dotnet test tests/Aether.Tests/Aether.Tests.csproj
dotnet run --project src/Aether.Desktop        # manual/visual verification
```

## Critical facts

- **Use `dotnet test`.** The suite is xunit (`XunitHarnessTests.cs` wraps the
  harness case lists). Parallelization is disabled at the assembly level
  because cases share temp data roots and SQLite pools; do not re-enable it.
  `dotnet run` on the test project is a silent no-op (there is no Main).
- **`TreatWarningsAsErrors` is on solution-wide** (`Directory.Build.props`).
  Any warning fails the build. Fix root causes; never blanket-suppress.
- Tests are integration-flavoured and touch temp data roots; they must not
  require a running llama-server or network access. If a test you add needs a
  model or server, gate it or fake the service.
- Packaging (only when release-relevant): `./build.sh` (Linux) or
  `pwsh ./build.ps1` (Windows), output under `dist/`.

## Verification expectations

For UI-affecting changes, launch the app and exercise the changed view; the
test harness does not cover AXAML bindings. Binding errors appear in the
console at runtime — check for `Avalonia` binding warnings there.

For storage/schema changes, run the data-safety tests
(`BackupMigrationTests`, migration-related cases in `ServiceTests`) and
verify a fresh data root initializes cleanly by launching with a temp
`%LOCALAPPDATA%\Aether` / `~/.local/share/Aether` moved aside.
