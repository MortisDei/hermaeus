# R31 final dogfood closure

Date: 2026-08-29

This is the close-out record for the final R31 dogfood findings. It records
each finding from the closure brief, the implementation or evidence trace, and
the remaining owner-only live gates. It does not rewrite the historical R31
review records.

## Finding matrix

| Finding | Disposition and evidence | Automated coverage | Owner live gate |
| --- | --- | --- | --- |
| A1 duplicate models after Update | Fixed. Local GGUF discovery compares canonical local path identity against provider rows, without deduplicating distinct files by display name. | `ModelManagementViewModelTests.Refresh_does_not_duplicate_a_local_model_reported_with_a_normalized_path` | Retest Update and Refresh with a model that moves from an old flat path to its repository folder. |
| A2 Auto Tune card remains stale | Already correct. Successful tuning saves the profile, refreshes the initiating card summary, and clears `RetuneRecommended`; failure leaves the recommendation visible. | Existing Auto Tune lifecycle and profile-store tests. | Retest a successful Auto Tune from the initiating card in packaged Windows and Linux builds. |
| A3 download leaves Search unusable | Already correct by trace. Search and file loading have separate busy flags, and download completion, failure, and cancellation leave the controls in their normal state. | Existing Hugging Face download and failure-path tests. | Retest Search after a completed, failed, and cancelled download. |
| A4 search result appears inert during GPU Fit | Fixed. Selecting a repository publishes the selected card and `Checking ... and calculating fit...` before metadata, tree, hardware, and fit work completes. | Existing repository selection and fit tests; the transient state is represented in the command path and UI. | Retest selection against a slow or offline repository and confirm the status remains visible. |
| A5 projector state leaks between models | Fixed. A projector path is cleared when the primary model identity changes, while same-model explicit paths, `UseProjector`, and provenance are retained. Disabled projector use remains excluded from every `--mmproj` launch form. | `ServicesViewModelMmprojTests.Switching_models_does_not_substitute_a_projector` plus existing launch-argument tests. | Retest switching projector-backed and text-only models, clearing, disabling, and re-enabling projector use. |
| A6 companion recovery is not role-aware | Fixed. Manual labels and fallback messages identify projector, MTP draft head, or generic companion roles. Mixed repair states expose both Reacquire and manual Services actions. Hash, revision, and provenance gates remain unchanged. | `ModelManagementViewModelTests.Mixed_companion_repair_state_exposes_verified_and_manual_roles` and existing recovery tests. | Retest mixed missing projector and draft-head states and confirm no draft repair says projector. |
| A7 numeric controls truncate values | Fixed. Services editor columns were widened for context and port values without changing numeric types or limits. | Existing Services layout and numeric binding coverage. | Retest five-digit context and port values on both packaged desktop targets. |
| A8 update and repair status clips remediation | Fixed. Model update, organize, auto-tune, and companion status surfaces wrap. Companion controls sit above the full-width wrapped status text. | Existing model layout and companion-state tests. | Retest long repository, failure, and mixed-repair messages at a narrow window width. |
| A9 managed llama.cpp path needs manual selection | Fixed. Services resolves an installed managed `llama-server` under the configured AI assets root for default or missing managed paths. Existing custom or external paths are preserved. | `ServicesViewModelModelDefaultsTests.Rebuild_resolves_an_existing_managed_llama_server_for_default_slots`. | Retest fresh setup, upgrade, missing runtime, and intentional external executable scenarios. |
| A10 mlock compatibility | Already correct and conservative. `MemoryLock` defaults off; `--mlock` or `--load-mode mlock` is emitted only for explicit configuration and runtime capability. The LFM2.5 observation is not encoded as model incompatibility. | `ServerLaunchArgumentTests.ContextShift_memory_lock_and_no_memory_map_are_opt_in_flags` and load-mode tests. | Retest LFM2.5 with the managed b10588/current runtime inventory on Linux, with mlock off and as an explicit expert override. |
| A11 Model Search Enter | Fixed. Enter invokes the existing Hugging Face search command through view input routing and preserves the same command guard as the Search button. | Build-time XAML/code-behind coverage and existing search tests. | Retest keyboard activation and recovery after a failed search. |
| A12 Auto Tune thread count | Already correct. The observed value of 4 follows `max(ProcessorCount - 1, 1)` capped at 16 on the observed host; an explicit configured value remains authoritative. | Existing `ChooseThreadCount` coverage. | Confirm the selected value against System Overview on each target if further tuning is desired. |
| A13 llama-server executable path paste | Requires owner live retest. Services exposes the executable as an ordinary editable TextBox with keyboard and context-menu handling supplied by Avalonia; source inspection found no field-specific paste suppression or browse-only replacement. Desktop control was unavailable in this pass, so no functional clipboard claim is made and no speculative workaround was added. | Static source trace retained in this closure review; no deterministic clipboard event test is available without a desktop session. | Required on Windows and Linux: paste an absolute executable path into Services with keyboard paste and the native context menu, verify the full value remains in the field, save, and confirm the same path is used by the server card. |
| A14 local model path identity by host OS | Fixed. Canonical full paths now compare case-insensitively on Windows and case-sensitively on non-Windows platforms. Nearby model identity consumers use the shared policy, while repository and filename-role comparisons remain deliberately separate. | `ModelManagementViewModelTests.Local_model_path_comparison_policy_matches_the_current_platform` plus existing alias and model-management coverage. | Retest one Windows path alias and two case-distinct Linux model paths in the packaged builds if the owner wants filesystem-backed confirmation beyond the policy test. |
| B1 Lab Chat server selector | Fixed. Lab consumes the live non-embedding server cards from `ServicesViewModel`, preserves selection by server id, and explains the zero and single-server prerequisites. The isolated experiment and Apply boundary are unchanged. | `LabViewModelTests.Configured_chat_server_comes_from_live_services_cards`. | Required: enter Lab in a release build, select the configured Chat server, start and complete a real isolated experiment, then inspect evidence and Apply review. |
| B2 evidence empty state | Fixed. The Evidence pane distinguishes no captured records from records excluded by the current filters and uses `MossEmptyState`. | `LabViewModelTests.Evidence_empty_state_distinguishes_no_records_from_filtered_records`. | Retest with a new install and with a filter that excludes known evidence. |
| B3 Lab late Services lifecycle | Fixed. Lab can exist before the eventual canonical Chat card appears in the Services snapshot; a production settings save causes Services to rebuild that card, raises its availability event, and Lab selects the resulting `BuildConfig` by its preserved server id while excluding the embedding card. | `LabViewModelTests.Lab_refreshes_when_services_later_rebuilds_the_eventual_canonical_chat_card`. | Covered by the existing required real Lab workflow in B1; also confirm the selector after Services cards are created or repaired in the packaged UI. |
| C1 budget pause presented as a fake question | Fixed. Step budget exhaustion is persisted as `Blocked` with an additive `StepBudgetExhausted` flag, no fake user question, a decision record, and a Continue/Add steps path. | `AgentUnreadableResponseTests` and the registered harness budget case. | Required: exercise a budget pause in the workbench and confirm the visible action is Continue/Add steps or Stop, not Reply. |
| C2 continuation obscure | Fixed. Continue title and watermark become budget-specific, while Run Step appears only for runnable New/Running tasks and Stop only while running. | `AgentWorkbenchLayoutTests` and state-derived property coverage. | Required: use WaitingForUser, approval, runnable, running, budget-paused, and terminal states in the desktop UI. |
| C3 structured response parsing | Already correct / no reproducible bypass found. Existing fenced, surrounding-prose, escaping, truncation, schema, retry, and strict protocol tests cover the parser without making it permissive. | Existing Agent parser and unreadable-response tests. | Retest the observed Granite/Gemma response shape against the packaged runtime if it recurs. |
| C4 internal protocol leakage | Fixed. Terminal task summaries prefer the persisted model final answer; progress, plan, and diagnostic state remain separate surfaces. | Existing Agent workbench state tests plus layout coverage. | Retest a successful read-only task and inspect Run, transcript, and diagnostic surfaces separately. |
| C5 completion inconsistency | Fixed. Budget pauses carry an explicit pause state and note; ordinary terminal tasks retain pending-step notes and the existing Continue path. | Agent budget and terminal-state tests. | Retest model-shortened plans and budget exhaustion in the workbench. |
| C6 duplicate copy and idle Stop | Fixed. State-reactive visibility keeps Run Step, Continue, Reply, and Stop scoped to the state that owns the action. | `AgentWorkbenchLayoutTests.Run_step_and_stop_controls_are_state_scoped`. | Retest the controls at idle, waiting, running, and terminal states. |
| D1 streaming scroll regression | Fixed. Streaming token and reasoning updates no longer invoke unconditional bottom scroll; initial/new-message anchoring remains, and the pin state resumes only at bottom. | Existing `ChatScrollPinStateTests` and source trace of the streaming path. | Required: scroll upward during a long response and confirm incoming tokens do not repin the transcript. |
| D2 clipboard Unicode fidelity | Already correct by source trace. Clipboard copy passes the displayed .NET string through the platform clipboard path without ASCII transliteration; Markdown and JSON export remain separate comparison paths. | Existing clipboard and export tests. | Required: copy quotes, math symbols, and non-ASCII text on Windows and Linux and compare code points with the displayed answer. |
| E retrieval and memory health | Fixed at the bounded surface. Recall retains keyword-only fallback as usable but marks it in the Chat trace, including when hits exist; no hit count is presented as healthy semantic retrieval. Existing RAG embedding degradation notes remain intact. | `ChatRecallInjectionTests.Keyword_only_recall_is_labelled_as_degraded_in_the_trace` plus existing Recall/RAG health tests. | Retest stopped and healthy embedding services and confirm the trace changes without suppressing usable lexical fallback. |
| E retrieval performance | No bounded defect established. Recall sources are launched together and the approximately 3.9 second observation is preserved for deeper profiling rather than guessed optimisation. | Existing Recall concurrency and timeout tests. | Capture a fresh trace only if the latency remains material after this close-out. |
| F1 cross-suite contradiction | Already correct / contradiction not reproduced in the deterministic path. Best overall and Best across every suite use explicit shared-case, per-suite, and hardware rules; the existing tests cover disjoint, single-model, shared, and order-independent cases. | `BenchmarkInsightsMathTests`, `BenchmarkCrossSuiteRankingTests`, and service tag/runtime normalization tests. | Compare the stored dogfood run set again if the contradiction recurs, preserving the report inputs. |
| F2 Insights auto-load and refresh | Fixed. Benchmark entry loads Insights, and a previously loaded report refreshes after load, run, rerun, delete, and clear operations without repeated refreshes before first use. | `BenchmarkViewModelInsightsTests` and existing insights math/service tests. | Retest page entry and each history mutation in the packaged UI. |
| F3 running feedback | Fixed. The header now shows an explicit Benchmark active indicator and retains suite/case progress from the existing service progress string. | Build and existing benchmark progress tests. | Retest a multi-suite run where case progress is visible throughout. |
| F4 counts and wording | Fixed. The Insights header says benchmark run(s); suite, case, result, and shared-case wording remains scoped to the value being described. | Existing Insights and cross-suite wording assertions. | Retest the displayed counts against exported run data. |
| G1 runtime identity missing | Already correct in the traced managed path. Telemetry requests are built from the running managed server's process identity and v2 runtime fingerprint rather than a generic endpoint. | Existing runtime identity and telemetry binding tests. | Required: open Chat telemetry while the managed Windows runtime is actively serving and confirm the identity is attached. |
| G2 process metrics | Already correct for owned managed processes. Process RAM and identity-scoped samples are propagated; remote/external endpoints do not receive invented process attribution. | Existing telemetry source, identity, and ownership tests. | Required: verify process metrics and restart identity separation on Windows and Linux. |
| H1 popup/context-menu containment | Bounded fix applied. Conversation context menus now have a maximum height and Avalonia placement constraint adjustments for flip and resize at window/work-area edges. | XAML compiles; framework-independent behavior remains a desktop interaction gate. | Required on Windows and Linux/COSMIC: open menus at bottom and right edges, click every item, and confirm no spill or fall-through. |
| I RAG bulk ingest | Already correct / performance evidence only. Ingest exposes stage, batch, failure, and cancellation state, flushes bounded batches, and does not claim an ETA it cannot derive. No broad performance rewrite was made. | Existing RAG ingest progress, cancellation, failure, and persisted-batch tests. | Retest a large ingest only if progress or cancellation is not visible in the release build. |

