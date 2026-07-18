# 02 - Model library (Models page rework, auto-tune, fits-check, flat folders)

Owner screenshots 2 and 3: the Models page lists 32 models, each as a
full always-expanded editor (3 text boxes + 8 numeric spinners + save
row), the bottom of the list cannot be reached regardless of window
resizing, and there is no way to auto-tune from here even though the
Services page has a working Auto Tune.

Files: `src/Aether.ViewModels/ModelManagementViewModel.cs`,
`src/Aether.Desktop/Views/ModelManagementView.axaml`,
`src/Aether.ViewModels/ServicesViewModel.cs` (auto-tune + tune-profile
persistence to lift), `src/Aether.Services/LocalAiAssetLocator.cs`
(folder migration), `src/Aether.Core/Models/LlamaTuneProfile.cs`.

## 2.1 Compact, expandable model cards

Replace the always-expanded editor grid
(ModelManagementView.axaml:40-195) with a collapsed-by-default card
per model:

- Collapsed row: effective name, Running badge (existing), provider,
  size, modified date, tags, the fits chip (2.5), the update chip
  (doc 03), and an expander toggle. One line, cheap to render.
- Expanded: the existing full editor grid, unchanged fields, plus the
  new per-model Auto tune button (2.3).
- Expansion state is per-item VM state (`IsExpanded` on
  `ModelProfileItemViewModel`), not persisted.
- Add a search/filter box at the top (name/tag substring, case
  insensitive) so 32+ models are navigable; filtering operates on the
  already-loaded list, no refetch.

Acceptance criteria:
- 32 models render as 32 one-line rows; expanding one shows the same
  editor and Save/Reset behavior as today (existing profile round-trip
  tests unchanged).
- Filter box narrows the list live; clearing restores all.

## 2.2 Fix the scroll (owner cannot reach the bottom of the list)

Reproduce live first (the `run` skill; the owner has 32 models). Two
code-level suspects, in likelihood order:

1. Wheel capture: each card contains 8 `NumericUpDown`s and the
   pointer is nearly always over one; if they (or the inner TextBox)
   handle `PointerWheelChanged`, the outer `ScrollViewer`
   (ModelManagementView.axaml:33) never sees the wheel and the page
   only scrolls from the thin scrollbar. 2.1's collapsed cards remove
   most spinners from the hit path, but the expanded state must still
   scroll: if Avalonia's NumericUpDown eats wheel events, attach a
   handler that re-raises the wheel to the ancestor ScrollViewer when
   the control is not keyboard-focused (spin only when focused,
   scroll otherwise).
2. Bottom clipping: verify the last card's bottom is fully visible at
   maximum scroll extent (DockPanel + ScrollViewer arrangement looks
   correct, but confirm live; if clipped, fix the layout, not with
   padding fudge).

Acceptance criteria:
- Live verification on the owner-size list: wheel scrolling works with
  the cursor anywhere over the list, including over an expanded card's
  spinners (unfocused), and the last model's Save/Reset row is fully
  visible at the bottom of the scroll range.
- Keyboard: Ctrl+End / scrollbar drag reach the same bottom.
- Note what the actual root cause was in the implementation notes.

## 2.3 Per-model Auto tune

`ServerProcessManager.AutoTuneAsync` (ProcessManagement) is already
config-driven and static; `ServicesViewModel.AutoTuneAsync`
(ServicesViewModel.cs:306-345) builds a `ServerConfig`, runs it, and
persists a `LlamaTuneProfile` keyed by model path
(PersistTuneProfileAsync, ServicesViewModel.cs:411-437, capped at
200). Lift, do not duplicate:

- Extract the tune-profile upsert (find-or-create by resolved model
  path, prune) into a shared helper (e.g. static
  `LlamaTuneProfileStore.Upsert(AppSettings, string modelPath,
  ServerTuneResult?)` in Aether.Services, or a small injectable
  service); ServicesViewModel and the new Models-page path both call
  it. One architecture-level guard test that ServicesViewModel no
  longer contains its own upsert logic is nice-to-have, not required.
- Models page: an "Auto tune" button on each local-GGUF card (hide for
  remote/provider-reported models; `Provider == "local GGUF"` rows
  from `DiscoverLocalGgufModels`,
  ModelManagementViewModel.cs:35-49). Synthesize the probe config:
  executable = the first enabled managed llama-server entry's resolved
  ExecutablePath (via the r11 `ExecutableResolver`); model path = the
  card's file; port = an ephemeral free port (AutoTuneAsync already
  port-preflights, r11 1.5); context = existing tune profile's
  ContextSize, else the profile's DefaultContextSize, else the
  managed entry's default.
- Refuse politely when: no managed llama-server executable is
  configured (status text tells the user to set one up on Services),
  or the model is currently running (`IsRunning`), or another tune is
  already in progress.
- Result: persist via the shared helper + `_settings.SaveAsync()`,
  and surface "N/M GPU layers, T threads" on the card (from the
  stored `LlamaTuneProfile`; also show this for models tuned earlier
  on the Services page - same store).

Acceptance criteria:
- Tuning a non-running local GGUF from the Models page persists a
  `LlamaTuneProfile` whose values the Services page then shows for a
  server pointed at that model (shared store, shared upsert).
- Running/remote models show no tune button or a disabled one with a
  reason tooltip.
