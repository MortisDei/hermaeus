# 01. Current state and evidence

## 1.1 Baseline and inspection boundary

Inspected on 2026-09-07, on Pop!_OS in a COSMIC Wayland session. Clean `main`
matched remote `main` at `c944feb` before creating `r33/planning`. The source
version is `0.40.0` with its existing suffix in `Directory.Build.props`.
Desktop references Avalonia 12.1.2 and Avalonia.AvaloniaEdit 12.0.0. The solution
also contains Composition, LocalApi, Mcp and Voice projects beyond the short
AGENTS solution table. No dependency upgrade is implied by this inventory.

This was a repository-wide, risk-directed source and workflow audit, not an
assertion that every line was manually reviewed. Source findings below are
anchored to the baseline; revalidate symbols when implementation starts.

| Area inspected | Existing authority and representative evidence | R33 disposition |
| --- | --- | --- |
| Host/composition | `HermaeusServiceRegistration.AddHermaeusCoreServices`, Desktop `App.InitializeAppAsync`, LocalApi `Program` | Existing graph; missing shared lifecycle, doc 02 |
| Chat/Projects/Recall | `ChatViewModel` context injection/send lifecycle, `MainWindowViewModel` project and Recall wiring, `docs/projects.md`, `docs/recall.md` | Move workflow ownership selectively; keep project identity and provenance, no new memory engine |
| Agent | `AgentService`, `AgentToolExecutor`, `AgentWorkspaceTools`, `AgentPatchReviewService`, task store, ledger, ViewModel, `docs/agent.md` | Mutation lifecycle spine, doc 03 |
| Local API/MCP | `LocalApiEndpoints`, `AgentApiPolicy`, `docs/agent-api.md`, composition of `McpToolBridge` | Preserve existing routes and deterministic scopes; no HTTP/SSE MCP expansion |
| Runtime/resources | `ServerProcessManager`, `ResourceCoordinator`, `IsolatedLabRuntimeHost`, managed identity/configuration models, Services and Models VMs | Preserve R32 machinery; close Auto-tune bypass and configuration drift |
| Lab/recommendations | Recipe service/runner, experiment service, recommendation application/derivation/store, Lab/Services review projections, `docs/lab.md` | Repair evidence-to-decision lifecycle before adding recipes |
| RAG/knowledge | Pipeline/watched-source/store generation structure, query surface, dataset management, `docs/rag.md`, R32 closure | Preserve atomic publication and temporal lineage; mature workflows, no GraphRAG |
| Benchmarks | Run preparation/case/finalization, rankings/insights, Speed Check docs, VM commands | Cancellation and quality evidence reuse, doc 07 |
| Voice | Composition, TTS/STT Services surfaces, startup probes and shutdown orchestration, `docs/voice.md` | Include owners in shared lifecycle; no new speech subsystem |
| Storage/security | Settings replacement, BackupService, security review/roadmap, data-root skill | Restore budgets and truthful contracts; no owner-data experiments |
| Desktop UX | Main navigation plus Agent, RAG, Lab, Services, Models, Settings, Memories, Doctor, diagnostic/confirmation views | Job-oriented source walkthrough, not live visual acceptance |
| Verification | Harness registration/isolation, layout guards, Agent scenarios, lifecycle/benchmark/recommendation tests, testing docs | Add scenario evidence at production boundaries, retain useful unit tests |
| Platform/release | `build.sh`, installed entry/link/runtime, Desktop Program/lock, Windows launcher, CI/branch/release workflows, remote rules/checks/PR #15 | Doc 09; no settings, workflow or publication changes |
| Review history | R32 README/roadmap/corrective closure, R31 and earlier ledger references, current deferred/security ledgers | Historical closure does not override new dogfood evidence |

## 1.2 Findings that materially change sequencing

Priority is implementation order, not a claim of an exploitable security severity.