## Explicitly deferred

Only the items approved by the closure brief are deferred here:

- Whole-active-workload GPU Fit. The measured Chat plus GPU embedding coexistence
  evidence is preserved, but the analytical model does not yet include every
  concurrent Hermaeus consumer.
- Hugging Face model artwork, avatar, or icon caching and display.
- The unproven COSMIC folder-picker or portal observation. It is kept separate
  from the shared popup/context-menu fix.

These are recorded in [`deferred.md`](deferred.md). No historical review file
was rewritten.

## Required owner retests

Automation cannot establish the following release behavior:

- Lab B1 and a complete real Lab experiment workflow.
- Context-menu containment on Windows and Linux/COSMIC.
- Chat deliberate-scroll behavior during a long stream.
- Agent blocking and continuation usability.
- Clipboard code-point fidelity on Windows and Linux.
- Services executable-path paste by keyboard and native context menu on Windows and Linux.
- Managed runtime identity and process metrics in the live telemetry flyout.
- Any model/runtime behavior requiring the owner's installed runtime, including
  LFM2.5 and companion recovery.

## Automated verification

- `dotnet build Hermaeus.sln --no-restore`: passed with 0 warnings and 0 errors.
- Focused follow-up regressions: 87 passed, 0 failed, 0 skipped in 2s with
  normal host access.
- `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj --no-build`: 2,268
  passed, 0 failed, 0 skipped in 3m58s with normal host access.
- `pwsh ./scripts/coverage.ps1`: passed the 60% line-coverage ratchet; 2,268
  instrumented tests passed, 0 failed, 0 skipped in 5m33s. The report was
  written and removed beneath the operating-system temp directory.
- `git diff --check`: passed. The initial restricted-runner baseline is not
  treated as product evidence because its app-data and SQLite access failures
  disappeared in the documented normal-host rerun.
