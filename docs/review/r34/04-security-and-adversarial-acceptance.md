# 04. Security and adversarial acceptance

This is an R34 plan. Current controls remain in [security-review](../../security-review.md);
accepted open items remain in [security-roadmap](../../security-roadmap.md).
Source anchors are in [01](01-current-state-and-evidence.md). No penetration
test or hostile-process reproduction was performed during planning.

## Threat model and priority

Protect owner workspaces, credentials, chats, Memories, RAG generations, task
receipts, model/runtime identities, availability and process ownership. Treat
model/tool/MCP output, workspace build scripts, archives, web responses and
user-selected data files as untrusted. A trusted download hash proves content
identity against its source, not an uncompromised publisher. A hostile process
with the same OS identity is outside ordinary file-policy isolation; do not
claim protection from it through path strings or encrypted same-user secrets.

| Boundary / exposure | Current verified fact or gap | Priority and selected work |
| --- | --- | --- |
| Approved workspace commands | S09 uses direct argv and declared recipes. Build/test tools can execute workspace code with ambient user rights. Caller cancellation excludes the timeout kill branch; readers allocate full stdout/stderr before keeping 200 lines | **P0 S1:** deterministic reproduction, bounded capture, owned cancellation/exit drain. Hostile command side effects are a known absence of OS isolation, not a newly proven approval bypass. Sandbox adoption remains unapproved. |
| RAG input availability | S15 buffers web response bytes before its 2 MiB check; local warning follows complete reads/PDF extraction. Directory file batches do not bound one file or total retained generation data | **P0 S2:** enforce input/extracted/cumulative budgets and cancellation before publication; no partial visible generation. Explicit URLs/local selection reduce exposure, not memory impact. |
| Prepared actions and filesystem races | S07/S08 bind policy/preimage/proposal; S10 serialises in-process tasks/targets; tools reject path/reparse escapes | **P1 S4:** audit revalidation-to-open/rename windows and siblings including direct owner save, Rewind, restore and watched publication. No exploit demonstrated; string preflight is not a kernel sandbox. |
| Fallback secrets | S24 `v2:` CBC, salted key derivation, strict UTF-8 structural check, separate protected key; vault excluded from backup | **P1 S3:** versioned authenticated encryption with migration/recovery tests. No active exploit or guarantee against profile theft asserted. |
| Managed network binding | S12 already rejects conflicting `--host` separated/equals arguments and normalises agreeing values; Trust warns about broader flags | **P1 S4:** exact alias/env/runtime/preset audit. Decide remaining policy with owner; do not weaken existing hard rejection to match stale docs. CORS is not authentication. |
| Executable trust | TrustService hashes existing configured files; setup downloads verify pinned or publisher digests; Lab recovery checks PID/start/hash | **P1 S4/B0:** pin/display/observed identity review, replacement during launch/recovery, no blind process kill. Hash display exists; launch-time allowlist trust is a distinct decision. |
| Local API | S22 health is deliberately unauthenticated; other mapped operations use named tokens, loopback, tracing. Agent execution disabled in S23 | **P1 S4:** invalid/revoked tokens, stale child reload, method/path and health-prefix audit, body/output budgets. Do not add Agent routes or another task authority. |
| Backup/restore | S21 canonical final/stage/rollback containment, actual-byte expansion limits and transactional moves | **P1 S4:** preserve existing budgets and rejection tests, expand race/failure/reopen matrix. Manifest/preview remain conditional existing roadmap work, not missing traversal protection. |
| Privacy/redaction | Runtime log redactor, path-free v2 fingerprints, separate prompt bodies versus hash-only Lab evidence | **P1 S4:** synthetic provider-token fixtures, command output and exception paths, exports, voice/remote context disclosure. Redaction is not a network sandbox or plaintext-at-rest encryption. |
| Packages and native code | No advisories reported by dated NuGet audit; native/external runtime attack surface remains | **P1 B0/G1:** final RID/native/module/licence review, signed distribution remains owner/public-release decision |

## S1 command lifetime and output contract

Reproduce before changing code using controlled scratch command processes with
explicit started/exited/grandchild signals. Reuse `AgentToolExecutor`, declared
recipe matching and existing test fixtures; introduce only the smallest test
seam needed to inject a short timeout/controlled executable. A production bypass
for arbitrary commands is forbidden. Never run hostile fixtures in owner roots.

Proposed bounded contract:

- Cancellation after start always requests cleanup of the **owned** process
  tree, joins both readers and awaits a bounded exit. Timeout and user cancel
  retain different outcomes. Do not merely dispose `Process`, which is not a
  process termination operation. Exceptions and failed starts also drain what
  was actually started. On host death, test OS job/process-group guarantees
  separately; `entireProcessTree` alone does not establish future descendant
  containment.
