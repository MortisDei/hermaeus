# 17. Backup credential artifacts and path privacy

This bounded safety follow-up continues [16](16-notice-delivery-and-integration.md).
It uses synthetic data only. v3 production activation remains disabled, and the
approved envelope, AAD, protected key location, OS backends and references are
unchanged.

## Reproduction

The backup filter excluded only `secrets.local.json` and `secrets.local.key`.
Both active legacy writers use `<primary-or-key>.<32-hex-guid>.tmp`; the isolated
v3 transaction uses `.vault-<32-hex-guid>.tmp` and protected recovery pairs named
`<64-hex-vault-id>.pre-v3.snapshot` / `.locator`, plus `secrets.local.lock`.
Two non-empty primary staging files remained from the real controlled Linux
worker-death probes. No contents or real credentials were printed.

Actual ZIP backup/restore fixtures reproduced inclusion of these private
artifacts and acceptance of credential replacement through generic restore.
They also showed that the inactive transaction accepted a protected directory
equal to or beneath its Data Root despite the approved separate-directory rule.

| Receipt | Actual result |
| --- | --- |
| `%TEMP%/hermaeus-r34-secret-artifacts-red/secret-artifacts-red.trx` | Windows: 16 failed, one ordinary-filename control passed |
| `/var/tmp/hermaeus-r34-linux-tests/secret-artifacts-red.trx` | Linux: 15 failed, two controls passed, including legitimate case-distinct paths |
| `%TEMP%/hermaeus-r34-secret-artifacts-green/secret-artifacts-green.trx` | First repair: 142 passed, two existing recovery fixtures failed because their alternate targets enclosed the protected directory |
| `%TEMP%/hermaeus-r34-secret-artifacts-green-final/secret-artifacts-green-final.trx` | 144 passed after moving those fixture targets to separate sibling Data Roots; original missing-key/history and stale-target IOException assertions retained |

The sibling path audit used actual Windows junctions and Linux symlinks. Both
redirected source cases copied the exact synthetic outside-root canary into the
ZIP, and a redirected destination created one archive outside the selected
path. All three refusal tests failed on each platform. Receipts are
`%TEMP%/hermaeus-r34-backup-reparse-observed-red/backup-reparse-observed-red.trx`
and the same filename under the external Linux result directory above.
The earlier Windows refusal-only run is retained separately as
`%TEMP%/hermaeus-r34-backup-reparse-red/backup-reparse-red.trx`.

The existing reparse fixture is reused. Its Windows `mklink` invocation now uses
individual ArgumentList entries; neither platform substitutes a fake link or
reports a skipped operation as Passed.

The native Linux snapshot-directory test also failed: existing default creation
produced mode 0755, with group/other read and execute permission, instead of private
0700. The production creation call was extracted unchanged as a small internal
test seam first; this receipt is `backup-snapshot-mode-red.trx` in the external
Linux directory. The Windows factory/location case passed, without claiming an
independent Windows ACL audit.

## Bounded repair and limits

Exact credential artifact families are excluded from backup, including nested
locations, and rejected by restore after normalising the actual target path.
Preflight refusal preserves ordinary data, the existing vault and key before
staging/publication. Nearby ordinary filenames remain backup/restore controls.
No generic `*.tmp` exclusion, content scanner or automatic credential recovery
is introduced. The inactive v3 constructor refuses configured protected-directory
overlap using the existing platform path comparison before creating artifacts.

Backup now reuses the existing reparse validator for the source root, source
inventory, destination and final archive path, and again immediately before each
source file read, including SQLite snapshot reads. Unix intermediate SQLite
directories request mode 0700 at creation. Windows retains its existing per-user
TEMP ACL inheritance. Directory enumeration still uses the existing manifest;
these checks do not claim race-free metadata traversal or kernel containment.

This enforces the existing backup privacy and separate recovery contracts.
Archives containing these private entries are refused rather than silently
dropping them or restoring credentials. It does not add handle-based filesystem
identity, a new security boundary or guaranteed protection against live ancestor
replacement and every native path alias. Those broader S4/native limits remain.

The repaired Windows focused run passed all 148 cases with no failures/skips at
`%TEMP%/hermaeus-r34-backup-privacy-final-focused/backup-privacy-focused.trx`.
This includes existing vault codec/transaction/integration cases, the registered
backup/migration harness, ZIP expansion controls and the new privacy fixtures.
Final Linux, full-suite, refreshed archive and canonical coverage receipts follow
only after execution. The prior archives and gates in 16 remain scoped to their
earlier source snapshot.

Linux `backup-privacy-focused.trx` then passed the same 148 cases with no
failures/skips in 10s, including real source/target symlinks and snapshot mode
0700. Windows used real junctions and passed in 15s. No native COSMIC session,
independent Windows TEMP ACL audit or production v3 activation follows from
these component fixtures.

The first integrated Windows run passed all 3,130 tests with no failures/skips
in 7m05s after zero-warning Debug/Release builds. Linux passed 3,090, failed one
and explicitly skipped 39 in 8m43s. Its failure was the existing
`Trust_rescan_never_mutates_the_live_settings_object` fixture: the scan took
674ms while its independently scheduled real autosave expires after 600ms.
Production rescan already builds a clone; the test had not isolated that writer.

