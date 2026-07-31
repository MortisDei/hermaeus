# 04. Models arrive complete

## The problem

A GGUF model is frequently not one file. It can be a set of shards, it can
have a multimodal projector, and (as doc 03 needs) it can have a
Multi-Token Prediction head. The app treats a model as exactly one file
everywhere it downloads or moves one.

**Download fetches only the file that was clicked.**
`DownloadHfFileAsync` (`ModelManagementViewModel.cs:798-820`) takes one
`HfFileResultViewModel`, resolves one URL, and writes one destination. There
is no notion of a companion anywhere in the path. So:

- a multimodal model arrives without its `mmproj` and quietly cannot see
- a model with an MTP head arrives without it, and doc 03's feature is
  unavailable for anything downloaded in-app
- a sharded model arrives as one shard and will not load at all

The last one is the sharp edge. `ModelFolderOrganizer` already understands
shard sets, with a regex capturing the base name and the total
(`ModelFolderOrganizer.cs:242`) so it can move them as a group. The codebase
knows what a shard set is everywhere except where it downloads one.

**Destination discards the repository entirely.**
`HuggingFaceBrowserSupport.PlanDestination` (`:19-24`) is:

```csharp
var fileName = Path.GetFileName(repoFilePath);
var destination = Path.Combine(modelsDirectory, LlmFolderName.Resolve(modelsDirectory), fileName);
return (destination, File.Exists(destination));
```

`ModelFolderOrganizer.Plan` flattens the same way, by design
(`:33-34`, r13 02-model-library.md 2.6, "so the folder is human-browsable").

Two things follow, and the owner has both on disk today:

- **Companion filenames collide across repositories.** The owner has
  **seven** files named `mmproj-F16.gguf`: both gemma-4-E4B snapshots,
  gemma-4-12B, Ministral 3B and 8B, Qwen3-VL-4B, and Qwen3.5-4B. A flat
  folder can hold one. Organize moves the first and skips the rest with "A
  file named mmproj-F16.gguf already exists at the destination" (`:70`), and
  a download of the second is refused outright (`:811`).
- **Projector discovery is a sibling-directory scan.**
  `ServicesViewModel.cs:231` enumerates `mmproj-*.gguf` in the model's own
  directory. In a flat folder every model is offered every other model's
  projector, and after the collisions above, most of them are offered one
  that is not theirs.

Per-model folders are not tidiness here. The sibling scan is load-bearing.

## 4.1 A model is a file set

The HF browser already fetches the repository tree as
`HfTreeEntry(string Path, long? SizeBytes, string? LfsSha256)`
(`HuggingFaceClient.cs:7`), so companion resolution needs no new request.

Selecting a GGUF resolves a **file set** from that tree:

| Class | Rule | Default |
| --- | --- | --- |
| Shard siblings | Same base name, `-NNNNN-of-NNNNN.gguf` pattern. Reuse the existing regex at `ModelFolderOrganizer.cs:242`; do not write a second one | **Required**, not optional |
| Projector | `mmproj-*.gguf` in the same repository directory | Offered, on by default |
| Draft head | `mtp-*.gguf`, including in an `MTP/` subdirectory | Offered, on by default |

The download panel shows the set, each file's size, and the total before
anything starts. Optional companions are checkboxes. Shards are not: a
partial shard set is a model that does not load, so it is part of the
download or the download is refused.

Every file in the set keeps the existing per-file guarantees, unchanged:
`LfsSha256` verification with deletion on mismatch (`:827-836`), and a
manifest entry (`:838-846`). Progress is reported across the set rather than
per file, so a three-shard download does not appear to finish three times.

A partial failure leaves what succeeded on disk and says which files are
missing. It does not roll back: a 4 GB shard that downloaded correctly
should not be deleted because a 60 MB companion failed.

## 4.2 Each model gets its own folder

`PlanDestination` stops discarding the repository path:

```
<models>/llm/<repo-derived-folder>/<filename>
```

The folder is derived from the repository id, sanitised. Requirements:

- **Path-traversal safe.** Repository ids contain `/` and are user-supplied
  through a text field. Sanitise to a single path segment; reject anything
  that escapes the destination root after `Path.GetFullPath`. This is a
  security-relevant path built from remote input and CLAUDE.md's rule
  applies directly.
- **Stable.** The same repository resolves to the same folder every time, so
  a later companion download lands beside its model rather than in a second
  folder.
- **Collision-handled.** Two different repositories that sanitise to the
  same name get distinct folders. Never silently merge them.
- Subdirectory structure inside a repository is flattened into the model
  folder, so `MTP/mtp-gemma-4-E4B-it.gguf` becomes
  `<model folder>/mtp-gemma-4-E4B-it.gguf` and the sibling scan at
  `ServicesViewModel.cs:231` finds it without changing that code. Verify
  that the projector scan and any MTP discovery doc 03 adds both work on
  this layout before calling the item done.

