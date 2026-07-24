# Review round 22: Fit for public view

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 04 is the roadmap and sequencing contract.

## Why this round exists

The repository is about to go public (docs/temp/, r11 audit pack). Of the
credibility items that gate a good first impression, two are still open and
both are product work, not owner-only work: the app has to look finished in
screenshots, and there has to be a real GitHub Release a stranger can
download. Meanwhile the owner has been daily-driving the app and came back
with concrete polish findings:

- **Moss earns his keep but is nearly invisible.** The mascot appears in
  exactly two places (RagView ingest progress, ServicesView error banner).
  The owner reports the error-banner Moss was genuinely useful during a
  real failure, and that the accompanying text rendered dark grey in dark
  mode and was hard to read. `docs/mascot.md` explicitly sanctions empty
  states, onboarding, and tips as Moss territory; none of them use him.
- **Tooltip coverage is uneven.** 240 `ToolTip.Tip` instances across 25
  axaml files, but the distribution is lopsided: ModelManagementView has
  33 while SettingsTrustSectionView and SettingsVoiceSectionView have 1
  each, SystemOverviewView has 2, and every dialog has 0. There is no
  guard, so coverage decays with every new control.
- **No release pipeline exists.** build.ps1/build.sh produce zip/tar.gz
  plus .sha256 under dist/, and have never published anything. The owner's
  decision: from this round forward, every minor version is tagged and
  released on GitHub; patch versions only for urgent hotfixes.

This round makes the app presentable (Moss presence, readability, tooltip
completeness) and makes shipping it a repeatable one-command act (tag-driven
GitHub Releases). It is the product half of flip readiness; screenshots and
the flip itself stay owner-only.

## Documents

| Doc | Theme |
| --- | --- |
| `01-moss-presence.md` | A shared Moss empty-state control; Moss in chat/agent/benchmark/memories/RAG empty states and the setup wizard; voice rules |
| `02-tooltips-and-readability.md` | Fix the dark-mode readability bug at the root; branded theme-aware tooltips; text-opacity floor; full tooltip coverage sweep plus a guard test |
| `03-release-pipeline.md` | Tag-driven GitHub Release workflow using the existing packaging scripts; versioning and tagging policy; changelog-derived release notes |
| `04-roadmap.md` | Ships as 0.29.0-alpha and becomes the first tagged release; sequencing, test budget, explicit rejections |

## Standing rules for the implementing agent

- Verify before implementing. File:line references were exact at spec time
  (tree at 2bd081b, v0.28.0-alpha); re-verify before editing.
- No em dashes anywhere. Zero-warning build. All tests pass. Register any
  new harness-style test methods in `XunitHarnessTests.HarnessCases`; the
  `HarnessRegistrationGuardTests` reflection guard fails otherwise.
- All Moss-attributed copy follows `docs/mascot.md` "Voice in UI copy".
  When in doubt, drop the personality and state the fact.
- No new NuGet packages. Everything in this round is achievable with
  Avalonia shapes, System.Xml.Linq, and shell scripts.
- The release workflow may use only `actions/*` first-party GitHub
  actions, pinned the same way `.github/workflows/ci.yml` pins its
  actions. No marketplace actions.
- Update `docs/features.md`, `docs/packaging.md`, `docs/mascot.md`
  (Current in-app usage section), and `CHANGELOG.md`. Do not document
  planned behaviour as existing.
