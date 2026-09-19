# 10. Scope and deferred decisions

> Historical planning record. The current `r33/round` disposition is recorded
> in [14-owner-dogfood-closeout.md](14-owner-dogfood-closeout.md). Monaco and
> the WebView dependency are no longer part of the current editor surface;
> this document's earlier conditional wording is retained as planning history.

## 10.1 Mandatory, conditional, parked

Mandatory R33 scope is application lifecycle/authority, Agent mutation validity
and outcome truth, task isolation, configuration reconciliation, bounded tune
recovery, recommendation lifecycle, benchmark cancellation, Linux launcher,
Doctor target navigation, restore budgets and targeted product UX/verification.
These are bounded repairs/maturation anchored in current evidence, not a mandate
to replace every subsystem.

Conditional scope is the Monaco/native-WebView path, new runtime
experiments/JSONL consumers where the selected binary supports them, and Agent
Local API execution after actual cross-process ownership is proven. The Monaco
path now has a bounded local implementation and AvaloniaEdit fallback, but its
native platform/worker ship gate remains open. Unsupported capabilities can
ship as explicit unavailable/Unknown outcomes. No endpoint or editor may
weaken mandatory authority.

The central `docs/review/deferred.md` is unchanged in this planning pass. The
following table is the complete disposition of its currently open selected,
parked and operational rows, plus its rejection. Owner review of this pack
precedes the later authoritative ledger update. Existing R32 closure is history,
not evidence that new owner observations can be dismissed.

## 10.2 Ledger reconciliation

| Existing row | R33 disposition | Evidence/gate |
| --- | --- | --- |
| Context checkpoints and cache RAM | Retain existing R32 controls; conditional measured scenarios, no reimplementation | Doc 05/06. Exact runtime support, RAM accounting and observed benefit; unavailable stays visible. |
| Multi-device placement | Park unverified execution/hardware remainder; preserve current per-device representation | Only one GPU per owner target is specified. Do not invent multi-GPU proof or default tensor split. |
| Same-repository CI run de-duplication | Existing R32 PR attachment evidence now observed; retain unproven cancellation details and R33 future-PR gate | Doc 09, PR #15 and run 34007907871. Do not close all topology behavior from one successful run. |
| Agent run/step Local API endpoints | Conditional R33, not unconditional unpark | Doc 02 single process owner, revocation/scope/recovery and two-process race tests first. |
| MCP HTTP/SSE | Park | No concrete non-stdio consumer was supplied or established. |
| llama.cpp backend sampling/internal instrumentation | Narrow conditional unpark for supplementary structured logs only | Current upstream JSONL envelope is established; installed b10821 lacks it. No debug/prompt logging or generic performance-instrumentation subsystem. |
| Knowledge-graph expansion/multi-hop retrieval | Park | Existing temporal lineage and one-hop evidence remain; no demonstrated failure requiring a graph engine. |
| Automatic model/profile selection/routing | Park | Explicit fit/tune/recommendation review only; no quality/rollback basis for autonomous routing. |
| TLB behavioral evidence interchange | Park | No versioned concrete two-project consumer. No workload-specific names in product requirements. |
| Deterministic timing for clock-dependent tests | Retain watch; touch only affected lifecycle seams | Use deterministic await signals for R33 scenarios. No suite-wide clock project without reproduced flake. |
| Windows CI Defender exclusion | Retain trusted-push security boundary | Re-measure only if CI changes; never apply exclusion to PR/fork execution. |
| Real Whisper ONNX graphs | Retain owner/live/download gate | No graph session/decoding proof in this pass. Shared driver plumbing does not close it. |
| COSMIC folder picker/portal | Retain watch | Desktop-entry quoting is a separate proven defect, not evidence that the portal is broken. |
| Continuous fine-tuning/adapters (rejected) | Preserve rejection | No separate owner-authorized training/provenance/consent/evaluation premise. |

## 10.3 Closed items that must stay correctly described

