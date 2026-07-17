# r12-01: Settings lifecycle and the live-settings mutable global

The root defect class: `ISettingsService.Settings` is one shared
mutable object, and ViewModels write into it directly at many points
outside the apply/validate/save path. Because `SettingsService.SaveAsync`
persists the whole object, *any* save from *anywhere* also persists
every stray in-memory mutation made since the last load. Individually
each write looks harmless; together they mean the settings file can end
up containing values the user never confirmed, and failed saves leave
memory and disk out of sync.

## 1.1 Wizard data-root save bypasses migration (data appears lost)

`SetupWizardViewModel.ApplyStepAsync` case 0
(`SetupWizardViewModel.cs:385-389`) writes `DataRootDirectory` /
`LocalAiAssetsRoot` into settings and calls plain `_settings.SaveAsync()`.
Migration only runs when `SaveAsync(previousDataRoot)` is passed (as
`SettingsViewModel.SaveAsync` does). So re-running the wizard (a
first-class flow since r8: Settings > "Re-run setup wizard") and
choosing a different data root switches the app to an empty root
without moving any databases: conversations, memories, RAG, traces all
"disappear". This is the same field symptom the r11 wizard fix
addressed, reachable through a second door. The wizard also skips the
conflict check (`PreviewDataRootMigration`) that the Settings page
runs.

Fix: capture the previous data root before applying step 0 and call
`await _settings.SaveAsync(previousDataRoot)`; surface the migration
result (files moved, backup dir) the same way `SettingsViewModel.SaveAsync`
does, and refuse with the same error the settings page would show when
the migration plan has conflicts.

Acceptance:
- Re-running the wizard and changing the data root moves the same file
  set the Settings page move does (r11 3.1 manifest), with a toast.
- A conflicting target root fails the step with an error; settings on
  disk still point at the old root.
- First run (no existing databases) still completes without noise.
- Unit test: wizard step-0 apply with an existing populated root calls
  the migration path (assert via a fake `ISettingsService` recording
  the `previousDataRootDirectory` argument).

## 1.2 SaveAsync mutates live settings before validation, rolls back only the data root

`SettingsViewModel.SaveAsync` (`SettingsViewModel.cs:203-246`) runs all
`ApplyTo`/`ApplyToAsync` mutations against the live `Settings` object,
then saves. On save failure it restores only
`DataManagement.DataRootDirectory`; every other applied edit (LLM
params, UI, memory, MCP servers, TTS fields) stays mutated in memory
while disk still has the old values. The next save from any code path
(1.4 below lists several) silently persists the "failed" edits.

Fix (choose one, document the choice):
- Apply into a deep copy, validate/save the copy, and only swap it into
  `ISettingsService.Settings` on success; or
- On save failure, `Reload()` the ViewModels from disk state after
  restoring the data root, so memory matches disk again.

Acceptance:
- Failing save (e.g. migration conflict) leaves `Settings` equal to the
  last successfully saved state, verified by a unit test that fails a
  save and then saves again from an unrelated path, asserting the
  first save's edits are not persisted.

## 1.3 Selecting a chat model overwrites global Llm.MaxTokens

