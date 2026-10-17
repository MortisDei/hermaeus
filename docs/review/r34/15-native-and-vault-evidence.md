# 15. Native and inactive vault implementation evidence

This receipt continues [13](13-execution-and-evidence.md). Production v3
activation is disabled. Passing a synthetic case does not approve activation,
prove power-loss durability or close the two-host native acceptance matrix.

## Windows package walkthrough

The development candidate ZIP and unchanged R33 control are identified in 13.
The candidate was built before the S3 integration and still reports
`0.41.0-beta`; it is not a release. Native Computer Use discovered the actual
packaged `Hermaeus.Desktop.exe` window after launcher handoff. The launcher
initially reported no targetable window; a fresh inventory found the child.
The first occluded capture showed another window despite valid accessibility
text, so it was rejected as pixel evidence. Foreground activation and a new
capture showed the actual product.

Actual inspected pixels and accessibility state covered Chat, Services,
Benchmarks, RAG and Settings. A synthetic unsent Chat draft survived navigation
through those pages, and returning to Chat restored input focus. The draft was
then cleared; Send was disabled. No message, query, benchmark, credential edit,
dataset mutation or preference edit was requested. The configured managed
runtime auto-started through the existing application workflow.

Benchmarks reported 13 loaded suites and 60 historical runs, with unverified
history excluded from rankings. That observation proves page loading, not
benchmark quality or performance. The rankings area remained horizontally
scrollable at the observed window size; minimum-size, mixed-DPI and full
keyboard/clipboard/microphone acceptance remain open. Captures contain existing
profile context and are not committed as public screenshots.

Window Close followed the existing configured close-to-tray behaviour. Computer
Use exposed no tray/taskbar surface, so the owner selected **Quit Hermaeus** and
confirmed that action. A read-only process check then found neither the known
desktop PID nor its observed managed runtime PID. The matching lifecycle record
started at `2026-10-16T10:15:11.2093836Z` and reported `CleanExit: true`.
This is owner-assisted clean tray exit plus observed known-process exit, not
proof of every possible descendant or shutdown scenario.

No COSMIC/Wayland, Linux scaling, complete surface matrix, physical audio,
active generation cancellation or S3 native credential workflow is closed by
this walkthrough.

## Inactive S3 implementation

The corrected contract is in [14](14-vault-format-decision.md). The implementation
uses the specified v3 codec, cooperating writer lock, authenticated immutable
snapshot/locator, exact legacy readers and explicit digest-reviewed recovery.
The only opt-in is an internal constructor used by tests. Public construction
continues to write v2, with no setting or environment activation switch.

Active legacy safety changes are narrower: recognised v3/unsupported payloads
are unavailable rather than falling through; unreadable existing credentials
block mutation; resolving or mutating an encrypted vault cannot silently
manufacture a replacement key. OS backend selection, secret references and the
protected key location are preserved.

| External evidence | Actual result |
| --- | --- |
| `%TEMP%/hermaeus-r34-s3/codec-transaction-1.trx` | 56 passed, zero failed/skipped. Initial isolated codec/transaction slice |
| `%TEMP%/hermaeus-r34-s3/integration-1.trx` | 244 passed, zero failed/skipped. Includes final 28 codec, 34 transaction and 15 integration cases plus existing service/backup/OS-secret regressions |
| Full Debug and Release solution builds | Both passed with zero warnings/errors after S3 integration |
| `%TEMP%/hermaeus-r34-s3/full.trx` | 3,052 passed, zero failed/skipped, 4m09s before the later restore/API cases |
| Linux VSTest, vault codec/transaction/integration filter | 77 passed, zero failed/skipped, 8s on Ubuntu WSL using the same managed assemblies. This exercises Unix filesystem behaviour, not desktop/native OS credential-store acceptance |

Tests cover field/name/purpose/identity authentication, strict Unicode/version
handling, same-process writers, actual legacy format compatibility, immutable
pair validation, relocated recovery, stale target review, missing/corrupt keys,
private Unix checks and Data Root transition admission. Thrown fault hooks
exercise operation boundaries with normal finally cleanup; they are not real
process interruption or power-loss evidence. Cross-process and interruption
receipts are recorded only after execution.

