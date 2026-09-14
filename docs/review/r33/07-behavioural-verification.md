# 07. Behavioural verification and owner evidence

## 7.1 Keep useful tests; add proof at the missing boundaries

**RF:** the planning baseline build passed and 2,675 tests passed with 17
platform skips. That result does not contradict the identified owner-visible
gaps. Existing tests prove valuable local contracts; the coverage claim must
match the exercised graph and observation.

| Existing seam | What it proves | Missing owner-visible evidence |
| --- | --- | --- |
| `AgentWorkbenchLayoutTests` | XML shape, tab names, bindings, pure next-action labels and manufactured ledger summaries | A real command produces corresponding filesystem/receipt/UI state; New Task prevents delayed stale updates |
| `LabEvidenceLayoutTests` | Expected layout/control structure | Selected experiment -> completion -> retained detail -> review/apply workflow and usable hierarchy |
| `AgentScenarioRunner` / tests | Production Agent/workspace tools in a deliberately isolated, narrowed graph | Whole application startup, production retrieval/MCP/traces/recommendations and shutdown; do not relabel seeded/null dependencies as full composition |
| `AgentSubtaskModelSelectionTests`, orchestration/patch tests | Model/parent/child policy and individual paths | Approved child write reaches intended real filesystem while active UI workspace changes |
| `RecommendationApplicationTests` | Service transaction/rollback behavior | Open Services editor reconciliation, final-event refresh and retained Details navigation |
| Benchmark VM/service tests | Cases, rankings, Rerun command, selected-model behavior | Cancellation during initialization/hardware/fingerprint/model preparation and final persistence |
| Resource admission tests | Covered managed allocation owners | Static Auto-tune and its Services/Models/bulk entry points |
| Packaging guards | Script contents/layout and launcher policy | Desktop-entry interpretation with spaced paths and an actual installed visible window |
| Doctor tests | Checks, action labels, callback page | Correct section/target revealed with keyboard focus and missing-target explanation |

Use deterministic signals and controlled await boundaries, not arbitrary sleeps.
Do not fabricate unreachable task states to claim complete workflows. Pure state
projection tests may manufacture inputs, but label them as projections and pair
high-risk cases with actual command-driven transitions. Retain structural guards
for the structural mistakes they actually prevent.

## 7.2 Benchmark cancellation, mandatory bounded repair

**Problem B1:** `BenchmarkService.RunAsync` performs initialization/hardware/
profile capture before its cancellation handler. `BenchmarkViewModel.RunAsync`
and Rerun only use finally, allowing a preparation exception to escape the async
command. The service's outer OperationCanceledException catch is also not
caller-token-filtered. Per-case timeout handling already exists and should be
preserved rather than replaced with a global cancellation swallow.

**P outcome:** establish a run/operation id before asynchronous preparation.
Preparation, cases, between-suite transitions, persistence and cleanup share one
lifecycle. User cancellation returns Cancelled with the last phase and retained
partial evidence, with no success toast or unhandled dispatcher exception.
Timeout, runtime failure and application fault have separate outcomes. If final
persistence fails, report both the operation outcome and evidence-save failure.

**Boundary:** shared application benchmark operation plus focused service/VM
error handling. Capture only safe minimal metadata when identity is incomplete.
Use `ct.IsCancellationRequested` to classify caller cancellation; timeout tokens
and provider failures retain their origin. Do not let cancellation of one Run All
suite blindly continue into later suites or show overall success. Re-enable
commands and release the correct CTS after the same operation settles.

**Dependencies:** operation identity and existing BenchmarkService/EvalStore.
**Verification:** cancel during initialization, model prepare, hardware capture,
binary hash/fingerprint, first response, between cases/suites, save and cleanup;
provider timeout without caller cancel; genuine exception; Rerun and Run All;
late progress after cancel/new run; no detached runtime or success notification.
**Non-goals:** global TaskCanceledException suppression, new benchmark engine,
scoring overhaul merely to increase coverage.