- Drain stdout/stderr concurrently into bounded tails, initially 256 KiB per
  stream and at most the existing 200 reported lines. A single unterminated line
  must also be bounded. Mark truncation with retained/omitted evidence; preserve
  final compiler errors and exit code. The proposed byte budget is a reviewed
  resource policy, not a measured requirement; adjust before implementation if
  real diagnostics need more, without restoring unbounded buffering.
- Audit `InspectGitDiffAsync`, command launch, setup/helper/voice/MCP/runtime
  paths for the **same cancellation or unbounded reader class**. Reuse existing
  bounded process utilities where their contract actually fits. Do not replace
  all process wrappers with a generic framework.
- Inventory inherited environment variables, standard handles, executable PATH
  resolution and Windows job/Linux process-group ownership. Sanitising the
  environment can break real SDK/tool discovery, so any allowlist or new
  sandbox boundary requires owner review and native toolchain proof.

## S2 ingestion contract

Use bounded streams with content-length preflight as an optimisation, never as
the enforcement boundary. Read at most limit plus one byte; enforce actual
decoded/extracted output and cumulative work, including absent/false length,
compressed/chunked responses, file growth after stat and huge PDF text.
Reuse the existing 2 MiB web limit. For local input, propose turning the current
50 MiB warning threshold into a pre-read default refusal, with a separately
bounded decoded/extracted character and PDF-page budget. Freeze those exact
limits and any explicit override policy in B0/S2 before code; no silent
truncation, arbitrary default expansion or new settings section.

PdfPig opens/parses synchronously and checks cancellation between pages
(`PdfTextExtractor.ExtractAsync`). A cancellation token or `Task.Run` cannot
forcibly interrupt a parser stalled inside native/managed work. Prove whether
file/page/text limits suffice. If strict wall-clock/memory enforcement needs an
isolated worker, stop at an owner-reviewed security-boundary decision; retain a
truthful limit instead of claiming a hard parser timeout. Tests must include
dry-run, manual/watch/web/Help paths and pre-publication source revalidation.

## S3 fallback vault migration contract

Use .NET `AesGcm` where supported, a new version prefix distinct from `v2:`,
fresh nonces and an explicit format/algorithm identity authenticated with the
payload. Preserve the existing OS secret stores and protected key location.
Read the legacy salted and older compatible CBC formats; write new values in
the new authenticated format. Never fall back to CBC after a recognised new
payload fails authentication. Do not overwrite unreadable values with empty
strings or generate a replacement key silently.

Choose lazy re-encryption after successful read or an explicit atomic migration
only after proving crash/write-failure recovery. Document older-app downgrade
limits before migration. Keep a recoverable old vault until atomic replacement
is verified; do not retain plaintext migration artefacts. Tamper/tag/nonce/key
failure, concurrency, missing-key migration, fresh reopen, backup exclusion and
remote-provider use all need regression coverage. Owner reviews the persistence
format/downgrade contract before landing S3; this plan does not enact it.

## Sandbox research and decision

Dated, primary-source evidence is pinned in R01/R02 in
[10](10-technology-decisions.md). **Defer production integration of both.**
The host-side prepared action, exact approval and durable receipt remain the
only mutation authority. A sandbox, if later approved, enforces a narrower
execution envelope beneath it; its policy UI, auto-approvals or credential store
must not grant independent Hermaeus action authority.

| Dimension | Microsoft Execution Containers | NVIDIA OpenShell |
| --- | --- | --- |
| Windows 11 fit | In-process SDK/native assets; exact OS build/backend capabilities must be probed. Verify argv, workdir, redirected handles, SDK/process-tree cleanup and host preparation | Current supported Windows path is experimental WSL2, with a Linux execution environment and gateway. Do not treat proposed/native driver work as released parity |
| Pop!_OS/COSMIC | Bubblewrap backend requires host user namespaces and bwrap; network modes have additional prerequisites. Filesystem/build-tool compatibility and mount identity require native proof | Linux kernel enforcement plus gateway/container or VM runtime; verify actual Landlock ABI, seccomp and configured policy enforcement rather than distribution name |
| Enforcement risks | Backend-specific unsupported policy must fail closed. Audit/permissive modes cannot establish containment. Session/experimental backend capability is not interchangeable with process containment | Mandatory baseline differs from additional filesystem policy; require hard enforcement for all restrictions. No independent policy-widening approval or uninspected credential proxy |
| Deployment/lifecycle | New native SDK dependency and possibly host setup/tools, hashes/licences, denied-start diagnostics, full teardown/uninstall review | CLI, gateway, image/runtime, policy and credential/routing machinery are substantial new owners; their upgrade/recovery/telemetry costs need justification |
| Resources | Cold start, repeated commands, idle memory, child fan-out and storage cost Unknown | Same measurements plus gateway/image/VM overhead Unknown |
| R34 exit | Research receipt and owner decision only. A limited disposable spike requires explicit approval for dependencies/security boundary | Same. Preview/experimental limitations block production reliance on those paths |

