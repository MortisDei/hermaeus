# 11. Dependency-spined roadmap

This document was the implementation proposal for owner review. The local
implementation now follows it on `r33/planning`, baseline `c944feb`, with the
mandatory local closeout recorded in `cc494ac` and the strict audit in
`docs/review/r33/12-completion-audit.md`. A future code branch/PR follows
`docs/pull-requests.md`; no automatic release/version or publication is implied.

## 11.1 Dependency spine

```text
B0: baseline + failure fixtures + ownership/identity contract
 |
 +--> B1: bounded launcher / benchmark boundary repairs -----------+
 |                                                               |
 +--> B2: shared application lifecycle + task owner + driver slice |
        |                                                        |
        +--> B3: prepared mutation / review contract              |
        |     +--> B4: execution receipts / verification / child  |
        |              recovery / New Task isolation             |
        |                                                        |
        +--> B5: configuration editor + recommendation reconcile  |
        |     +--> B6: canonical bounded fit/Auto-tune operation   |
        |     +--> B7: Lab discovery / evidence / targeted details|
        |                                                        |
        +--> B8: Agent/RAG/Lab hierarchy + Doctor target mapping <-+
        |         ^        ^          ^
        |         B4       B5         B7
        |
        +--> C1: Monaco comparison/adoption (requires B3/B4/B8 target contract)
        +--> C2: JSONL / selected runtime experiment (requires B6/B7)
        +--> C3: Agent API execution (requires B2-B4 + actual single host)

S: bounded restore hardening can run independently after B0
B9: integrated verification + docs + owner platform evidence <- all selected work
Owner actual PR-context / enforced checks gate <- owner publication, not planning
```

Implement regression coverage with each behavior batch, not as a late separate
testing project. The headless driver starts with the first application slice and
grows with each workflow; it must not become a final mock wrapper around old VMs.
B1 can repair demonstrated crashes/launch breakage without waiting for the whole
application extraction. Its regression then participates in the shared driver.

## 11.2 Batches and acceptance