- Tests: shared upsert (new profile, existing profile update, prune at
  cap) as pure settings-object tests; the Models-page command's
  refusal paths with a fake/absent managed entry. No live llama-server
  in tests.

## 2.4 Auto-tune all

Top-of-page "Auto-tune all" next to Refresh:

- Sequential (one llama-server probe at a time, they fight for VRAM),
  cancellable, progress "Tuning 3/17: <name>".
- Scope: local GGUF, not running, and stale-or-missing tune profile
  (missing, or `ModelSizeBytes`/`ModelModifiedAtUtc` mismatch with
  the file, or tuned with a different `LlamaServerVersion` when the
  current one is known). Already-fresh profiles are skipped and
  reported as skipped.
- One summary toast at the end: tuned N, skipped M, failed K (first
  failure message included); per-model failures do not abort the run.

Acceptance criteria:
- Cancel stops after the in-flight probe, no orphan processes (the
  probe path already job-objects children; verify).
- Staleness predicate is a pure function with tests (fresh, size
  drift, mtime drift, version drift, missing).

## 2.5 Fits-on-your-hardware chip

What Unsloth shows at download time, Aether should show for what is
already on disk. Pure estimator in Aether.Services:

`ModelFitEstimator.Estimate(long fileSizeBytes, HardwareProfile hw)`
-> enum + short reason string:

- `FitsGpu`: fileSize * 1.2 + 1.5 GB <= MaxGpuVramBytes (weights +
  KV/compute headroom fit in VRAM).
- `FitsPartial`: not FitsGpu but fileSize * 1.2 + 2 GB <=
  TotalRamBytes (runs with partial/zero offload, slower).
- `TooLarge`: neither.
- `Unknown`: hw has no data (both zero) - render nothing rather than
  guessing.

Constants live in one place with a comment stating they are
deliberate rough headroom, not a science; reasons are user-facing
("~9.0 GB model vs 8.0 GB VRAM: needs partial CPU offload").

Surface: chip on each local-GGUF card (2.1), in the HF file list
(doc 03 item 3.4), and next to the wizard's starter model tiers
(reuse, do not duplicate, the existing `StarterModelCatalog.Recommend`
stays as-is for the default selection).

Acceptance criteria:
- Pure tests across the tier boundaries and the Unknown case.
- With doc 01 shipped, the owner's machine shows real chips (manual
  verification note in the implementation summary).

## 2.6 Flat model folder: "Organize models folder"

Owner: wants `C:\AI\Models\LLM\<file>.gguf` instead of the HF hub
cache maze (`hub\models--unsloth--gemma...\snapshots\<sha>\*.gguf`).
`FindGgufModels` already searches recursively
(LocalAiAssetLocator.cs:25-42), so nesting is a human problem, not a
detection problem - which is exactly why it is worth fixing: humans
browse this folder.

New action on the Models page ("Organize folder..."), implemented as
plan -> preview -> confirm -> execute:

- Plan (pure, heavily tested): input = the detected models directory
  tree (list of gguf paths + their sizes); output = a list of moves.
  Rules:
  - Destination directory: `<ModelsDirectory>\LLM\` (create if
    missing). Files already directly under `LLM\` or directly under
    the models root are left alone (moving root-level files into LLM
    is included in the plan as optional, default on).
  - `embed`/`embedding`/`embeddings`/`rerank`/`reranker` subtrees are
    never touched (they are the existing special dirs,
    LocalAiAssetLocator.cs:372-382).
  - Keep original filenames. No renaming: the filename is the
    identity used for HF update matching (doc 03) and encodes the
    quant. Name collisions -> skip that file, report it, never
    overwrite.
  - Multi-part sets (`*-00001-of-00003.gguf`) move together or not at
    all.
  - When a source path contains a `models--{org}--{repo}` segment
    (the HF hub cache convention), record org/repo with the move so
    execution can write provenance into the model manifest (doc 03
    item 3.1). This turns the owner's existing Unsloth-downloaded
    models into update-checkable ones for free.
- Preview dialog: list of "from -> to" moves, collision skips, and
  the count of provenance records to be captured. Confirm-gated.
- Execute:
  - Refuse to start while any managed server is running (Windows file
    locks; tell the user to stop servers first).
  - `File.Move` per file (same volume; if the destination is a
    different volume, copy+verify-length+delete).
  - Rewrite every stored reference to a moved path: managed
    `ServerConfig.ModelPath` entries, `LlamaTuneProfile.ModelPath`
    (LlamaTuneProfile.cs:5), model profiles keyed by path
    (`ModelProfile.ModelId` is the path for local GGUFs), and the
    embeddings model path in Rag settings if it happens to be inside
    a moved set (it should not be, given the special-dir exclusion,
    but check). One settings save at the end.
  - Leftover empty directories under the vacated hub tree: offer to
    remove empty dirs only (a second, separate confirmation; never
    delete non-empty dirs, never delete files).
- Post: refresh the model list; profiles must follow their files
  (display names, temps etc. survive the move).

Acceptance criteria:
- Planner tests: nested hub-cache file -> LLM move with provenance
  captured; embed subtree untouched; collision -> skip; multi-part
  atomicity; already-flat file -> no move.
- Reference-rewrite tests on a settings object: ServerConfig,
  LlamaTuneProfiles, ModelProfile keys all follow.
- Live verification on a scratch folder mimicking the owner's layout.
- A model that was renamed by the move (there are none - assert the
  plan never renames) keeps working: Save Config on Services still
  points at an existing file.