Do not require either sandbox to complete S1/S2, and do not present today's
workspace command recipes as OS-contained while research remains deferred.

## Adversarial acceptance matrix

These are **required future cases, all unexecuted/Unknown in R34**. Extend
existing `AgentCommandRiskClassificationTests`, `WorkspaceCommandFamilyCoverageTests`,
`AgentPatchReviewServiceTests`, `AgentLifecycleRecoveryTests`, `ServiceTests`,
`BackupRestoreSafetyTests`, `BackupMigrationTests`, `RagTests`, `LocalApiTests`
and process/lifecycle fixtures. A test file name is a seam, not evidence it
already covers this case. Use event signals and injectable short timeouts.

| Case | Hostile input / timing | Required refusal or bounded outcome | Package |
| --- | --- | --- | --- |
| A01 | Traversal, absolute/UNC/drive path, case-variant sibling, symlink/junction ancestor | No out-of-root read/write, same policy for direct/queued/rewind/restore paths | S4 |
| A02 | Replace ancestor or file after prepare/approve and before open/move | Revalidation conflict/refusal, or documented stronger handle containment; never claim protection without race proof | S4 |
| A03 | Edit content/policy/proposal/revision/fingerprint after review | Refuse stale approval; no mutation or consumed newer action | S4 |
| A04 | Two tasks and owner save target same file | Single correct preimage winner; loser conflicts, no silent overwrite | S4/L2 |
| A05 | Crash after write before final receipt; child/parent restart | Readback reconciliation, ambiguous Interrupted/Unknown, no blind replay/approval inheritance | S4/L2 |
| A06 | Shell syntax, undeclared recipe, unknown tool, fake model approval | Deterministic Blocked; constrained/native tool protocols have equal authority | S4 |
| A07 | Approved npm/MSBuild recipe reads a synthetic outside canary or sends data | Current host execution is **not expected to be sandbox-refused**. Demonstrate residual exposure safely with controlled local fixtures. A future approved sandbox must deny both; never infer that from approval gates | S4 research |
| A08 | Cancel immediately after process start, during output, or parent exit with a live grandchild | S1 must stop/join owned tree within deadline or retain incomplete evidence; no clean success/orphan claim | S1/L2 |
| A09 | Infinite stdout/stderr, huge unterminated line, reader failure | Bounded memory and tail, pipe drain, truthful truncation/cancel/exit; final diagnostics retained | S1 |
| A10 | PID reused, executable replaced, corrupt/missing ownership record | No kill on ambiguous identity; corrupt record remains blocking Unknown | S4/L2 |
| A11 | Host override aliases/equals/multi-address values, environment/preset override | Existing core host conflicts refused; unresolved recognised escape either explicitly denied by approved policy or accurately warned; verify final listener | S4 |
| A12 | Large local text/PDF, grow file after stat, extreme page/text expansion | Refuse actual budget excess before unbounded accumulation; previous RAG generation unchanged | S2 |
| A13 | Chunked/no-length/false-length web response, compressed expansion, never-ending body | At most bounded body/work plus cancellation deadline, no publication or silent truncation | S2 |
| A14 | Restore ZIP traversal, duplicate aliases, false sizes, zip bomb, symlink swap, late disk-full/cancel | Budgets/containment hold; no out-of-root writes; staging/rollback preserve existing data | S4 |
| A15 | Bit-flipped/truncated vault, wrong tag/key/nonce, migration write failure | Fail authentication closed, legacy still readable, no corrupt rewrite; backups contain no secrets | S3 |
| A16 | No/wrong/revoked token, spoofed client name, `/health/...`, unexpected methods, Agent route calls | Correct 401/503 or routing refusal; health reveals only liveness; verified identity used in trace; no Agent execution | S4 |
| A17 | Provider secrets/home paths in child output, exceptions, exports or MCP descriptions | Redact persisted diagnostics; descriptions never widen gate; no claim arbitrary content redaction is perfect | S4 |
| A18 | Shutdown during embedding, Lab restore, ONNX inference or pending approval | Stop new work, bounded drain, retained cancellation/recovery receipt, incomplete when ownership unresolved | L2 |
| A19 | New AEAD payload reopened by old build | Explicit downgrade refusal/compatibility documentation, no destructive fallback | S3 |
| A20 | Optional sandbox missing/broken policy or unsupported platform; attempted policy self-widening | Future contained action refuses without an unsandboxed fallback; existing Hermaeus approval remains required | S4 research |

Classify hard security failures independently from performance. A faster denied
operation, stale approved action or silently dropped receipt cannot pass R34.
