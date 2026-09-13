# 12. R33 strict completion audit

Audit date: 2026-09-13. Branch: `r33/planning`. Release target: `v0.41.0-beta`.
Baseline: `c944feb`. Implementation closeout: the local continuation commit
for this audit.

This audit is against the supplied planning pack and roadmap, not against a
reduced list of implemented files. The mandatory local code is complete, but
R33 owner acceptance is not complete: the live platform, native-runtime,
visible-UI, two-process, and actual PR/check cells below still require owner
validation. No mandatory batch is assessed as `not implemented`; the items
that are not earned are called out as owner gates or conditional rejections.

The status words have the following precise meaning in this document:

- **Implemented and verified** means the repository behavior and the relevant
  deterministic or host-automated proof exist. It does not mean that a live
  owner GUI or hardware gate has passed.
- **already satisfied with evidence** is used only for a retained existing
  contract that the pack explicitly said not to recreate and whose current
  source/tests provide the required evidence.
- **conditionally rejected/not earned with evidence** means the conditional
  gate was inspected and was not earned. No speculative implementation was
  added.
- **owner validation required** means the local boundary exists, but the
  planned owner observation, native runtime, physical platform, two-process,
  or publication gate has not been proven here.
- **not implemented** means a mandatory requested behavior is absent. None of
  the mandatory B0-B9 or S rows has this status. This does not turn an
  unearned conditional branch into a failure.

## Evidence ledger

| Id | Exact evidence | What it establishes and what it does not establish |
| --- | --- | --- |
| E0 | Planning baseline in `docs/review/r33/07-behavioural-verification.md:120-130`: `2,675` passed, `17` skipped, `2,692` total; source inventory and owner limits in `docs/review/r33/01-current-state-and-evidence.md:37-83` | The comparison baseline and the original Unknown crash/child mechanisms. It is not proof of R33 repairs. |
| E1 | `/tmp/hermaeus-r33-tests-debug-current/r33-final-debug-current.trx` | Debug sequential suite: `2,695` passed, `17` skipped, `0` failed, `2,712` total. |
| E2 | `/tmp/hermaeus-r33-tests-release-current/r33-final-release-current.trx` | Earlier Release sequential closeout: `2,695` passed, `17` skipped, `0` failed, `2,712` total. |
| E7 | `/tmp/hermaeus-r33-final-tests-6/r33-final-current-6.trx` and `/tmp/hermaeus-r33-final-release-tests-4/r33-final-release-4.trx` | Resumed UX/editor/pet continuation: current Debug and Release sequential suites each passed `2,703`, skipped `17`, failed `0`, `2,720` total. |
| E3 | Direct final coverage command using `scripts/coverage.sh`'s inner `dotnet test` command; `/tmp/hermaeus-r33-coverage-final-4/c96e8abd-d1d9-4152-adbf-e0f21e07efed/coverage.cobertura.xml` | `50,267/77,713` lines, `64.68%` line coverage, above the `60%` ratchet. The initial instrumented run exposed two timing-sensitive reconciliation assertions; strengthened waits produced the final passing run. |
| E4 | `dotnet run --project src/Tools/R33Driver/R33Driver.csproj --no-build -- --settings-path /tmp/hermaeus-r33-driver-final-continued/settings/settings.json --data-root /tmp/hermaeus-r33-driver-final-continued/data --workspace /tmp/hermaeus-r33-driver-final-continued/workspace` | Current production composition with explicit scratch substitutions returned `ok:true`, an `Applied` changed/readback-verified Agent receipt, cancelled benchmark evidence, one RAG generation/query, Chat retrieval context, voice completion, Lab failure cleanup, Lab Apply/reopen, and shutdown. It is not native-runtime or GUI proof. |
| E5 | `/mnt/Dev/GitHub/hermaeus/dist/hermaeus-0.41.0-beta-linux-x64.tar.gz`, its `.sha256` sidecar, and the isolated install at `/tmp/hermaeus-r33-xdg-final-current` from package path `/tmp/hermaeus-r33-package-final-current/package with spaces` | SHA256 passed; installer exited 0; `desktop-file-validate` exited 0; `Exec` is quoted and points through `Hermaeus -> app/hermaeus-app`; the only validator output is the existing multiple-main-category hint. It is not visible-window proof. |
| E6 | Local commits `26ec8d7`, `ad42f59`, `75409f1`, `15104d1`, `1601413`, `7db1d46`, `c431c1d`, `cc494ac`, `53ed924`, and `911e4ba` | The implementation sequence and bounded ownership of changes. No remote publication, tag, PR, merge, or release was performed. |
| E8 | Two Release runs of a temporary `NativeAutoTuneOwnerScenarioTests` harness on 2026-09-13, with a copied owner settings file and temporary data root | The production `ModelManagementViewModel.AutoTuneModelCommand` started the real owner Gemma GGUF plus its Gemma MTP draft, stopped that source, observed a real b10924 `llama-server` candidate for Qwen3.5-4B, and restored Gemma afterward. `/proc` observations showed a target-only window with no source model, the target command used `--ctx-size 16384` and `--n-gpu-layers all` with no draft flags, and the target profile was persisted only after restoration. This is native runtime/model-card command-path evidence, not a visible GUI click. |
| E9 | Controlled b10930 Linux probe on 2026-09-13, loopback port `39352`, PID `17768`, owner Gemma GGUF, `--n-gpu-layers 17`, `--props`, `--metrics`, and trace logging; stopped with Ctrl-C | The runtime returned context as `default_generation_settings.params.n_ctx = 4096` and slots as `total_slots = 1`, omitted GPU layers from `/props`, and emitted `offloaded 17/36 layers to GPU` plus the PID-visible Vulkan model allocation. This establishes the native evidence shape used by the repaired parser. It is not a completed owner Lab run or GUI acceptance proof. |
| E10 | Final 2026-09-13 continuation: Debug and Release solution builds, focused Release regressions, complete Debug/Release suites, `./scripts/coverage.sh`, and the Release R33 driver on fresh `/tmp/hermaeus-r33-driver-final-Uek2YZ` state | Both builds completed with `0` warnings and `0` errors; the focused set passed `131/131`; each complete suite passed `2,719`, skipped `17`, failed `0`, total `2,736`; the coverage gate passed; the driver returned `ok:true`. The process-association validator and deterministic Lab fixtures now require PID, executable, and argv evidence before Lab control or Apply. This remains host-automated evidence, not owner GUI proof. |
| E11 | Approved-host native b10930 matrix on 2026-09-13 using the exact owner Gemma GGUF, loopback ports `39413`, `39411`, and `39412`, `--props`, `--metrics`, and bounded SIGINT cleanup; detailed receipts are in `docs/review/r33/13-lab-benchmark-authority-audit.md` | CPU reported `0/36` offloaded, partial GPU `17/36`, and all GPU `36/36`; all three returned HTTP 200, exposed context `4096` and slots `1`, reported timing metrics, exited `0`, and left no server. This proves native placement and cleanup, not semantic equivalence or owner Lab GUI acceptance. |
| E12 | Final evidence continuation on 2026-09-13: approved-host Debug/Release builds, focused Release authority tests, complete Debug/Release suites, fresh Release R33 driver, and `./scripts/coverage.sh` | Both builds completed with `0` warnings and `0` errors; the focused authority set passed `260/260`; each complete suite passed `2,722`, skipped `17`, failed `0`, total `2,739`; the fresh driver returned `ok:true`; the final coverage gate passed the repository's 60% line ratchet. This remains host-automated evidence, not owner GUI proof. |
| E13 | Current local repair continuation on 2026-09-13: focused R33 regressions, complete Debug/Release suites, final `./scripts/coverage.sh`, fresh Release driver, rebuilt `v0.41.0-beta` Linux package, and isolated package apphost launch | The focused repair set passed `119/119`; each complete suite passed `2,731`, skipped `17`, failed `0`, total `2,748`; the coverage gate passed the 60% ratchet; the driver returned `ok:true`; package checksum/layout/no-PDB checks passed. The apphost created a native `Hermaeus - WIZARD` `1280x820` window and its syscall trace found no owner-data path, but interruption was used after the native check. No GUI pixel, control-flow, or clean-window-close proof is claimed. |
| E14 | 2026-09-13 cleanup-source continuation: per-process test scratch ownership, scenario-run outer-finally cleanup, build failure traps, isolated driver wrapper, clipboard/Python/voice/provider cleanup, R30 temp-parent cleanup, and an approved-host packaged launch using existing Linux owner data | The final Debug suite passed `2,733`, skipped `17`, failed `0`, total `2,750`; the Release suite had the same result; the cleanup-focused regression set and driver passed; the package checksum/layout/no-PDB checks passed. The live package loaded the configured Gemma chat and Qwen embedding servers, and a COSMIC screenshot was captured and pixel-inspected. The native CUA surface exposed no app/window controls, so model switching, Lab/Benchmark walkthroughs, editor/approval/dialog checks, and GUI close remain owner validation. The fallback console interrupt stopped the app and children but left the lifecycle journal `CleanExit:false`; no clean-close claim is made for this attempt. |
| E15 | 2026-09-13 pre-dogfood correction: reusable verification scratch helper, bash smoke coverage for success/failure/SIGINT/stale/unrelated paths, fresh Release driver, and isolated packaged Linux signal checks | `scripts/verification-scratch.sh` passed syntax and cleanup smoke checks, and the Release driver returned `ok:true` with its isolated Agent, benchmark, RAG, Chat, voice, Lab, Apply/reopen, and shutdown receipt. The direct packaged SIGINT check left the app alive with `CleanExit:false` and `LastOperation:"running"`; the exact test process was then stopped with SIGTERM and exited 143, also without a clean marker. This classifies the current `CleanExit:false` as console-interrupt/harness termination evidence that bypasses the Avalonia close/tray path, not as a demonstrated product shutdown defect. The original exit-139 event remains `UNRESOLVED`. |

