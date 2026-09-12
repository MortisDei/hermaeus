# Review round 33: Hermaeus beyond the desktop

Status: **planning only, awaiting owner review**. Nothing in this pack is an
implementation claim or authorization to implement, commit, push, open a PR,
merge, tag, release, change settings, or publish.

Baseline: `c944febf8e7eda96dcf8f6461a02e870b40b8465`, verified against remote
`main` on 2026-09-07. Local branch: `r33/planning`. All pack files remain
uncommitted. No production, test, dependency, workflow, or owner-data edits.

## Recommendation

Make the existing product callable and observable through UI-independent
application operations, then repair the mutation and configuration lifecycles
using those operations. Do not start with a daemon, new HTTP endpoints, Monaco,
or more runtime switches. Shared composition already exists; shared application
lifecycle and mutation authority do not yet follow from it.

The mandatory result is a Desktop client and an exclusive headless application
driver exercising the same production workflows, with trustworthy Agent
mutation outcomes, bounded runtime recovery, and useful evidence/navigation.
Agent HTTP execution and Monaco remain conditional branches with explicit
exit gates. Neither may hold correctness repairs hostage.

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

## Verification performed during planning

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

Authoritative user-facing behavior docs and CHANGELOG remain unchanged because
no behavior changed. Documentation drift discovered here is recorded for its
owning implementation batch. Archived R32 evidence and the central deferred
ledger remain intact; doc 10 records the proposed R33 reconciliation.
