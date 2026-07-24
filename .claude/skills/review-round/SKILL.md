---
name: review-round
description: How Hermaeus review rounds work, from spec pack in docs/review/ through implementation, versioning, archive, and tag-driven release. Use when writing a new rNN spec pack or implementing one.
---

# Review rounds (spec-then-implement)

Hermaeus evolves in numbered review rounds. A planning agent writes a spec
pack; an implementing agent executes it exactly; the pack is then archived.

## Spec pack structure

- Lives in `docs/review/` while active; archived packs are under
  `docs/review/archived/rNN/` (r1 through the latest are the format
  reference; recent rounds are the style to copy).
- `README.md`: why the round exists, a table of the numbered docs, and
  "Standing rules for the implementing agent".
- `01-*.md` .. `0N-*.md`: one theme per doc, with numbered work items
  (e.g. 2.3) that the roadmap references.
- Final doc is always the roadmap: version to ship, strict sequencing,
  test-count estimate, practical warnings, and an "Explicit rejections
  (do not do these)" section. The roadmap is the sequencing contract.
- File:line references must be exact at spec time and state the commit
  they were verified against; the implementer re-verifies before editing.

## Standing rules every round inherits

- No em dashes anywhere (code, docs, UI copy, YAML comments).
- Zero-warning build; all tests pass; new harness-style tests register in
  `XunitHarnessTests.HarnessCases`.
- No new NuGet packages without written justification.
- Update `docs/features.md`, the relevant workflow docs, and
  `CHANGELOG.md`; never document planned behaviour as existing.
- Moss-attributed copy follows `docs/mascot.md` "Voice in UI copy".
- No AI co-author trailer on commits.

## Versioning and close-out

- Version bump in `Directory.Build.props` only (VersionPrefix,
  AssemblyVersion, FileVersion). Minor bump for feature rounds; patch only
  for urgent hotfixes.
- Every minor version is tagged `vX.Y.0-alpha` and released on GitHub via
  the tag-driven `.github/workflows/release.yml`. The owner pushes the
  tag; agents never do.
- Close-out order: implement, build/tests green, docs truthful, archive
  the pack to `docs/review/archived/rNN/`, update CHANGELOG, commit.
- Since r23, work lands via pull request, not direct pushes to main; see
  `docs/pull-requests.md` (one open PR per maintainer at a time).