Existing starter suites, SuiteVersion/ScoringProfile, cold/warm semantics,
quality scores, common-case comparisons and cross-suite mean standings remain.
New constrained-runtime experiments reuse the suitable correctness/quality
requirements. Do not pool incompatible scores into a universal winner. New
benchmark dimensions need demonstrated decision value, not another dashboard.

## 7.3 Production driver scenario set

Doc 02 defines the shared application driver. Each scenario names production
services and replaced external boundaries and asserts observable bytes/state/
events, including intermediate and reopened state. Register harness-style tests
in `XunitHarnessTests.HarnessCases` when applicable. Keep sequential execution
and `Helpers.NewSettings`' isolated Data Root; no real settings or task mutations.

| ID | Scenario | Deterministic evidence required |
| --- | --- | --- |
| V01 | Malformed mutation | Actual scripted provider proposal fails validation before pending review; zero writes and useful refusal |
| V02 | Approved versus Applied versus Verified | Approve valid prepared proposal; force execution refusal/no-effect/write; receipts and UI projection match each distinct outcome |
| V03 | Full-file replacement | Old/new controlled content, complete expected output, readback hash, obsolete-section assertion, reopen; model Final cannot mask failure |
| V04 | Child mutation | Approve parent plan and child write through real owner; bytes appear only in inherited workspace; receipts survive reopen |
| V05 | New Task | Complete task A with artifacts/context/follow-up; create B while delayed A reads resolve; no A state returns |
| V06 | Benchmark cancellation | Phase-by-phase cases in section 7.2 through public application commands and VM adapters |
| V07 | Model switch/recovery | Model identity changes with infeasible carried context; canonical bounded candidate descent; no silent save, no leak, retained failed attempts |
| V08 | Recommendation Apply | Open clean/dirty Services editor, Apply through Lab, reconcile, next Start projection, restart stores, retained Details and stale Undo |
| V09 | Linux launcher | Generated package in space/special-character scratch paths; desktop parser and helper argv/cwd proof without touching owner install |
| V10 | Doctor remediation | Produce actual finding; dispatch action; correct typed panel/section/server target or explicit unavailable reason |
| V11 | Shared owner/recovery | Competing commands, target conflict, policy changed after proposal, cancellation, crash after write, restart lock handoff/interrupted status; never duplicate execution |
| V12 | Whole-product lifecycle | Shared bootstrap, RAG ingest/query generation, Chat context with scripted provider, voice boundary, source restore and bounded shutdown |
| V13 | Optional API owner | Two real host/client processes, scope/revocation, reconnect/replay, approval race, no direct-write fallback |
| V14 | Optional editor | Adapter renders/diffs and bridge cannot gain authority; offline worker/resource interception and cleanup |

Passing deterministic V07 with a fake runtime does not establish hardware fit;
V09 with a harmless helper does not prove an Avalonia window is visible. Both
are necessary evidence at different boundaries.

## 7.4 Owner-live matrix

Unclosed R33 live cells are tracked as **NEEDS OWNER VALIDATION**. The scoped
Linux owner passes recorded below do not widen into passes for Windows, other
models, other controls, or the remaining workflows. Historical R32 Doctor
action presentation/Agent tabs/data migration passes are retained as historical
facts, not upgraded into passes for new target navigation, task isolation or
R33 changes. Use one row per run with binary/build/config identity, date and
retained evidence reference. Explicitly mark Unknown or Not applicable rather
than an empty pass.

