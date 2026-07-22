# 01. Solution, projects, namespaces, build plumbing

The mechanical core of the rename. Do this doc first, in one continuous
pass, and get the build green before touching doc 02. Everything here is
low-judgement but high-blast-radius; the danger is a missed literal that
only fails at runtime (avares URIs, embedded resources), so the acceptance
criteria lean on greps and the doc 04 guard test, not on eyeballing.

## 1.1 Project directories, csproj files, solution

Eleven projects: Core, Services, Rag, Agent, ViewModels, Desktop, Tests,
Composition, Mcp, Voice, LocalApi.

- `git mv src/Aether.X src/Hermaeus.X` for each project directory, and
  `git mv` each `Aether.X.csproj` to `Hermaeus.X.csproj` (two-step moves
  are fine on Windows's case-insensitive filesystem; verify `git status`
  shows renames, not delete+add pairs).
- `git mv Aether.sln Hermaeus.sln`; edit project names and paths inside it.
  Keep the existing project GUIDs unchanged.
- Update all `<ProjectReference>` paths in every csproj.
- `src/Aether.Desktop/Aether.Desktop.csproj` and the Agent csproj carry
  special items: the Agent scenarios `<None Include="Scenarios\**\*">` +
  `<Compile Remove="Scenarios\**\*.cs" />` pair, and the Voice csproj's
  embedded `Assets/cmudict.txt.gz`. Preserve them; they are path-relative
  and survive the rename, but verify scenario files still flow into Tests
  and Desktop output after the move.

**Acceptance:**
- `dotnet build Hermaeus.sln` zero warnings from a clean tree (delete all
  `bin/`/`obj/` first; stale obj caches from renamed projects cause
  baffling duplicate-assembly errors).
- `git log --follow src/Hermaeus.Core/Models/AppSettings.cs` shows
  pre-rename history (rename detection intact).
- Agent scenario fixtures present under Tests and Desktop output dirs.

## 1.2 Namespaces and usings

Every `.cs` file: `namespace Aether.X` becomes `namespace Hermaeus.X`,
`using Aether.X` becomes `using Hermaeus.X`, plus fully-qualified
references in code and in nameof/typeof/doc-comment strings. This is
script-friendly (word-boundary replace of `Aether.` in .cs/.axaml/.csproj
content), but the following are NOT plain namespace references and need
individual attention, in doc 01 or a later doc as marked:

- `src/Aether.Composition/AetherServiceRegistration.cs`: rename class and
  file to `HermaeusServiceRegistration` and fix all call sites.
- `src/Aether.Tests/ArchitectureTests.cs` (:29, :50, :57-94): assembly and
  namespace name strings drive real reflection checks; update the strings
  AND confirm each check still trips when violated (temporarily break one
  reference to prove the test is still live, then revert).
- xml doc comments and interface-contract comment strings that name
  `Aether.Core.Models.*` etc. (e.g. `AgentInterfaces.cs:100-123`,
  `AgentScenarioModels.cs:5-54`, `AgentModels.cs:124-189`,
  `Memory.cs:133`): sweep them in the same pass so the doc 04 guard does
  not have to allowlist comments.
- `.axaml` files: `xmlns` clr-namespace declarations and `x:Class`
  attributes all carry `Aether.`; the XAML compiler fails loudly on
  these, so the build is the check.

**Acceptance:**
- `grep -rw "Aether" src --include=*.cs --include=*.axaml --include=*.csproj`
  returns only hits scheduled for docs 02/03 (string literals, asset
  filenames); zero namespace/using/xmlns hits.
- Build and full test suite green.

## 1.3 Directory.Build.props and packaging metadata

- `Directory.Build.props:9` `<Product>Aether</Product>` becomes `Hermaeus`.
- `:13-14` RepositoryUrl/PackageProjectUrl become
  `https://github.com/MortisDei/hermaeus` (the owner renames the GitHub
  repo in doc 04; GitHub redirects the old slug in the interim, so this
  ordering is safe).
- Version stays untouched here; doc 05 owns the bump.

## 1.4 avares:// URIs and embedded resource names

These break silently at runtime when the assembly name changes:

- Every `avares://Aether.Desktop/...` reference (App.axaml styles,
  `DesktopIntegrationService.cs:138` tray icon, any others the grep finds)
  must become `avares://Hermaeus.Desktop/...`.
- `src/Aether.Voice/CmuPronouncingDictionary.cs:17` loads manifest resource
  `"Aether.Voice.Assets.cmudict.txt.gz"`. The manifest name follows the
  root namespace, so it becomes `"Hermaeus.Voice.Assets.cmudict.txt.gz"`.
  There is an existing test that exercises CMUdict lookups; run it and
  also launch-check that the resource loads (a wrong name here returns
  null stream, and the failure mode is silent bad pronunciation, not an
  exception, depending on the call site's null handling; verify which).

**Acceptance:**
- App launches (`dotnet run --project src/Hermaeus.Desktop`), tray icon
  renders, no XAML resource errors in the runtime log.
- CMUdict-dependent voice tests pass (they fail hard if the embedded
  resource name is wrong; confirm by temporarily misspelling it once).

## 1.5 Build scripts, CI, coverage

- `build.sh` / `build.ps1`: project paths, output zip names (any
  `aether-*` artifact naming becomes `hermaeus-*`), sln filename.
- `.github/workflows/ci.yml`: sln/test project paths.
- `scripts/coverage.sh` / `scripts/coverage.ps1`: test project path.
- `.github/ISSUE_TEMPLATE/*.yml` and `config.yml`: any product-name text
  (the copy edits themselves are doc 03; path/slug references are here).

**Acceptance:**
- `pwsh ./build.ps1 -SkipRestore` completes and produces `hermaeus-*`
  artifacts.
- CI workflow file references only paths that exist post-rename (CI green
  is confirmed by the owner after push, doc 04).

## 1.6 Tests project internals

`src/Aether.Tests` has name-sensitive fixtures beyond namespaces:

- `Helpers.cs:143` temp roots `aether-tests-*` become `hermaeus-tests-*`.
- `Program.cs:302` fixture `"Aether.Sample.sln"`, `:337` `.aether`
  manifest assertions (pairs with doc 02's workspace-manifest item; keep
  the test and the production constant in the same commit).
- `ServiceTests.cs:2724-2789`: `LocalAiSetupScriptGenerator` expectations
  reference `Aether.LocalApi.exe`, `Aether.sln`, `Aether.Desktop` paths.
  The generator's emitted scripts must produce the new names; update
  generator and tests together.
- `AcceptanceTests.cs:78` `aether-test-logs`, `OrphanServerDetectorTests`
  and `ServerProcessViewModelOrphanTests` fake paths `C:\aether\...`,
  `ServerProcessStopLoggingTests.cs:29-30` fake binary names: rename for
  consistency so the doc 04 guard needs no test-file allowlist.

**Acceptance:** full suite green; no `aether` (case-insensitive) hits left
in `src/Hermaeus.Tests` except any the guard test itself deliberately
contains.