The fixture now reuses its existing controllable autosave delay. Both original
value assertions remain, with added live-instance identity and zero-save-count
assertions. Cleanup cancels and joins the owned pending task. The complete
Windows settings lifecycle selection passed 27 cases in 5s at
`%TEMP%/hermaeus-r34-trust-autosave-focused/trust-autosave-focused.trx`.
This is a fixture correction; production settings/trust behaviour is unchanged
and no user-feature documentation change is required for it. The failed full
receipt remains under `privacy-final-full-x11.trx`; later verification does not
silently replace it. The refreshed archives below contain the same production
code and user guide; only this subsequent test-fixture correction is absent
from their source transfer snapshot.

Linux also passed all 27 settings lifecycle cases in 5s at
`/var/tmp/hermaeus-r34-linux-tests/trust-autosave-focused.trx`. Final Debug and
Release solution builds after that fixture correction passed with zero warnings
and errors. Windows then passed all 3,130 tests with no failures/skips in 4m46s
under `%TEMP%/hermaeus-r34-final-gate-4e3721d4424a415b874028e55a46f96b/`.
The initial Linux rerun left no terminal receipt and no remaining test process;
it is not counted as completed. Its replacement passed 3,091 tests with zero
failures and 39 explicit Windows-only skips in 6m46s, at
`/var/tmp/hermaeus-r34-linux-tests/privacy-final-fixed-full-x11.trx`.
The 39 skipped test identities exactly match `notice-final-full-x11.trx`.
The canonical final precommit `pwsh ./scripts/coverage.ps1` then passed all
3,130 tests with zero failures/skips in 6m33s. Line coverage was **72.74%**
(`60,891 / 83,711`), above the unchanged 60% floor. Stdout is retained at
`%TEMP%/hermaeus-r34-privacy-final-coverage.log`; the script removed its temporary
report as designed. This verifies the combined implementation tree, without
claiming native acceptance or production activation.

The repair is split into local commits: backup privacy/path checks `df072fd`,
isolated protected-directory validation `268de86`, and deterministic trust-scan
fixture `9d44eb4`. The receipts above cover their combined tree; they do not
claim each intermediate commit was independently run through the full suite.

## Refreshed binaries and development archives

The final Debug Services assembly SHA256 is
`ae1878c4f55037813008d2247eea438344fe1ce75003135b13e6d94a34afa88b`.
All four retained vault process cases passed again on Windows and Linux:
cross-process writer exclusion, pre-replacement death, post-replacement recovery
with the actual R33 reader, and key-only interruption refusal. The synthetic
fallback/provider-reference probe passed on both platforms. Windows also passed
the real Credential Manager round-trip with exact synthetic-target cleanup,
sibling preservation and unchanged key. Linux Secret Service acceptance remains
unavailable. The Linux console log is `linux-vault-component.log` beneath
`%TEMP%/hermaeus-r34-privacy-gate-fb91dc019f034e7188b31583a371a7d5/`;
Windows console results were observed directly; synthetic scratch state was
retained separately.

A fresh 1,349-file source snapshot includes the repaired production code and
published user-guide text, with transfer ZIP SHA256
`ece6384fbf18ba8b80b736d27542bd37baa6171f1e5945251301f0eb6a4bcd02`.
Later receipt text is not part of that earlier snapshot. Both existing packaging
scripts ran RID restore, Release publish and archive creation outside the main
checkout. Scratch Linux shell-script line endings/execute bits were normalised;
legal texts were unchanged.

| Refreshed development archive | Bytes | SHA256 |
| --- | --- | --- |
| Windows x64 ZIP | 41,022,863 | `56c97c960a894c9269aa46bf7c40be2721988a3d56c1b54c6b7898540d29cccd` |
| Linux x64 tar.gz | 42,888,225 | `554c525bb9dd4fdd3449087a14fcca5e7b4671ad6af9bc4885f8736fdf058ad8` |

Both actual archives contain exactly the 73 retained notice files and two
standalone notices, each SHA256-equal to source, with verified archive checksums
and no PDBs. Both selected SQLite modules per archive match the previously
identified 3.50.3 binaries in 16. Linux tar ownership/modes again passed the
numeric-zero/no-group-or-other-write checks. The original R33 control ZIP remains
unchanged at the digest recorded in 16.

Windows sources, outputs and raw archive checks are under
`%TEMP%/hermaeus-r34-privacy-package-3a05b9753c5d4ae48cdff0699c099ff4/`;
Linux sources/archive use the same suffix beneath `/var/tmp/`. These archives
still say `0.41.0-beta`, are not installed or published, and do not inherit a
fresh physical/native workflow acceptance. S3 activation, native Windows/COSMIC
workflows, controlled performance attribution and unresolved package/asset
clearance remain open.

The owner has deferred COSMIC testing to their Pop!_OS box at a later point
and reaffirmed local commits. This schedules that acceptance; it does not
waive the native gate, approve v3 activation or supply a passing receipt.
