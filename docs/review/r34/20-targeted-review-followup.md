# R34 targeted independent-review follow-up

2026-10-17. Starting branch: `r34/round`, clean at `007013c`.
The four observations were hypotheses, evaluated against current source and
existing R34 contracts. Only the reproduced export exception-masking defect
changes production code. No dependency, process manager, security authority,
persisted format, native inference or vault activation change is selected.

## 1. OpenAI-compatible endpoint paths: verified existing

`MediaGenerationService.ValidateEndpoint` normalises the complete absolute URI
to a trailing slash. `GenerateOpenAiAsync` trims the path only for its version
test; the selected URI retains that slash. For `/v1/` it reuses the endpoint;
otherwise resolving relative `v1/` appends beneath the preserved prefix.
The later relative image/audio/video routes preserve that prefix too.

The existing image test covered `/api/v1` without its trailing slash. Eight new
cases cover empty/root, `/v1`, `/v1/`, `/gateway`, `/gateway/`, `/gateway/v1`
and `/gateway/v1/`. Every case executes image creation, audio-output chat and
video creation/content retrieval, asserting each complete request URI and method.
All eight passed before any production repair. No endpoint implementation
failure was demonstrated, so URI construction remains unchanged.

## 2. Agent process cleanup: honest best effort, containment remains unproved

The deadline includes both root exit and pipe drain. A live root receives
`Kill(entireProcessTree: true)` on cancellation, timeout or capture failure.
Kill exceptions retain an incomplete-termination diagnostic for an aggregate
failure or an observed live root. An already exited root cannot be used to
reconstruct descendants safely; that branch explicitly reports unverified
descendant ownership instead of claiming termination.

Cleanup has a separate bounded asynchronous wait. Read/drain failure and cleanup
deadline diagnostics remain in `CleanupDetail`; late task faults are observed.
The synchronous OS kill itself is not pre-empted by that asynchronous deadline.
Root exit code remains an observation, not proof of containment. Both the command
and Git-status callers retain failure/cancellation signals and cleanup detail;
the command caller does not promote a cleanup exit code to completed success.

A new actual Windows fixture launches a child which inherits the capture pipes,
signals its PID and controlled parent PID through a named pipe, then waits.
After observing root exit while the child and pipe drain remain live, the test
cancels capture. It asserts Cancelled, observed root exit code zero, the exact
unverified-ownership diagnostic, no tree-termination-request claim, and a still
live child. Its `finally` terminates and verifies the fixture child's exit.
Existing live-root descendant cancellation, decoder failure, timeout and stream
flood cases were also exercised. No production Agent change was justified.

This is evidence for accurate reporting and the known ownership limit, not a
clean-containment pass. Arbitrary descendants, host death, inaccessible processes,
Linux groups/sessions and native launch guarantees remain outside this proof.
No new manager or containment boundary was introduced.

## 3. Media export cleanup: reproduced and repaired

The original `finally` deleted the staging file directly. If export or caller
cancellation threw and deletion also threw, the deletion exception replaced the
earlier cause. A small internal deletion delegate allows deterministic failure
injection after real file creation/write/publication attempts; public callers
still use `File.Delete` and unchanged filesystem operations.

The first focused run passed 14 and failed three (`media-followup-red.trx`):
injected deletion hid cancellation and failed publication, and an actual Windows
read-only staging file caused `File.Delete` to throw UnauthorizedAccessException
which hid cancellation. The endpoint cases and normal export/path cases passed.

The repair records and rethrows the original exception. If staging deletion also
throws, it raises an AggregateException containing the original cause first and
the cleanup cause second, retaining their stacks. Successful cleanup preserves
the original exception type, including ordinary cancellation. No deletion failure
is converted to success or clean cancellation. Atomic move, existing-target
preservation on failure, Unix private mode, extension and reparse checks remain.
Only the exact staging path is eligible for deletion. Failed deletion may leave
that file; there is no erasure or filesystem-identity guarantee.

Regression cases cover cancellation plus deletion failure, actual publication
failure with successful/failed deletion, and real Windows read-only deletion.
Existing exact atomic export, cancellation, extension, reparse and import bounds
tests still pass. The Media ViewModel's general export-failure handler also
handles AggregateException; it does not display completed export on this path.

## 4. Fallback v3 activation: verified disabled

Production DI registers `ISecretStore` to `SecretStore`. Its public constructor
chains to the internal default with authenticated fallback `false`. Only the
explicit internal verification constructor accepts `true`; no setting or
environment flag activates it. Default reads/writes/removals do not enter the
authenticated transaction path.

The existing `Default_constructor_chain_keeps_v2_and_explicit_test_optin_migrates_whole_vault`
fixture was rerun using synthetic values and an isolated key directory. It writes
and resolves `v2:` through default construction, then separately opts into v3 for
the tested migration. Other selected inactive-reader/corruption/key identity
regressions also pass. No real credentials or owner vault were used.

Current `docs/features.md`, the Unreleased changelog, security review/roadmap,
S3 decision, execution receipts and G1 roadmap explicitly distinguish inactive
AEAD implementation from production CBC. The user guide contains no active-v3
claim. G1 acceptance requirements are not evidence that activation occurred.
No misleading active-authentication claim or activation defect was found;
no vault code or existing activation claim required correction.

## Executed evidence and remaining gates

- Windows focused selection: 48 passed, zero failed/skipped, 4s
  (`targeted-followup-green.trx`).
- Linux focused selection: 44 passed, zero failed, four Windows-only skips, 2s
  (`targeted-followup-linux.trx`). The new actual Windows process/deletion cases
  are skipped rather than reported as Linux passes.
- Raw Windows receipts are outside the checkout under
  `%TEMP%/hermaeus-r34-review-followup/`; Linux TRX files are under
  `/var/tmp/hermaeus-r34-linux-tests/`.

The later complete Windows/Ubuntu branch audit, full suites and canonical
coverage receipt are recorded in [21](21-independent-branch-audit.md), including
an initial unexplained Linux native abort that remains a release blocker despite
completed reruns. Those current-tree checks include this inherited follow-up.
No packaging layout or dependency changed. The preceding
development archive receipts predate this export repair and are not fresh binary
or packaged GUI acceptance for it. Native Windows/COSMIC product workflows,
S3 activation/recovery acceptance, performance attribution and unresolved release
licensing remain open. First-party inference remains owner-deferred.
