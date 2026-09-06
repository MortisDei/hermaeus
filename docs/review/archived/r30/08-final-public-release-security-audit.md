# R30 final public-release security audit

Date: 2026-08-23

Audited base: `d80758b50a65d8972e8117ae79ec1d98a3fbd929` on `r30/round`

SecretStore reassessment base: `9a646db8750482c218802f61354c79263963ae5d`

Final Windows packaging base: `d32e928825bc911a21a1c8c4fa68795020cf4e42`

Scope: complete release candidate, reachable Git history, packages, workflows,
runtime boundaries, security-sensitive documentation, and bounded audit fixes

## Executive verdict

**CONDITIONAL PASS**

The frozen candidate contained one HIGH Agent approval-boundary failure and one
MEDIUM executable supply-chain weakness. A focused follow-up found a second
MEDIUM weakness: the fallback vault and its decryption key were both stored in
the portable data root. All three are remediated in the local tree with
regression coverage. One LOW package information disclosure was also fixed. No
CRITICAL, HIGH, or MEDIUM finding remains open in the audited local tree.

Public release must not proceed from remote PR head `d32e928...`. The final
Windows packaging commit must first be incorporated into PR #8, both CI legs
must pass on that exact head, and the package-root `Hermaeus.exe` must be
double-clicked once on Windows to confirm it starts the bundled application.
Broader Windows 11 and Linux dogfooding is complete. Updater-path testing is not
a release requirement. No push, merge, tag, release, visibility change, or
publication was performed by this audit.

## Findings summary

Counts describe findings as discovered, before local remediation:

| Severity | Count | Open after audit |
| --- | ---: | ---: |
| Critical | 0 | 0 |
| High | 1 | 0 |
| Medium | 2 | 0 |
| Low | 6 | 5 |
| Info | 2 | 2 |

## Detailed findings

### HERM-SEC-001: Patch review could approve an unrelated pending Agent action

- **Severity:** HIGH
- **Component:** `AgentPatchReviewService`, `AgentService.AppendApprovalAsync`,
  Agent review UI
- **Evidence:** Before remediation, patch apply and revert called
  `AppendApprovalAsync(... approved: true ...)` with the fingerprint of
  `task.PendingToolAction`. Reject and block called the same generic approval
  method with `approved: false`. `AppendApprovalAsync` authenticates the
  fingerprint but does not bind its audit-label argument to a review surface;
  an approved call executes whichever pending action owns that fingerprint.
  The patch preview UI displays the patch, not the separate pending tool
  action.
- **Attack/preconditions:** A task retains a queued draft patch while also
  waiting on a command, write, sub-task, or MCP action. The user reviews only
  the patch and clicks Apply or Revert.
- **Impact:** The unrelated pending action could execute without its own
  displayed approval. Reject or block could dismiss that action. This is a
  concrete cross-surface approval bypass and can cross the filesystem/process
  boundary.
- **Why controls failed:** The pending-action fingerprint correctly prevented
  stale-action substitution, but the patch service supplied the pending
  action's current fingerprint even though the user was deciding something
  else.
- **Remediation/status:** **Remediated locally.** Patch decisions now persist
  only patch state and have no dependency on `IAgentService`. Regression
  coverage keeps an unrelated `create_file` action pending and proves it is not
  executed by patch apply/revert. This blocks public release unless the fix is
  incorporated.

### HERM-SEC-002: Managed llama.cpp executable archives were not verified

- **Severity:** MEDIUM
- **Component:** `LlamaServerSetupService`, managed runtime install/update
- **Evidence:** Both pinned and latest paths downloaded a GitHub release
  archive, extracted it, located `llama-server`, and later executed it without
  a checksum. The source comment incorrectly said GitHub did not expose asset
  hashes. The live b10034 release API and release page publish a `sha256:`
  digest for each asset.
- **Attack/preconditions:** Corruption or malicious replacement occurs in the
  archive delivery/publisher path, or the wrong archive bytes are returned for
  the selected metadata.