- External draft/EAGLE workflow, speculative tuning and prompt reuse experiments
  exist. Missing pair engagement/counters remain evidence gates; DFlash/DSpark
  is a separate conditional current-capability investigation.
- Whole-workload GPU Fit exists. Auto-tune's direct allocation path is an R33
  hole in coverage of the authority, not proof the whole R32 coordinator is absent.
- Recommendations and empirical experience exist. Reconciliation and details
  are repairs to their lifecycle, not a new learning system.
- Workflow/subtask orchestration and inherited workspace context exist. Prove
  child filesystem outcomes before declaring that dogfood class fixed.
- Temporal memory and atomic RAG generations exist. Keep deletion, as-of/history,
  exact citations and plaintext/privacy truth. Do not reopen autonomous truth
  resolution under a new name.
- Runtime artwork and owner-passed Agent tabs/Doctor action presentation remain
  implemented. R33 targets output hierarchy and actual remediation destinations,
  not another tab/artwork/tooltip rewrite.
- The optional ChatGPT Pet overlay is a data-only ambient surface. Its generic
  v2 package boundary, bundled Moss asset, disabled-by-default setting, and
  position persistence are in scope; marketplace, scripting, voice coupling,
  and a pet editor are not.
- The R32 owner data migration passed. Do not require another destructive owner
  migration as a routine round gate. Test lifecycle changes with scratch roots.

## 10.4 Security-roadmap reconciliation

Select restore resource budgets and associated extraction cancellation/duplicate
preflight. Preserve already-landed path/reparse protections. The confusing
preapproval versus execution-time `run_command` optional-path policy is in scope
with prepared mutations because the reported class now meets its revisit trigger.

CodeQL enforcement is an owner-controlled repository-policy gate, not permission
for an agent to alter settings. API scope must evolve only if execution endpoints
pass their owner gate. Backup preview/manifest, fallback-vault AEAD, package
signing, binary trust display, web domain lists and broader runtime-host blocking
policy remain separately gated. Correct source-facing skill/security-doc drift
with the owning future batch; do not claim it was fixed during this pass.

## 10.5 Explicit exclusions and rejection rationale

| Attractive expansion | Why it does not earn R33 scope |
| --- | --- |
| Full GraphRAG, arbitrary multi-hop graph reasoning, universal workspace graph | Does not solve the proven proposal/execution/verification boundaries; existing bounded lineage has different semantics. |
| Autonomous swarm or parallel mutation agents | Would multiply authority conflicts before the current sequential child path is trustworthy. |
| Continuous LoRA/adapter training, autonomous truth resolution | Violates current evidence/consent direction and needs a distinct project. |
| Automatic provider/model routing | User-approved reconciliation does not justify autonomous selection. |
| Internet-facing/multi-user service | Outside the local-first, single-owner threat and lifecycle model. |
| Broad live runtime mutation | Saved/planned/effective/observed state must become clearer first; restart remains explicit. |
| MCP HTTP/SSE | No demonstrated consumer. |
| Full IDE ecosystem or Chromium distribution by default | Editor presentation need does not justify terminal/debugger/compiler/package/Git authority or large browser maintenance without measured benefit. |
| Theme rewrite/raw-color sweep | Touch semantic colors only where changing affected workflows. |
| Test-suite rewrite or coverage-percentage project | Preserve working unit/guard/scenario tests; add representative production behavior evidence. |
| New benchmark scoring framework | Existing suites/quality profiles/common-case comparisons provide the needed starting point. |

## 10.6 Descope order

If implementation grows beyond a credible single round, first drop new runtime
recipes, then API execution endpoints. The optional editor adoption decision is
already closed in the current continuation. Keep the shared
owner/driver, mutation correctness, task isolation, configuration/recommendation
reconciliation, cancellation and demonstrated platform defects. Reduce UX scope
to these changed workflows before expanding to secondary pages. Do not descope
required regression or shutdown/recovery evidence to preserve optional features.
