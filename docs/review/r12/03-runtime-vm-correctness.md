# r12-03: Runtime ViewModel correctness

Single-item defects in the big runtime ViewModels, ordered by user
impact.

## 3.1 Finishing the setup wizard leaves the app uninitialized

`MainWindowViewModel.InitializeAsync` (`MainWindowViewModel.cs:130-154`)
returns early when `SetupWizardCompleted` is false, before
`Rag.LoadDatasetsAsync`, `Agent.LoadAsync`, `Benchmarks.LoadAsync`,
`Services.AutoStartAllAsync`, `EnsureLocalApiRunningStateAsync`,
`Chat.LoadModelsAsync`, and the startup doctor scan. The
`WizardCompleted` handler (`:111`) only sets `ActivePanel = "chat"`. So
the very first run ends the wizard on an empty chat panel: no servers
auto-started, no models listed, no datasets, until the user restarts
the app or happens to navigate somewhere that lazily loads. The
first-run experience r8 built ends on a dead screen.

Fix: extract the post-wizard block into
`CompletePostSetupInitializationAsync()`; call it from
`InitializeAsync` (normal path) and from the `WizardCompleted` handler
(first-run path, fire via the existing `RunBackgroundTaskAsync` so
completion stays responsive). Guard against double-run with a bool.

Acceptance:
- New test: with `SetupWizardCompleted == false`, InitializeAsync stops
  early; after raising `WizardCompleted`, servers auto-start and
  `Chat.LoadModelsAsync` runs exactly once (fakes with counters).
- Skip ("Do this later") gets the same treatment as Finish.

## 3.2 One failing store aborts the whole startup init

Inside `InitializeAsync`, the sequential awaits have no per-step
isolation: if `Rag.LoadDatasetsAsync` or `Benchmarks.LoadAsync` throws
(locked DB, corrupt file), every later step (auto-start, Local API,
chat models, doctor scan) silently never runs; the only trace is a
console line from `App.InitializeAppAsync`'s outer catch. The user
sees a working-looking app with no models and no explanation.

Fix: wrap each independent step with the existing
`RunBackgroundTaskAsync(operation, ...)` error funnel (runtime log +
toast) or a local try/catch per step; keep hard-ordering only where a
step truly depends on the previous one (AutoStart before LoadModels).

Acceptance: unit test where the RAG step throws: chat models still
load, a runtime log Error entry names the failed step, and a toast
fires.

## 3.3 Background model refresh resets user-tuned chat sampling parameters

