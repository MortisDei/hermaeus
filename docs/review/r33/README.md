# Review round 33: Hermaeus beyond the desktop

Status: **mandatory local implementation complete on `r33/planning`; owner
validation remains**. The pack is the authoritative R33 implementation record
for the `v0.41.0-beta` target.
Local commits are
authorized; push, PR, merge, tag, release, settings, and other publication
actions remain owner-only.

Baseline: `c944febf8e7eda96dcf8f6461a02e870b40b8465`, verified against remote
`main` on 2026-09-07. The review pack was anchored locally before implementation.
Local implementation closeout: the local continuation commit for this audit.
Production and regression-test changes are scoped to the batches below. The
only new runtime dependency is the bounded Desktop WebView used by the local
workspace editor; no workflow or owner-data publication changes are included.

## Recommendation

Make the existing product callable and observable through UI-independent
application operations, then repair the mutation and configuration lifecycles
using those operations. Keep the optional editor and pet work bounded by the
same authority and data-safety rules. Shared composition already exists; shared
application lifecycle and mutation authority do not yet follow from it.

The mandatory result is a Desktop client and an exclusive headless application
driver exercising the same production workflows, with trustworthy Agent
mutation outcomes, bounded runtime recovery, and useful evidence/navigation.
Agent HTTP execution and Monaco remain conditional branches with explicit
exit gates. Neither may hold correctness repairs hostage.

## Implementation status

The mandatory R33 scope is implemented locally:

- B0: shared lifecycle authority, startup recovery, retryable partial startup,
  operation identity, and bounded shutdown evidence.
- B1: benchmark operation identity, phase reporting, cancellation persistence,
  terminal evidence save, and Linux installer path escaping.
- B2: shared host ownership, Services runtime registry, embedding/voice owner
  drain, bounded restart handoff, and the first production-composition driver.
- B3: typed prepared mutation proposals with exact policy, target, preimage,
  and post-image facts.
- B4: durable mutation receipts, readback verification, `AlreadySatisfied`,
  child-task ownership, and fresh-task view isolation.
- B5: canonical AutoTune configuration and recommendation save-failure
  reconciliation.
- B6: full-config transient tuning probes with GGUF and hardware context,
  shared Services/Models cancellation, bounded failure evidence, and cleanup.
  The original native/runtime crash reproduction is still an owner validation
  gate because the planning pack did not contain a reproducible stack or
  runtime fixture.
- B7: baseline-aware Lab availability plus isolated run, failure cleanup,
  Apply, settings reopen, and retained evidence paths.
- B7 continuation: Lab and Benchmark results now share a fail-closed runtime
  evidence envelope, with every shipped Lab recipe audited for effective field
  and process-bound telemetry proof.
- B8: Agent/RAG/Lab hierarchy, save-state visibility, typed Doctor target
  mapping, missing-entity feedback, and focused control navigation. The
  continuation also makes Agent outcomes, RAG evidence state, and Lab
  capability state readable in the views, with narrow-window wrapping and
  explicit next actions.
- S: restore budgets, duplicate-target checks, transactional staging, actual
  expanded-byte enforcement, and rollback-safe commit cleanup.
- B9: integrated sequential verification, package validation, and this
  implementation ledger. Owner platform and publication gates remain open.
- P1: a local, offline Monaco workspace editor is preferred when the native
  WebView host is available, with a bounded AvaloniaEdit fallback. The bridge
  carries only document text, display path, theme, and layout.
- P2: generic ChatGPT Pet v2 package support is data-only, bounded, disabled
  by default, and includes the bundled Moss package without granting it
  filesystem, network, script, or chat-content access.

Agent HTTP execution remains deferred because its R33 ownership gate was not
earned. JSONL/runtime experiments remain deferred pending installed-runtime
evidence. Monaco is implemented locally but still requires owner platform and
native WebView validation before it can be called a fully shipped editor path.

Automated closure evidence includes sequential Debug and Release solution builds
and test harnesses, focused Lab/Benchmark authority regressions, the isolated
driver mutation receipt, the approved-host b10930 CPU/partial-GPU/all-GPU
receipt matrix, Linux package creation, package checksum verification, and an
installed-path test with spaces. The current Debug and Release suites each
passed `2,722`, skipped `17`, failed `0`, total `2,739`; the focused authority
set passed `260/260`. Native Windows, live owner Lab/Benchmark walkthroughs,
and GUI pixel acceptance remain owner validation gates. The exact dispositions
for every batch, acceptance cell, V01-V14
scenario, owner defect, and conditional branch are recorded in
[12-completion-audit.md](12-completion-audit.md).

## Read order