Key-only interruption, missing primary with an existing key, missing key for an
empty existing vault and ambiguous legacy-key import alongside recovery history
fail closed. No automatic initialisation, restoration or downgrade resolves
these states. Their explicit investigation workflow remains an activation gate.
The recovery snapshot restores the old state and loses later fallback changes;
it cannot roll back external provider revocation or OS credential-store changes.

## Actual cross-process and interruption evidence

An external console probe calls the built internal transaction through reflection
and loads the preserved R33 assemblies in an isolated assembly context. It uses
only synthetic credentials and its own scratch roots. It does not replace a
production dependency, expose an activation setting or use the owner's vault.
Its source is retained in [probes](probes/README.md) and locally at
`%TEMP%/hermaeus-r34-vault-process-probe/Program.cs`,
SHA256 `310122fdee46f4c1d66872fb75c56e2fdd8735b5afc770be977320db324c3314`.
The first probe build failed CS4007 because a span expression crossed an await;
materialising the second digest first repaired the probe. No product change
was needed, and that failed build ran no acceptance cases.

The preserved R33 Services assembly used by the old-reader control is
SHA256 `1973e4b8cd8bcc0483a4fb37f0bf9a85582ac8bdc74a33e81ec36e764daebd8a`,
byte-identical to that entry in the unchanged R33 control ZIP identified in 13.

| Actual child-process case | Windows 10.0.26300 / .NET 10.0.13 | Ubuntu 24.04.5 WSL / .NET 10.0.12 |
| --- | --- | --- |
| Second process observes exclusive lock contention; serialised mutations retain both names | Passed | Passed |
| Kill the owned worker after primary flush, before replacement; old bytes remain and retry succeeds | Passed | Passed |
| Kill after primary replacement; fresh v3 read succeeds, explicit reviewed restore preserves exact old bytes and the actual R33 reader resolves them | Passed | Passed |
| Kill after first key replacement; next mutation refuses the key-only state without replacing the key | Passed | Passed |

Both probe builds completed with zero warnings/errors and reported all four
booleans true. Linux vaults and locks were on its native temporary filesystem,
not the Windows mount. Only explicitly owned probe children were terminated.
These are process-death observations at selected barriers, not power-loss,
directory-fsync durability, malicious non-cooperating writer or physical Linux
desktop proof. WSL temporary evidence is volatile; command output and the
Windows probe source are the retained local receipt. Production activation and
owner acceptance of recovery/downgrade remain open.

## Linux archive and native SQLite identity

An isolated source snapshot under
`/var/tmp/hermaeus-r34-linux-package-20261016` produced the actual linux-x64
development archive. The first copy inherited Windows CRLF shell endings and
failed at `set -euo pipefail`; normalising shell files only in the scratch copy
matches Linux checkout semantics. An intermediate `/tmp` copy was absent on
the later inspection; the successful build uses `/var/tmp`. No owner settings,
Windows build outputs or existing package paths were changed by this build.

The archive checksum verifies, size 42,477,337 bytes, SHA256
`0df2e11f463cb2d1db7b8603e84af06273e9ce10f0a8f638c89c33cc28b6dd39`.
Both required notice files occur in the actual archive and their extracted
SHA256 values match the source: third-party inventory
`3fdbd59c7800d8039b514e14aec77246092631f49ec038cc84a70776e4bacb5a`,
dictionary licence
`bd4ce8e44170a5f9f481310ca85c51de3c4f851a65e679b40e603b143bd3542a`.
This snapshot contains
the inactive S3 implementation and precedes the later restore/API test work;
it is not a final integrated release candidate or COSMIC acceptance.

The actual Windows candidate and Linux archive native SQLite libraries both
report `3.50.3`, source ID
`2025-07-17 13:25:10 3ce993b8657d6d9deda380a93cdd6404a8c8ba1b185b2bc423703e41ae5f2543`.
Loading each exact packaged library and calling its exported identity functions
gave binary SHA256:

- Windows: `3061b1c0e66be7ba1a3223b5b3af90aff353c9bca07bd0a02aefbe2dfbacb81f`.
- Linux: `63ed8433123cf71158ecdb7981abcb54401193d0d1bf80562633888cd21d02c4`.

