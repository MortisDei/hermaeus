# Avalonia Upgrade Playbook

Avalonia is the load-bearing dependency of the whole product thesis (native,
non-WebView desktop UI). Version bumps are deliberate, tested events, not
routine package updates. See docs/review/archived/r1/02-dependency-review.md
for the containment rationale.

## Current pin

All five Avalonia packages (`Avalonia`, `Avalonia.Desktop`,
`Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.AvaloniaEdit`) are
pinned to the same exact version in `src/Aether.Desktop/Aether.Desktop.csproj`
(no wildcard, no floating minor). They must always move together.

## When to upgrade

- A patch release (`11.3.x`) fixing a bug Aether actually hits: low risk, can
  land in a normal PR with the checklist below.
- A minor release (`11.x`): review the Avalonia changelog for styling/theming
  breaking changes before touching the pin; budget a dedicated PR.
- A major release (`1x.0`): treat as a project, not a PR. Expect
  styling-system churn (10 to 11 was painful project-wide); do not combine
  with unrelated feature work.

## Checklist for any pin bump

1. Bump all five `Avalonia*` package versions in `Aether.Desktop.csproj` to
   the same new version in one commit. Never let them drift apart.
2. `dotnet build Aether.sln -v q --nologo` clean, zero warnings
   (`TreatWarningsAsErrors` is on solution-wide).
3. `dotnet test tests/Aether.Tests/Aether.Tests.csproj -v q --nologo` green.
4. Manually exercise, on both Windows and Linux if possible: chat rendering
   (virtualized long conversations, fenced code blocks via AvaloniaEdit),
   theme switching (Fluent light/dark), the tray icon and hotkeys (Windows;
   DBus tray on Linux), and window chrome/resizing.
5. Check `Directory.Build.props`'s `TreatWarningsAsErrors` catches any new
   analyzer warnings Avalonia's own analyzers introduce.
6. Update `CHANGELOG.md` noting the version bump and anything visibly
   changed (theme tweaks, control behavior).

## What NOT to do

Do not bump Avalonia as a drive-by inside an unrelated feature PR. Do not let
the five packages diverge in version. Do not skip the manual pass: XAML
styling regressions do not reliably show up as build or test failures.