## Mandatory batches and roadmap acceptance cells

The acceptance cells are the rows in `docs/review/r33/11-dependency-roadmap.md:44-61`.
The status is for the complete cell, so a row can be locally repaired and still
require its explicitly planned owner gate.

| Batch | Status | Exact implementation and verification | Remaining acceptance boundary |
| --- | --- | --- | --- |
| B0 baseline, identity, and failure fixtures | **Implemented and verified** | `src/Hermaeus.Core/Services/ApplicationLifecycle.cs`, `src/Hermaeus.Core/Models/RuntimeIdentityModels.cs`, `src/Hermaeus.Services/ApplicationLifecycleCoordinator.cs`, and `src/Tools/R33Driver/Program.cs` establish operation/lifecycle identity and fail-closed scratch isolation. `ApplicationLifecycleCoordinatorTests.Partial_startup_is_retryable_and_ready_startup_is_cached` and `Shutdown_deadline_returns_without_waiting_for_an_owner_that_ignores_cancellation` verify retry and incomplete-shutdown evidence. Initial spine: `26ec8d7`; final owner/drain correction: `cc494ac`; native repair and evidence update: `53ed924`. | The original exit-139 dogfood event was not separately reproduced as an isolated crash. E8 exercises the repaired source-loaded/target-card path with real models and runtime; visible GUI and broader owner hardware cells remain separate gates. |
| B1 Linux launcher and benchmark cancellation | **owner validation required** | `build.sh` now escapes desktop-entry `Exec`; `src/Hermaeus.Tests/ReleaseReadinessRegressionTests.cs` checks the generator, E5 checks the generated/install path. `BenchmarkService.RunAsync`, `BenchmarkViewModel`, `ServiceTests.BenchmarkCancellationDuringPreparationPersistsTerminalEvidence`, its `XunitHarnessTests.HarnessCases` registration, and E4 prove a durable preparation-cancel outcome with no fabricated case result. Implemented across `26ec8d7`, `75409f1`, and `cc494ac`. | The roadmap cell requires V06's phase-by-phase public-command/VM coverage and V09's owner launch. The current deterministic proof covers preparation cancellation and parser/install behavior, not every case/save/cleanup timing or a visible COSMIC/Windows window. |
| B2 shared lifecycle, runtime registry, task owner, and driver | **owner validation required** | `ApplicationLifecycleCoordinator`, `ManagedRuntimeRegistry`, `ServerProcessViewModel`, `App.axaml.cs`, `Program.cs`, `SingleInstanceGuard.cs`, and `MainWindow.axaml.cs` provide shared startup/drain, stable server manager ownership, embedding/voice ownership, and bounded restart handoff. `ServicesViewModelTests.Rebuild_reuses_the_existing_row_instead_of_replacing_it`, `ApplicationLifecycleCoordinatorTests`, `SingleInstanceGuardTests.ReleaseFreesTheLockForANextAcquire`, E4, and the native tray run in E8 verify the local seams and installed Linux shutdown path. Commits `26ec8d7`, `7db1d46`, `c431c1d`, `cc494ac`, and `53ed924`. | The owner must validate a real two-process restart handoff, Windows/native platform behavior, and remaining visible window-close semantics. The installed Linux tray quit, managed child exit, and clean journal result are no longer untested. |
| B3 prepared mutation and review contract | **owner validation required** | `src/Hermaeus.Agent/Services/AgentMutationPreparation.cs`, `AgentPatchReviewService.cs`, `AgentService.cs`, `AgentTaskCommandOwner.cs`, and `AgentModels.cs` validate typed arguments, workspace/target/preimage/post-image and policy before review. `AgentPatchReviewServiceTests.QueueAsync_prepares_and_persists_the_patch_without_viewmodel_state_writes`, `ApplyAsync_refuses_a_prepared_patch_after_the_target_changes`, `AgentSteeringTests`, `AgentSubtaskModelSelectionTests`, `AgentPolicyRevalidationTests`, and E4 cover direct/queued policy and a real prepared write. Commits `26ec8d7`, `1601413`, and `7db1d46`. | Owner review must exercise the complete malformed/manual/child/command path with the selected local model and platform filesystem behavior. The local contract is verified, but the owner-live cell is not. |
| B4 receipts, readback, child recovery, and New Task isolation | **owner validation required** | `FileAgentTaskStateStore` startup reconciliation classifies post-image as applied, pre-image as unknown, and unexpected content as conflict without replay. `AgentLifecycleRecoveryTests.Startup_recovery_marks_a_written_pending_receipt_applied_without_replay`, `Startup_recovery_does_not_replay_when_only_the_preimage_exists`, `Startup_recovery_classifies_unexpected_content_as_conflict`, `AgentContinueTaskTests`, `AgentOrchestrationViewModelTests`, `AgentTaskContinuityViewModelTests`, `AgentPatchReviewServiceTests`, and E4 verify durable receipts, reopen, and task transitions. Commits `26ec8d7`, `ad42f59`, and `7db1d46`. | The full parent/child filesystem run, delayed callback after New Task, crash-after-write timing, and owner-visible artifact review remain live acceptance cells. No blind replay is claimed. |
| B5 configuration and recommendation reconciliation | **owner validation required** | `ServicesViewModel` tracks dirty fields/base revision; `RecommendationApplicationService` records durable decision status before consumer refresh; `RecommendationReviewViewModel` surfaces transaction status; `ServicesConfigurationReconciliationTests.External_save_updates_clean_fields_but_preserves_dirty_editor_fields` and `External_save_refreshes_a_clean_editor_without_marking_it_dirty`, `RecommendationApplicationTests`, `RecommendationTests`, `LabViewModelTests`, and E4's Lab Apply/reopen result verify the local transaction and reopen boundaries. `75409f1`, `7db1d46`, and `cc494ac`. | Owner must verify clean/dirty editor behavior, rapid/out-of-order saves, stale Undo, retained Details after restart, and that next Start does not overwrite applied settings. No automatic runtime restart is claimed. |
| B6 canonical AutoTune and runtime recovery | **owner validation required** | Production Services/Models/bulk callers route through singleton `src/Hermaeus.Services/ManagedRuntimeTuningService.cs`, which uses `IManagedRuntimeProcessFactory`, `IResourceCoordinator`, full `ServerConfig` launch identity, lease cleanup, candidate outcome persistence, and cancellation/admission/preparation evidence. `ServerProcessManager.AutoTuneWithProbe_preserves_the_full_launch_configuration_for_each_candidate`, `ModelManagementViewModelTests.AutoTuneModel_refuses_a_running_model_without_touching_the_tune_profile_store`, `AutoTuneModel_refuses_when_no_managed_executable_resolves`, `ModelAutoTuneLifecycleTests`, and E4 prove bounded local behavior. E8 additionally proves the production model-card command path with an owner model already loaded, a different real target, source suspension, native candidate fit, target-only command identity, and source restoration. Commits `15104d1`, `cc494ac`, and `53ed924`. | The original exit-139 event was not separately reproduced with a stack or process/OS classification, and E8 was a production VM command-path run rather than a visible card click. Owner must still validate the visible Models-card action, additional placement/backend combinations, constrained failure behavior, and no leak across those cases. |
| B7 Lab lifecycle, discovery, evidence, and Apply | **owner validation required** | `LabRecipeService.ReconcileBaselineAvailability`, `LabExperimentService`, `IsolatedLabRuntimeHost`, `LabViewModel`, `LabRecipeService`, and `EffectiveLaunchObservationParser` retain baseline/candidate/evidence/apply ownership. Engine candidates now update typed and legacy placement together; the isolated launch requests transient `/props`; b10930 nested properties and its startup offload receipt are associated with PID and redacted argv; missing effective proof ends as `Inconclusive`. `LabRecipeTests.Engine_recipe_rewrites_typed_gpu_placement_for_each_candidate`, `Missing_effective_launch_evidence_is_inconclusive_and_cannot_be_applied`, `AdaptiveInferenceTests.Effective_parser_reads_b10930_nested_props_and_process_bound_gpu_receipt`, `Lab_effective_parser_requires_process_association_for_auditable_receipt`, and E9-E10 verify the repaired evidence boundary. | Owner must rerun the real selected runtime/model path through cancel and source restore, inspect retained details, and validate actual loaded-versus-configured identity and the repaired receipt in the visible Lab surface. The driver intentionally uses a deterministic/fake Lab boundary. |
| B8 Agent/RAG/Lab UX and Doctor targets | **owner validation required** | `AgentView.axaml`, `RagView.axaml`, and `LabView.axaml` now make the primary job, current state, next action, evidence disclosure, capability availability, and terminal outcome readable while retaining the Run/Changes/Workspace/History, Ask/Sources/Diagnostics/Manage, and Experiment/Evidence direction. Agent adds clean New Task reset feedback, friendly approval/mutation/verification/conflict labels, a bounded workspace editor/fallback that reapplies current text after Monaco failure or detach, and persisted run-artifact navigation. RAG clears stale query projections, labels Local files versus Remote web, and exposes citation/trace inspection without putting raw diagnostics in the normal answer path. Lab disables actions when no eligible server/recipe exists and surfaces availability, run, restore, Apply, and cancellation next actions. `R33UxAndPetTests`, `AgentTaskContinuityViewModelTests`, `AgentWorkbenchLayoutTests`, `LabViewModelTests`, `RagQuestionBoxTests`, `RagViewModelWatchedSourceTests`, and the structural guards cover the local projection. | Static XAML, view-model tests, and source output cannot prove keyboard focus, DPI, resize, accessibility, contrast, usable artifact width, native editor behavior, or the owner walkthrough of Agent/RAG/Lab. Those remain owner validation. |
| S bounded restore hardening | **Implemented and verified** | `src/Hermaeus.Services/BackupService.cs` uses `BackupRestoreLimits`, preflights entry/total budgets, rejects duplicate normalized targets, streams actual expanded bytes through temporary files, honors cancellation, and commits only after validation. `BackupRestoreSafetyTests.Restore_refuses_an_entry_over_the_per_file_budget_before_writing`, `Restore_refuses_when_total_uncompressed_size_exceeds_the_budget`, `Restore_rejects_duplicate_targets_even_when_overwrite_is_allowed`, `Restore_cancellation_leaves_no_target_or_restore_temporary_file`, plus existing containment tests verify the boundary. Commits `26ec8d7` and `75409f1`. | The roadmap explicitly says no owner restore. The remaining security/CI review is tracked under B9/S2; existing reparse containment is retained, not reimplemented. |
| B9 integrated verification, docs, package, and owner platform evidence | **owner validation required** | `CHANGELOG.md`, `docs/features.md`, `docs/user-guide.md`, `docs/agent.md`, `docs/rag.md`, `docs/lab.md`, `docs/security-review.md`, `docs/review/r33/README.md`, the v0.41.0-beta package, E1-E5, E7, and E3 provide local integrated evidence. The continuation also carries the Monaco notices/assets and pet provenance. | Owner still controls Linux visible launch, Windows build/runtime/GUI validation, actual R33 PR merge checks, CodeQL enforcement, native editor/pet behavior, and release/tag/publication. No publication action was taken. |

