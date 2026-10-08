# Avalonia Upgrade Playbook

Avalonia is the load-bearing dependency of the whole product thesis (native,
non-WebView desktop UI). Version bumps are deliberate, tested events, not
routine package updates. See docs/review/archived/r1/02-dependency-review.md
for the containment rationale.

## Current pin

The Avalonia framework packages (`Avalonia`, `Avalonia.Desktop`,
`Avalonia.Themes.Fluent`, and `Avalonia.Fonts.Inter`) are pinned to the same
exact `12.1.3` version in `src/Hermaeus.Desktop/Hermaeus.Desktop.csproj`.
`Avalonia.AvaloniaEdit` is pinned to its latest published `12.0.0` release
(NuGet checked 2026-10-06) because
it remains on a separate package line. Its package dependency targets Avalonia
12.0.0 or newer, so restore resolves it against the 12.1.3 framework. This is the only
intentional package-family version difference and must be checked whenever the
framework moves.

## R33 patch update

R33 consolidates Dependabot PRs #17, #18 and #19 into one coherent framework
update from 12.1.2 to 12.1.3. Each PR updates the base package and only one
companion; applying the complete set together preserves the framework pin.
No package reference is added, and AvaloniaEdit remains at 12.0.0.

The [upstream release](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.3)
includes tray attachment, theme-switch crash, text layout, shutdown and Linux
tray fixes relevant to Hermaeus's desktop surfaces. These release notes are
not evidence that Hermaeus's historical native crash or COSMIC editor flicker
is fixed. The tooltip workaround and reflection-binding default remain in
place. Packaged theme, tray, rendering and lifecycle checks remain required
before release, including Linux/COSMIC owner validation.

## R32 migration record

The R32 closeout moves the four framework packages to 12.1.2, keeps
AvaloniaEdit at 12.0.0, and retains the direct `Tmds.DBus.Protocol` reference
at 0.94.1. Avalonia 12 enables
compiled bindings by default, so the Desktop project explicitly retains the
existing reflection-binding default until views are migrated to explicit data
types. The migration also replaced the obsolete clipboard and drag/drop APIs
with Avalonia 12's typed transfer APIs, and replaced the obsolete `Watermark`
property with `PlaceholderText` without changing the displayed prompts.

The built-in tooltip service remains disabled. Upstream issue #19218 is still
open, and this migration records no assumption that Avalonia 12.1.2 fixes that
feedback loop. Owner validation of tooltips, theme switching, window behavior,
tray integration, and Windows runtime behavior remains required.

## When to upgrade

- A patch release (`12.1.x`) fixing a bug Hermaeus actually hits: review the
  framework and satellite package compatibility before touching the pin.
- A minor release (`12.x`): review the Avalonia changelog for
  styling/theming breaking changes before touching the pin; budget a dedicated
  migration.
- A major release (`1x.0`): treat as a project, not a routine dependency PR.
  Expect styling-system and platform API churn.

## Checklist for any pin bump

1. Bump the four framework package versions together in
   `Hermaeus.Desktop.csproj`, then verify that the pinned AvaloniaEdit release
   supports that framework major and minor. Never introduce an unverified
   package-family split.
2. `dotnet build Hermaeus.sln -v q --nologo` clean, zero warnings
   (`TreatWarningsAsErrors` is on solution-wide).
3. `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj -v q --nologo` green.
4. Manually exercise, on both Windows and Linux if possible: chat rendering
   (virtualized long conversations, fenced code blocks via AvaloniaEdit),
   theme switching (Fluent light/dark), the tray icon and hotkeys (Windows;
   DBus tray on Linux), and window chrome/resizing.
5. Check `Directory.Build.props`'s `TreatWarningsAsErrors` catches any new
   analyzer warnings Avalonia's own analyzers introduce.
6. Exercise startup ordering, second-launch rejection, tray integration, and
   the strict lock invariant before claiming the migration is complete.
7. Update `CHANGELOG.md` noting the version bump and anything visibly changed
   (theme tweaks, control behavior).

## What NOT to do

Do not bump Avalonia as a drive-by inside an unrelated feature PR. Do not let
the four framework packages diverge in version, and do not use an AvaloniaEdit
version without evidence that it supports the selected framework. Do not skip
the manual pass: XAML styling regressions do not reliably show up as build or
test failures.
