# 03. Docs, branding assets, and user-facing copy

Text-only, but it is the layer the public actually reads. The rule for
history: anything that documents a shipped release keeps its original
wording; anything a first visitor reads as current truth says Hermaeus.

## 3.1 User-facing UI copy sweep

Grep-driven over `src/`: every string a user can see that says Aether.
Known sites (not exhaustive; the doc 04 guard is the completeness check):
`MainWindowViewModel.cs:81` window titles, `DesktopIntegrationService.cs:132,201,219`
tray tooltip and menu items, `DoctorViewModel.cs:158,163` toast titles,
`SetupWizardView.axaml.cs:24` / `SettingsView.axaml.cs:28,50,52` folder and
backup picker titles, `SettingsViewModel.cs:243,257` / `SetupWizardViewModel.cs:464`
toasts, `DataManagementSettingsViewModel.cs:128`, `ModelManagementViewModel.cs:187`,
`TtsSettingsViewModel.cs:109`, `LocalAiSetupSettingsViewModel.cs:219`,
`LocalAiSetupModels.cs:61`, the Doctor/setup narrative strings in
`DoctorService.Runtime.cs`, `DoctorService.Rag.cs`, `DoctorService.Startup.cs:79,85`,
`LocalAiSetupService.cs` (:55,125,164,175,515,562), `BenchmarkService.cs:990`
report label, `AgentViewModel.cs:1368`, `LocalApi/Program.cs:22`.

**Acceptance:** launch the app and eyeball the main window title, tray
menu, wizard, Settings, and Doctor; no "Aether" visible anywhere.

## 3.2 Branding assets

- `src/Aether.Desktop/Assets/`: `aether-app.png`, `aether-tray.png`,
  `aether-tray-dark.png`, `aether-tray-light.png`, `aether.ico` rename to
  `hermaeus-*` via `git mv`; update every reference (csproj items, App
  icon, `DesktopIntegrationService.cs:138`, window Icon attributes).
- `docs/aether-branding.png` renames to `hermaeus-branding.png`; fix the
  README reference.
- The artwork itself is unchanged. Producing new Hermaeus-themed art is
  explicitly out of scope (doc 05); the owner may replace the images later
  without code changes since only filenames are referenced.
- Moss (docs/mascot.md, MossIcon control) stays exactly as shipped; only
  the four product-name mentions in mascot.md change.

## 3.3 Root-level docs

- `README.md` (26 hits): product name throughout, clone URL
  `MortisDei/hermaeus`, and note the Quick Start `cd` target becomes
  `hermaeus` (the go-public pass fixed the case bug; do not regress it).
- `AGENTS.md` / `CLAUDE.md`: opening description and any Aether-specific
  wording, including the data-root convention line, which now names
  `Hermaeus` paths.
- `CONTRIBUTING.md`, `SECURITY.md`, `COMMERCIAL.md`, `NOTICE.md`,
  `LICENSE.md`: product references change; the copyright holder
  (`MortisDei`) and license terms do not.
- `CHANGELOG.md`: add the 0.25.0-alpha entry describing the rename (why,
  what breaks: data-root default, LocalApi headers, log filenames, secret
  service names on Linux/macOS). Existing entries are untouched; they
  describe Aether releases and remain correct as history.

## 3.4 docs/ tree sweep

Update: `features.md`, `rag.md`, `agent.md`, `voice.md`, `benchmarks.md`,
`packaging.md`, `security-review.md` (add a short r20 subsection: rename,
no new attack surface, outbound identity strings changed),
`rag-eval-harness.md`, `avalonia-upgrade.md`, `mascot.md`, anything under
`docs/schemas/`.

Exempt (historical record, never touch): `docs/changelog-archive.md`,
`docs/review/archived/**`. `docs/temp/` is gitignored and owner-personal;
leave it alone.

## 3.5 Repo tooling text

- `.claude/skills/*/SKILL.md` (add-a-feature, build-and-verify,
  security-posture, storage-and-data-root): they instruct future agents
  about this repo and must name Hermaeus paths/projects, including the
  data-root example paths in storage-and-data-root.
- `.github/ISSUE_TEMPLATE/bug_report.yml`, `feature_request.yml`,
  `config.yml`: product name in labels/descriptions.
- `.github/workflows/ci.yml`: workflow display name if it says Aether.

**Acceptance for the doc:** case-insensitive grep for `aether` over the
repo (excluding `src/`, covered by docs 01-02) hits only: CHANGELOG
historical entries, `docs/changelog-archive.md`, `docs/review/**`, and
`.git` internals. This is exactly the doc 04 guard's allowlist; if the
grep and the guard disagree, fix the guard.
