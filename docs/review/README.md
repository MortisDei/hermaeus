# Review round 20: Rename Aether to Hermaeus

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 05 is the roadmap and sequencing contract.

## Why this round exists

The go-public trademark check found "Aether" carries real risk: multiple
live USPTO Class 9 (software) registrations plus several existing local-AI
desktop projects already using the name. The owner has chosen **Hermaeus**
(after Hermaeus Soter, the Indo-Greek king, ~90-70 BC) as the replacement.
This round executes the rename completely, before the repo goes public.

This is not a find-and-replace. "Aether" appears ~1,839 times across ~490
C# files, in every namespace, every assembly name, the solution file, the
data-root default, avares:// resource URIs, SQLite table names, OS secret
store service names, HTTP headers, user agents, lock files, crash log
filenames, the voice lexicon, benchmark fixtures, CI, build scripts, and
every doc. A partial rename is worse than none: it ships a public repo that
looks half-migrated and leaves runtime breakage (avares URIs and embedded
resource names silently 404 when the assembly name changes but the
reference does not).

## Naming decisions (fixed, do not relitigate)

- Product / display name: **Hermaeus**
- Namespaces, projects, assemblies: **Hermaeus.Core**, **Hermaeus.Desktop**, etc.
- Repo slug (owner renames on GitHub later): **hermaeus** (lowercase, like `aether` today)
- Lowercase forms in filenames/ids: **hermaeus** (e.g. `hermaeus.lock`, `hermaeus-backup-*.zip`)
- Executable: `Hermaeus.Desktop` / `Hermaeus.LocalApi`
- The mascot stays **Moss** (docs/mascot.md); only the product name around him changes.

## Docs in this pack

| Doc | Scope |
| --- | --- |
| 01 | Solution, projects, namespaces, assemblies, build/CI plumbing |
| 02 | Runtime identity: data root, stores, lock/log/header/lexicon literals |
| 03 | Docs, branding assets, UI copy, skills |
| 04 | Naming guard test + owner-only checklist (GitHub, local machine) |
| 05 | Roadmap, sequencing, version, explicit rejections |

## Ground rules

- Repo rules apply throughout: zero-warning build, no em dashes anywhere,
  ASCII escapes (not glyphs) for any non-ASCII in .cs/.axaml, tests green.
- History is history: never touch docs/review/archived/, docs/changelog-archive.md,
  or existing CHANGELOG entries. They correctly describe releases that
  shipped under the old name.
- Claude never changes GitHub repo settings, including the repo rename
  itself. Doc 04 hands those steps to the owner.
- The owner is currently the app's only user. No automated data migration
  is wanted (explicitly waved off); doc 02 lists the few cheap compat shims
  that ARE in scope and doc 05 rejects the rest.