This verifies those native modules and notice delivery, not all native assets,
complete upstream licence texts or unresolved redistribution terms.

## Later adversarial audit and actual failures

The first new restore/API run (`%TEMP%/hermaeus-r34-adversarial/restore-api.trx`)
passed six cases and failed three. All five API cases passed. The failed restore
cases proved that the .NET 10 ZIP reader can stop at a forged declared length:
the existing restore reported success and replaced an 8 KiB member with one
byte. A diagnostic rerun reproduced that result. Existing byte-count equality
and budget checks did not establish intact extraction.

Restore now computes the ZIP CRC32 while copying each staged member and checks
it before publication. The fix complements existing byte/path/rollback checks;
it does not add a raw ZIP parser or dependency. Independent standard CRC vectors,
valid multi-buffer stored/deflated members, forged lengths and wrong checksums
are regression controls. `restore-crc-api-focused.trx` passed 20 cases and
`backup-harness-crc.trx` passed all 24 registered backup/migration cases. The
static `BackupMigrationTests` class name alone is not the registered harness
filter, so the actual harness cases were selected explicitly.

CRC32 establishes consistency, not archive authenticity. Mutually consistent
malicious size/checksum metadata is outside that claim. The platform reader's
hidden expansion is not counted as observed output beyond its own length cap.
Live ancestor swaps, disk-full commit interruption and physical power loss
remain separate unverified cases.

The API matrix exercises the production endpoint map and token middleware through
the existing TestServer: missing/wrong/valid/revoked/unconfigured tokens,
case-varied health, health descendants, wrong methods, near-prefix paths and
unmapped Agent routes. A successful models call is the positive control.
Persisted traces use verified identity, keeping the spoofed client header as
unverified display context. No new endpoint or approval authority was added.

Final independent S3 review identified another key-loss sibling: a fresh v3
vault has no legacy snapshot, so recovery-history checks alone could permit
copying a replacement-looking legacy key before authentication failed. The
inactive transaction and public constructor now validate the complete existing
legacy vault with the candidate before relocation. Recognised v3/unknown,
corrupt and mismatched entries refuse copying/deletion; ordinary v2 cleanup
also preserves a mismatched legacy key. This repairs the approved fail-closed
contract rather than changing key location or approving activation.

`%TEMP%/hermaeus-r34-s3/final-safety-focused.trx` passed 267 cases with zero
failures/skips after those repairs, including the new key-loss siblings, restore,
API, service/backup harness and outside-workspace recipe control. The Linux
`/var/tmp/hermaeus-r34-linux-tests/safety-linux.trx` run passed 111 cases and
explicitly skipped two Windows-only process cases; zero failed. It covers the
same vault/restore/API/recipe classes plus cross-platform command capture.
The Windows-only decoder and known-descendant cancellation cases are not
relabeled Linux passes.

`approval-diagnostics.trx` passed all 12 new approval cases and failed its one
new diagnostic fixture: the fixture incorrectly expected a filesystem exception
to escape. The executor converts it to an Unavailable tool result. Correcting
that expectation retained the assertions that actual synthetic child output and
secret-bearing exception-derived detail stay out of persisted runtime diagnostics
and both existing log-copy commands. `approval-diagnostics-final.trx` then passed
all 13 cases. This does not exercise the outer uncaught-exception logger or
claim arbitrary task content is redacted before reaching the model.

The approval matrix invokes public dispatch after independently changing persisted
proposal identity/revision/workspace/preimage/content hash/arguments/policy/schema
or the rendered fingerprint. Stale approvals leave targets and pending state
unchanged and do not consume approval or mutation receipts. Tampering with only
the complete replacement payload is separately refused by execution validation.
An inert cached fingerprint positive control confirms identity is recomputed.

A controlled declared MSBuild recipe read a synthetic canary outside its
workspace and returned it successfully on Windows and Linux. This demonstrates
the approved host recipe's residual authority rather than a containment pass.
The canary lived under the fixture's own temporary parent; no owner data or
network destination was used. No sandbox or new execution policy was added.

## Actual Agent approval interruption and parent recovery

The initial external A05 worker used the actual public preparation/approval path,
real workspace executor and real durable task store. A test decorator blocked
immediately before the write or immediately after the real executor returned,
before the final receipt could save. Only the exact returned worker handle was
terminated after its PID/barrier/durable Pending receipt were verified.

