# 12. R33 strict completion audit

Audit date: 2026-09-12. Branch: `r33/planning`. Release target: `v0.41.0-beta`.
Baseline: `c944feb`. Implementation closeout commit: `cc494ac`.

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
| E6 | Local commits `26ec8d7`, `ad42f59`, `75409f1`, `15104d1`, `1601413`, `7db1d46`, `c431c1d`, and `cc494ac` | The implementation sequence and bounded ownership of changes. No remote publication, tag, PR, merge, or release was performed. |

## Mandatory batches and roadmap acceptance cells

The acceptance cells are the rows in `docs/review/r33/11-dependency-roadmap.md:44-61`.
The status is for the complete cell, so a row can be locally repaired and still
require its explicitly planned owner gate.

| Batch | Status | Exact implementation and verification | Remaining acceptance boundary |
| --- | --- | --- | --- |
| B0 baseline, identity, and failure fixtures | **Implemented and verified** | `src/Hermaeus.Core/Services/ApplicationLifecycle.cs`, `src/Hermaeus.Core/Models/RuntimeIdentityModels.cs`, `src/Hermaeus.Services/ApplicationLifecycleCoordinator.cs`, and `src/Tools/R33Driver/Program.cs` establish operation/lifecycle identity and fail-closed scratch isolation. `ApplicationLifecycleCoordinatorTests.Partial_startup_is_retryable_and_ready_startup_is_cached` and `Shutdown_deadline_returns_without_waiting_for_an_owner_that_ignores_cancellation` verify retry and incomplete-shutdown evidence. Initial spine: `26ec8d7`; final owner/drain correction: `cc494ac`. | The pack deliberately retains the native AutoTune crash and exact child dogfood mechanism as Unknown, as required by `docs/review/r33/01-current-state-and-evidence.md:79-83` and `docs/review/r33/05-runtime-fit-and-evidence.md:40-46`. |
| B1 Linux launcher and benchmark cancellation | **owner validation required** | `build.sh` now escapes desktop-entry `Exec`; `src/Hermaeus.Tests/ReleaseReadinessRegressionTests.cs` checks the generator, E5 checks the generated/install path. `BenchmarkService.RunAsync`, `BenchmarkViewModel`, `ServiceTests.BenchmarkCancellationDuringPreparationPersistsTerminalEvidence`, its `XunitHarnessTests.HarnessCases` registration, and E4 prove a durable preparation-cancel outcome with no fabricated case result. Implemented across `26ec8d7`, `75409f1`, and `cc494ac`. | The roadmap cell requires V06's phase-by-phase public-command/VM coverage and V09's owner launch. The current deterministic proof covers preparation cancellation and parser/install behavior, not every case/save/cleanup timing or a visible COSMIC/Windows window. |
| B2 shared lifecycle, runtime registry, task owner, and driver | **owner validation required** | `ApplicationLifecycleCoordinator`, `ManagedRuntimeRegistry`, `ServerProcessViewModel`, `App.axaml.cs`, `Program.cs`, `SingleInstanceGuard.cs`, and `MainWindow.axaml.cs` provide shared startup/drain, stable server manager ownership, embedding/voice ownership, and bounded restart handoff. `ServicesViewModelTests.Rebuild_reuses_the_existing_row_instead_of_replacing_it`, `ApplicationLifecycleCoordinatorTests`, `SingleInstanceGuardTests.ReleaseFreesTheLockForANextAcquire`, and E4 verify the local seams. Commits `26ec8d7`, `7db1d46`, `c431c1d`, and `cc494ac`. | The owner must validate a real two-process restart handoff, installed clean exit, native managed resources, and actual Desktop startup. Static source, deterministic substitutes, and one-process tests do not prove those observations. |
| B3 prepared mutation and review contract | **owner validation required** | `src/Hermaeus.Agent/Services/AgentMutationPreparation.cs`, `AgentPatchReviewService.cs`, `AgentService.cs`, `AgentTaskCommandOwner.cs`, and `AgentModels.cs` validate typed arguments, workspace/target/preimage/post-image and policy before review. `AgentPatchReviewServiceTests.QueueAsync_prepares_and_persists_the_patch_without_viewmodel_state_writes`, `ApplyAsync_refuses_a_prepared_patch_after_the_target_changes`, `AgentSteeringTests`, `AgentSubtaskModelSelectionTests`, `AgentPolicyRevalidationTests`, and E4 cover direct/queued policy and a real prepared write. Commits `26ec8d7`, `1601413`, and `7db1d46`. | Owner review must exercise the complete malformed/manual/child/command path with the selected local model and platform filesystem behavior. The local contract is verified, but the owner-live cell is not. |
| B4 receipts, readback, child recovery, and New Task isolation | **owner validation required** | `FileAgentTaskStateStore` startup reconciliation classifies post-image as applied, pre-image as unknown, and unexpected content as conflict without replay. `AgentLifecycleRecoveryTests.Startup_recovery_marks_a_written_pending_receipt_applied_without_replay`, `Startup_recovery_does_not_replay_when_only_the_preimage_exists`, `Startup_recovery_classifies_unexpected_content_as_conflict`, `AgentContinueTaskTests`, `AgentOrchestrationViewModelTests`, `AgentTaskContinuityViewModelTests`, `AgentPatchReviewServiceTests`, and E4 verify durable receipts, reopen, and task transitions. Commits `26ec8d7`, `ad42f59`, and `7db1d46`. | The full parent/child filesystem run, delayed callback after New Task, crash-after-write timing, and owner-visible artifact review remain live acceptance cells. No blind replay is claimed. |
| B5 configuration and recommendation reconciliation | **owner validation required** | `ServicesViewModel` tracks dirty fields/base revision; `RecommendationApplicationService` records durable decision status before consumer refresh; `RecommendationReviewViewModel` surfaces transaction status; `ServicesConfigurationReconciliationTests.External_save_updates_clean_fields_but_preserves_dirty_editor_fields` and `External_save_refreshes_a_clean_editor_without_marking_it_dirty`, `RecommendationApplicationTests`, `RecommendationTests`, `LabViewModelTests`, and E4's Lab Apply/reopen result verify the local transaction and reopen boundaries. `75409f1`, `7db1d46`, and `cc494ac`. | Owner must verify clean/dirty editor behavior, rapid/out-of-order saves, stale Undo, retained Details after restart, and that next Start does not overwrite applied settings. No automatic runtime restart is claimed. |
| B6 canonical AutoTune and runtime recovery | **owner validation required** | Production Services/Models/bulk callers route through singleton `src/Hermaeus.Services/ManagedRuntimeTuningService.cs`, which uses `IManagedRuntimeProcessFactory`, `IResourceCoordinator`, full `ServerConfig` launch identity, lease cleanup, candidate outcome persistence, and cancellation/admission/preparation evidence. `ServerProcessManager.AutoTuneWithProbe_preserves_the_full_launch_configuration_for_each_candidate`, `ModelManagementViewModelTests.AutoTuneModel_refuses_a_running_model_without_touching_the_tune_profile_store`, `AutoTuneModel_refuses_when_no_managed_executable_resolves`, and E4 prove bounded local behavior. Commits `15104d1` and `cc494ac`. | The original crash was never reproduced with a stack or process/OS classification. `docs/review/r33/05-runtime-fit-and-evidence.md:40-46` explicitly forbids claiming closure without that evidence. Owner must validate real model switch, Vulkan/Auto/CUDA/CPU placement, constrained VRAM, native failure, and no leak. |
| B7 Lab lifecycle, discovery, evidence, and Apply | **owner validation required** | `LabRecipeService.ReconcileBaselineAvailability`, `LabExperimentService`, `IsolatedLabRuntimeHost`, `LabViewModel`, and `LabRecipeService` retain baseline/candidate/evidence/apply ownership. `LabRecipeTests.Production_recipe_reconciliation_keeps_missing_baselines_unknown`, `Production_recipe_reconciliation_requires_an_exact_runtime_and_readable_model`, `Runner_refuses_unknown_recipe_before_launch`, `Runner_cleans_owned_runtime_after_ordinary_workload_exception`, `Successful_recipe_materializes_the_candidate_for_review`, `LabViewModelTests.Baseline_refresh_failure_cancels_the_owned_run_before_source_restore`, and E4 verify availability, cleanup, fake-host Apply/reopen, and stop count. Commits `75409f1`, `7db1d46`, and `cc494ac`. | Owner must run the real selected runtime/model path through cancel and source restore, inspect retained details, and validate actual loaded-versus-configured identity. The driver intentionally uses a deterministic/fake Lab boundary. |
| B8 Agent/RAG/Lab UX and Doctor targets | **owner validation required** | `AgentView.axaml`, `RagView.axaml`, and `LabView.axaml` now make the primary job, current state, next action, evidence disclosure, capability availability, and terminal outcome readable while retaining the Run/Changes/Workspace/History, Ask/Sources/Diagnostics/Manage, and Experiment/Evidence direction. Agent adds clean New Task reset feedback, friendly approval/mutation/verification/conflict labels, a bounded workspace editor/fallback, and persisted run-artifact navigation. RAG clears stale query projections, labels Local files versus Remote web, and exposes citation/trace inspection without putting raw diagnostics in the normal answer path. Lab disables actions when no eligible server/recipe exists and surfaces availability, run, restore, Apply, and cancellation next actions. `R33UxAndPetTests`, `AgentTaskContinuityViewModelTests`, `AgentWorkbenchLayoutTests`, `LabViewModelTests`, `RagQuestionBoxTests`, `RagViewModelWatchedSourceTests`, and the structural guards cover the local projection. | Static XAML, view-model tests, and source output cannot prove keyboard focus, DPI, resize, accessibility, contrast, usable artifact width, native editor behavior, or the owner walkthrough of Agent/RAG/Lab. Those remain owner validation. |
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
| V07 model switch/recovery | **owner validation required** | `ManagedRuntimeTuningService`, `ResourceAllocationFactory`, `ServerProcessManager`, Services/Models/bulk caller routes, `ServerProcessManagerTests.AutoTuneWithProbe_preserves_the_full_launch_configuration_for_each_candidate`, and the canonical cancellation/evidence code prove local full-config admission and cleanup. The original native crash, real constrained VRAM, and effective backend behavior remain Unknown/owner validation. |
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
| R1 reduced-config/direct AutoTune allocation path | **owner validation required** | Original finding `:49`; `ManagedRuntimeTuningService`, `ResourceAllocationFactory`, full launch configuration preservation test, candidate evidence, and production caller routing repair the local authority bypass. Native runtime fit and the reported crash remain owner validation. |
| R2 model switch or Models tuning loses recovery context | **owner validation required** | Original finding `:50`; Services and Models use the canonical service with GGUF/hardware inputs and cancellation; `ModelManagementViewModel` refuses post-cancel save. Real model switch, effective backend, and VRAM behavior remain unproven. |
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
| Runtime/AutoTune recovery | **owner validation required** | `ManagedRuntimeTuningService`, `ServerProcessManager`, `ResourceAllocationFactory`, Services/Models/bulk routes, cancellation handling, and full-config test repair the local authority/recovery path. The original crash mechanism and native fit are Unknown, exactly as required by the pack. |
| Benchmark cancellation | **owner validation required** | `BenchmarkService`, `BenchmarkViewModel`, registered harness case, and E4 persist preparation cancellation. The full phase matrix and owner dispatcher behavior remain. |
| Whole-product driver | **Implemented and verified** | `src/Tools/R33Driver/Program.cs` and E4 exercise production composition across Agent, benchmark, RAG, Chat, voice, Lab, Apply/reopen, and shutdown with isolated settings/data/workspace. It is not a native or GUI proof. |
| Desktop/service authority and security | **owner validation required** | `ApplicationLifecycleCoordinator`, `ManagedRuntimeRegistry`, `AgentTaskCommandOwner`, prepared mutation/receipt boundaries, `Program` handoff, `SingleInstanceGuard`, `BackupService`, security docs, E1-E5, and `cc494ac` cover local authority and safety. Owner two-process, hardware, Windows, visible UI, PR, and CodeQL enforcement gates remain. |
| Monaco decision | **owner validation required** | The local Monaco 0.56.0 bundle, typed `WorkspaceEditorView` bridge, WebView 12.1.0 host, and AvaloniaEdit fallback are implemented and locally tested. The full cross-platform/offline/native worker/resource gate in doc 04 remains open, so the fallback remains a supported result. |
| ChatGPT Pet v2 package | **owner validation required** | `ChatGptPetPackageCatalog` validates generic data-only v2 packages with path/reparse/resource/type checks; Moss is bundled under `assets/pets/moss`, the setting is backward-compatible and off by default, and the overlay persists a clamped position. `R33UxAndPetTests` covers manifest/package safety and bounds. Owner must validate rendering, selection/import, drag direction, lifecycle, DPI, and unobtrusive interaction. Moss provenance and licensing uncertainty are recorded beside the asset. |
| JSONL/runtime experiments | **conditionally rejected/not earned with evidence** | `docs/review/r33/05-runtime-fit-and-evidence.md:94-123` records installed b10821 without `--log-jsonl` and no exact compatible dense-FFN or DFlash/DSpark pair. No speculative adapter or experiment was added. |
| Agent API execution | **conditionally rejected/not earned with evidence** | `AgentApiContract.ExecutionRoutesAvailable=false` in `src/Hermaeus.LocalApi/LocalApiModels.cs`; `LocalApiEndpoints` maps no execution route. `docs/review/r33/02-application-authority-and-lifecycle.md:126-155` requires one actual host owner and two-process proof first. |
| Release, security, and platform work | **owner validation required** | `Directory.Build.props`, `build.sh`, `CHANGELOG.md`, packaging/security docs, E1-E5, E3, and `cc494ac` establish local `v0.41.0-beta` build/package/security evidence. Windows/native/live launch, actual R33 PR checks, CodeQL enforcement, tag, release, and publication are owner-only. |

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
and release-doc synchronization.

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
open-task isolation. The current Debug and Release harness results are each
`2,711` passed, `17` skipped, `2,728` total.