| Batch | Evidence/problem and desired outcome | Implementation boundary | Dependencies | Verification and stop gate |
| --- | --- | --- | --- | --- |
| B0 | A1-S2: source/owner symptoms need reproducible boundaries and explicit ownership | Freeze owner matrix, operation/config/task identity, isolated failure fixtures; capture current installed/runtime capabilities | Reviewed pack | No production fix inferred solely from OO; retain Unknown crash/child mechanisms until isolated repro |
| B1 | P1/B1: valid desktop entry and normal cancellation terminal outcome | Linux package generator; benchmark service/VM preparation-to-finalization boundary | B0 | V06/V09, no owner installation mutation; no unhandled preparation cancel or success toast after cancel |
| B2 | A1-A4: shared DI must become shared boot/run/drain ownership | Thin lifecycle coordinator, Services runtime registry, Agent per-task command owner; first real production driver slice | B0 | V11/V12; settings before stores, no VM write bypass, scoped cancellation, clean-exit ordering and interrupted recovery |
| B3 | G1/G6: fully prepared mutation before review | Typed proposal/preconditions, persisted workspace/target, review fingerprint and schema validation; include queued/manual/child/command-path siblings | B2 | V01/V02, policy/path/argument drift refused; owner reviews complete actual mutation |
| B4 | G2-G5: truthful result/completion and task isolation | Durable attempt/approval/observed/verified records; readback, child orchestration and New Task projection reset | B3 | V02-V05/V11, crash after write, failed write never Applied, model Final never fabricates verification |
| B5 | R3/L1/L2: Apply survives editor lifecycle and stale proposals stop being actionable | Dirty-field/base-revision reconciliation; final recommendation event; semantic satisfaction; retained evidence/detail ids and refusal display | B2, existing settings/experience identities | V08, apply with clean/dirty row, next Start, reopen, concurrent saves, stale Undo and absent evidence |
| B6 | R1/R2: model switch and tune use one truthful launch/admission path | Canonical full-config tune operation; bounded context/placement candidates; Services/Models/bulk callers; backend reason projection | B2/B5 | V07; original crash reproduced and repaired before claiming closure; metadata missing/fail/cancel leaves evidence and no leaks |
| B7 | L1/L2: understandable existing Lab and retained decisions | Baseline-aware recipe availability, run/restore/evidence/review flow, no duplicate catalogue or optimizer | B5; B6 for recovery experiments | V07/V08; exact-current model/runtime eligibility, restore outcomes and retained details |
| B8 | UX/P2: primary jobs, outputs and remediation destinations | Focused Agent/RAG/Lab hierarchy; typed Doctor/details navigation; touched semantic resources and save-state visibility | B2/B4/B5/B7 as each view needs | Doc 08 walkthroughs, V05/V10/V12 and owner keyboard/DPI/resize; no broad redesign |
| S | S1: bounded restore expansion without losing containment | Entry/size/actual-byte budgets, cancellation and destination-alias preflight; exact failure cleanup; relevant documentation drift | B0 | Doc 09 hostile/large scratch archives plus existing containment tests and CodeQL; no owner restore |
| C1 | Rich source/diff editing must justify dependencies | Dedicated Monaco versus AvaloniaEdit spike, narrow bridge and local asset packaging | B3/B4 and B8 artifact contract | Doc 04 full cross-platform/offline/security/resource gate; fallback is a complete acceptable result |
| C2 | Useful current upstream evidence without permanent speculative knobs | Supplementary JSONL adapter; at most selected dense-FFN and DFlash/DSpark experiments through existing Lab | B6/B7; supported exact binary/pair | Doc 05 adapter regressions and real evidence; unsupported is explicit, not a fake implementation pass |
| C3 | Local Agent clients need one actual authority | Chosen single-host topology, authenticated local transport, scope editor and guarded endpoint mapping | B2-B4, explicit topology review, no competing authority | V13 with two real processes, revocation/recovery/approval races; otherwise endpoints stay unmapped |
| B9 | Integrated owner workflows and truthful release evidence | Full automated verification, authoritative behavior/security docs, CHANGELOG, ledger update, process audit and owner matrix | All retained mandatory/conditional batches | Build/tests/package/coverage as applicable; unresolved hardware/UI gates remain explicit; owner controls commit/PR/release |

Each batch inherits detailed invariants and non-goals from its owning document.
In particular B2 is not a generic workflow framework, B4 is not universal semantic
verification, B6 is not routing or live mutation, B8 is not a theme rewrite, S is
not cloud backup, and C3 is not a multi-user service.

## 11.3 Cross-cutting risks and decisions to freeze before code

| Risk/unknown | Decision/evidence needed | Impact |
| --- | --- | --- |
| Per-task locks leave shared-file conflicts | Expected pre-image and short target write serialization, including rewind and children | Required for honest mutation authority |
| Crash after write before persisted result | Durable prepared attempt and recovery reconciliation; ambiguous means Interrupted | No blind exactly-once claims or replay |
| Settings clone/full-save races and dirty forms | Version/base revision plus per-field provenance; conflict review | Prevent undoing owner edits and Lab Apply |
| Auto-tune crash not yet reproduced | Stack/process/OS evidence from isolated failure, including callbacks and siblings | Cannot close via another broad catch |
| Shutdown blocking UI versus async drain | One awaitable host shutdown with bounded result; UI closing adapter awaits safely | Clean marker follows actual completion |
| Optional dependency constructors omit production owners | Driver resolves real graph and lists substituted boundaries | Green isolated tests cannot stand for production |
| WebView 12.1.0 docs/backend differences | Test exact package on actual Pop!_OS and Windows; record fallback | Monaco is conditional, no runtime install assumption |
| 6 GB versus 8 GB/runtime/driver differences | Real selected hardware evidence, process versus device attribution | No mocked GPU pass or universal preset |
| Existing CodeQL analyzes but lacks inspected enforcement | Owner-controlled merge-policy decision, re-read at actual PR | Keep external gate visible; never alter settings autonomously |
| R33 too broad | Apply doc 10 descope order, keep safety/correctness tests | Optional editor/API/recipes do not block the mandatory spine |

