# 03 - Hugging Face integration (provenance, update checks, get-models browser)

Owner asks: "see if any of the models I have downloaded from Hugging
Face have an update, and the ability to update" and "if HF has an API
and it is safe and secure, we should probably integrate".

Precedent already in-repo: the r8 starter-model catalog downloads from
`huggingface.co/.../resolve/main/...` with pinned SHA256 values that
were themselves cross-checked against HF's LFS tree API and the
resolve endpoint's `X-Linked-ETag` header
(src/Aether.Services/StarterModelCatalog.cs:22-25), and
`ModelDownloadService` already does resumable downloads + SHA256
verification. This doc builds on both.

Security posture (applies to every item): HTTPS only, `huggingface.co`
host only, anonymous access only (public repos; gated repos and HF
tokens are explicitly out of scope, see roadmap rejections), all
network calls manual-action-triggered (a button), never on startup or
on a timer, failures non-fatal with a status message. The implementer
MUST verify the current API response shapes live before coding
fixtures (the endpoints below are correct as of the r8 work; shapes
drift).

API surface to use (verify live):
- `GET https://huggingface.co/api/models/{org}/{repo}` - model card:
  `sha` (commit), `lastModified`, `cardData.license`, `downloads`.
- `GET https://huggingface.co/api/models/{org}/{repo}/tree/main?recursive=true`
  - file list; GGUF entries carry `size` and `lfs.oid` (the SHA256 of
  the file content). This oid is the update-detection primitive.
- `GET https://huggingface.co/api/models?search=...&filter=gguf&sort=downloads&limit=25`
  - repo search for the browser (3.4).
- `https://huggingface.co/{org}/{repo}/resolve/main/{file}` - download
  URL (what the starter catalog already uses).

## 3.1 Model provenance manifest

New store: `{DataRoot}/model-manifest.json` (atomic-write via the
existing storage conventions; see the storage-and-data-root skill),
list of entries:

```
{ "file_path": "C:\\AI\\Models\\LLM\\x.gguf",
  "repo_id": "unsloth/gemma-...-GGUF",
  "repo_file": "x.gguf",
  "revision_sha": "<commit sha at download/check time>",
  "sha256": "<lfs oid / verified local hash>",
  "size_bytes": 123,
  "recorded_at_utc": "...",
  "source": "starter|hf-browser|migration|manual" }
```

Writers:
- Starter-model wizard downloads and the embedding-model download
  (they already know repo + hash).
- The doc 02 migration when it captured a `models--org--repo` segment
  (source "migration"; `revision_sha` empty until the first update
  check fills it).
- The 3.4 browser on every completed download.
- Manual "Link to Hugging Face repo..." action on a model card (owner
  types/pastes `org/repo`; validate it resolves via the model-card
  endpoint before saving). This covers models copied in by hand.

Registered through DI like other stores; include it in
`DataRootManifest.EnumerateAll` so backup and data-root migration
carry it (r11 3.1 infrastructure).

Acceptance criteria:
- Round-trip tests (write, reload, entries keyed by file path,
  case-insensitive on Windows).
- Deleting a model file then loading prunes (or ignores) its entry
  without error.
- Backup includes the manifest (extend the existing manifest test).

## 3.2 Manual update check

"Check for updates" button at the top of the Models page + per-card
status. Only models with a manifest entry carrying `repo_id`
participate; others show nothing.

Flow per distinct repo (batch by repo, one tree call each, sequential,
short timeout ~10s):
- Fetch the tree, find the entry whose path matches `repo_file`.
- File gone from the repo -> chip "No longer published" (no action).
- `lfs.oid` == stored `sha256` -> up to date (record checked-at).
- Differs -> chip "Update available" with new size; stash the new oid
  + current `revision_sha` on the entry as pending-update fields.
- For migration-sourced entries with no stored hash yet: hash the
  local file once (background, with progress status, it is
  gigabytes), store it, then compare. Hashing is the expensive path;
  never re-hash when a stored hash exists and size+mtime are
  unchanged.

Privacy audit: `PrivacyAuditService.CountOutboundDestinationsAsync`
and the audit items list must disclose "Hugging Face update checks /
downloads (huggingface.co), manual only" whenever the manifest has at
least one repo-linked entry. The System page currently proudly says
"0 configured outbound destinations"; that statement must stay honest.

Acceptance criteria:
- Tests with canned tree JSON fixtures: match -> up to date, oid
  drift -> update available, file missing -> no-longer-published,
  malformed JSON -> non-fatal error status.
- No network calls at startup or without the button press (guard test
  via a counting fake HttpClient handler on the new service).
- Privacy audit item appears/disappears with manifest content.

## 3.3 Apply an update

Per-card "Update" button when 3.2 flagged one:

- Refuse while the model is running (same `IsRunning` signal as
  auto-tune) or while any managed server's ModelPath points at it and
  that server is running.
- Download to `<file>.update.tmp` beside the target via
  `ModelDownloadService.DownloadAsync` (resume works), then
  `VerifyHashAsync` against the tree's `lfs.oid`. Verification
  failure -> delete the tmp, report, change nothing.
- On success: atomic swap - move the old file to `<file>.previous`,
  move the tmp into place, then delete `<file>.previous` only after
  the swap succeeded (if any step fails, restore the original and
  report). No user data is destroyed on any failure path.
- Update the manifest entry (new sha256, revision_sha, size,
  recorded_at) and refresh the card. Tune profiles for this path are
  now stale by size/mtime, which 2.4's staleness predicate already
  detects - surface "re-tune recommended" on the card after an
  update.

Acceptance criteria:
- Tests with a fake download handler: happy path swaps and updates
  the manifest; hash mismatch leaves the original untouched; running
  model refuses.
- The swap sequence is crash-safe in the sense above (simulate a
  failure between the two moves in a test via an injectable
  file-system seam or by pre-creating the .previous file).

## 3.4 "Get models" browser

In-app HF browsing so a new user never has to learn the hub cache
layout. New section on the Models page (collapsed expander at the
top, "Get models from Hugging Face") - not a separate nav panel:

- Search box -> repo search endpoint (`filter=gguf`, sort by
  downloads); list repo id, downloads count, license (from the
  model-card endpoint, fetched lazily on selection, not per row).
- Selecting a repo lists its `.gguf` files from the tree endpoint:
  file name, size, and the doc 02 fits chip per file
  (`ModelFitEstimator` on `size` + `HardwareProfile`).
- Download: destination `<ModelsDirectory>\LLM\<file>` (the 2.6
  convention; create the dir), progress bar + cancel via the existing
  `DownloadProgress` plumbing, SHA256-verified against `lfs.oid`
  before the file is moved out of its .tmp name, manifest entry
  written (source "hf-browser"). Name collision with an existing
  file -> refuse with a message (no silent overwrite).
- Multi-part GGUFs: either download all parts of the selected set (in
  sequence, one progress) or, if that is too much for this round,
  hide multi-part sets with a "not yet supported" note - choose one,
  do not half-support them.
- License display is informational; no gating logic.

Acceptance criteria:
- Fixture-driven tests for search/tree parsing and the
  destination-path + collision logic; download/verify path reuses
  ModelDownloadService tests plus one integration-style test with a
  local fake handler serving bytes with a known hash.
- A fresh model downloaded here appears in the model list after
  refresh, with fits chip and update-check support (manifest entry
  present).
- Live verification: actually search, download the smallest real
  GGUF you can find (a tiny test model like stories260K exists on HF)
  on the owner's machine, confirm chat can start it.