All three Windows red cases reconciled readback correctly to Unknown, Applied
or Conflict and refused stale approval, but retained `WaitingForUser` with no
pending decision on both fresh reopens. The red Agent assembly was SHA256
`412079b32e0942e4c52abe887ac6e9137a5a82e0dd012a1bd27210f074d4a598`.
This was a lifecycle defect, not an observed replay or incorrect file outcome.

Recovery now marks that disappeared waiting execution Interrupted. The new
public-approval snapshot regression then exposed two parent/child siblings:
`recovery-parent-red.trx` passed 16 and failed two because the child's parent
plan entry remained Running. Receipt recovery now finishes before the existing
plan-status pass. A vanished interrupted-child approval mirror retires only
when no healthy owner decision remains; another child's question still projects.
Deliberate blocked/terminal states and never-approved pending proposals retain
their existing meanings.

`%TEMP%/hermaeus-r34-focused/a05-owner-recovery-green/a05-owner-recovery-green.trx`
passed all 60 cases: 12 new boundary/status/parent cases plus existing lifecycle,
owner interactions, policy, review queue and owner-save races. Snapshot tests
copy actual durable public-approval state into a fresh scratch store; they do not
pretend that copying a file is process death.

The external killed-worker cases were then repeated on Windows and Ubuntu WSL:

| Actual process-death case | Windows | Linux |
| --- | --- | --- |
| Before write: pre-image remains, receipt Unknown, task Interrupted | Passed | Passed |
| After write: complete post-image remains, receipt Applied and verified, task Interrupted | Passed | Passed |
| After write plus an explicit synthetic external edit: receipt Conflict, task Interrupted | Passed | Passed |
| Two fresh initialisations retain exact target bytes, clear pending authority and refuse the stale fingerprint without new approval/receipt consumption | Passed | Passed |

Both runs used the repaired Agent assembly SHA256
`e651004021e101a1b4aca26deafafbc92840835d68a3e06c5469689423d8a0c9`,
Services `82a74d259729b0fb764a31f7496213ad3fc2a01f3b306226597d498787dd0bf9`
and Core `45eaf07e989c2b07886068bfce81552709ed191d3de501d5eac7bb2d36f6dbba`.
Linux used native SQLite and temporary storage. Probe sources and owned-worker
parent drivers are retained in [probes](probes/README.md). The first red probe
mixed decoded-text and raw-byte hash labels; the final driver explicitly
separates them and compares exact raw bytes. The observed difference was a BOM,
not evidence of a replay. Power loss and arbitrary write interruption remain
outside these selected real-process barriers.

## Synthetic S3 component and native backend acceptance

The external component console initially failed CS4007 on a span expression
across await. Materialising the retained key bytes repaired that driver; no
acceptance action ran before the corrected build, which emitted zero warnings.

On both Windows and Linux, an isolated test-only v3 SecretStore persisted the
synthetic value, a fresh instance resolved its unchanged `secret:` reference,
and the actual `OpenAiService.GetModelsAsync` supplied that resolved value as
Bearer authentication to an injected synthetic HTTP handler. The handler
validated the expected request and returned one synthetic model. No request
left the process; this is component/provider reference evidence, not external
provider, transport or desktop acceptance.

The Windows case additionally preflighted one random GUID credential target as
absent, seeded two isolated v3 fallback entries, then used the actual Windows
Credential Manager backend to store and resolve only its own synthetic target.
The matching fallback entry was removed, the sibling still decrypted and the
protected key bytes were unchanged. Finally, exact-target deletion succeeded
and native read returned not found. No credentials were enumerated, no real key
was used and the owner's settings were not loaded. Linux `secret-tool` is absent
on this WSL host, so Linux OS keyring acceptance remains unavailable.

The console source is retained in [probes](probes/README.md). Public construction,
DI, settings and production activation remain unchanged. These synthetic
receipts do not grant the explicitly required owner recovery/downgrade acceptance.

## Linux legacy-key identity sibling