`LlmFolderName.Resolve` keeps doing its job for the `llm` versus `LLM`
casing problem (`LlmFolderName.cs`); this nests one level below it.

## 4.3 Organize stops flattening

`ModelFolderOrganizer.Plan` produces per-model folders on the same rule as
4.2, using repository provenance where the manifest has it (`ModelMoveItem`
already carries `HubRepoOrg` and repo, and `ProvenanceCount` counts them,
`:11-24`) and the file's own base name where it does not.

The properties that must survive, because they are what makes this operation
safe on real files:

- **Plan stays pure and previewable.** It is "heavily tested" by its own
  comment (`:34`) and `OrganizeModelsPreviewDialog` shows it before anything
  moves. Do not collapse plan and execute.
- **Execution still rewrites references.** `ExecuteAsync` already takes
  `ISettingsService` and `ModelManifestStore` (`:92`) so that a moved model
  does not orphan a `ServerConfig.ModelPath` or a tune profile. Every new
  destination must flow through the same rewriting. A model that moves and
  leaves a server pointing at nothing is a worse outcome than a messy
  folder.
- **The running-server guard stays.** `OrganizeModelsFolderAsync` refuses
  while any managed server is running because Windows locks files in use
  (`ModelManagementViewModel.cs:439-443`).
- **Companions move with their model**, into the same folder, as one group.
  This is the item that fixes the seven-way `mmproj-F16.gguf` collision: in
  per-model folders they no longer compete for one name.

## 4.4 An already-flat folder is migrated, carefully

An install that has already run Organize has a flat `llm/` folder where
filenames are the only identity. Re-running Organize must improve that
without guessing wrong.

- Files whose repository is known from `ModelManifestStore` move into their
  repository's folder.
- Files with no provenance move into a folder named from their own base
  name, which is the best available answer and is at least stable.
- A file that cannot be attributed confidently is **left where it is**, and
  the plan says so in the skip list, which already exists as
  `ModelMoveSkip(SourcePath, Reason)` (`:17`).
- `FindEmptyDirectories` and the existing empty-directory cleanup
  (`:168-190`, and the confirmation dialog wired at
  `ModelManagementViewModel.cs:474`) run after, unchanged.

Leaving a file alone is always allowed. Moving it to the wrong place is not.

## Tests

| Area | Test |
| --- | --- |
| 4.1 | A sharded model resolves all shards from the tree, and the set is not downloadable partially |
| 4.1 | A repository with `mmproj-*` and `mtp-*` offers both, on by default |
| 4.1 | A repository with neither offers only the model |
| 4.1 | An `MTP/` subdirectory entry is resolved as a draft-head companion |
| 4.1 | Hash verification failure deletes only the failed file, not the set |
| 4.1 | Progress totals across the set, and reports completion once |
| 4.2 | Destination is `<models>/llm/<repo folder>/<file>` and is stable across calls |
| 4.2 | A repository id containing `../` or an absolute path cannot escape the destination root |
| 4.2 | Two repositories sanitising to the same name get distinct folders |
| 4.2 | Same-named companions from different repositories coexist |
| 4.3 | Plan groups a model with its companions into one folder |
| 4.3 | Plan is unchanged by being called twice (idempotent) |
| 4.3 | Execute rewrites `ServerConfig.ModelPath` and tune profiles for moved files |
| 4.3 | Organize is refused while a managed server is running |
| 4.4 | A flat folder with manifest provenance migrates into repository folders |
| 4.4 | A file with no provenance and an ambiguous name is skipped with a reason, not moved |
| 4.4 | A model already in a per-model folder is not moved again |

`Plan` is a pure function and carries most of this. Filesystem execution
tests use a temp root, per the existing pattern in `ModelFolderOrganizer`'s
current tests, and must clean up (r25's bounded temp cleanup rule).

## What this doc explicitly does not do

- **No re-downloading or repairing of models already on disk.** The app does
  not go looking for missing companions for models it did not fetch. 4.4
  organises what is there.
- **No automatic download of companions for an existing model.** The user
  browses the repository and picks, as they do today.
- **No model deletion, deduplication, or "reclaim space" feature.** Moving
  files is already the riskiest thing in this round.
- **No change to the HF search, the repository browser, or the trust and
  privacy behaviour around Hugging Face.** `PrivacyAuditHuggingFaceTests`
  exists and must keep passing untouched.
- **No new download protocol, resumable downloads, or parallel file
  fetching within a set.** Files download one at a time, as they do now.
  Parallelism here competes with the user's own bandwidth for no clear win.
- **No renaming of model files.** Folders change; filenames do not. A
  filename is how the user recognises a quantisation.