## V01-V14 acceptance scenarios

These rows are the exact scenario set from `docs/review/r33/07-behavioural-verification.md:73-88`.

| Scenario | Status | Exact evidence and conclusion |
| --- | --- | --- |
| V01 malformed mutation | **Implemented and verified** | `src/Hermaeus.Agent/Services/AgentMutationPreparation.cs`; `AgentTests.AgentPlanSubtasksApprovalRejectsAnInvalidPlanInstead`; `AgentSteeringTests`; `AgentPatchReviewServiceTests.QueueAsync_prepares_and_persists_the_patch_without_viewmodel_state_writes`. Invalid plans and malformed tool shapes are refused before a pending approval and before a write. |
| V02 Approved versus Applied versus Verified | **Implemented and verified** | `AgentPatchReviewServiceTests.ApplyAsync_writes_the_file_and_marks_the_patch_applied`, `ApplyAsync_refuses_a_prepared_patch_after_the_target_changes`, `AgentLifecycleRecoveryTests`, `AgentPolicyRevalidationTests`, and E4's receipt fields distinguish decision, outcome, changed, and readback verification. |
| V03 full-file replacement | **owner validation required** | `AgentPatchReviewService`, `AgentService`, `FileAgentTaskStateStore`, `AgentTaskContinuityViewModelTests`, and E4 prove complete replacement/readback/reopen for the controlled driver artifact. The planning cell also requires obsolete-section semantics, full owner-selected artifact inspection, and no model Final masking; the owner live cell has not passed. |
| V04 child mutation | **owner validation required** | `AgentService.CreateChildTaskAsync`, `AgentSubtaskModelSelectionTests`, `AgentOrchestrationViewModelTests.Child_approval_shows_the_parent_goal_and_resumes_the_orchestrated_run`, `AgentPatchReviewServiceTests.RevertTaskAsync_reverts_a_parent_and_its_finished_children`, and receipt persistence cover the local child contract. A real selected-model parent approval, child write, reopen, and filesystem review remain owner validation. |
| V05 New Task | **owner validation required** | `AgentViewModel.NewTask`, generation guards, `AgentTaskContinuityViewModelTests.NewTask_clears_the_composer_and_leaves_the_persisted_task_untouched`, `Starting_a_fresh_goal_after_NewTask_creates_a_task_with_a_new_id`, and `AgentContinueTaskTests` cover local reset/identity. The delayed task-A callback and owner visual artifact boundary remain live gates. |
| V06 benchmark cancellation | **owner validation required** | `BenchmarkService.RunAsync`, `BenchmarkViewModel`, `ServiceTests.BenchmarkCancellationDuringPreparationPersistsTerminalEvidence`, harness registration, and E4 verify cancellation before case execution and persisted `Cancelled` phase. The roadmap's every-phase cancellation matrix and owner dispatcher/no-success-toast walkthrough are not fully earned. |
| V07 model switch/recovery | **owner validation required** | `ManagedRuntimeTuningService`, `ResourceAllocationFactory`, `ServerProcessManager`, Services/Models/bulk caller routes, `ServerProcessManagerTests.AutoTuneWithProbe_preserves_the_full_launch_configuration_for_each_candidate`, `ModelAutoTuneLifecycleTests`, and the canonical cancellation/evidence code prove local full-config admission and cleanup. E8 proves a real source-loaded/target-unloaded model-card command path with native b10924 candidate launch and restoration. The original crash classification, real constrained failure, visible click, and broader effective backend combinations remain owner validation. |
| V08 recommendation Apply | **owner validation required** | `ServicesConfigurationReconciliationTests`, `RecommendationApplicationTests`, `RecommendationReviewViewModel`, `LabExperimentService.ApplyAsync`, `LabViewModel`, and E4's Lab Apply/reopen receipt prove local clean/dirty reconciliation and durable Apply plumbing. Owner must verify the complete UI/restart/Details/stale-Undo sequence. |
| V09 Linux launcher | **Implemented and verified** | `build.sh`, `ReleaseReadinessRegressionTests`, E5's quoted desktop entry and `desktop-file-validate` result establish package generation, parser acceptance, symlink target, executable bit, and space-containing XDG/package paths without owner-data mutation. A visible owner menu/window launch remains separately required by the owner matrix. |
| V10 Doctor remediation | **owner validation required** | `DoctorActionTarget`, `DoctorService`, `DoctorViewModel`, `MainWindowViewModel.NavigateToDoctorTarget`, `ServicesViewModel.NavigateToDoctorTarget`, `ServicesView.axaml.cs.FocusPendingDoctorTarget`, and `DoctorAdvisoryTests.Server_specific_doctor_findings_keep_a_typed_remediation_target` prove typed target resolution and missing-entity feedback. Owner must verify actual focus, keyboard, DPI, and unsupported-target presentation. |
| V11 shared owner/recovery | **owner validation required** | `ApplicationLifecycleCoordinator`, `ManagedRuntimeRegistry`, `AgentTaskCommandOwner`, `AgentPolicyRevalidationTests`, `AgentLifecycleRecoveryTests`, `SingleInstanceGuard.TryAcquireForHandoff`, existing conflict/receipt tests, and E4 cover local ownership, policy drift, cancellation, recovery, and bounded handoff code. Two real processes, lock transfer under actual restart, and native interruption remain owner validation. |
| V12 whole-product lifecycle | **Implemented and verified** | E4 and `src/Tools/R33Driver/Program.cs` exercise production composition through shared bootstrap, Agent, benchmark cancel, RAG ingest/generation/query, Chat retrieved context, voice, Lab failure cleanup, Lab Apply/reopen, and bounded shutdown. E4 uses only named scratch substitutions for nondeterministic external providers/runtime timing, so native hardware and visible GUI remain owner gates. |
| V13 optional API owner | **conditionally rejected/not earned with evidence** | `src/Hermaeus.LocalApi/LocalApiEndpoints.cs` maps health/chat/memory/RAG/models/embeddings/capabilities only; `src/Hermaeus.LocalApi/LocalApiModels.cs` sets `AgentApiContract.ExecutionRoutesAvailable=false` and explains the missing shared owner. `docs/review/r33/02-application-authority-and-lifecycle.md:126-155` requires two-process ownership/revocation/recovery proof first. No Agent execution endpoint was added. |
| V14 optional editor | **owner validation required** | `WorkspaceEditorView`, the local Monaco bundle, `Avalonia.Controls.WebView` 12.1.0, the AvaloniaEdit fallback, and `R33UxAndPetTests` establish the bounded local editor/bridge contract. Windows/Linux native rendering, workers, offline requests, focus, keyboard/IME, DPI, large artifacts, and teardown still require owner GUI validation. |