| Scenario | Deterministic evidence | Linux GTX 1660 Super 6 GB | Windows RTX 4060 8 GB | Status/evidence |
| --- | --- | --- | --- | --- |
| Install/menu launch, reopen, clean exit | V09/V12 | **OWNER PASS, scoped:** packaged Linux launch and tray Quit after normal use; children exited and `CleanExit:true` | Portable launcher, spaces, clean exit | Linux tray path passed; visible window-close, restart handoff, and Windows remain needs owner |
| Agent create/edit/full rewrite and child approval | V01-V04/V11 | Actual selected local model, filesystem/artifact review | Same workflow with Windows path/locking behavior | Needs owner |
| New Task and primary artifact | V05 | Slow callbacks, narrow/wide window, keyboard | Same, DPI/accessibility | Needs owner |
| Switch model then fit/tune recovery | V07 | **OWNER PASS, scoped:** Models-card AutoTune with a loaded source and different real target; source restore observed | 8 GB pressure; real effective backend | Linux model-card path passed; broader combinations and Windows remain needs owner; native mechanism is unresolved but the old invalid draft trigger is classified in doc 12 |
| Preferred/installed/service/effective backend | V07/V12 | Vulkan, explicit CPU placement, unavailable/manual path | Auto/CUDA/Vulkan as actually installed | Needs owner; do not force backend installation |
| Lab discovery/run/cancel/restore | V07/V08 | Correct baseline, observable pressure, restore failure | Same, process VRAM may be Unknown | Needs owner |
| Recommendation applied then Details/restart | V08/V10 | Services row, no duplicate action, retained history | Same | Needs owner |
| Benchmark cancellation at preparation and case | V06 | **OWNER PASS, scoped:** Linux cancellation left the dispatcher alive without a success result | Same | Linux cancellation scope passed; remaining phases and Windows remain needs owner |
| RAG retrieval and citations | V12 | **OWNER PASS, scoped:** Linux retrieval returned citations for the selected knowledge scope | Query scope, path casing, file sharing | Retrieval/citation scope passed; ingest/manage/history/cancel and Windows remain needs owner |
| Voice playback and audio feedback | V12 | **OWNER PASS, scoped:** Linux playback and configured feedback paths were exercised | Device selection, playback, failure recovery | Linux scope passed; Windows device/backend and failure recovery remain needs owner |
| RAG ingest/manage/query/history and cancel | V12 | Mounted-source paths, selected versus query scope | Path casing and file sharing | Needs owner for the unclosed ingest/manage/history/cancel workflow |
| Doctor target remediation | V10 | **OWNER PASS, scoped:** Linux remediation reached the intended target path | Exact supported Windows target | Linux target path passed; physical focus and Windows remain needs owner |
| Optional Monaco editor/offline/diff | V14 | Actual WebKitGTK/XWayland or verified WPE | Actual WebView2 | Conditional; no renderer proof yet |
| Optional runtime experiments | Existing recipe controls + doc 05 | Exact compatible binary/model only | Exact compatible binary/model only | Conditional; unsupported remains unavailable |
| Existing live watches | Existing targeted tests | COSMIC picker, real Whisper, warm/cold Recall as relevant | Real Whisper/speculation pairs as relevant | Retain original gates; no destructive repeat migration |

## 7.5 Verification record and next gates

Planning baseline:

- Build: `dotnet build Hermaeus.sln -m:1`, exit 0, 0 warnings/errors; log
  `/tmp/hermaeus-r33-build.log`.
- Full sequential suite: command in README, exit 0, 2,675 passed, 17 skipped,
  0 failed, 2,692 total; `/tmp/hermaeus-r33-tests/r33-baseline.trx` and
  `/tmp/hermaeus-r33-tests.log`. No reports in the worktree.
- Linux desktop entry: read-only installed parse and temporary quoted control;
  no app launch or installation change. Runtime: b10821 help/version only.
- No new production/regression test was added during planning. V01-V14 are
  future acceptance scenarios, not completed tests.

After authorized implementation: focused meaningful regression tests; full
solution Debug/Release builds and sequential suite on the appropriate host;
packaging where touched; owner-live gates after automated work; canonical
coverage once immediately before an authorized commit. Keep all reports outside
the checkout. Never run a known-doomed restricted IPC/network build first.

Final planning validation: pack internal links and cited repository files/symbols
checked; no em dashes; whitespace and branch/diff scope checked. Baseline
verification predates only Markdown creation. Planning does not require a second
identical full suite or a release packaging run.