| Document | Purpose |
| --- | --- |
| [01-current-state-and-evidence.md](01-current-state-and-evidence.md) | Baseline, audit coverage, source findings, dogfood provenance |
| [02-application-authority-and-lifecycle.md](02-application-authority-and-lifecycle.md) | Host ownership, operation contracts, driver, conditional API topology |
| [03-agent-workspace-and-mutations.md](03-agent-workspace-and-mutations.md) | Proposal, approval, execution, verification, children, completion |
| [04-workspace-editor-decision.md](04-workspace-editor-decision.md) | Monaco research, AvaloniaEdit comparison, offline/security gates |
| [05-runtime-fit-and-evidence.md](05-runtime-fit-and-evidence.md) | Model switching, tuning, backend hierarchy, current llama.cpp |
| [06-lab-and-recommendation-lifecycle.md](06-lab-and-recommendation-lifecycle.md) | Existing recipes, discovery, Apply/reconciliation/history |
| [07-behavioural-verification.md](07-behavioural-verification.md) | Test gaps, benchmark cancellation, production scenarios, owner matrix |
| [08-desktop-product-ux.md](08-desktop-product-ux.md) | Product jobs, hierarchy, disclosure, output inspection |
| [09-security-platform-and-release.md](09-security-platform-and-release.md) | Installed Linux launch, Doctor, restore, real CI gates |
| [10-scope-and-deferred-decisions.md](10-scope-and-deferred-decisions.md) | Every open ledger item, rejection and descope decisions |
| [11-dependency-roadmap.md](11-dependency-roadmap.md) | Batches, dependencies, acceptance gates, implementation handoff |
| [12-completion-audit.md](12-completion-audit.md) | Strict batch, scenario, defect, conditional, and owner-gate audit |
| [13-lab-benchmark-authority-audit.md](13-lab-benchmark-authority-audit.md) | Complete Lab recipe matrix, shared runtime evidence contract, Benchmark eligibility, and native Linux receipts |

## Evidence vocabulary

- **Repository fact (RF):** current source/configuration, with file and symbol
  anchors, or a named deterministic observation. Source proof is not GUI proof.
- **Owner observation (OO):** supplied dogfood report. It is valid evidence of
  a symptom, not automatic proof of a proposed root cause.
- **Upstream fact (UF):** primary source, dated and preferably revision-pinned.
  Advertised syntax is not proof of installed execution or hardware benefit.
- **Inference (IN):** a causal explanation or usability judgment requiring the
  specified reproduction or owner observation.
- **Proposed (P):** R33 contract, not current behavior.
- **Unknown (U):** absent evidence. Never translate this into zero, supported,
  passed, safe-to-apply, or a completed implementation.

## What changed from the supplied direction

1. Workspace inheritance, Run/Changes/Workspace/History, core composition,
   isolated Lab recipes, resource admission, and Apply transactions already
   exist. R33 repairs their remaining boundaries instead of recreating them.
2. Auto-tune is a remaining direct allocation path and a reduced-config probe,
   not simply a missing context slider. It must converge on production launch
   identity/admission before experimental optimization grows.
3. Recommendation staleness has both configuration-editor reconciliation and
   decision-publication seams. Empty newly derived managed patches are already
   rejected; do not claim the implementation lacks that check.
4. Monaco is viable enough for a bounded spike: current Avalonia WebView is MIT
   and has Linux backends. Pop!_OS/COSMIC rendering, worker delivery, native
   dependencies, and resource cost still require measured proof.
5. Installed Linux launch has a concrete unquoted-space defect. The old generic
   launcher hypothesis can be narrowed without changing the installation.
6. Existing R32 same-repository PR CI evidence is now observable. R33's own
   eventual PR-context gate remains future owner work. CodeQL analysis success
   is verified; CodeQL merge enforcement is not present in the inspected rules.

## Verification performed during planning (historical baseline)

- `dotnet build Hermaeus.sln -m:1`: passed, 0 warnings, 0 errors.
- Full sequential `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj
  --no-restore -m:1 --logger 'trx;LogFileName=r33-baseline.trx'
  --results-directory /tmp/hermaeus-r33-tests`: 2,675 passed, 17 skipped,
  0 failed, 2,692 total. These are baseline tests, not proof of proposed repairs.
- Installed entry parsing was compared with a quoted temporary copy, without
  launching the application. Installed runtime version/help were read without
  loading a model. No GPU performance, Windows, or GUI pass is claimed.
- Final pack links, source anchors, scope, whitespace, and working-tree checks
  are recorded in doc 07. Coverage is not run: this pass creates no commit and
  changes no executable behavior; retain the mandatory final precommit coverage
  gate for subsequent authorized implementation.

## Verification performed during implementation

- Debug and Release solution builds passed with zero warnings and zero errors.
- The complete sequential Debug and Release harnesses each passed 2,695 tests,
  skipped 17 platform-gated tests, and reported 0 failures out of 2,712.
- The resumed UX/editor/pet continuation rebuilt and reran both sequential
  Debug and Release harnesses: each passed 2,703 tests, skipped 17
  platform-gated tests, and reported 0 failures out of 2,720.
- Focused lifecycle, restore-safety, Agent patch, steering, sub-task, runtime
  ownership, restart, Doctor, and voice tests passed in their targeted runs.
- The isolated R33 driver completed a real prepared file mutation and emitted
  an `Applied`, changed, readback-verified receipt. An unknown-argument run was
  rejected before startup.
- The Linux `v0.41.0-beta` package was created, its SHA256 sidecar verified,
  and its installer was exercised from a path containing spaces against an
  isolated XDG data directory. The desktop entry passed validation apart from
  the existing category hint.
- The direct final coverage command passed at 64.68% line coverage
  (`50,267/77,713`) against the 60% ratchet, with results kept under `/tmp`.
  The initial instrumented run exposed two timing-sensitive reconciliation
  assertions; the waits were strengthened and the final full run passed.
  Native Windows, live managed-runtime/GPU behavior, GUI pixel acceptance, and
  the owner PR/check gate remain outside this local proof.

Authoritative user-facing behavior docs and CHANGELOG now describe the
implemented R33 behavior and `v0.41.0-beta` target. Archived R32 evidence and
the central deferred ledger remain intact; JSONL/runtime experiments and Agent
HTTP execution remain deferred, while Monaco and ChatGPT Pet remain subject to
the explicit owner GUI/platform gates in the completion audit.