## 11.4 Verification, documentation and handoff

Before implementing a batch, revalidate its evidence and read the relevant
repository skill/current docs. Behavioral work follows investigate -> prove root
cause -> bounded repair -> sibling audit -> representative regression -> full
automated verification -> owner live observation. Do not stop at a live gate
while assigned automated work remains.

Keep `docs/features.md`, `docs/user-guide.md`, relevant Agent/API/Lab/benchmark/
RAG/runtime/packaging/security docs, skills that demonstrably drifted, and
CHANGELOG synchronized with landed behavior only. Document host bootstrap,
headless driver commands, task/operation recovery, receipt migration, editor
assets/dependencies and changed UI semantics when implemented. R33 planning
alone changes none of those user-facing contracts.

Build the solution, run full sequential tests with results outside the checkout,
Debug/Release as required, relevant package checks, and inspect lingering owned
processes. Canonical coverage runs once at the final authorized precommit gate.
Owner Linux/Windows observations follow the completed automated work. Do not
claim Windows, real inference, speech graphs, native editor rendering or actual
installed application launch from source inspection/mocks.

No push, PR, merge, tag, release or repository-settings operation is performed
by this implementation. Local commits are authorized by the owner.

## 11.5 Implementation progress

| Batch | State | Required evidence |
| --- | --- | --- |
| B0 | Implemented and verified | `ApplicationLifecycleCoordinatorTests`, identity/isolation source, and driver evidence; retained Unknown crash/child mechanisms remain explicit |
| B1 | Owner validation required | Launcher parser/install and preparation cancellation are verified locally; full phase matrix and visible launch remain open |
| B2 | Owner validation required | Shared lifecycle, runtime registry, restart handoff, and driver are verified locally; live two-process/native startup remains open |
| B3 | Owner validation required | Prepared mutation/policy boundaries are verified locally; owner selected-model/manual/child/command review remains open |
| B4 | Owner validation required | Receipt recovery, readback, child/task projections are verified locally; live delayed callbacks and interruption remain open |
| B5 | Owner validation required | Configuration/recommendation reconciliation is verified locally; owner UI/rapid-save/Details/restart cells remain open |
| B6 | Owner validation required | Canonical full-config tuning, cancellation, admission and evidence are locally verified; original native crash and fit gate remain open |
| B7 | Owner validation required | Baseline-aware Lab, cleanup, Apply/reopen are locally verified with deterministic boundaries; real runtime/restore remains open |
| B8 | Owner validation required | Typed Doctor destination and touched UX projections are locally verified; owner keyboard/DPI/focus/resize walkthrough remains open |
| S | Implemented and verified | Restore budgets, actual-byte staging, duplicate-target and cancellation cleanup tests pass; no owner restore is required |
| B9 | Owner validation required | Local build/test/package/coverage/docs closeout is complete; owner platform, PR/check, CodeQL enforcement and publication remain open |
| C1 Monaco | conditionally rejected/not earned with evidence | No cross-platform offline/native/resource spike gate was earned; AvaloniaEdit remains the fallback |
| C2 JSONL/runtime experiments | conditionally rejected/not earned with evidence | Installed b10821 lacks `--log-jsonl`; no exact compatible pair/benefit evidence was earned |
| C3 Agent HTTP execution | conditionally rejected/not earned with evidence | `AgentApiContract.ExecutionRoutesAvailable=false`; no single cross-process execution owner is proven |
| Actual R33 PR/CodeQL enforcement | owner validation required | Exact PR/check/rules evidence remains an owner action; no synthetic planning PR or settings change was made |

Do not infer progress from document existence. The complete row-by-row evidence
and the V01-V14, owner-defect, named-scope, conditional, and commit-count
dispositions are in `docs/review/r33/12-completion-audit.md`. Keep unresolved or
descope decisions clear and do not turn local verification into owner platform
or publication evidence.