`ChatViewModel.OnSelectedModelChanged` (`ChatViewModel.cs:1181-1182`)
writes `_settings.Settings.Llm.MaxTokens = max` whenever the selected
model has a `DefaultMaxTokens` profile value. Unsaved global mutation
(persisted by the next unrelated save), sticky across model switches
(switching to a model without the profile value keeps the previous
model's cap), and it also silently changes what Benchmark/Agent/RAG
sends see as the global cap.

Fix: give ChatViewModel a local `MaxTokens` field (like Temperature,
TopP, etc.) seeded from settings and overridden per model, passed via
`LlmChatOptions`; never write model defaults into `Settings.Llm`.

Acceptance:
- Selecting models never dirties `Settings.Llm.MaxTokens` (unit test on
  the VM with a profile-carrying model).
- Chat sends still honor the per-model max-token default.

## 1.4 Every save triggers a Services rebuild storm

`SettingsService.SaveAsync` fires `SettingsChanged` unconditionally
(`SettingsService.cs:133`). `ServicesViewModel` subscribes and runs
`Rebuild()` (`ServicesViewModel.cs:662`), which clears and re-adds all
server rows, re-runs orphan port detection (synchronous process/port
scanning started on the UI thread), and fires
`ServerAvailabilityChanged`, which `MainWindowViewModel` answers with
`Chat.LoadModelsAsync(force: true)`: model cache invalidation plus an
HTTP refetch. Net effect: clicking Save on *any* settings tab, adding a
Local API token, or saving a server config refetches models, rescans
ports, and rebuilds the Services panel. It also churns
`ServerProcessViewModel` instances: rows removed from config are never
disposed (their `ServerProcessManager` leaks), and the whole list loses
UI state (expanded logs) whenever ids change.

Fix:
- Fire `SettingsChanged` from `SaveAsync` only when relevant state
  changed, or (simpler and sufficient) make `Rebuild()` diff: reuse
  rows whose config id is unchanged without Clear/re-add, dispose rows
  whose config is gone, and only fire `ServerAvailabilityChanged` when
  the server set, ports, or paths actually changed.
- Move orphan detection off the UI thread (`Task.Run` around
  `_orphanDetector.Detect`).

Acceptance:
- Saving an unrelated setting (e.g. UI font size) does not invalidate
  the model cache, refetch models, or run orphan detection (assert via
  counters on fakes).
- Removing a managed server config disposes its VM (test with a
  disposable-tracking fake or an `IsDisposed` probe).
- Orphan detection no longer runs synchronously on the UI thread.

## 1.5 Trust scan and Local AI setup write unapplied edits into live settings

`TrustSettingsViewModel.SyncSettingsForTrustScan`
(`TrustSettingsViewModel.cs:73-83`) and
`LocalAiSetupSettingsViewModel.SaveLocalAiPathsForSetupAsync`
(`LocalAiSetupSettingsViewModel.cs:218-229`) copy current text-box
values (TTS paths, assets root, reranker path) into live settings; the
trust variant does not even save, so the mutation lingers until some
unrelated save persists it. Running a trust scan should never be a
write operation on settings.

Fix: both should build a scan-scoped copy of the settings (or pass the
candidate paths explicitly to `TrustService.ScanAsync` /
`LocalAiSetupService.ScanAsync`) instead of mutating
`_settings.Settings`. Where the setup flow genuinely wants to persist
paths before running an action, that is `_saveSettings()` (the full
apply/save), not a partial side-channel write.

Acceptance:
- After a trust rescan with edited-but-unsaved TTS path boxes, the live
  settings object is unchanged (unit test).
- Setup actions still see the paths currently in the edit boxes.

## 1.6 Dead secret-reference guards on TTS python path

`TtsSettingsViewModel.ReloadFrom` blanks `TtsPythonPath` when
`_secrets.IsReference(settings.Tts.PythonPath)`
(`TtsSettingsViewModel.cs:281`), but nothing anywhere stores
`Tts.PythonPath` as a secret reference, and `ApplyTtsTo` writes the box
back unconditionally. Today the branch is dead; if any future code ever
did store a reference there, the reload/apply asymmetry would wipe it
on the next save. Remove the `IsReference` special-case (paths are not
secrets), or, if it stays, mirror the `OpenAiApiKey` pattern: only
overwrite the stored value when the box is non-empty.

Acceptance: either the branch is gone, or a unit test proves a
reference-valued `Tts.PythonPath` survives a reload+save round trip.

## 1.7 Reload() coverage and IsSaved delay

`SettingsViewModel.Reload()` reloads eight child VMs but clears errors
on Trust/LocalAiSetup only during Save; Reset leaves their stale error
text. Also `SaveAsync` ends with `await Task.Delay(2000)` inside the
command, keeping the async command "executing" for 2 extra seconds.
Small fixes: clear all child `SettingsError`s in `Reload()`, and flip
`IsSaved` back via a fire-and-forget delayed reset (or a timer) so the
command completes immediately.

Acceptance: Reset clears Trust/LocalAiSetup error text; SaveAsync
returns without an artificial 2 s tail (existing save toasts still
show).