Every `GetModelsAsync` fetch materializes new `LlmModel` instances, so
`LoadModelsAsync` reassigns `SelectedModel` to a *different object*
with the same id, firing `OnSelectedModelChanged`
(`ChatViewModel.cs:1177-1200`), which re-applies the model profile
defaults over Temperature/TopP/TopK/MinP/penalties. Model refreshes
happen behind the user's back (server availability events, panel
navigation after the 30 s cache window, and after 1.4's storm every
settings save), so a user who dialed Temperature to 0.2 mid-session
gets silently reset to the profile default. Also related: profile
defaults are sticky across model switches (a model without a profile
value keeps the previous model's), and `Settings.Llm.MaxTokens` is
mutated (r12-01 1.3).

Fix: in `LoadModelsAsync`, when the re-matched model has the same id as
the current selection, update the reference without re-applying
defaults (set the backing field + raise PropertyChanged, or guard
`OnSelectedModelChanged` with an id comparison). Apply profile
defaults only on genuine model *changes*, and reset non-profiled
params to the settings defaults on change so values do not leak
across models.

Acceptance:
- Unit test: set Temperature to 0.2, force `LoadModelsAsync(force: true)`
  returning equal-id fresh instances: Temperature still 0.2, selection
  preserved.
- Unit test: switching model A (profile temp 0.9) to model B (no
  profile temp): temperature returns to the settings default, not 0.9.

## 3.4 AgentViewModel keeps a stale SelectedModel after refresh

`AgentViewModel.LoadAsync` (`AgentViewModel.cs:468-471`) does
`SelectedModel ??= AvailableModels.FirstOrDefault()`: after any
re-load, the previously selected instance is not in the new
`AvailableModels` list (fresh instances), so the ComboBox shows an
item that is not in Items (renders blank) while `CanStart` still
passes with the stale id. Chat already re-matches by id; Agent must
too. Same for `SelectedDataset` after `GetDatasetsAsync`.

Acceptance: unit test: LoadAsync twice with fresh instances keeps a
selection whose reference is in the current `AvailableModels` and id
matches the prior selection.

## 3.5 The agent's default workspace is the whole user profile, analyzed at startup

`AgentViewModel` constructor sets
`WorkspaceRoot = Environment.GetFolderPath(SpecialFolder.UserProfile)`
(`AgentViewModel.cs:404`), and `LoadAsync` (called from
`InitializeAsync` at every startup and on every Agent panel
navigation) unconditionally runs `RefreshWorkspaceFilesAsync` (full
file enumeration of the profile) and `ExplainWorkspaceAsync`, which
analyzes the profile *and writes a "Workspace profile" workspace-memory
entry for it* (`:1040-1046`). Cost aside, the agent silently treating
`C:\Users\<name>` as an active workspace is against the r2+ workspace
posture: the workspace is supposed to be an explicit user choice.

Fix: default `WorkspaceRoot` to empty; the existing `HasWorkspace`
empty-state UI (r8 2.6) already handles "no workspace selected".
`LoadAsync` must skip file listing/analysis when `HasWorkspace` is
false; `ExplainWorkspaceAsync` should remain reachable only for an
explicitly chosen root. Optionally remember the last explicitly chosen
workspace in settings and restore that instead.

Acceptance:
- Fresh start with no prior workspace: no file enumeration, no
  workspace analysis, no auto-created workspace-memory entry (fakes
  with counters), empty-state visible.
- Choosing a folder restores the current analyze/activate behavior.

## 3.6 RAG "Add to dataset" ignores a renamed dataset name

`AddToDataset` stores `_targetDatasetForIngest`
(`RagViewModel.cs:431-444`) and `RagIngestRequestBuilder.PrepareDataset`
ignores `newDatasetName` entirely when `existing` is non-null. A user
who clicks "Add to dataset", then edits the Dataset name box intending
to create a *different* dataset, silently ingests into the original
one. `_targetDatasetForIngest` is also only cleared on successful
ingest, so it survives arbitrary later interactions.

Fix: clear `_targetDatasetForIngest` whenever `NewDatasetName` no
longer equals the target's name (partial `OnNewDatasetNameChanged`),
and reflect the mode in `StatusMessage` ("adding to X" vs "creating
Y"). Also clear it in `ClearChat`-equivalent resets (dataset delete of
the target).

Acceptance: unit test: AddToDataset(A), change name to "B", ingest:
result is a new dataset "B"; A unchanged. AddToDataset(A), ingest
unchanged: documents land in A (existing behavior).

## 3.7 Reindex mutates the dataset's recorded model before it succeeds

`ReindexDatasetAsync` sets `dataset.Config.EmbeddingModel = newModel`
(`RagViewModel.cs:551`) *before* `ReindexDatasetAsync` runs. On
failure or cancellation partway, the in-memory dataset (and whatever
the pipeline persisted of it) claims the new model while some or all
vectors are still old: `ReindexRequired` goes false and the guard from
r10 1.4 is defeated. Verify where the pipeline persists the config; the
VM must not flip the label until the pipeline reports success (pass
the target model as a parameter to `_pipeline.ReindexDatasetAsync` and
let it commit the config atomically with the re-embedded vectors, or
at minimum reassign only after the await succeeds and force a
`LoadDatasetsAsync` on the failure path too).

Acceptance: unit/integration test: cancel a reindex mid-run; the
dataset still reports the old embedding model and `ReindexRequired`
remains true after refresh.

## 3.8 Benchmark rerun lacks guards; insights rerun bypasses CanExecute

`BenchmarkViewModel.RerunAsync` (`BenchmarkViewModel.cs:168-186`) has
no `IsRunning` check or CanExecute: clicking Rerun during a run
overwrites `_runCts` (Cancel now stops only the newer run; the old CTS
leaks) and interleaves status updates. `RerunFromInsightsAsync` calls
`RunAsync()` directly, skipping `CanRun`. Fix: give Rerun the same
`CanExecute = nameof(CanRun)` (plus notify hooks), and route the
insights path through `RunCommand.ExecuteAsync` guarded by `CanRun`.
Also dispose the replaced CTS in `RunAsync` (it already does; Rerun
must match).

Acceptance: Rerun is disabled while `IsRunning`; unit test that a
second Rerun during a run is a no-op.

## 3.9 Small fixes (batch)

- `ServicesViewModel.Rebuild`: dispose `ServerProcessViewModel`s that
  are dropped (covered by 1.4 acceptance); add `Name` to
  `HasUnsavedChanges`.
- `ModelCompareOrchestrator.ToResult` indexes `run.CaseResults[0]`
  unguarded: return an error row when empty instead of throwing.
- `MainWindowViewModel.OnConversationSaved`: the new-item branch checks
  `!string.IsNullOrWhiteSpace(item.Folder)` immediately after setting
  `Folder = string.Empty`; delete the dead call.
- `ChatViewModel`: `EstimateTokensForSend` is unreferenced; delete.
  `ClearChat` should reset `SystemPrompt` to the default the way
  `NewConversation` does.
- `ChatViewModel.RemoveContextAttachment` leaves a stale
  `AttachmentStatus` ("N files ready") when attachments remain;
  recompute the label.
- `DoctorViewModel.RunFix`: four copy-pasted install blocks differing
  only in flags/strings; extract one
  `RunInstallAsync(key, isBusy setter, progress setter, installer)`
  helper. Dispose `_installCts` after use.
- `DoctorViewModel.HandleEmbeddingProgress` appends to
  `embedding_downloads.log` with one fire-and-forget `Task.Run` per
  progress line (interleaved writes); batch through a single
  serialized writer or drop the per-line file append.
- `SetupWizardViewModel.ApplyStepAsync` case 2 silently does nothing
  when `ManagedServers` is empty; surface a toast (parity with the
  guard in case 1).
- `McpServerConfigViewModel.ToConfig` splits arguments on spaces, so a
  quoted argument with spaces is impossible; either honor quotes
  (reuse the existing args tokenizer from Services if present) or
  document the limitation in the field's watermark.

Acceptance: build clean; each bullet either has a small unit test
(ToResult empty case, attachment status recompute) or is verified by
inspection in the PR description.