## Owner-observed defects and source findings

The supplied dogfood classes are listed in `docs/review/r33/01-current-state-and-evidence.md:65-71`; the numbered source findings are in `:37-58`. Each row below records the disposition of that exact defect class.

| Finding | Status | Evidence and conclusion |
| --- | --- | --- |
| A1 whole-product boot and lifecycle ownership | **owner validation required** | Original source finding `docs/review/r33/01-current-state-and-evidence.md:39`; repaired through `ApplicationLifecycleCoordinator`, Desktop registration, Local API startup/shutdown, and E4. Live startup, resource readiness, and clean exit remain owner gates. |
| A2 duplicate runtime managers and task writes | **Implemented and verified** | Original finding `:40`; `ManagedRuntimeRegistry`, `AgentTaskCommandOwner`, `ServicesViewModelTests.Rebuild_reuses_the_existing_row_instead_of_replacing_it`, Agent mutation tests, commits `26ec8d7` and `c431c1d`. Stable server identity and owner routing are locally verified. |
| A3 detached embedding/backfill and clean-on-failed-drain | **owner validation required** | Original finding `:41`; `App.axaml.cs` registers embedding warm-up/backfill and voice with lifecycle, `ApplicationLifecycleCoordinator` drains reverse registration order, and `ApplicationLifecycleCoordinatorTests` verify incomplete shutdown. Owner must observe real shutdown with the installed stores/providers. |
| A4 restart lock race | **owner validation required** | Original finding `:42`; `MainWindow.RestartAsync`, `Program.RestartHandoffArgument`, and `SingleInstanceGuard.TryAcquireForHandoff` now implement explicit bounded handoff, with `SingleInstanceGuardTests.ReleaseFreesTheLockForANextAcquire`. A two-process live race test has not been run. |
| G1 malformed or infeasible approved write reaches review | **Implemented and verified** | Original finding `:43`; `AgentMutationPreparation`, `AgentPatchReviewService`, `AgentTests.AgentPlanSubtasksApprovalRejectsAnInvalidPlanInstead`, `AgentSteeringTests`, and `AgentPatchReviewServiceTests` refuse invalid proposals before review/write. |
| G2 Applied displayed after blocked/unavailable execution | **Implemented and verified** | Original finding `:44`; `AgentPatchReviewService`, `AgentService`, receipt outcomes, `AgentLifecycleRecoveryTests`, `AgentPolicyRevalidationTests`, and E4 keep blocked/unavailable/failed/cancelled outcomes out of Applied. |
| G3 full-file replacement leaves stale/narrow artifacts or Final masks failure | **owner validation required** | Original finding `:45`; readback/hash/changed fields in `AgentPatchReviewService` and E4 close the byte-level path. Full owner-selected artifact semantics and visual inspection remain unproven. |
| G4 New Task leaks old projections | **owner validation required** | Original finding `:46`; `AgentViewModel.NewTask`, task-generation checks, and `AgentTaskContinuityViewModelTests` close the deterministic reset path. Delayed callbacks in a live view remain owner validation. |
| G5 child mutation has unreliable ownership | **owner validation required** | Original finding `:48`; canonical child inheritance in `AgentService.CreateChildTaskAsync`, orchestration/patch/revert tests, and durable receipts cover the local contract. Owner-selected child filesystem execution/reopen remains unproven. |
| G6 direct/queued policy drift after review | **Implemented and verified** | Original finding `:47`; `AgentPatchReviewService.WithPolicyAsync` clears stale caller policy, and `AgentPolicyRevalidationTests.Direct_approval_refuses_when_the_persisted_policy_changes_after_review` plus `Queued_patch_refuses_when_the_persisted_policy_changes_after_review` cover both paths. |
| R1 reduced-config/direct AutoTune allocation path | **owner validation required** | Original finding `:49`; `ManagedRuntimeTuningService`, `ResourceAllocationFactory`, full launch configuration preservation coverage, candidate evidence, and production caller routing repair the local authority bypass. E8 confirms the real b10924 target invocation is isolated from the loaded source and uses native candidate fit. The original crash classification and broader native combinations remain owner validation. |
| R2 model switch or Models tuning loses recovery context | **owner validation required** | Original finding `:50`; Services and Models use the canonical service with GGUF/hardware inputs and cancellation. `ModelManagementViewModel` now suspends running source servers, builds an isolated target probe from verified companions, restores the source on success/failure/cancel, and saves only after restoration. Focused coverage proves failure/cancellation ordering, and E8 proves the real b10924 source-loaded/target-unloaded production command path with Qwen target identity and restoration. Visible card activation and additional native failure/backend combinations remain owner validation. |
| R3 external Apply overwrites editor state | **owner validation required** | Original finding `:51`; `ServicesViewModel` base revision/dirty-field reconciliation plus both `ServicesConfigurationReconciliationTests` repair the local seam. Owner must exercise actual open editor/rapid-save/restart behavior. |
| L1 recommendation decision publication ordering | **owner validation required** | Original finding `:52`; `RecommendationApplicationService`, `RecommendationReviewViewModel`, `RecommendationApplicationTests`, and E4 establish durable status before the visible transaction result. Owner must validate refresh ordering and retained Details. |
| L2 stale/satisfied recommendation and discarded details | **owner validation required** | Original finding `:53`; `RecommendationReviewViewModel` now presents transaction result/status, Lab baseline reconciliation preserves Unknown reasons, and E4 proves Apply/reopen. Full stale Undo, deleted evidence, and owner UI navigation remain live. |
| benchmark preparation/cancellation crash class | **owner validation required** | Original finding `:54`; `BenchmarkService`, `BenchmarkViewModel`, harness registration, `ServiceTests.BenchmarkCancellationDuringPreparationPersistsTerminalEvidence`, and E4 repair the demonstrated preparation boundary. Every phase and owner dispatcher behavior remain to be observed. |
| P1 unquoted Linux launcher | **Implemented and verified** | Original finding `:55`; `build.sh`, `ReleaseReadinessRegressionTests`, E5 checksum/install/parser evidence repair the exact desktop-entry defect. Visible owner launch is a separate acceptance gate, not silently inferred. |
| P2 Doctor page-only navigation | **owner validation required** | Original finding `:56`; typed `DoctorActionTarget`, stable server selection, unavailable message, and control focus mapping are covered by source and `DoctorAdvisoryTests`. Owner keyboard/focus/DPI proof remains. |
| S1 restore resource exhaustion and duplicate targets | **Implemented and verified** | Original finding `:57`; `BackupService.BackupRestoreLimits`, four `BackupRestoreSafetyTests`, and existing containment tests cover per-file/total/actual-byte budgets, cancellation cleanup, duplicate aliases, traversal, and reparse containment. |
| S2 CodeQL analysis versus enforced merge gate | **owner validation required** | Original finding `:58` and `docs/review/r33/09-security-platform-and-release.md:102-139`; local security source/docs remain intact, but actual R33 PR rules/checks and CodeQL enforcement are owner-controlled and were not changed or claimed. |
| full-file rewrite leftovers | **owner validation required** | Supplied dogfood class in doc 01; byte readback is in `AgentPatchReviewService` and E4, while owner inspection of the complete selected artifact remains open. |
| repeated read/reason loops | **owner validation required** | Supplied dogfood class in doc 01; existing bounded failure/read loop behavior and `AgentUnreadableResponseTests` remain, but no live model run was used to establish the reported loop closure. |
| narrow artifacts | **owner validation required** | Supplied dogfood class in doc 01; task artifact/readback projection and E4 cover the controlled file, not the owner's actual generated artifact and usable-width walkthrough. |

