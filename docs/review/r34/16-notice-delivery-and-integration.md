# 16. Notice delivery and final integration

This continues [15](15-native-and-vault-evidence.md). It records delivery of
available upstream legal texts, not legal clearance, a release or v3 activation.
Execution date: 2026-10-17, Australia.

## Source and retained text

The resolved Desktop/Local API union contains 44 package/version pairs. There
are 71 unchanged licence/NOTICE texts for 43 of those packages, plus a provenance
index and one explicit unresolved native-package note: 73 retained files under
`docs/third-party/`. The source/hash table is in its
[index](../../third-party/README.md).

Thirty-three texts across 18 packages were compared byte-for-byte with their
members in existing original local nupkgs. The remaining 38 copies came from
the repositories and commits recorded by package metadata, including Avalonia's
upstream NOTICE, plus the Inter OFL text at the commit recorded in the embedded
regular font. Its 309,828-byte source font matches the cached package resource,
SHA256 `41ab0f707a2bfab8133ccdfcdab52282f5f79e5751f43a264805451c7bb95fb8`.
The font name table identifies version 3.019 and git-0a5106e0b. This identifies
that regular font, not an exhaustive audit of every embedded font/native asset.

Git attributes preserve original upstream bytes, including punctuation and
line endings and existing trailing whitespace. Whitespace checks stay enabled
for authored source/docs; the verbatim notice directory is explicitly exempt.
All 73 staged notice-file blob IDs matched raw `git hash-object --no-filters`
results, proving staging retained their exact working-source bytes.
No dependency version, framework, runtime manager, production
persisted format, security boundary or repository publication policy changed.
The existing guard now checks version-matched evidence and actual retained
licence files without interpreting their legal sufficiency.

The native SQLite 3.50.3 nupkg lacks a licence declaration and standalone text.
Its recorded commit has no repository URL; lookup in SQLitePCL.raw returned
404. The inventory now reports that uncertainty rather than inheriting the
wrappers' Apache licence. The pin is unchanged. The delivered unresolved note
is not permission to redistribute. Moss/other asset provenance and complete
embedded native attribution remain release gates.

## Actual archive checks

The source snapshot contains 1,345 files and was copied outside the checkout.
Its transfer ZIP SHA256 is
`81dba5eae383df31dab21f8d7e52c9e06d42785c10b9fbc4247be1bb57fd2887`.
That snapshot predates this receipt and the later whitespace-only Git attribute
clarification; the app/build files and shipped notices are the reviewed bytes.
Windows packaging ran `pwsh ./build.ps1 -Runtime win-x64` from the isolated
source. Linux ran its copied `build.sh` on Ubuntu 24.04.5 WSL; only scratch
shell-script CRLFs were normalised and execute bits restored. Legal texts were
not normalised. Both performed RID restore, Release publish and archive creation.

| Actual archive | Bytes | SHA256 |
| --- | --- | --- |
| Windows x64 ZIP | 41,021,519 | `ccb4b2f921fb4f305a33742c770632999de03a7f7e38465e7dc17f3caf86e1a5` |
| Linux x64 tar.gz | 42,886,789 | `4bd4e675a76ac6ac466ffca333bf4f11ed5ef196de50217e7d6714ca9d160f91` |

Inspection read every archive member matching the 73 retained notice files,
required exactly one of each, compared SHA256 with source, and also checked the
standalone third-party inventory and CMU licence. All 75 matches passed on both
platforms. Both generated archive checksums matched. Neither archive contained
PDBs; Linux member ownership was numeric zero with no group/other write bits.

Both SQLite modules in each archive (Desktop and Local API) also matched the
native binaries previously loaded and identified as SQLite 3.50.3 in 15.
Windows SHA256 was
`3061b1c0e66be7ba1a3223b5b3af90aff353c9bca07bd0a02aefbe2dfbacb81f`;
Linux was
`63ed8433123cf71158ecdb7981abcb54401193d0d1bf80562633888cd21d02c4`.
The two native-module check logs are retained alongside the archive checks.
Binary identity does not resolve the missing package licence declaration.

Windows sources, outputs and logs are under
`%TEMP%/hermaeus-r34-notice-package-8d7dc8d00173429391b8a46e5f63f4e5/`.
Linux sources/archive are under the same suffix beneath `/var/tmp/`.
`windows-archive-check.log` and `linux-archive-check.log` retain the actual
counts/hashes. The existing R33 control ZIP remained SHA256
`2d4858e7f9e3f297285b194d3cacc7ec2cf79d64dcc12a7280d143f24cd28476`.

These are unreleased development archives still labelled `0.41.0-beta`.
Nothing was installed, pushed, tagged or published. The earlier native Windows
walkthrough is scoped to its earlier candidate; these archives do not inherit
a fresh native workflow pass. Physical COSMIC/Wayland, full Windows/native
acceptance, controlled performance evidence and explicit owner S3 recovery/
downgrade acceptance remain open. Production v3 activation remains disabled.

## Automated integration

The focused notice/package selection passed all 11 cases with no failures/skips
at `%TEMP%/hermaeus-r34-notice-focused-1bb7981134b14160854dae5f8100e6fa/`.
The final Debug and Release solution builds passed with zero warnings/errors;
Windows then passed all 3,109 tests with no failures/skips (console duration 7m),
with logs and `notice-full.trx` under
`%TEMP%/hermaeus-r34-notice-gate-3e01841e438a4e11b0495483319b1904/`.
Build/test operations shared a host and overlapped; these durations are not
controlled R33/candidate performance evidence. Linux/Xvfb
`/var/tmp/hermaeus-r34-linux-tests/notice-final-full-x11.trx` passed 3,070 tests,
with zero failures and the same 39 skipped Windows-only case identities, in
10 minutes 13 seconds. Existing guard assertions were extended; no test cases
were removed to preserve the 3,109 total.

The slower Linux run was investigated against the preceding `key-path-final`
TRX. Measured test durations totalled 613.20s versus 430.35s. The largest increases
were the rename text guard (7.038s to 47.749s) and source punctuation guard
(7.805s to 39.636s). Source inspection confirms recursive repository traversal;
the former excludes generated paths only after traversal, and the latter also
traverses generated directories before filtering extensions. Other increases
were spread across unrelated existing cases. This is a diagnostic observation,
not proof of a cause or production regression. The raw comparison is retained
as `linux-duration-review.log` beneath the Windows gate directory above.
Controlled host/performance attribution remains open; no guard assertions or
production code were changed to improve these timings.

The final `pwsh ./scripts/coverage.ps1` passed all 3,109 tests with no failures
or skips in 12 minutes 22 seconds. Coverage was **72.72%** (`60,847 / 83,673`),
above the unchanged 60% floor. The script removed its temporary report;
stdout remains at `%TEMP%/hermaeus-r34-notice-final-coverage.log`.

A final source audit found that the backup filter excludes the primary vault
and key but omits their atomic-write leftovers and the new recovery filenames.
Two non-empty `.vault-*.tmp` files from the controlled Linux crash probes remain
under their synthetic evidence roots. This notice batch does not repair that
privacy gap; the separate bounded regression/repair is recorded in
[17](17-backup-credential-artifact-privacy.md). No safety or
activation gate is closed by the notice delivery result.
