# R34 independent branch audit

2026-10-18, Australia. Scope: all 30 commits in `main..r34/round`,
baseline `23cec510c86a65d71c0dbffeaebe31b33973c8f5`, branch tip
`007013c2c58fd60ab6c51681f221f6353fa41777`. Findings refer to the final working
tree unless explicitly labelled pre-fix. This is a stabilisation audit, not a
new feature round or release approval.

## Executive assessment

The branch materially improves bounded process capture, ingestion limits,
callback ownership, approval recovery, credential backup exclusion and notice
delivery. Passing tests did not establish complete correctness: this audit
reproduced a serious inherited restore recovery-file deletion defect and two
Media lifecycle regressions. Their bounded repairs and regression coverage are
described below. The inherited uncommitted export cleanup repair was preserved
and included in verification, with its provenance kept separate.

The bounded Windows repairs can proceed to targeted exact-package native
acceptance. **Release remains blocked.** An initial Linux suite aborted with
an invalid-pointer native allocator error; completed reruns do not explain that
failure. This is an unresolved stability/evidence blocker alongside the mandatory
Windows/COSMIC, native/provider, performance and licensing gates. Production
vault v3 remains inactive. There is no first-party
media inference, new dependency, new approval authority or CI-policy change.

## Starting-tree provenance

The branch was already checked out and had the expected 30 commits. At entry,
ten tracked files were modified and `20-targeted-review-followup.md` was
untracked. The inherited changes were MediaFileService's exception-preserving
cleanup, its four regression cases, eight endpoint-prefix cases, the exited-root
Agent descendant case, and their changelog/feature/media/security/index updates.
Those are not attributed to this audit. The baseline build and 3,205-case full
suite include that inherited working tree, rather than pristine `007013c`.

This audit adds restore rollback preservation, cancellable Media picker waits,
Media callback identity/preview ownership, representative regressions and this
report. It corrects the unconditional rollback claims in current documentation
and the archive-relative notice-index wording. No existing work was reset,
discarded, or silently treated as a committed baseline.

## Review passes and commit coverage

The five passes were changed-code inspection; cross-subsystem integration;
security/adversarial analysis; resource/architecture analysis; and independent
reproduction plus verification of fixes. They were performed serially. Source
inspection is not equivalent to executing every platform or every failure mode.
The original 223-file diff includes archived R33 renames and large unchanged
upstream legal texts. Legal-text byte provenance was checked mechanically;
this is not an independent legal opinion on every embedded dependency.

| Commit(s), oldest first | Changed behaviour and integration paths examined |
| --- | --- |
| `01efd40` | Initial R34 plan, authority map, implementation ordering and release gates |
| `1984384` | R33 archive moves; historical evidence remains historical |
| `8780c9a` | Minimum safety baseline versus native/performance acceptance |
| `0ac5b6b` | AgentProcessCapture, command/Git-status callers, outcome normaliser, cancellation and flood fixtures |
| `7023270` | Doctor, GPU telemetry, help and system probes; deadlines, stderr drain and unavailable evidence |
| `a921432` | Voice/setup/secret-command readers, stdin backpressure, retained logs and progress consumers |
| `479b7c4` | Local/PDF/web readers, watched hashes, Chat PDF attachment budgets, parent/child staging and atomic dataset publication |
| `408f93e` | Chat/RAG load, detach, replacement, scroll/dictation/picker callbacks and retained VM ownership |
| `344ba75` | Activity/Agent/Doctor/Logs/Memory/Models/Settings/Services/Wizard callback siblings; Benchmark load coalescing/retry |
| `1629c92` | Shared Agent target owner, editor save snapshots, conflict/readback and queued draft/selection changes |
| `b26fad5` | Initial packaging notice and dictionary delivery |
| `1a169ae` | Safety receipts and conditional vault contract; implementation requirements remain distinct from execution |
| `f026705` | Inactive AEAD envelope, format dispatch, strict UTF-8, KDF/AAD, tamper and fixed-vector coverage |
| `8da8a37` | Inactive vault transaction, key relocation, locking, recovery identity/review, interruption and publication paths |
| `b044ba3` | Default v2 versus explicit internal v3 opt-in, missing/corrupt key handling, legacy relocation and mutation validation |
| `625ca55` | ZIP length/CRC checks, preflight, staging, publication, rollback and original-file retention |
| `8d22606` | Interrupted receipts before child projection, parent owner interactions, terminal states and repeated reopen |
| `0de3e0c` | Prepared approval tamper tests, diagnostic sinks, actual Local API middleware/routes and host-authority recipe exposure |
| `4f86c9b` | Native/vault process receipts and their stated limits; no inherited result counted as fresh execution |
| `164e55c` | Versioned licence/notice inventory, immutable legal bytes and provenance index |
| `2eed469` | Both archive scripts and notice guards; archive delivery versus unresolved redistribution terms |
| `df072fd` | Credential artifact names, normalised restore targets, backup source/destination reparses and private Unix snapshots |
| `268de86` | Inactive protected-vault directory separation, snapshot exclusions and synthetic fault cases |
| `9d44eb4` | Controlled autosave isolation in trust-scan test; no weakening of production persistence |
| `3021293` | Backup privacy evidence and open native/legal boundaries |
| `bda6332` | Historical planning gates versus current receipts |
| `d79000b` | Media request contracts, credential resolution, URI prefixes/origins, finite polling/deadline, byte limits, signatures and atomic export |
| `5e2d00b` | Media DI/navigation/palette, provider settings and shared save results, preview, session state and shutdown integration |
| `71843d3` | Bundled media help, archive layout and receipts |
| `007013c` | Owner deferral of first-party inference; no engine or dependency adoption |