## Explicit reconciliation of the named R33 scope

| Named item | Status | Exact accountability |
| --- | --- | --- |
| Configuration/recommendation reconciliation | **owner validation required** | `ServicesViewModel`, `RecommendationApplicationService`, `RecommendationReviewViewModel`, `ServicesConfigurationReconciliationTests`, `RecommendationApplicationTests`, `LabViewModel`, and E4 implement and locally verify dirty/clean reconciliation, durable decision status, Apply/reopen, and refusal display. Owner clean/dirty/rapid-save/Details/restart validation remains. |
| Lab lifecycle | **owner validation required** | `LabRecipeService`, `LabExperimentService`, `IsolatedLabRuntimeHost`, `LabViewModel`, Lab tests, and E4 cover baseline availability, failure cleanup, Apply, reopen, and stop count. Real runtime/model/cancel/restore is not proven. |
| Agent UX | **owner validation required** | `AgentView.axaml` and `AgentViewModel` retain the Run/Changes/Workspace/History direction while making New Task, active/waiting/approval states, prepared versus applied/verified/conflicted outcomes, child/plan status, artifacts, and next actions readable. Workspace now prefers the bounded local Monaco editor with AvaloniaEdit fallback; narrow action groups wrap. `AgentTaskContinuityViewModelTests`, `AgentWorkbenchLayoutTests`, `R33UxAndPetTests`, and the full suite cover deterministic projections. Owner must dogfood ask_user, approvals, prepared mutations, diff/artifact inspection, child recovery, failure/cancel/stale callbacks, keyboard/focus, narrow width, and DPI. |
| RAG UX | **owner validation required** | `RagView.axaml` and `RagViewModel` retain the mature ingest/retrieval/citation/trace architecture while exposing Local files versus Remote web, query state, next action, evidence count, source/trace disclosure, ingest state, cancellation wording, and stale-query clearing. Toolbar, navigation, source actions, and evaluation controls wrap on narrow layouts. `R33UxAndPetTests`, RAG question/watched-source tests, and the full suite cover deterministic projections. Owner must add/attach sources, inspect ingest/index state, query successful/degraded/refused paths, open citations and traces, cancel/fail/retry, and use Chat Knowledge attachment. |
| Lab UX | **owner validation required** | `LabView.axaml` and `LabViewModel` retain the existing recipe/evidence/apply lifecycle while distinguishing unavailable capability from a runnable recipe, disabling actions without a configured server or eligible recipe, and surfacing run, cancellation, restore, Apply, and recovery next actions. Recipe detail and filters are disclosed, and action groups wrap on narrow layouts. `LabViewModelTests`, `R33UxAndPetTests`, and the full suite cover local availability/state projections. Owner must exercise current-model capability discovery, recommendation/effective-state presentation, cancellation, source restore, Apply/reopen, Doctor/remediation navigation, and native runtime behavior. |
| Doctor remediation | **owner validation required** | `DoctorActionTarget`, `DoctorService`, `DoctorViewModel`, `MainWindowViewModel`, `ServicesViewModel`, Services focus mapping, and `DoctorAdvisoryTests` implement the typed destination and unavailable reason. Physical focus and keyboard proof remain. |
| Linux launcher | **Implemented and verified** | `build.sh`, `ReleaseReadinessRegressionTests`, E5 package checksum, quoted `Exec`, symlink/executable check, and space-containing scratch install pass. Owner visible menu/window launch remains a separate live cell. |
| Restore budgets | **Implemented and verified** | `BackupService` and the four named `BackupRestoreSafetyTests` prove declared-entry, total, actual expansion, cancellation, duplicate-target, and cleanup boundaries. |
| Runtime/AutoTune recovery | **owner validation required** | `ManagedRuntimeTuningService`, `ServerProcessManager`, `ResourceAllocationFactory`, Services/Models/bulk routes, source suspension/restoration, cancellation handling, and `ModelAutoTuneLifecycleTests` repair and locally verify the source-loaded/target-unloaded lifecycle and save ordering. E8 verifies the real production model-card command path with Gemma loaded, Qwen selected as target, native candidate fit, no source draft contamination, and restoration. The original crash mechanism, visible card activation, and broader native fit matrix remain owner gates. |
| Benchmark cancellation | **owner validation required** | `BenchmarkService`, `BenchmarkViewModel`, registered harness case, and E4 persist preparation cancellation. The full phase matrix and owner dispatcher behavior remain. |
| Whole-product driver | **Implemented and verified** | `src/Tools/R33Driver/Program.cs` and E4 exercise production composition across Agent, benchmark, RAG, Chat, voice, Lab, Apply/reopen, and shutdown with isolated settings/data/workspace. It is not a native or GUI proof. |
| Desktop/service authority and security | **owner validation required** | `ApplicationLifecycleCoordinator`, `ManagedRuntimeRegistry`, `AgentTaskCommandOwner`, prepared mutation/receipt boundaries, `Program` handoff, `SingleInstanceGuard`, `BackupService`, security docs, E1-E5, `cc494ac`, and `53ed924` cover local authority and safety. Owner two-process, hardware, Windows, visible UI, PR, and CodeQL enforcement gates remain. |
| Monaco decision | **owner validation required** | The local Monaco 0.56.0 bundle, typed `WorkspaceEditorView` bridge, WebView 12.1.0 host, and AvaloniaEdit fallback are implemented and locally tested. The full cross-platform/offline/native worker/resource gate in doc 04 remains open, so the fallback remains a supported result. |
| ChatGPT Pet v2 package | **owner validation required** | `ChatGptPetPackageCatalog` validates generic data-only v2 packages with path/reparse/resource/type checks; Moss is bundled under `assets/pets/moss`, the setting is backward-compatible and off by default, and the overlay persists a clamped position. `R33UxAndPetTests` covers manifest/package safety and bounds. Owner must validate rendering, selection/import, drag direction, lifecycle, DPI, and unobtrusive interaction. Moss provenance and licensing uncertainty are recorded beside the asset. |
| JSONL/runtime experiments | **conditionally rejected/not earned with evidence** | `docs/review/r33/05-runtime-fit-and-evidence.md:94-123` records installed b10821 without `--log-jsonl` and no exact compatible dense-FFN or DFlash/DSpark pair. No speculative adapter or experiment was added. |
| Agent API execution | **conditionally rejected/not earned with evidence** | `AgentApiContract.ExecutionRoutesAvailable=false` in `src/Hermaeus.LocalApi/LocalApiModels.cs`; `LocalApiEndpoints` maps no execution route. `docs/review/r33/02-application-authority-and-lifecycle.md:126-155` requires one actual host owner and two-process proof first. |
| Release, security, and platform work | **owner validation required** | `Directory.Build.props`, `build.sh`, `CHANGELOG.md`, packaging/security docs, E1-E5, E3, `cc494ac`, and `53ed924` establish local `v0.41.0-beta` build/package/security evidence. Windows/native/live launch, actual R33 PR checks, CodeQL enforcement, tag, release, and publication are owner-only. |

