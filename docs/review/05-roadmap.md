# 05. Roadmap and sequencing

## Version

Ships as **0.25.0-alpha** (`Directory.Build.props` only: VersionPrefix,
AssemblyVersion, FileVersion). The minor bump is justified: the rename is
user-visible and carries deliberate breaking changes (data-root default,
LocalApi headers, log filenames, secret service names on Linux/macOS).

## Sequencing (strict)

1. **Doc 01** in one pass: directory/csproj/sln moves, namespace sweep,
   props, avares/embedded resources, build scripts, CI, test fixtures.
   Get `dotnet build` + full suite green from a clean tree before moving on.
   Do the moves with `git mv` so history follows.
2. **Doc 02** in item order (2.1 data root first; 2.2 and 2.8 carry the
   only two compat shims; everything else is a clean break).
3. **Doc 03** copy/docs/assets sweep.
4. **Doc 04** guard test last, once the greps come back clean; then the
   sanity pass.
5. Roadmap close-out: CHANGELOG 0.25.0-alpha entry (respect the 10-version
   FIFO, archiving the oldest entry to docs/changelog-archive.md),
   security-review.md r20 subsection, archive this pack to
   `docs/review/archived/r20/`.

Commit when done (owner's standing pattern: implement in full, commit
after build/tests/docs are truthful). Expect most of the diff to be
renames; keep the commit message explicit that this is the Aether to
Hermaeus rename so history reads sanely.

## Test estimate

~12-18 new tests from 1090: naming guard, two migration-shim tests,
workspace-manifest legacy-read fallback, `hermaeus` pronunciation golden,
AppVersion persisted-run compat, default data-root resolution, crash-log
filename round-trip, LocalApi header rename, plus whatever the fixture
renames force. Register any new harness-style methods in
`XunitHarnessTests.HarnessCases` as they are written; the
`HarnessRegistrationGuardTests` reflection guard fails otherwise.

## Practical warnings for the implementer

- Delete every `bin/` and `obj/` before the first post-rename build; stale
  compilation caches from renamed projects produce misleading errors.
- The Bash-tool Unicode gotcha applies to the lexicon item (2.9):
  byte-verify any non-ASCII written to .cs files using codepoint
  construction, never typed glyphs, per the standing repo workflow rule.
- `Aether` inside historical docs is correct and protected; if a sweep
  script touches `docs/review/archived/` or `docs/changelog-archive.md`,
  revert those hunks.
- The em-dash architecture test scans all .cs/.axaml: any copy rewritten
  in doc 03 must stay ASCII-safe.

## Explicit rejections (do not do these)

- **No data migration framework.** The two shims (schema-version table
  rename, `.aether` manifest fallback read) are the entire compat surface.
  No settings/data-root auto-migration, no secret re-keying, no crash-log
  fallback reads, no old-header acceptance in LocalApi.
- **No git history rewrite.** Old commits, old release notes, and archived
  review packs keep saying Aether forever. That is correct.
- **No new artwork.** Icon/branding pixels ship unchanged under new
  filenames; a visual rebrand is a separate, owner-led effort.
- **No mascot changes.** Moss survives the rename untouched.
- **No GitHub settings changes by the agent**, including the repo rename
  itself, releases, or visibility. Doc 04.3 is owner-only.
- **No renaming persisted identifiers beyond spec**: benchmark suite ids in
  old runs, SQLite scope names inside `hermaeus_schema_versions`, and
  existing eval-run JSON stay as written; only the code-side names change
  with graceful reads.
- **No opportunistic refactors** riding the rename diff. The rename commit
  must stay reviewable; genuinely unrelated fixes found along the way get
  recorded in the final response instead (per AGENTS.md working agreement).
