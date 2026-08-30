# 01. A first run that ends with a usable model

## Evidence and current shape

The Pop!_OS report is the release-blocking item. Doctor recommended the right
VRAM tier, the first download looked failed, retry reported that the file
already existed, and no model appeared in Services. Data Root and AI Root also
looked accepted during setup and differed in Settings after completion.

Current code narrows the investigation:

- `SetupWizardViewModel.DownloadStarterModelAsync` writes to
  `<LocalAiAssetsRoot>/Models/chat`, verifies SHA256, then assigns the full file
  path to `ModelFolder` (`SetupWizardViewModel.cs:291-325`). It does not add a
  provenance-manifest entry and does not independently prove discovery.
- an existing destination is treated as a download failure before the wizard
  asks whether the existing bytes already match the pinned hash. That explains
  the reported retry dead end and is a required fix whether or not it caused
  the first failure.
- leaving the model step writes `ModelFolder` into only the first managed
  server (`SetupWizardViewModel.cs:471-480`). Post-setup initialization loads
  chat models but does not explicitly assert that the Services VM rebuilt its
  detected paths around that saved file.
- the wizard already exposes every `StarterModelCatalog.All` entry in a
  ComboBox (`SetupWizardView.axaml:71-115`). L-P1 is therefore a recovery and
  legibility defect, not a missing alternate-model catalogue.
- the r12 migration-aware root save is present
  (`SetupWizardViewModel.cs:490-533`). The Linux report must be reproduced past
  that method, including path comparison, symlink validation, save ordering,
  and the Settings VM reload. Do not replace it with another save path.

## 1.1 Reproduce the whole golden path before changing it

Build a Linux regression harness around a genuinely clean `AppSettings`, a
temporary case-sensitive directory tree, the real `SettingsService`, the real
wizard VM, and a deterministic download handler. Drive the same sequence as
the user:

1. choose distinct Data Root and AI Root paths;
2. leave the roots step;
3. choose a starter model and complete a successful download;
4. leave the model step and finish the wizard;
5. construct or refresh Services from the persisted settings;
6. discover the model through `LocalAiAssetLocator.FindGgufModels`;
7. reload settings from disk and compare both roots and the server model path.

The test must preserve path case. Any path equality or deduplication used by
this flow is `OrdinalIgnoreCase` on Windows and `Ordinal` elsewhere. Include
two sibling directories whose names differ only by case so a Linux failure
cannot be hidden by a Windows comparer.

Capture which assertion fails before editing. The fix belongs at that boundary.
Do not encode the brief's candidate causes as if they had already been proven.

Acceptance criteria:

- the pre-fix test reproduces at least one observed broken invariant, or the PR
  records why the repository path passes and identifies the external cause with
  equivalent evidence;
- the test uses no owner's settings or data and runs on both CI platforms;
- the Linux leg exercises case-sensitive names rather than skipping them.

## 1.2 A successful or already-complete download is adopted atomically

Extract one small starter-download completion operation shared by the first
attempt and retry path:

- resolve the destination under the selected AI Root using the same
  platform-aware model-directory policy as `LocalAiAssetLocator`;
- reject traversal and symlinks before any write;
- if the destination exists, hash it first. A matching pinned SHA256 is a
  completed download and continues through adoption. A mismatch is reported
  as a conflicting file and is never overwritten or silently deleted;
- for a new download, use `ModelDownloadService`, verify the pinned hash, and
  remove only a failed temporary/download file;
- upsert a `ModelManifestEntry` with source `starter`, pinned hash, final path,
  repo/file identity from `StarterModelEntry`, and size;
- set `ModelFolder` to the final full path and expose a completed state only
  after the file, hash, and manifest steps succeed.

The manifest write and settings save are separate durable operations, so make
retry idempotent. A crash after the file lands but before either later step
must recover through the matching-hash existing-file path.

Acceptance criteria:

- retrying against a valid complete file says it is ready and does not issue a
  second HTTP request;
- retrying against a conflicting file refuses without modifying it;
- hash mismatch removes only the new failed download and leaves no completed
  state or manifest entry;
- a successful first attempt and an adopted retry produce the same final
  `ModelFolder` and manifest entry;
- progress remains monotonic and reaches 100 only when adoption succeeds.

## 1.3 Completing setup makes the model usable immediately

The model step is not complete merely because a file exists. When the user
advances:

- save the chosen path to the chat managed-server config through
  `SettingsService`;
- force a Services model rescan/rebind after the save, on the UI dispatcher;
- invalidate model caches and refresh the Models and Chat lists once;
- prove the selected path exists in `DetectedModelPaths` and remains selected
  after the collection reset behavior that r19 already guards;
- if adoption cannot reach that state, keep the wizard on the model step and
  show a factual error containing the final path and a retry/change-model
  action. Never mark setup complete behind an unusable model.

Do not start a server merely to prove discovery. Discovery is local disk and
settings behavior.

Acceptance criteria:

- after Finish, Services shows the downloaded model without navigation or an
  app restart;
- the saved chat server points at the same canonical file path;
- a missing file between download and Finish is surfaced and blocks Finish;
- a Services `CollectionChanged Reset` cannot null the adopted selection;
- the flow works with `Models`, `models`, and case-distinct sibling directories
  on the appropriate platform.

## 1.4 Failure has a recovery path inside the wizard

Keep the existing starter-model ComboBox visible after any failure. Add clear
actions to Retry the same entry or select any other catalog entry and Download.
Changing entries resets only the failed attempt state, never roots or runtime
choices. The error text distinguishes network/download failure, conflicting
existing file, hash failure, and adoption/discovery failure.

Acceptance criteria:

- every failure state leaves the catalog picker and a valid next action visible;
- selecting another tier after failure downloads that exact catalog entry;
- no error path sets `StarterModelDownloadCompleted` or allows an unusable
  selected path to pass the model step;
- cancel and retry do not create duplicate manifest rows.

## 1.5 Roots survive Finish and reload on Linux

Keep `ApplyDataRootStepAsync` as the only wizard root save. Fix the reproduced
boundary from 1.1, then make completion verify persisted truth rather than
assuming it:

- Data Root and AI Root are copied into the candidate settings object together;
- the migration-aware save completes before later wizard steps read AI Root;
- a failed validation or migration stays on the roots step and preserves the
  user's typed values for correction;
- after Finish, `SettingsViewModel.Reload` observes the exact persisted roots;
- symlinks remain rejected according to current security rules and produce an
  error instead of silently reverting.

Acceptance criteria:

- save, process-level reload from JSON, and Settings VM reload all return the
  same canonical roots selected in the wizard;
- a path differing only by case is treated according to the host filesystem;
- the test covers both an empty old data root and a migration with real files;
- no direct `settings.json` write or second serializer is introduced.

## Tests and documentation

Budget 14 to 18 tests: golden path, existing-valid adoption, conflict, hash
failure, cancellation, manifest idempotence, forced rescan, missing-before-
Finish, alternate selection, both root reload cases, case-sensitive paths, and
symlink rejection.

Update `docs/features.md`, the onboarding/setup portions of the relevant docs,
and `CHANGELOG.md`. The PR description records the Pop!_OS reproduction command
or VM/container image and the exact pre-fix failed invariant.