### Automated evidence versus owner GUI dogfooding

The following distinction is deliberate and applies even where the local
projection is fully tested:

- Automated evidence proves friendly state/next-action strings, stale-view
  generation guards, command enablement, path and resource boundaries, local
  asset presence, and the persisted Agent/RAG/Lab contracts. It does not prove
  that a person can read the hierarchy at the target window size or that a
  native control receives focus and input correctly.
- Agent owner dogfooding remains required for the complete new-task to
  completion path, ask_user and approval visibility, prepared mutation and
  before/after inspection, child/subtask recovery, failed/cancelled/conflicted
  outcomes, run-artifact opening, narrow workspace editing, and stale callback
  prevention in a live window.
- RAG owner dogfooding remains required for source add/attach through ingest,
  Local/Remote scope, successful/degraded/refused/error/cancelled queries,
  citation and trace drill-down, and Chat Knowledge attachment. The existing
  RAG architecture is not being replaced.
- Lab owner dogfooding remains required for capability and recipe availability,
  recommendation versus effective runtime state, cancellation and recovery,
  source restore, Apply/reopen, and Doctor remediation navigation.
- The optional Monaco path additionally needs native WebView2 and WebKitGTK/
  WPE validation. The optional pet path needs rendering, drag, resize/DPI,
  selection/import, and lifecycle validation. Neither surface is claimed
  visually verified here because no native Desktop session was available.