| ID | Evidence and current problem | Confidence and consequence |
| --- | --- | --- |
| A1 | Desktop `App.axaml.cs:117` starts stores, recommendation reconciliation and Lab process recovery; `MainWindowViewModel:367` owns post-setup/background lifecycle. LocalApi `Program:1` loads settings and only initializes memory/RAG explicitly. | RF. Shared DI is not whole-product boot/shutdown. |
| A2 | `ServicesViewModel.cs:570` constructs a manager in each server row; `AgentViewModel:942` constructs patch review; ViewModel task writes include `:1450` and `:2038`. Task store has an initialization gate, not a per-task operation owner. | RF. Extract authority, not just Desktop code-behind. |
| A3 | MainWindow normal close already awaits shutdown, but catches failure and closes anyway. App exit records clean before reawaiting shutdown. App embedding warmup/backfill uses detached work and CancellationToken.None (`App.axaml.cs:243-310`), outside the MainWindow task list. | RF; clean-on-failed-drain and unowned-work risks. Preserve existing awaitable close, repair its result/coverage. |
| A4 | `MainWindow.axaml.cs:165` starts the replacement process before closing; `Program.Main` releases SingleInstanceGuard only when the old lifetime exits. | RF ordering; IN replacement can lose the lock race and exit. Reproduce without repeating owner data migration. |
| G1 | `AgentService.cs:565` checks write policy and tool availability but does not fully validate mutation argument shape/pre-image before creating pending approval at `:590`. `AgentToolExecutor:213` reads missing arguments as strings; workspace validation occurs later. | RF. Malformed or infeasible mutations can reach owner review. |
| G2 | Direct approval at `AgentService.cs:1395` appends Applied patch metadata after execution without requiring successful/changed outcome. `AgentToolExecutor:230` can return a structured Blocked/Unavailable result instead of throwing. | RF. Proposed/Approved/Applied presentation can disagree with filesystem outcome. |
| G3 | `AgentService.cs:767` marks a model Final complete. File write methods return the intended content after atomic write; there is no task-wide artifact verification gate. | RF gap; OO superseded content retained. Byte equality and semantic acceptance need separate evidence. |
| G4 | `AgentViewModel.NewTask` (`:1255`) clears only part of transient state; `RefreshTaskPreview` clears queued patches/state/ledger, not all response, context, follow-up, selection and pending async projections. | RF partial reset; OO stale previous-task presentation. |
| G6 | Direct approval receives `AgentViewModel.BuildOptions:2593` without Policy; `AppendApprovalAsync:1341` replaces the root but does not reload the manifest. Workspace enforcement reads only options.Policy (`AgentWorkspaceTools:400`), where null allows. Queued patch review does reload policy. | RF missing current-policy propagation; IN changed-policy-after-review bypass. Mandatory sibling regression, not proof of absent initial risk gating. |
| G5 | `CreateChildTaskAsync` (`AgentService:287`) already inherits workspace and Project. Approval uses persisted task root (`:1335`). | RF. Sub-agent write failures remain OO/U as to exact cause; verify later paths end to end. |
| R1 | `ServerProcessManager.AutoTuneAsync:356` reconstructs a subset of ServerConfig, tries at most one reduced context, then returns to original-context layer descent. `TryProbeAsync:476` directly starts a Process. | RF. Existing admission/configuration identity does not cover this path. |
| R2 | `ServicesViewModel.ApplyModelDefaultsIfPathActuallyChanged:1759` changes context only when a model card supplies a default. Models tuning (`ModelManagementViewModel:930,1007`) omits hardware/GGUF arguments for context recovery. | RF. Carried context remains possible and sibling recovery differs. |
| R3 | Services `RebindConfig:377` intentionally preserves all editable fields; `SettingsService.SaveCandidateAsync` replaces live settings. `SyncToConfig:1492` later copies editor values back. | RF mechanism; IN applied settings can be silently overwritten without dirty-field reconciliation. |
| L1 | `RecommendationApplicationService:75` saves settings before recording final decision/status (`:85`). Services refreshes on SettingsChanged (`:2318`). `ReconcileAsync:177` addresses pending transactions only. | RF. A refresh can see old status; already-satisfied independent proposals need semantic reconciliation. |
| L2 | `ManagedServerRecommendationPatch.Create:55` already rejects an empty delta. `RecommendationReviewViewModel.InspectTarget:102` sends only a page name; Apply/Undo discard transaction result. | RF. Do not propose an already-existing empty-patch guard; fix satisfaction, retained evidence routes and refusal display. |
| B1 | `BenchmarkService.RunAsync:155-169` captures hardware and runtime/profile identity before its try/catch; VM Run/Rerun use finally without catch (`BenchmarkViewModel:193,258`). | RF escaping cancellation path, matching OO dispatcher crash class. |
| P1 | Installed desktop Exec has an unquoted path with a space; `build.sh:183` generates the same unquoted form. Intended symlink and target exist and are executable. GLib rejects installed entry; a quoted temporary control parses. | Deterministic local evidence, doc 09. Visible launch still untested. |
| P2 | `DoctorService.BuildCheck:202` infers action kind from label; `DoctorViewModel:585` maps category to page, not a target control. MainWindow `:157` assigns ActivePanel only. | RF. ActionTarget/precise remediation is not honored by this path. |
| S1 | `BackupService.RestoreAsync:105` preflights paths then calls ExtractToFile; no entry count, total expanded-byte or streaming expansion budget. Reparse ancestor checks do exist. | RF. Select bounded exhaustion protection; do not reopen already-fixed containment as absent. |
| S2 | Live main ruleset requires Ubuntu/Windows build checks. CodeQL default setup is configured and analyses succeeded, but neither inspected ruleset requires CodeQL. | Live RF. Successful analysis is not proof of enforced merge blocking. |

Line numbers are baseline navigation aids. The named methods and surrounding
control flow are the evidence; ranges here are not claims that one line alone
proves the whole defect. Repository paths in this table are under `src/` with
the project implied by class; detailed documents give the full owning paths.

## 1.3 Dogfood provenance and limits

The supplied planning request is the OO source for full-file rewrite leftovers,
malformed/blocked approved writes, repeated read/reason loops, child mutations,
New Task leakage, narrow artifacts, model-switch/Auto-tune crash, benchmark
cancellation crash, stale recommendations/details, launcher failure and Doctor
navigation. No external workload or model names become requirements.

The local settings projection was inspected read-only: preferred and installed
runtime were both Vulkan, while saved service placement differed between Chat
and embeddings. This does not establish what was configured during the reported
failure, nor a detection bug. Private roots, model names, task contents and
identifiers are intentionally omitted from this pack.

No matching crash stack was established in this pass. The Auto-tune crash remains
Unknown as to mechanism. The presence of a broad catch in Services and Models
means adding another generic catch is not a justified root-cause repair. Capture
process exit, exception stack, callback/dispatcher boundary and memory pressure
in an isolated reproduction during R33.
