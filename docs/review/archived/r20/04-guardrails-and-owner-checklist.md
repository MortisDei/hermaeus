# 04. Naming guard test and owner-only checklist

## 4.1 NamingConsistencyTests (implement last)

A permanent regression test in the same spirit as
`SourceStringsAvoidLongDashes` (reuse its file-walking infrastructure):
scan the repo for `aether` case-insensitively and fail with the offending
file list. Scope:

- Scanned: `src/**` (.cs, .axaml, .csproj, scenario fixture files),
  `Hermaeus.sln`, `Directory.Build.props`, `build.sh`, `build.ps1`,
  `scripts/**`, `.github/**`, `.claude/skills/**`, root-level .md files,
  `docs/**`.
- Allowlisted paths: `CHANGELOG.md` (historical entries plus the rename
  entry itself legitimately say Aether), `docs/changelog-archive.md`,
  `docs/review/**` (archived packs and this pack are history).
- Allowlisted content (exact-match, not path-wide, so drift still fails):
  the legacy-table string `aether_schema_versions` in the two migration
  runners and their tests (the 2.2 shim), the legacy `.aether/workspace.json`
  fallback constant and its tests (the 2.8 shim), the `aether` dictionary
  word entry in `KokoroUserLexicon.cs` and its test, and the guard test's
  own file.

Design note: prefer a small list of (path, allowed-substring) pairs over
regex cleverness; when someone adds a new legitimate legacy reference they
should have to register it here consciously.

**Acceptance:**
- Guard passes on the finished rename.
- Guard demonstrably fails when a stray `Aether` is introduced into a .cs
  file and into a docs file (verify once manually, then revert).

## 4.2 Sanity pass before handing back

- Clean-tree build, full test suite, `pwsh ./build.ps1 -SkipRestore`.
- Launch the app: window title, tray, wizard, Doctor scan, one chat send
  against whatever backend is available, voice preview if Kokoro assets
  are installed (exercises the embedded-resource rename).
- `git status` shows renames (R) for moved files, not delete/add pairs.

## 4.3 Owner-only checklist (Claude must not perform these)

Recorded here so the round is complete on paper; every step below is the
owner's, after the implementation lands:

1. Rename the GitHub repo `MortisDei/aether` to `MortisDei/hermaeus`
   (Settings > General). GitHub redirects the old URL indefinitely, so
   code referencing the new slug before the rename is safe.
2. Update the local clone: `git remote set-url origin
   https://github.com/MortisDei/hermaeus.git`. Optionally rename the local
   folder `Documents\GitHub\aether` and re-open the IDE workspace.
3. Rename `%LocalAppData%\Aether` to `%LocalAppData%\Hermaeus` while the
   app is closed. If `settings.json` inside it contains an absolute
   `DataRootDirectory` or `LocalAiAssetsRoot` that embeds `\Aether`, edit
   those values to the new path in the same sitting (or re-point them in
   Settings on next launch).
4. Existing agent workspaces (e.g. `C:\AI\Aether`) keep working via the
   `.aether` manifest fallback; optionally rename the folder and manifest
   dir at leisure.
5. Re-pin any Start menu/taskbar shortcuts to the new executable name.
6. Confirm CI green on both matrix legs after push.
7. Update the private `docs/temp/` go-public checklist wording where it
   says Aether, since screenshots/release/flip steps still reference the
   old name.