## Conditional decisions

| Conditional branch | Status | Gate evidence |
| --- | --- | --- |
| C1 Monaco/WebView editor spike | **owner validation required** | A bounded local implementation, narrow bridge, local asset packaging, and AvaloniaEdit fallback now exist. The required comparison protocol and ship gate remain `docs/review/r33/04-workspace-editor-decision.md:102-128`; no source or static test is being treated as Windows/Linux native proof. |
| C2 JSONL and selected runtime experiments | **conditionally rejected/not earned with evidence** | `docs/review/r33/05-runtime-fit-and-evidence.md:94-123` records b10821 help without `--log-jsonl`; exact installed binary/model pair and measured benefit were not established. |
| C3 Agent HTTP execution | **conditionally rejected/not earned with evidence** | `src/Hermaeus.LocalApi/LocalApiModels.cs` keeps execution unavailable; `LocalApiEndpoints` exposes only read/query/chat/RAG/memory/embedding/model/capability routes. The topology, token revocation, reconnect, approval race, and two-process gate in doc 02:126-155 is not earned. |

## Commit `26ec8d7` and test-count reconciliation

`26ec8d7` is the first R33 implementation spine, not the whole R33 delivery.
Its files map to the roadmap as follows:

- B0/B2: `src/Hermaeus.Core/Services/ApplicationLifecycle.cs`,
  `src/Hermaeus.Services/ApplicationLifecycleCoordinator.cs`, and the initial
  production `src/Tools/R33Driver/Program.cs` composition.
- B1/S: `src/Hermaeus.Services/BenchmarkService.cs`, `build.sh`,
  `src/Hermaeus.Services/BackupService.cs`, and the initial benchmark/restore
  regression tests.
- B3/B4: `AgentMutationPreparation.cs`, `AgentPatchReviewService.cs`,
  `AgentService.cs`, `AgentTaskCommandOwner.cs`, task receipts, policy/schema
  checks, child/task lifecycle code, and Agent regression tests.
- B5/B6 wiring: `RecommendationApplicationService.cs`, the initial Services and
  Models caller changes, and the first shared ownership shape. It did not yet
  close full AutoTune configuration/admission or reconciliation.
- B9/document identity: `Directory.Build.props`, `CHANGELOG.md`, README and
  the R33 roadmap anchors.

The later commits complete the spine in bounded pieces: `ad42f59` adds receipt
recovery, `75409f1` adds configuration/recommendation reconciliation, restore
budgets, Doctor target data and benchmark harness coverage, `15104d1` routes
production tuning through managed ownership, `1601413` closes the queued/direct
policy sibling, `7db1d46` expands the production driver and owns embedding
shutdown work, `c431c1d` centralizes runtime manager identity, and `cc494ac`
closes voice/drain/restart, typed Doctor focus, AutoTune cancellation/evidence,
and release-doc synchronization. `53ed924` then closes the reentrant native
shutdown path and the loaded-source Models-card AutoTune path, with focused
regression coverage and the native evidence recorded as E8.

The phrase “increased by only eight” applies to the intermediate checkpoint,
not to the final rebuilt tree. The planning baseline was `2,675` passed,
`17` skipped, `2,692` total (E0). The `26ec8d7` checkpoint added exactly eight
test cases:

- two in `AgentPatchReviewServiceTests`;
- two in `ApplicationLifecycleCoordinatorTests`;
- four in `BackupRestoreSafetyTests`.

That produced the previously recorded `2,683` passed, `17` skipped, `2,700`
total artifact. The strict audit then continued implementation. The final
rebuilt assembly contains twelve additional regression cases introduced by the
subsequent commits: three `AgentLifecycleRecoveryTests` in `ad42f59`, six
configuration/Lab/Doctor/benchmark cases in `75409f1`, one AutoTune case in
`15104d1`, and two policy revalidation cases in `1601413`. The earlier E1/E2
result is therefore `2,695` passed, `17` skipped, `2,712` total, or **twenty
more passing cases than the planning baseline**, not eight. The resumed
UX/editor/pet continuation added seven focused regression cases; its E7
Debug/Release checkpoint result was `2,703` passed, `17` skipped, `2,720`
total. `cc494ac` added no new test case; the continuation adds the bounded
presentation/package cases without changing the earlier checkpoint count.

The small count relative to the size of the implementation is expected: the
pack required production-boundary repairs, not one test method per source
change; many paths are exercised through existing tests and the single
production-composition driver. V01-V14 are acceptance scenarios, not fourteen
automatic xUnit methods. The count must nevertheless be reported as the final
observed count above, not rounded back to the earlier checkpoints. The
acceptance repair pass adds eight regression cases for effective Lab launch
evidence, saved-projection verification, parent/child lifecycle recovery, and
open-task isolation, followed by one process-association regression in the
final continuation. The current Debug and Release harness results are each
`2,722` passed, `17` skipped, `2,739` total. The continuation adds the shared
runtime evidence envelope, effective-value mismatch guard, and complete Lab
recipe authority audit.

## Acceptance continuation

The post-dogfood repair pass closes the two persisted contradictions that
remained after the earlier implementation closeout. Window close and tray
service stopping now enter the bounded managed-process shutdown path. Agent
parents with pending or running children cannot be finished or dismissed, a
terminal parent with live children is recovered as blocked, and the workbench
cannot start a second top-level task over an open task. Missing workspace
`AGENTS.md` suggestions are previewed and queued as normal prepared patches.

Lab stores configuration-scoped effective launch observations from the managed
runtime properties endpoint and the PID-associated startup receipt. Current
b10930 exposes context and slots through nested `/props` fields but exposes GPU
layers only in the explicit `offloaded N/M layers to GPU` startup line. The
receipt now retains PID, redacted exact argv, executable path, and bounded
startup evidence, and stale typed GPU intent cannot collapse recipe candidates.
Missing or invalid process association, or missing or mismatched effective
fields, finish the workload as `Inconclusive`, not `Succeeded`, with no
recommendation or Apply. Lab and recommendation Apply
paths read the live Services projection after save before recording success.

