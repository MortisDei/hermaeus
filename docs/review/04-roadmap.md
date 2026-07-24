# 04. Roadmap and sequencing

## Version

Ships as **0.29.0-alpha** (`Directory.Build.props` only: VersionPrefix,
AssemblyVersion, FileVersion). Minor bump: user-visible presentation
changes across most views plus a new release process. Per the policy this
very round introduces (doc 03.1), **0.29.0-alpha becomes the first tagged
GitHub Release**; the owner pushes the tag after the round lands.

## Sequencing (strict)

1. **2.1** first: reproduce and fix the dark-mode readability bug. It is
   the one reported defect in this round; everything else is enhancement.
   Record in the final response exactly what the root cause was.
2. **2.3** (branded tooltip style) and **2.2** (text-dim classes): the
   styling substrate the rest of the round builds on.
3. **1.1** (MossEmptyState control), then **1.2 + 1.3 + 1.4** (empty
   states, wizard greeting, existing placements) consuming those classes.
4. **2.4** (tooltip coverage sweep) and **2.5** (icon-only guard test)
   together; the sweep should leave the guard green with an empty or
   near-empty allowlist.
5. **3.1-3.4** (release policy docs, release.yml, release-notes scripts,
   notes footer). This is independent of the UI work and may be built in
   parallel, but lands after it so the workflow's first real run releases
   the finished round.
6. Docs and close-out: `docs/features.md`, `docs/packaging.md`,
   `docs/mascot.md` (Current in-app usage), `CHANGELOG.md`. Archive this
   pack to `docs/review/archived/r22/`, commit (owner's standing pattern:
   implement in full, commit after build/tests/docs are truthful). No AI
   co-author trailer on the commit.
7. Owner-only afterwards: push `v0.29.0-alpha` tag, watch the workflow,
   smoke-test the artifacts (doc 03.5).

## Test estimate

Roughly 8 to 14 new tests from the current suite (1118+ as of r21):

- Icon-only tooltip guard scan plus its allowlist behaviour (1-2).
- Release-notes extraction: found, missing, latest, archived (3-4).
- Text-opacity floor scan if implemented as a guard (optional, 1): flag
  TextBlocks carrying `Opacity` below 0.4 in axaml; only add it if the
  sweep leaves the tree clean, otherwise it is a lie waiting to fail.
- MossEmptyState property/logic tests only if the control gains any
  code-behind logic worth testing; do not write render tests, the suite
  has no UI-automation harness and this round does not add one.

All new harness-style methods register in `XunitHarnessTests.HarnessCases`
(the `HarnessRegistrationGuardTests` reflection guard fails otherwise).
Tests stay sequential; do not re-enable parallelization. Nothing in this
round needs a live server or network.

## Practical warnings for the implementer

- Re-verify every file:line in this pack before editing; the tree may
  have moved since spec time (2bd081b).
- The em-dash scan covers all .cs/.axaml, and this round writes an
  unusual amount of UI copy and docs; keep em and en dashes out of
  tooltip strings, Moss lines, workflow YAML comments, and release-note
  templates alike.
- Moss copy is the easiest thing in this round to get wrong. Reread
  `docs/mascot.md` "Voice in UI copy" before writing each line; when a
  line feels fun, cut it.
- Verifying the tooltip style and empty states requires actually running
  the app in both themes. `dotnet run` shares the owner's real
  settings.json; look, do not resave settings casually, and never kill
  the process with taskkill /F.
- YAML in release.yml: quote the tag wherever it reaches a shell, keep
  `set -euo pipefail` (bash steps) so a failed extraction cannot publish
  an empty-notes release.
- The dialogs' zero tooltip count is mostly correct as-is (their body
  text explains the choice); resist padding them to make a number look
  better.

## Explicit rejections (do not do these)

- **No Moss animation, popups, timed tips, or tip engine.** Static
  presence in sanctioned locations only.
- **No Moss in the chat transcript.** He is not the AI; he must never
  appear to author or decorate a model response.
- **No MossIcon art redesign.** The woodland-goblin update needs fresh
  illustration and is tracked in `docs/mascot.md` as future work.
- **No theme system overhaul.** Two text classes and one tooltip style,
  not a design-token framework.
- **No blanket tooltip mandate.** The guard enforces icon-only controls
  only; judgement covers the rest. No tooltips that restate labels.
- **No third-party GitHub Actions, no code signing, no auto-updater.**
  Signing stays documented future work; updates stay manual downloads.
- **No release automation beyond tag-triggered.** The workflow never
  bumps versions, never writes the changelog, never creates tags.
- **No new NuGet packages** (standing rule, restated because a markdown
  parser or YAML helper will look tempting; use line-based parsing).
