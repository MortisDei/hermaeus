# 02. Services, models, and the update flow

## 2.1 Model-card defaults must flow into the Services server card

Owner: "settings on a model in Models propagate nowhere; Services should default to the
metadata set on the model card."

Verified: `ModelProfile` (`src/Aether.Core/Models/ModelProfile.cs`) carries
`DefaultContextSize` (plus sampling defaults), the Models page edits it
(`ModelManagementViewModel.cs:915,984,1021`), and `ChatViewModel` consumes the sampling
side on model switch (`ChatViewModel.cs:1395`). `ServicesViewModel` never references
`ModelProfileService` at all (zero matches), so a card's `DefaultContextSize` does
nothing to the server that actually loads the model.

Fix, scoped to what a server config can honestly use:
- Inject `IModelProfileService` into `ServicesViewModel`. In
  `ServerProcessViewModel.OnModelPathChanged` (`ServicesViewModel.cs:799`), after the
  existing tune-profile application (`ApplyTuneProfile`, :669-683), look up the profile
  for the newly selected model (model-card `ModelId` for local GGUFs is the full file
  path, see `ModelManagementViewModel.cs:972`; match by normalized full path, fall back
  to filename match).
- Precedence, most specific wins: an existing `LlamaTuneProfile` for that model path
  (user ran Auto Tune) > model card `DefaultContextSize` > leave the current value.
  Apply only when the model path actually changed to a different file, never on
  rebind/reload of the same path, and never overwrite a value the user just typed
  (apply inside the same guard that `ApplyTuneProfile` uses).
- Show provenance in the card: reuse the existing status/hint text to say "Context from
  model card" / "Context from Auto Tune" when a default was applied.

Acceptance: unit test that selecting a model path with a card `DefaultContextSize` and
no tune profile sets `ContextSize` to the card value; with a tune profile present, the
tune profile wins; reselecting the same path changes nothing.

## 2.2 Updating llama.cpp must stop running servers, then bring them back

Owner: update should stop running services so it can actually update, then reload what
was stopped.

Current behaviour verified: `DoctorService.InstallLlamaServerUpdateDetailedAsync`
(`src/Aether.Services/DoctorService.Runtime.cs:348-401`) installs into a fresh
tag-versioned directory (so the copy itself no longer fails), repoints every
`ManagedServers[].ExecutablePath` at the new binary, saves settings, and logs "running
servers keep the old build until restarted". The running processes keep serving the old
build indefinitely, and the superseded version directory cannot be pruned while they
hold it (`LlamaServerSetupService.PruneVersionDirectories` skips locked dirs,
:400-402). The owner's log shows exactly this dance.

Fix, in the caller `DoctorViewModel` (:304) so the service stays policy-free:
- Before calling the update: snapshot which managed servers are currently Running whose
  `ExecutablePath` resolves to a llama-server binary; stop them via the existing
  `IServerProcessManager` stop path, reporting "Stopping Chat (llama-server) for
  update..." through the same `IProgress<string>`.
- Run the update exactly as today.
- After a successful update: restart precisely the snapshot set (now pointing at the
  new binary via the settings rewrite that already happens), report each restart, then
  offer the prune as today (which will now actually reclaim the old directory).
- On update failure: restart the snapshot set against their unchanged executable paths,
  so a failed update never leaves the user with dead servers.
- Update the log wording at `DoctorService.Runtime.cs:397`; "running servers keep the
  old build until restarted" is no longer the whole truth when the caller restarts them.

Acceptance: unit test at the ViewModel level with fakes: two servers (one Running
llama-server, one Stopped), update succeeds -> stop called once, restart called once for
the previously-running server only; update fails -> the same server is restarted and
executable paths are untouched.

## 2.3 Stop re-downloading the CUDA runtime on every update

Owner: "it downloads the whole cuda 12.4 runtime each time; a hash check should show if
another download is needed."

Verified: `LlamaServerSetupService.InstallLatestAsync` (:217-253) extracts each update
into a new `bNNNNN` directory and, for CUDA variants, unconditionally downloads the
cudart companion archive (:233-240, several hundred MB) into that fresh directory,
every single time, even though the companion asset (e.g.
`cudart-llama-bin-win-cuda-12.4-x64.zip`) changes only when llama.cpp bumps its CUDA
toolkit version.

Fix (no remote hash is available: GitHub release assets expose no usable per-asset
SHA256 through the fields we already parse, so key on identity, not content):
- Before downloading the companion, look for the same set of files in the previous
  install's version directory. Concretely: after each successful companion extraction,
  write a small marker file `cudart.json` into the version directory recording the
  companion asset name and the list of extracted file names with sizes.
- On the next update, if a sibling version directory has a `cudart.json` whose asset
  name matches the companion the new release wants, copy those files (verified present
  with matching sizes) into the new version directory instead of downloading. Report
  "Reusing CUDA runtime from bNNNNN" through progress. Any mismatch or missing file
  falls back to the download.
- The prune flow must keep working: copied runtimes make old dirs fully deletable.

Acceptance: unit test with a fake previous version dir containing marker + files:
matching asset name -> files copied, downloader never invoked for the companion;
mismatched asset name or missing file -> download path taken. (The download itself is
already behind `ModelDownloadService`, which tests fake.)

## 2.4 Downloaded model folder: `llm`, not `LLM`

Owner request, verified: `HuggingFaceBrowserSupport.cs:22` and
`ModelFolderOrganizer.cs:49` both build `Path.Combine(modelsDirectory, "LLM")`.

Fix: switch the literal to `"llm"` in both places (plus the assertions in
`HuggingFaceBrowserSupportTests.cs`, `ModelFolderOrganizerTests.cs`,
`ModelManagementViewModelTests.cs`). Windows is case-insensitive so existing `LLM`
folders keep working; for correctness on Linux, both call sites should first probe for
an existing directory named either `llm` or `LLM` (case-sensitive enumeration of the
parent) and reuse whichever exists before creating `llm`. Extract that probe into one
small shared helper rather than duplicating it.

Acceptance: existing tests updated; new test that a pre-existing `LLM` directory is
reused (no second `llm` directory created beside it).

## 2.5 Remove the manual model-path text box in Services

Owner: the free-text path box under the model dropdown is redundant with the Browse
button. Verified at `src/Aether.Desktop/Views/ServicesView.axaml:254-258` (a `TextBox`
bound to `ModelPath` directly under the `DetectedModelPaths` ComboBox, with
`BrowseModelCommand` right beside it).

Fix: delete the TextBox. Keep the ComboBox + Browse button. One consideration: today
the TextBox is the only place a path OUTSIDE the detected list is visible after a
Browse pick, because the ComboBox's `SelectedItem` binding cannot select a value not in
`ItemsSource`. Verify this (it is the same class of bug r11 fixed for the wizard
dropdown): if a browsed path renders blank in the ComboBox, add the browsed path into
`DetectedModelPaths` (top of list) when it is not already present, so the selection
displays. That behaviour change is part of this item's acceptance, not optional.

Acceptance: manual: browse to a .gguf outside the assets root; the card shows the full
path in the dropdown and Start works. No free-text path entry remains on the card.