The continuation also audits all twelve shipped Lab recipes and the reusable
Benchmark path against one `RuntimeEvidenceEnvelope`. Requested, resolved,
launched, effective, and telemetry identities are compared independently. A
completed Benchmark workload with missing or conflicting evidence is retained
as visible `Unverified` or `Mismatch` evidence but is excluded from ranking,
Insights, and Speed Check. The complete recipe/effective-field matrix and
approved-host CPU, partial-GPU, and all-GPU receipts are in
`docs/review/r33/13-lab-benchmark-authority-audit.md`.
The workbench now exposes before/after mutation receipt content, human-readable
runtime/model telemetry identities, a bounded Monaco readiness fallback whose
AvaloniaEdit host is bounded and reattached with current text, and semantic
audio cues for the repaired terminal transitions. Pet idle animation is slowed
and avoids the observed blink frame, with a small drag direction deadzone.

The owner-supplied Linux Lab run on b10930 exposed the exact pre-repair defect:
the displayed candidate slices inherited a typed `exact:999` placement, the
runtime `/props` response used nested context and `total_slots` fields, and the
run was shown as `Succeeded` while effective fields were Unknown or mismatched.
The owner-supplied Agent screenshot likewise showed the fallback status beside
a blank editor. The bounded source repairs and focused regressions address
those roots. E9 confirms the installed runtime's native receipt shape, and E10
confirms the fail-closed process association plus final automated gates, but a
post-repair owner Lab rerun and visible editor walkthrough remain acceptance
gates.

The isolated R33 driver was rerun on fresh scratch paths and returned
`ok:true`, with an Applied/readback-verified Agent receipt, benchmark
cancellation, RAG generation/query, Chat retrieval context, voice completion,
Lab failure cleanup, Lab Apply/settings reopen, and runtime stop count one.
This is production-composition evidence, not native runtime, GUI, or pixel
acceptance evidence.

## Cleanup-source continuation

The temporary-artifact audit found that successful paths were not enough to
establish ownership. Test roots were previously one top-level directory per
test, scenario cleanup could be bypassed by cancellation or post-processing
exceptions, the driver required caller-created scratch paths, clipboard images
and Python health checks had implicit temporary ownership, and several voice
providers could leave partial or playback-failed WAV files. The bounded repairs
give each producer an owner and a cleanup boundary:

- `TempDir` uses one per-process run container, reclaims stale Hermaeus test
  containers on the next process, and deletes the container at process exit.
- `AgentScenarioRunner` cleans the leaf, run id, and parent roots from an outer
  `finally`, including cancellation and post-processing failures.
- `build.sh` removes only its exact publish/package outputs when a build does
  not reach checksum completion; `scripts/verification-scratch.sh` owns one
  namespace-scoped scratch root, removes it on every normal exit path, and
  performs bounded stale recovery without sweeping unrelated `/tmp` paths.
  `scripts/run-r33-driver.sh` is only the R33-specific adapter that passes its
  settings, data, and workspace paths to the driver.
- Clipboard image paste, Python health validation, Kokoro bootstrap scripts,
  and OpenAI, F5, Kokoro, XTTS, and native Kokoro implicit audio outputs now
  delete owned partial or playback-failed artifacts. Explicit output paths stay
  caller-owned.

The complete Debug and Release suites both passed `2,733`, skipped `17`, and
failed `0`. The focused cleanup regressions passed, and the fresh Release
driver returned `ok:true` with no verification scratch root remaining after
completion. The helper smoke checks also covered failure, SIGINT cancellation,
bounded stale recovery, and preservation of an unrelated namespace. This is
source and host automation evidence. It does not claim that a hard process
kill can run managed cleanup; stale-run recovery is the bounded fallback for
that boundary.

The focused AutoTune regression suite covers a loaded source server and an
unloaded target model. It observes the order `suspend -> tune -> restore`,
builds the target probe without the source model's draft, projector, or extra
arguments, and proves that failure and cancellation restore the source without
saving a target profile. E8 then exercises the production model-card command
path with the owner Gemma model running, the real Qwen GGUF unloaded, and the
real b10924 llama runtime. The source is absent during the target candidate,
the target command has no Gemma draft, a profile is saved at effective context
16,384, and Gemma is restored afterward. This is not a visible GUI click, and
the original exit-139 event was not separately reproduced.

On 2026-09-13, the Release Linux package was launched with the owner's
configured Chat and Embeddings services, then the real status-notifier tray
item **Quit Hermaeus** was invoked. The process exited with code 0, both
`llama-server` children were gone, and the lifecycle journal recorded
`CleanExit:true`; runtime logs recorded all three shutdown owners and
`timedOut:false`. This is native Linux shutdown evidence for the exact tray
path. A later isolated package check sent SIGINT directly to the app and
observed the process remain alive with `CleanExit:false` and
`LastOperation:"running"`; stopping that exact test process with SIGTERM
returned 143 and still did not invoke the lifecycle clean marker. `Program.Main`
does not register a console interrupt handler, and the product shutdown
coordinator is entered by the Avalonia window/tray close path, so this is
classified as console-interrupt/harness evidence rather than a product close
failure. The original exit-139 event remains `UNRESOLVED`, and normal packaged
window close remains `NEEDS OWNER VALIDATION`. Windows, restart handoff, and
broader owner-live GUI checks remain open.

## Final owner gate

R33 can be called locally implemented and ready for owner validation. It cannot
yet be called owner-accepted complete. Before acceptance, the owner must record
the rows marked `owner validation required` in the matrix from
`docs/review/r33/07-behavioural-verification.md:94-116`, especially:

- visible Linux and Windows package launch, clean exit, restart handoff, and
  GUI keyboard/DPI/focus/resize checks;
- visible Models-card activation and additional managed runtime/model/GPU
  AutoTune and model-switch recovery combinations, including the original crash
  mechanism or a documented inability to reproduce it;
- real Lab cancel/restore and recommendation clean/dirty/Details/restart
  workflow;
- selected local-model Agent parent/child/full-file/New Task workflows;
- Agent ask_user, approval, Changes/diff, artifact, failure/cancel/conflict,
  narrow editor, and stale-view workflows;
- RAG source ingest/query/citation/trace/cancel/retry and Chat Knowledge
  attachment workflows;
- Lab capability/recommendation/effective-state, cancellation/recovery,
  Doctor/remediation, and Apply/reopen workflows;
- Monaco native WebView/worker behavior and ChatGPT Pet rendering, drag,
  selection/import, resize, DPI, and lifecycle behavior;
- benchmark cancellation across the remaining phases and RAG/voice live paths;
- actual R33 PR head, required build checks, CodeQL analysis/enforcement state,
  and owner-controlled release/publication actions.

No tag, release, push, PR, merge, repository-setting change, owner-settings
write, or publication action was performed during this audit or implementation.