### Integration and adversarial conclusions

- Agent capture bounds retained characters while continuing both drains. The
  command normaliser consumes structured cancellation/failure signals instead
  of promoting an observed zero exit to success. Large Git status deliberately
  retains its beginning. A stopped root is not descendant containment.
- Doctor/system/help/GPU, setup/voice and OS-secret paths have differing evidence
  and output contracts. Truncated help/counter/secret output is refused rather
  than parsed as complete. These owners were audited as siblings; they were not
  replaced with a speculative universal process manager. OS kill calls themselves
  are synchronous and not pre-empted by the asynchronous cleanup budgets.
- Approval execution still uses deterministic classification, prepared identity,
  current policy/preimage validation and shared task/target ownership. Owner
  editor saves share the target lock without acquiring model approval authority.
  Recovery observes post-images and never replays interrupted commands. The
  two-pass child projection preserves healthy sibling questions. Direct tool
  path containment does not sandbox an approved build recipe's transitive code.
- Local API tests compose the real middleware and routes with controlled stores
  and providers. Health liveness is deliberately unauthenticated; missing,
  wrong, revoked or unconfigured tokens cannot reach protected data. The
  self-reported client header does not become authenticated ownership. These
  fixtures do not establish live network binding/firewall behaviour.
- Ingest source extent, retained extracted text, pages and accounted candidate
  text/vectors have separate limits. Budget failures precede generation
  publication; retained sources and parent chunks participate. Watched-source
  hashing shares local reader limits. A PDF parser's intermediate memory or
  synchronous wall time is not bounded by these checks. That is an existing
  documented limit, not a newly discovered bypass of the promised payload limit.
- Media requests freeze endpoint/configuration, resolve secret references and
  use per-request credentials. Production HttpClient disables redirects/cookies.
  Same-origin downloads, bounded actual reads, base64 budgets and format
  signatures are distinct controls. Container recognition is not full decoding.
  ComfyUI nodes and queued backend jobs remain server authority. No global
  interrupt, automatic retry, capability probe or first-party worker was added.
- Production SecretStore still selects v2; only an internal verification
  constructor selects v3. Authenticated snapshots/locators remain isolated.
  Missing protected keys and unsupported versions do not silently generate
  replacement identities in the reviewed paths. Inactive codec tests do not
  authenticate production CBC. Existing same-user race and downgrade limits
  remain explicit.
- Backup restore bounds declared and actual expansion, verifies CRC before
  publication and rejects reserved credential targets. CRC is not authenticity.
  The late rollback branch was insufficient despite those preflight controls;
  finding A01 repairs that distinct failure window.