- **Impact:** Hermaeus could extract and execute unverified native code under
  the user's account.
- **Why controls failed:** HTTPS and repository identity were treated as the
  complete provenance control, despite SHA256 verification being used by
  managed model, embedding, reranker, and voice downloads.
- **Remediation/status:** **Remediated locally.** Pinned b10034 CPU archives for
  all six platforms use source-controlled SHA256 values. Latest and CUDA
  companion assets require a valid digest in GitHub release metadata. Archives
  are verified before extraction and deleted on mismatch. Tests cover every
  pinned platform, digest validation, successful verified extraction, and
  mismatch refusal. This blocks public release unless the fix is incorporated.
  The upstream [b10034 release](https://github.com/ggml-org/llama.cpp/releases/tag/b10034)
  is the metadata source.

### HERM-SEC-003: RAG input limits are applied after unbounded buffering

- **Severity:** LOW
- **Component:** `RagPipeline`
- **Evidence:** Local text files are read by `File.ReadAllTextAsync` before the
  50 MB condition records only a warning. Web responses use
  `ReadAsByteArrayAsync` before rejecting content over 2 MB.
- **Attack/preconditions:** The user explicitly ingests a hostile large local
  file or an attacker-controlled HTTP(S) endpoint that streams a large body
  within the 15-second request timeout.
- **Impact:** Excessive memory use and an application-level denial of service.
  No code execution or data disclosure path was found.
- **Remediation/status:** **Deferred.** Enforce a pre-read local size ceiling,
  reject oversized `Content-Length`, and copy unknown-length responses through
  a bounded stream. This does not block release because ingestion is explicit,
  availability is the only demonstrated impact, and the request timeout bounds
  the web case in time.

### HERM-SEC-004: Backup restore lacks expansion and symlink-ancestor bounds

- **Severity:** LOW
- **Component:** `BackupService.RestoreAsync`
- **Evidence:** Restore performs lexical root-containment checks and rejects
  overwrites unless confirmed, but it has no entry-count or total uncompressed
  size budget. It does not re-check existing target ancestors for symlinks or
  reparse points before extraction.
- **Attack/preconditions:** The user explicitly selects a malicious archive.
  A root-escape additionally needs a pre-existing symlink/junction below the
  configured data root that matches an archive path.
- **Impact:** Disk exhaustion, partial restore, or a write through that existing
  link to a user-writable location outside the data root.
- **Remediation/status:** **Deferred.** Add a staged validation pass with entry
  and expansion budgets plus symlink-ancestor rejection before any write. The
  explicit import action and additional link precondition keep this LOW.

### HERM-SEC-005: Managed server extra args can override loopback binding

- **Severity:** LOW
- **Component:** `ServerProcessManager`, managed server settings
- **Evidence:** Hermaeus appends user Extra Args after `--host 127.0.0.1`.
  llama.cpp b10034's argument handler assigns each later `--host` value to the
  same hostname field, so `--host 0.0.0.0` wins. Privacy Audit detects and
  warns about network-facing host/listen flags.
- **Attack/preconditions:** The user deliberately or mistakenly adds a
  network-facing host flag to advanced server configuration. The managed
  llama.cpp API has no Hermaeus bearer-token layer.
- **Impact:** Model service endpoints can become reachable from the LAN or a
  broader interface, exposing prompts/model access and increasing the native
  parser attack surface.
- **Remediation/status:** **Deferred and documented.** Consider rejecting bind
  flags in managed Extra Args or requiring an explicit network-exposure mode
  with authentication. Default configuration remains loopback-only and the UI
  warns on the unsafe configuration, so this does not block release.

### HERM-SEC-006: Local API revocation leaves an orphaned stored secret

- **Severity:** LOW
- **Component:** `LocalApiSettingsViewModel`, `ISecretStore`
- **Evidence:** Revocation removes and saves the token reference immediately,
  so live authentication fails on the next request. `ISecretStore` has no
  delete operation, leaving the no-longer-referenced token value in the OS
  credential backend or fallback vault.
- **Attack/preconditions:** An attacker later gains same-user access to the
  credential backend or fallback vault and key. The token is no longer
  enumerated by Local API settings and therefore is not accepted by the host.
- **Impact:** Unnecessary stale credential retention, not continued API access.
- **Remediation/status:** **Deferred.** Add backend deletion with migration and
  failure semantics, then delete only after settings revocation is durably
  saved. Immediate authorization revocation works correctly.

### HERM-SEC-007: Linux archives disclosed builder ownership and write bits

- **Severity:** LOW
- **Component:** `build.sh`, Linux tar package
- **Evidence:** The pre-fix archive recorded the local builder account and group
  names in tar owner fields and preserved group-write bits from the worktree.
- **Attack/preconditions:** A locally generated archive is distributed. Shared
  group permissions matter only if another account belongs to the extraction
  directory's owning group.
- **Impact:** Unintended maintainer username disclosure and unnecessarily
  writable installed payloads on uncommon shared-group setups.
- **Remediation/status:** **Remediated locally.** Tar creation now uses numeric
  `0/0` headers and removes group/other write bits while retaining required
  executable bits. The rebuilt archive was inspected and contains no offending
  mode or personal owner/group name.

### HERM-SEC-008: Queued patches have no draft-time stale-file binding

- **Severity:** LOW
- **Component:** Agent draft patch model/apply path, `docs/agent.md`
- **Evidence:** The documentation claimed `AgentDraftPatch` stored a
  `baseHash` and refused changed files. The model has no such field, and
  `ApplyDraftPatchAsync` atomically writes the approved full content without a
  draft-time comparison.
- **Attack/preconditions:** Another tool, process, or user edits the same file
  after preview but before Apply.
- **Impact:** The approved full-content patch can overwrite that intervening
  edit. Apply captures the live pre-image immediately before writing, so Revert
  can recover it unless later changes create another conflict.
- **Remediation/status:** **Behavior deferred; documentation corrected.** A
  future additive base fingerprint could refuse stale application, but that is
  a broader task-state/UI behavior change. The recoverable impact and explicit
  user-approved write keep this LOW.

### HERM-SEC-009: Arbitrary third-party models are not sandboxed

- **Severity:** INFO
- **Component:** model browser, llama.cpp/Ollama runtime boundary
- **Evidence:** User-selected GGUF files are loaded by a native third-party
  runtime under the user's account. Hugging Face LFS objects are verified
  against their OID when present; non-LFS files can be stored without a hash.
  Hermaeus does not isolate llama.cpp in a container or restricted account.
- **Attack/preconditions:** The user selects a malicious or malformed model
  from an untrusted publisher, or configures an untrusted runtime executable.
- **Impact:** Exposure to native runtime parser vulnerabilities or arbitrary
  behavior of the configured executable.
- **Remediation/status:** **Accepted and documented.** This is inherent to a
  local workstation that runs user-selected models and runtimes. The upstream
  [llama.cpp security policy](https://github.com/ggml-org/llama.cpp/security)
  recommends isolation for untrusted models. Hermaeus's pinned b10034 is newer
  than the b8146 fix identified by the relevant
  [GGUF advisory](https://github.com/ggml-org/llama.cpp/security/advisories/GHSA-3p4r-fq3f-q74v),
  but future parser defects remain possible.

### HERM-SEC-010: Local API intentionally exposes minimal unauthenticated health

- **Severity:** INFO
- **Component:** `Hermaeus.LocalApi`
- **Evidence:** `GET /health` returns only `ok` without a token. All functional
  routes require `X-Hermaeus-Token`; no CORS policy is enabled, so browser
  cross-origin callers do not receive permission and the custom token header
  requires an unsuccessful preflight. Kestrel's default request-body limit is
  used rather than a smaller product-specific limit.
- **Attack/preconditions:** Another local process can reach the fixed loopback
  port. Functional abuse additionally requires a valid token.
- **Impact:** Process-presence disclosure and a bounded local availability
  surface. No sensitive unauthenticated response or Agent endpoint was found.
- **Remediation/status:** **Accepted.** A minimal health endpoint is required
  for lifecycle monitoring. Product-specific per-route input limits are a
  reasonable future defence-in-depth improvement.

### HERM-SEC-011: Fallback vault key was stored beside the encrypted vault

- **Severity:** MEDIUM
- **Component:** `SecretStore`, backup and restore documentation
- **Evidence:** When an OS credential backend was unavailable or disabled,
  both `secrets.local.json` and the random `secrets.local.key` used to derive
  its AES keys were written below `SettingsService.ResolveDataRoot(...)`.
  Owner-only file permissions restricted other live local accounts but did not
  separate the key from a copied or stolen data root.
- **Attack/preconditions:** An attacker obtains a copy of the complete Hermaeus
  data root while fallback secrets are present.
- **Impact:** The copy contains both ciphertext and exact decryption material,
  allowing stored provider credentials and Local API tokens to be recovered
  offline. Per-value salts and PBKDF2 do not mitigate possession of the random
  source key.
- **Why controls failed:** Fallback encryption protected against inspecting the
  JSON file alone, but key co-location made the protection ineffective against
  the stated portable data-root theft case. Documentation also implied that a
  normal backup restored encrypted secrets and needed the original key, even
  though backup excludes the fallback vault and legacy key filename.
- **Remediation/status:** **Remediated locally and release-blocking until
  incorporated.** OS credential stores remain preferred. The fallback key now
  lives outside the portable data root in a user-specific OS configuration
  location with owner-only Unix permissions. Existing same-root keys migrate
  there and are removed when the secret store initializes. A copied data root
  without that separate key fails closed, and restore guidance now requires
  credential re-entry on another machine. Regression coverage verifies legacy migration,
  key separation, atomic owner-only key creation, and refusal to decrypt a
  copied vault without the separate key. Full user-profile theft and same-user
  compromise remain residual risks rather than claims this fallback can solve.

## Public-repository exposure result

No real credential, API key, bearer token, password, private key, certificate
with private material, cookie, webhook secret, cloud credential, personal
email, private host/IP, personal filesystem path, database, conversation,
memory export, debug dump, or environment file was found in HEAD or the 443
commits reachable from local heads, remote refs, and release tags.

The only credential-like fixtures are clearly synthetic security tests and
Agent scenarios. Author and committer identities are GitHub noreply identities.
Deleted reachable paths were review documents, prior product-name sources,
fonts/assets, and ordinary code, not deleted secret stores or user databases.

All four README screenshots were visually inspected. They contain generic
model/runtime examples and paths such as `/path/to/ai-assets/Models`, not private
user content. PNG `file`/`strings` inspection found no embedded personal path,
author, comment, or software/date metadata. ImageMagick metadata tooling was
not installed, which is recorded under limitations.

## Privacy and local-first result

Implementation materially matches the public local-first claims. Local models,
RAG, memory, conversations, traces, crash logs, and lifecycle journals stay
local by default. No analytics, telemetry, remote crash reporting, or hidden
upload path was found.

OS credential stores remain the primary secret backend. When the local
fallback is required, its encrypted vault stays in the data root but its random
key now lives in a separate user-specific OS configuration location. Normal
Hermaeus backup excludes the vault and cannot make credentials portable;
restoring elsewhere requires credential re-entry. This separation protects a
copy of the data root, not theft of the complete user profile or same-user
compromise.

Meaningful outbound surfaces are explicit remote chat/voice/STT profiles,
user-triggered RAG web ingest, user-triggered GitHub/Hugging Face/runtime/model
downloads, configured MCP processes that may themselves use a network, and
anonymous GitHub release checks cached for one hour while the app is in use.
The Privacy Audit and documentation disclose remote prompt, attachment, RAG,
memory, Recall, image, and microphone flows. Diagnostics are local and pass
through best-effort redaction.

## Local API result

The Local API is off by default, refuses to serve when disabled, and the
desktop manager starts it only when at least one token exists. The host clears
other URL configuration and binds only `http://127.0.0.1:<port>`. Functional
routes fail closed when no tokens exist and use fixed-time token comparison
against values resolved live from `ISecretStore`, so saved revocation takes
effect without a restart.

The unauthenticated surface is only `/health`. No CORS permission is granted.
The exposed functional set is chat, embeddings, memory query, RAG query, models,
and capabilities. No Agent run/step, settings, benchmark, filesystem, or secret
endpoint is present.

## Agent boundary result

One concrete cross-surface approval bypass was found and remediated as
HERM-SEC-001. After remediation, no model output, prompt injection, RAG text,
workspace instruction, lesson, or MCP description path was found that can
bypass deterministic tool classification and user approval.

`run_command` remains restricted to hardcoded families declared by the
workspace, uses `ArgumentList` without a shell, and requires approval. Approved
package scripts/MSBuild targets can execute transitive workspace code, which is
truthfully documented. File tools canonicalize under the workspace, reject
absolute/traversal and symlink/reparse ancestors, and use atomic writes. A
same-user TOCTOU race between validation and I/O remains defence-in-depth risk,
not a credible new privilege boundary.

## Supply-chain and update result

- Managed llama.cpp archives now verify before extraction. Pinned b10034 uses
  source-controlled hashes; latest/CUDA assets require GitHub asset digests.
- Starter models, embedding assets, ONNX reranker, Kokoro, and Whisper assets
  use fixed SHA256 values or publisher LFS OIDs and delete mismatches.
- Hugging Face browsing is user-triggered and repository/file destinations are
  sanitized. A non-LFS object without an OID remains explicitly unverified.
- App update checks do not download or apply an update. They compare release
  metadata and open the browser for manual installation.
- Archive extraction rejects traversal and unsafe link targets. Downloads use
  temporary files and resume support before atomic destination replacement.
- `dotnet package list --vulnerable --include-transitive` reported no known
  vulnerable packages for the resolved dependency graph on 2026-08-23. No new
  dependency was added by remediation.

## CI and GitHub result

Remote `r30/round` is at `d32e928...`, containing both security remediations and
the deterministic benchmark restart test repair. The final Windows packaging
commit is local only and therefore has no remote CI result yet. All workflow
actions use full commit SHA pins. Default permissions are `contents: read`,
checkout does not persist credentials, PR CI receives no write permission or
repository secrets, and the tag-only release job scopes `contents: write` to
release creation after build artifacts are downloaded.

Public forks would increase untrusted PR volume but not grant fork code a
write-capable token or repository secrets under this workflow. GitHub documents
the default read-only fork-PR token posture in
[Actions repository settings](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository?apiVersion=2022-11-28).

## Package result

The Linux package was rebuilt and its archive/sidecar checksum verified. The
archive contains the intended launchers, integration scripts, docs, licenses,
icons, desktop and Local API payloads, and synthetic Agent scenarios. No PDB,
environment file, database, log, test-result, personal owner/group, or
group/other-writable entry remains. Launcher targets are relocation-safe.

Install/uninstall scripts quote paths, derive the package identity from the
validated package root, install only under the user's data home, and delete only
the matching installed package plus fixed desktop/icon files. No plausible
path-confusion route to system or unrelated user-data deletion was found.
The Windows package now exposes only a 25,088-byte native `Hermaeus.exe` plus
`app/`, `docs/`, and `icons/` at its root. Desktop runtime files live under
`app/`, and Local API files under `app/LocalApi/`. The launcher imports only
`KERNEL32.dll` and `USER32.dll`, uses the Windows GUI subsystem, embeds the
canonical seven-image icon, derives a fixed
`app\Hermaeus.Desktop.exe` target from its own location, forwards arguments,
and uses `app\` as the working directory. A Wine probe against a disposable
fixed target verified the target path, spaced argument forwarding, and working
directory. No PDB or command launcher remains. The ZIP sidecar checksum was
verified.

## Validation performed

- Verified clean starting tree, exact branch/HEAD/origin, tags, and PR #8 state.
- Scanned current files and 443 reachable commits for high-confidence secrets,
  credential assignments, personal paths, private networks, sensitive deleted
  filenames, generated artifacts, dumps, databases, logs, and environment
  files.
- Visually inspected all four README screenshots and checked PNG strings/type.
- Inspected Local API middleware/routes, secret/token lifecycle, Agent gates and
  executors, filesystem canonicalization, archive/backup handling, RAG input,
  process lifecycle, privacy audit, downloads, packaging, and workflows.
- Queried the official llama.cpp b10034 release API for all supported SHA256
  digests and inspected the pinned argument parser's host handling.
- Ran NuGet direct/transitive vulnerability review with no reported advisory.
- Focused security tests: 48 total, 47 passed, 1 expected Windows-only skip.
- Focused SecretStore, migration, and harness-registration tests: 147 passed.
- Focused Windows package and launcher guards: 7 passed.
- Full `dotnet build Hermaeus.sln`: succeeded, 0 warnings, 0 errors.
- Full sequential test suite: 1,899 total, 1,883 passed, 16 expected platform
  skips, 0 failed. TRX was written outside the repository.
- Linux `./build.sh --skip-restore`: succeeded. SHA256 sidecar verified; archive
  ownership/modes and sensitive/debug payload classes inspected.
- Windows `pwsh ./build.ps1 -SkipRestore -Runtime win-x64`: succeeded on Linux
  with the supported MinGW cross-toolchain. Package layout validation passed,
  the ZIP sidecar verified, and inspection confirmed the GUI subsystem, fixed
  target, minimal imports, canonical icon resources, and PDB/cmd exclusions.
- The packaged launcher executed successfully under Wine against a disposable
  fixed target; the probe recorded the spaced argument and `app\` working
  directory exactly.
- `git diff --check`: passed.

## Limitations

- Broader Windows 11 and Linux dogfooding completed successfully before the
  final package layout change. This Linux host cross-built and executed the new
  launcher under Wine against a disposable probe target, but the final packaged
  root launcher has not yet been double-clicked on Windows. Windows credential
  manager, job-object runtime, and SmartScreen/signing behavior were not
  re-exercised for this packaging-only change.
- No macOS host was available for Keychain or macOS archive execution.
- ImageMagick `identify` was unavailable. Screenshot review used visual
  inspection plus `file` and printable-string metadata checks.
- Static review and headless tests do not prove live UI presentation, native
  model parser safety, or every behavior of third-party runtimes.
- Vulnerability databases and upstream metadata are time-sensitive. Results
  reflect checks performed on 2026-08-23.

## Required actions before public release

1. Incorporate the local final Windows packaging commit into PR #8 without
   dropping any earlier remediation or report change.
2. Require fresh green Ubuntu and Windows CI on that exact head.
3. Double-click the packaged root `Hermaeus.exe` on the owner's Windows laptop
   and confirm it starts `app\Hermaeus.Desktop.exe`. No broader smoke campaign
   or updater-path test is required.

## Deferred hardening

- Bound local and streamed web RAG input before buffering.
- Stage and pre-validate backup restores with expansion and symlink budgets.
- Make network-facing managed llama-server binding an explicit authenticated
  mode, or reject host/listen overrides in Extra Args.
- Add delete semantics to secret backends and remove orphaned revoked tokens.
- Add draft-time content fingerprints to queued full-content patches.
- Consider stronger isolation guidance or an optional sandbox for untrusted
  GGUF/native runtimes.
- Consider narrower per-route Local API body limits.

## Release recommendation

Do not release or make the repository public from remote head `d32e928...`.
After the three required actions above, proceed to the owner's final release
decision. No remaining audited finding independently blocks public release.

The security remediations are committed as `9a646db` and `c3fa533`; the Ubuntu
benchmark test repair is `d32e928`. The final packaging change is committed
separately as `fix(packaging): tidy Windows portable release`; its exact hash
is recorded in the final handoff because a Git commit cannot contain its own
stable hash.

Nothing was pushed, merged, tagged, released, made public, or used to change
repository visibility during this audit.