The final key-relocation call-site audit found three public legacy comparisons
still using case-insensitive equality on Linux. A new cross-platform fixture
uses `caseroot` and `CaseRoot` beneath its own temporary parent. Windows correctly
treated them as one physical alias and passed; Linux failed to resolve the valid
existing credential because it skipped the required relocation. The Linux red
receipt is `key-case-red.trx` under the external Linux result directory above.

All three comparisons now use the existing platform policy: ordinal-ignore-case
on Windows, ordinal on Linux. The regression checks first resolution, original
key bytes, correct source retention/removal and a second successful store.
`%TEMP%/hermaeus-r34-adversarial/key-case-green.trx` passed 160 Windows cases;
Linux `key-case-green.trx` passed all 25 secret integration cases. No key location,
credential backend, format default or user-facing setting changed.

The preceding recovery integration had already passed zero-warning Debug/Release
builds, 3,108 Windows tests and 3,069 Linux/Xvfb tests with 39 Windows-only skips.
Those receipts remain scoped to the tree before this last platform correction;
the final integrated verification follows it rather than borrowing those passes.

The post-correction Services Debug assembly SHA256 is
`64EC526F634BF5DD8B99B7AF9D8FD95FFB39167C2DBAC13AEE0EACBE7076D173`.
The unchanged Agent assembly remains
`E651004021E101A1B4ACA26DEAFAFBC92840835D68A3E06C5469689423D8A0C9`.
The synthetic Windows vault process, three Agent process-death cases, fallback
provider and scoped Credential Manager probes passed against these binaries.
Linux repeated all four vault cases, all three Agent cases and the fallback
provider case successfully. The Linux repeat initially supplied the control
package root instead of its `app/` directory and failed in the driver before
acceptance; correcting that argument passed without a product change. Both
logs remain outside the checkout as `hermaeus-r34-linux-post-case-probes.log`
and `hermaeus-r34-linux-post-case-probes-corrected.log` beneath Windows TEMP.

The final Windows integration at
`%TEMP%/hermaeus-r34-gate-647a6465fded40f293d8ea437608abf0/` passed Debug and
Release builds with zero warnings/errors, then all 3,109 tests with no failures
or skips in 4 minutes 56 seconds. An earlier interrupted attempt had no terminal
TRX and no remaining test host; it was not counted as a completed test run.
Linux/Xvfb `key-path-final-x11.trx` passed 3,070 tests, with zero failures and
39 explicit Windows-only skips, in 7 minutes 10 seconds. Those skips match the
39 current `[WindowsOnlyFact]` declarations. Xvfb remains distinct from COSMIC
or physical desktop acceptance.

The final precommit `pwsh ./scripts/coverage.ps1` passed all 3,109 tests with no
failures/skips in 7 minutes 22 seconds. Line coverage was **72.73%**
(`60,852 / 83,673`), above the unchanged 60% floor. The script removed its
temporary report as designed; stdout is retained outside the checkout at
`%TEMP%/hermaeus-r34-vault-final-coverage.log`. This gate covers the integrated
codec, transaction, SecretStore, restore, Agent recovery and adversarial cases;
it does not close native/owner acceptance or authorise activation.

The verified implementation is split into reviewable commits: codec `f026705`,
transaction/recovery `8da8a37`, SecretStore integration and key identity
`b044ba3`, restore CRC `625ca55`, Agent recovery `8d22606`, and adversarial
boundary coverage `0de3e0c`. The integrated verification above covers their
combined tree. None activates v3 or closes the outstanding release gates.

## Notice delivery audit before the follow-on repair

The later implementation and actual Windows/Linux archive checks are in
[16](16-notice-delivery-and-integration.md). The audit below records why that
work was feasible; native-package and asset clearance remain separate gates.

A read-only cache audit found 44 packages in the Desktop/Local API union and
33 available upstream licence/NOTICE texts across 18 packages. None had a
byte-exact standalone match in the checked Windows packages. The raw local
availability/provenance report is under `%TEMP%/hermaeus-r34-notice-audit/`.
Delivering those existing texts is feasible follow-on packaging work; it is
not classified as blocked by native desktop access. The other 26 packages need
primary-source verification of missing texts. Native SQLite's package licence
declaration and embedded-font attribution also need source-specific review;
wrapper metadata alone is not clearance. Asset-provenance and owner legal
decisions remain separate release gates. No legal model changed in this audit.