## Acceptance continuation

The post-dogfood repair pass closes the two persisted contradictions that
remained after the earlier implementation closeout. Window close and tray
service stopping now enter the bounded managed-process shutdown path. Agent
parents with pending or running children cannot be finished or dismissed, a
terminal parent with live children is recovered as blocked, and the workbench
cannot start a second top-level task over an open task. Missing workspace
`AGENTS.md` suggestions are previewed and queued as normal prepared patches.

Lab stores configuration-scoped effective launch observations from the managed
runtime properties endpoint. Context, GPU placement, and slots must be
auditable and match the reviewed baseline/candidate, with effective fit also
required for `Auto`; unknown evidence remains a refusal. Lab and recommendation
Apply paths read the live Services projection after save before recording
success. The workbench now exposes before/after mutation receipt content,
human-readable runtime/model telemetry identities, a bounded Monaco readiness
fallback, and semantic audio cues for the repaired terminal transitions. Pet
idle animation is slowed and avoids the observed blink frame, with a small
drag direction deadzone.

The isolated R33 driver was rerun on fresh scratch paths and returned
`ok:true`, with an Applied/readback-verified Agent receipt, benchmark
cancellation, RAG generation/query, Chat retrieval context, voice completion,
Lab failure cleanup, Lab Apply/settings reopen, and runtime stop count one.
This is production-composition evidence, not native runtime, GUI, or pixel
acceptance evidence.

## Final owner gate

R33 can be called locally implemented and ready for owner validation. It cannot
yet be called owner-accepted complete. Before acceptance, the owner must record
the rows marked `owner validation required` in the matrix from
`docs/review/r33/07-behavioural-verification.md:94-116`, especially:

- visible Linux and Windows package launch, clean exit, restart handoff, and
  GUI keyboard/DPI/focus/resize checks;
- real managed runtime/model/GPU AutoTune and model-switch recovery, including
  the original crash mechanism or a documented inability to reproduce it;
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