## Ranked findings register

| ID | Severity | Classification | R34 disposition |
| --- | --- | --- | --- |
| A01 | High | Confirmed inherited data-integrity defect | FIXED in source and covered automated cases |
| A06 | High | Observed Linux native abort, source/root cause unknown | Unresolved R34 stability/evidence blocker |
| A02 | Medium | Confirmed R34 shutdown regression | FIXED in source and automated cases; native picker acceptance open |
| A04 | Medium | Inherited uncommitted export cleanup defect/repair | Preserved and reverified; not newly discovered here |
| A03 | Low | Confirmed R34 view-ownership/resource regression | Repaired; non-blocking in isolation |
| A05 | Low | Confirmed archive-relative documentation defect | Corrected; non-blocking |
| I01 | Improvement | Media temporary-buffer pressure | R35 measurement first |
| I02 | Improvement | Ingest work before retained-budget refusal | R35 measurement first |
| I03 | Improvement | Repeated process-owner plumbing | R35 only if contracts permit net simplification |
| I04 | Improvement | Vault test fixture bypasses shared scratch ownership | R35 test infrastructure cleanup |

### A01. Failed restore rollback deletes remaining originals

**Location:** [BackupService.cs:327](../../../src/Hermaeus.Services/BackupService.cs#L327),
rollback through line 366. The pre-fix branch caught rollback errors and then
unconditionally recursively deleted `backupRoot` in `finally`, contradicting its
own comment that originals would remain inspectable. This exact defect also
exists on the `main` baseline; it is not attributed to the R34 CRC repair.

**Impact:** after a late publication failure, a denied rollback can destroy the
only remaining original file while reporting only the publication error.
Preflight, ZIP size checks and CRC do not cover this phase. Current features
and user-guide wording unconditionally described rollback as safe.

**Reproduction:** a two-file archive overwrites existing synthetic originals;
publication of the second member fails, then rollback of the first fails.
`rollback-data-loss-red.trx` recorded one pass (successful rollback control) and
one failure because the recovery directory was empty/absent. An earlier fixture
used mixed path separators and failed to inject its faults; that run does not
prove this defect. Paths were normalised before the conclusive reproduction.

**Correction:** collect rollback failures while attempting remaining entries;
preserve recovery storage on any rollback failure; throw an AggregateException
with publication first and rollback causes after it, naming the retained path.
Repeat existing reparse checks before rollback deletion/replacement. Successful
rollback retains the original exception. Successful restore still cleans its
staging/recovery storage. No new restore format or dependency.

**Regression requirements:** successful rollback must restore exact originals
and remove scratch storage; incomplete rollback must preserve exact remaining
originals and both errors; later entries must still be rolled back. Added two
controlled move cases plus a real Windows read-only replacement/directory
conflict case. The latter causes actual File.Delete/File.Move failures and
checks that both original files remain recoverable. These are not disk-full,
power-loss, hostile live-swap or directory-fsync proofs.

**Disposition:** R34 release blocker while unfixed. Repaired in this audit;
final automated evidence below governs closure. Manual recovery remains needed
after a genuine incomplete rollback; a mixed Data Root is not labelled unchanged.

### A02. Media pickers can hold shutdown before managed services stop

**Location:** [MediaGenerationViewModel.cs:115](../../../src/Hermaeus.ViewModels/MediaGenerationViewModel.cs#L115),
workflow import at line 135 and shutdown at line 154;
[MainWindowViewModel.cs:545](../../../src/Hermaeus.ViewModels/MainWindowViewModel.cs#L545).

**Impact:** export awaited a picker without cancellation; workflow import passed
a token but its native picker cannot consume it until after the dialog returns.
Shutdown cancelled the operation then joined its command, so either open dialog
could hold the Media owner indefinitely. MainWindow stops managed services after
that await. The application-wide deadline reports incomplete shutdown, but does
not make those skipped owner operations complete.

**Reproduction:** both controlled picker tasks remain pending, ignore their
tokens, and are released in test cleanup. The shutdown joins failed their
two-second liveness guards in `audit-red.trx`. This proves the wait defect, not
an observed physical desktop dialog or an actual orphaned runtime.

**Correction:** use .NET Task.WaitAsync with the operation token at both picker
awaits. Existing shutdown checks reject late results. The native platform still
owns dialog dismissal; cancellation does not pretend to close an OS dialog.

**Regression requirements:** shutdown completes while either picker is pending;
busy state clears, commands remain disabled, and late workflow/path results
cannot mutate state or export a file. Both cases pass after the repair.

**Disposition:** R34 correctness/lifecycle repair, not R35 redesign. Exact-package
Quit/restart with native pickers remains owner acceptance.

### A03. Obsolete Media views retain active ownership

**Location:** [MediaGenerationView.axaml.cs:27](../../../src/Hermaeus.Desktop/Views/MediaGenerationView.axaml.cs#L27),
callback capture at line 63 and preview update at line 79.

**Impact:** unlike the repaired Chat/RAG and page siblings, Media's ownership
predicate checked view/context/version only, not whether it still owned the VM's
callbacks. Two overlapping loaded views sharing the VM both decoded each new
preview. An older outstanding picker also lacked replacement-owner invalidation.
No speedup, leak size or physical stale-dialog action is claimed.

**Reproduction:** two real Avalonia control hosts share one Media VM. Selecting
a synthetic PNG created a Bitmap in both views. `audit-red.trx` failed the
obsolete-view null-preview assertion while the current view still decoded it.

**Correction:** bind ownership to the exact save/import delegates, preserving
the existing per-delegate release checks. Refuse new preview decoding without
that ownership. Existing unload/reload/context-replacement tests stay intact.

**Regression requirements:** obsolete preview remains empty while current
preview is real; old unload must not erase new callbacks; same-VM reattachment
and image-to-audio disposal must still work. Actual picker races remain a native
interaction gate, rather than being inferred from the preview fixture.

**Disposition:** non-blocking resource/ownership regression in isolation, safely
repaired for R34. No shared lifecycle framework was introduced.

### A04. Export cleanup obscures the primary failure

**Location:** [MediaFileService.cs:64](../../../src/Hermaeus.Services/MediaFileService.cs#L64)
through line 78. Full original reproduction/repair provenance is in
[20](20-targeted-review-followup.md).

**Impact/evidence:** staging deletion could replace a cancellation/publication
error. The inherited repair retains both causes and never reports clean export
or clean cancellation when deletion fails. Tests include actual Windows
read-only deletion plus injected publication/cancellation combinations.

**Correction/regressions:** retain the inherited minimal AggregateException
repair and run those regressions in the full current-tree suite. Cleanup targets
the exact staging path; a denied deletion can still leave private bytes. No
erasure guarantee or broadened deletion sweep is appropriate.

**Disposition:** retained R34 correctness repair, not a new discovery by this
audit. Prior report test results remain historical; this audit's suite is fresh.

### A05. Notice-index link resolves beneath the wrong archive directory

**Location:** [THIRD-PARTY-NOTICES.md:28](../../../THIRD-PARTY-NOTICES.md#L28),
copied into `docs/` by `build.ps1` and `build.sh`.

The source-relative link `docs/third-party/README.md` is correct at repository
root, but resolves to `docs/docs/third-party/README.md` beside the copied archive
inventory. Both scripts actually deliver `docs/third-party/README.md`.
Source inspection establishes the path mismatch; this audit does not claim a
newly rebuilt archive was opened in a document viewer.

Correction: state the source and archive-relative paths explicitly, without
publishing one misleading universal relative link. Validation is the script
layout comparison and final diff review; no production behaviour changed and
no extra regression test is justified for this prose-only correction.
Non-blocking for R34, corrected here.

### A06. Linux full-suite native abort remains unexplained

Later dump-capture validation, repeated suites and partial Windows acceptance
are recorded in [22](22-crash-diagnostics-and-windows-acceptance.md).
They do not establish A06's root cause or close its release blocker.

**Location:** the actual Linux test-host process; a responsible source line has
not been established. [AvaloniaTestHost.cs:39](../../../src/Hermaeus.Tests/AvaloniaTestHost.cs#L39)
is the native-control bootstrap under test, not a proven corrupting call.
It would be false precision to assign the allocator failure to that line,
Media's decoder, SQLite, a driver, or the operating system without a stack.

**Evidence/impact:** the first full Xvfb run terminated with
`free(): invalid pointer`. VSTest reported 1,308 passes and 12 skips before
`Test Run Aborted`; its summary outcome is Failed. The last recorded completed
tests included Media ViewModel cases, but completion order does not identify
the invalid free or any earlier heap corruption. No kernel core, VSTest sequence or
usable native dump was retained by that first non-instrumented run.

The actual distribution is **Ubuntu 26.04.1 LTS**, SDK 10.0.112, runtime 10.0.12.
The WSL registration happens to be named `Ubuntu-24.04`; it is not evidence of
the installed OS version. Native COSMIC execution is still unavailable here.

**Investigation:** the complete 10-case Media VM/native-view selection passed.
Two complete runs with `--blame-crash --blame-crash-dump-type mini` each passed
3,169 tests with the same 42 Windows-only skips. Their native controls were not
disabled and the renderer was not changed. Both skip sets exactly match the
42 current WindowsOnlyFact declarations. No dump is expected from a completed
run. The exact `main` baseline is separately archived/built as a control;
terminal control and further diagnostic results are recorded below.

**Minimal correction:** first obtain a repeatable trigger/native stack and
compare the affected subsystem against the same-host baseline; then repair
the demonstrated owner or ABI defect. There is no safe source correction yet.
Do not suppress a test, force another renderer, weaken assertions, or blame
the host merely because instrumentation did not reproduce the crash.

**Regression requirement:** a representative failing native case followed by
repeated full sequential Linux runs under the default production renderer;
native owner acceptance remains separate. [The runtime crash-report controls](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/collect-dumps-crash)
were checked before enabling diagnostic-only environment variables in the
isolated control. No diagnostics configuration was persisted to the product.

**Disposition:** unresolved R34 blocker, not a speculative code defect and not
an R35 performance task. Completed reruns establish their own scope; they do
not close the earlier unexplained fatal abort.

## R35 opportunities, with measurement requirements

| ID | Exact source | Observation, minimal next investigation and acceptance |
| --- | --- | --- |
| I01 | `MediaGenerationService.cs:294-325` | Bounded download uses a growing MemoryStream then ToArray; JSON/base64 parsing and decoded arrays coexist. Profile allocations/peak working set at representative and maximum payloads before choosing exact-size buffering or a streaming parser. Preserve missing/false-length limits, strict decoding, cancellation and combined budgets. The 128 MiB output ceiling is not a process-memory ceiling. No measured speed or RSS saving is asserted |
| I02 | `RagPipeline.cs:102-103,239-247,618-623` | Existing embeddings are materialised and local retained-source budget is charged after candidate embedding. Measure large datasets and a near-budget refresh; consider validating the retained contribution earlier while preserving changed-source semantics and generation visibility. Test retained/parent/multi-batch/dry-run/web/reindex siblings. Do not silently change approved limits or claim parser isolation |
| I03 | `AgentProcessCapture.cs:16-114`, `DoctorService.Runtime.cs:877-916`, `SystemInfoService.cs:376-438`, `LocalModelCapabilityService.cs:493-568`, `VoiceProviderProcessRunner.cs:152-215`, `SecretStore.cs:641-772` | Several owners repeat bounded-drain/stop/join plumbing, but differ in output ordering, truncation authority, secret handling and cancellation contracts. First enumerate those differences, then evaluate whether a smaller existing .NET-based primitive can delete code without reversing Agent-to-Services dependency direction. No broad refactor selected; retain separate contracts and tests until equivalence is demonstrated |
| I04 | `FallbackVaultTransactionTests.cs:467-501` | Fixture creates a separate top-level temporary directory and deletes it on Dispose instead of using Helpers.TempDir's shared process container. Normal tests clean it, but abrupt host death bypasses that fixture's deletion. Replace this duplicate lifecycle with the existing helper and rerun codec/transaction/integration tests; no new scratch manager or product change is needed. This is test hygiene, not evidence that owner vault data leaks |

These are improvements, not proven correctness/security failures or measured
performance regressions. They do not independently block R34. Existing P1/P2/P3
host/corpus measurements and permitted Retain/Defer decisions remain in the
roadmap. Recall, inference cache policy, model eviction and runtime formats were
not redesigned during this stabilisation audit.

## Existing release gates and residual risks

The following remain pre-existing requirements or documented limitations,
not newly discovered defects disguised as findings:

| Gate | Current audit disposition |
| --- | --- |
| RG01/RG02 builds, suite, coverage | Fresh current-tree evidence is recorded below; previous receipts are not borrowed |
| RG03/RG12 complete native process/view lifecycle | Controlled root/pipe/native-control tests are available. Arbitrary escaped descendants, inaccessible processes, host death, OS kill latency and every native dialog race are not contained by this audit |
| RG04/RG11 ingest/retrieval | Payload/generation regressions exercised by the suite. Hard parser RSS/wall-time isolation and full labelled-corpus/scale equivalence are not established |
| RG05 approval/containment | Prepared identity and real route boundaries reviewed/tested. Approved command recipes still execute with host-user authority. Metadata path checks do not defeat every same-user TOCTOU race |
| RG06 vault | v3 implementation remains inactive; explicit native/provider/recovery/downgrade acceptance still gates activation. Production CBC authentication is not claimed |
| RG07 Windows and RG08 COSMIC | Prior partial Windows walkthrough remains historical. No fresh exact-package full owner matrix, physical audio/GPU inference or COSMIC/Wayland session was executed here. Ubuntu WSL/Xvfb is not COSMIC acceptance |
| RG09/RG10 runtime/performance | No controlled comparable per-host R33/candidate performance or VRAM result was produced. No performance winner or measurable speedup is claimed |
| RG13 packages/licensing | 71 recorded source/hash rows match retained legal-text bytes; all 33 NuGet-sourced rows independently match original cached nupkg members. The remaining pinned-source rows were not freshly downloaded. NuGet advisory query names all 13 projects and reports no vulnerable entries. Native SQLite package terms, full embedded attribution and unresolved artwork/pet rights remain owner release gates. Inventory/guard success is not legal clearance; old archives predate this audit's code |
| RG14 docs | Current feature/user/security/media wording updated for this audit; historical receipts retained. First-party inference stays deferred |
| RG15 publication | No push, PR, merge, tag, release, repository-setting or CI-policy action |

Current schema checks used the primary
[OpenAI video API reference](https://developers.openai.com/api/reference/resources/videos/methods/create)
and [ComfyUI route documentation](https://docs.comfy.org/development/comfyui-server/comms_routes).
They support protocol inspection only, not endpoint/model access or real inference.
Provider versions and accepted options remain server-specific.

## Fresh verification and repair receipt

Raw Windows test receipts are outside the checkout at
`%TEMP%/hermaeus-r34-independent-audit/`. Source snapshot and Linux receipts,
if executed, are identified below. Tests are sequential; no xUnit parallelism
was enabled. No owner application/settings/vault or paid provider was used.

| Command/receipt | Actual result |
| --- | --- |
| Baseline `dotnet build Hermaeus.sln` | Passed, zero warnings/errors, inherited working tree included |
| `baseline-full.trx` | 3,205 passed, zero failed/skipped, 4m43s |
| First rollback-test compile attempt | xUnit analyser rejected Assert.Empty over filtered data; no tests ran; corrected to Assert.DoesNotContain |
| `audit-red.trx` | Five failures: three valid Media failures, two invalid rollback fault fixtures due mixed path separators; not five proven defects |
| `rollback-red-corrected.trx` | Successful rollback control passed; incomplete rollback failed because only the original IOException survived |
| `rollback-data-loss-red.trx` | Successful rollback control passed; incomplete rollback failed because recovery directory/originals had been deleted |
| `audit-focused-green.trx` | 45 passed, zero failed/skipped; restore/credential/reparse/Media VM and native-view selection |
| `audit-native-rollback-green.trx` | 24 passed, zero failed/skipped; includes actual Windows read-only/directory-conflict rollback failure |
| `dotnet build Hermaeus.sln -c Release` | Passed, zero warnings/errors |
| `dotnet list Hermaeus.sln package --vulnerable --include-transitive --no-restore --format json` | Exit 0, NuGet source and all 13 projects, no vulnerable entries/errors. Advisory evidence is not proof of absence of every vulnerability |
| Notice provenance check | 71 indexed SHA256 rows checked, zero missing/mismatched files |
| Independent nupkg provenance check | All 33 NuGet-sourced index members checked against original cached package ZIP members; zero missing/hash mismatches. No package modified or downloaded |
| Final `dotnet build Hermaeus.sln` | Passed, zero warnings/errors |
| `audit-final-full.trx` | 3,211 passed, zero failed/skipped, 4m17s. The increase from 3,205 is exactly six new regression cases |
| Ubuntu Debug and Release solution builds | Both passed, zero warnings/errors, using the installed Ubuntu SDK and native temporary filesystem |
| Initial Ubuntu full suite, `results/` | Aborted with `free(): invalid pointer`, 1,308 passed/12 skipped before abort. Failed run; no full-suite pass claimed |
| Ubuntu Media isolation, `media-isolation/` | 10 passed, zero failed/skipped |
| Ubuntu diagnostic full suite, `crash-repro/` | 3,169 passed, zero failed, 42 declared Windows-only skips, 1m17s |
| Ubuntu diagnostic full repeat, `full-repeat/` | 3,169 passed, zero failed, identical 42 skips, 1m22s |
| Exact `main` Ubuntu control, `main-control-results/` | 2,853 passed, zero failed, 18 Windows-only skips, 1m01s. Its zero-warning build completed; the initial build wrapper then hit an extra carriage-return command. That wrapper error did not invalidate the separately completed test command |
| Ubuntu default-renderer repeat without attached crash utility, `default-repeat/` | 3,169 passed, zero failed, 42 skips, 1m16s; runtime crash reporting and VSTest sequence capture enabled, no crash reproduced |
| Final canonical `pwsh ./scripts/coverage.ps1` | Exit 0, 3,211 passed/zero failed/skipped, 6m44s; 72.92% line coverage, 61,572/84,443, unchanged floor 60%. Stdout retained as `coverage.log`; script removed its temporary report |

The Linux source snapshot is `/var/tmp/hermaeus-r34-audit-oI8AVY/source`.
Its transfer tar SHA256 is
`bdf69a4548df77ecbde3437352234366d03854e1302dda84459f647086c499c9`.
All 966 `src/` files were compared byte-for-byte with the Windows tree and
matched. Six shell scripts copied with Windows CRLF were normalised to their
Git LF checkout representation; all eight shell scripts were then checked as LF.
No legal-text or managed-source byte was normalised. The first WSL snapshot
invocation lost its shell variable across native quoting and failed before
copying; a stdin-driven retry produced the snapshot above. A later heredoc
delimiter error occurred after shell normalisation; the independent LF check
passed. Neither setup failure is counted as a product defect or test pass.

The baseline control and three complete current-tree Linux reruns did not
reproduce the allocator abort. That result limits attribution; it does not
resolve A06. No renderer, test, assertion, dependency or runtime pin was changed
to obtain these passes. No safe source repair is supported without a failing
trigger or crash stack. Retain A06 as an R34 blocker and capture the next failure
with the documented runtime diagnostics before changing native code.

Final diff review found no dependency, format activation, unrelated refactor,
test weakening or generated report in the staged scope. Only receipt/documentation
edits followed the final coverage gate. The local stabilisation commit containing
this report also contains the bounded repairs and the reviewed inherited
follow-up, with the provenance distinction above retained. Its subject is
`fix: retain restore recovery and close media lifecycle gaps`; the exact commit
identity is reported in the handover. No publication is authorised or performed.

**Final recommendation:** A01's data-integrity repair and the bounded Media
repairs have source and automated evidence and can proceed to targeted Windows
acceptance using a freshly built exact package. Release is not ready: A06's
unexplained native abort and the existing mandatory native/provider/performance/
licensing gates remain open. No fresh package, COSMIC, physical audio, real
media inference, native activation/recovery acceptance or performance benefit
is claimed. Production vault v3 remains inactive. The four I-items belong in
R35 investigation rather than speculative R34 subsystem redesign.
