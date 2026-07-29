# 03. Living knowledge: watched sources

## The problem

A RAG dataset is a photograph of a folder taken once. Edit a source file
and the dataset keeps answering from the old text, confidently, with a
citation pointing at a file that no longer says that.

The app already knows this is happening. `RagDatasetHealthService` compares
`File.GetLastWriteTimeUtc(path)` against the stored
`source_modified_utc` and counts stale files
(`RagDatasetHealthService.cs:71-72`), and the Dataset Manager displays that
count (`docs/features.md:517-521`). So today the app can tell you your
knowledge has rotted, and then does nothing about it. The data to fix it is
already on the chunk row: `source_path`, `source_hash`,
`source_modified_utc` (`RagChunk.cs:20-23`, columns at
`SqliteRagStore.cs:100-115`).

This item turns that detection into an action.

## Design decisions, settled

**No `FileSystemWatcher`.** There is not one in the codebase today and this
round is not the place to introduce one. Watchers differ meaningfully
between Windows and Linux, drop events under load, fire several times for
one logical save, need per-platform buffer tuning, and hold handles on user
directories for the life of the process. A deterministic scan that the user
can trigger, and that can optionally run on a schedule, gives the same
outcome with none of that. If a future round proves polling is too slow in
practice, revisit it then with evidence.

**Deletions are never automatic.** A watched refresh that would drop chunks
because a file vanished must ask first. This is not a new rule: "Remove
missing sources" already works exactly this way, and `docs/features.md:536-539`
states the contract plainly, that removal "is always an explicit,
user-confirmed action; ingest never removes sources automatically". A
watched source does not get to be the exception. A file that disappeared
because an external drive was unmounted must not silently delete a
dataset's contents.

**Refresh reuses the ingest pipeline.** No parallel path. A refresh is an
ingest with a computed file list and `IngestDuplicatePolicy.Replace`
(`RagIngestModels.cs`), so chunking, embedding, BM25 rebuild and query-cache
invalidation (`docs/features.md:544-546`) all happen exactly as they do for
a manual ingest. Every fix made to ingest keeps applying.

**Globs reuse `glob_files` semantics.** Same rule r23 set for workspace
policy: one glob engine in this codebase, not two.

## 3.1 The watched source model

A dataset gains zero or more watched roots. Store them in
`RagDatasetConfig` (`RagDataset.cs`), which is already persisted as
`config_json` on `rag_datasets` (`SqliteRagStore.cs:91-98`), so this needs
no schema migration at all:

```
WatchedSources : List<RagWatchedSource>
```

```
RagWatchedSource(
  Root            string,   // absolute folder path
  IncludeGlobs    List<string>,   // empty = the extensions ingest already accepts
  ExcludeGlobs    List<string>,   // always includes sensible defaults
  Recursive       bool,
  LastRefreshUtc  DateTime?)
```

Exclude defaults must ship non-empty and cover the folders that will
otherwise wreck a first refresh: `.git`, `node_modules`, `bin`, `obj`,
`.venv`, `__pycache__`, `dist`, `target`. A user who points a watched
source at a repo root and gets 40,000 chunks of build output will never use
the feature again.

`Root` is user-controlled path input: normalize, reject traversal, reject
symlinks, per `security-posture`.

## 3.2 The drift scan

`ScanAsync(dataset, ct)` walks each watched root under its globs, compares
what is on disk against the dataset's stored source rows, and returns a
plan without changing anything:

```
RagRefreshPlan(
  NewFiles      List<string>,
  ChangedFiles  List<string>,   // hash differs, or mtime is newer
  MissingFiles  List<string>,   // indexed, no longer on disk
  UnchangedCount int,
  Errors        List<string>)   // unreadable paths, named not swallowed
```

Change detection prefers `source_hash`; fall back to
`source_modified_utc` only when a stored hash is absent, and use the same
one-second tolerance the health service already applies
(`RagDatasetHealthService.cs:71`) so a filesystem with coarse timestamps
does not report permanent drift.

Cancellable. A scan over a large tree must be interruptible, and the r23
round found a reindex-cancel race in this area, so test cancellation
explicitly rather than assuming it.

## 3.3 Refresh

`Refresh now` on a dataset card runs the scan, shows the plan, and applies
it after confirmation:

- New and changed files are ingested with `Replace`.
- Missing files are listed and, only if the user confirms that part
  separately, their chunks are removed. Confirming an ingest is not
  confirming a deletion. Two decisions, two confirmations, matching the
  existing "Remove missing sources" flow.
- A refresh that would remove more than half a dataset's sources warns
  prominently before the confirmation, because that shape is almost always
  an unmounted drive or a wrong glob rather than an intended purge.
- Embedding-model mismatch blocks the refresh with the existing message
  naming both models (`docs/features.md:529-531`). Do not let a background
  refresh become a way to bypass that guard.
- One refresh per dataset at a time; a second request while one runs is
  refused with a clear reason, not queued silently.

## 3.4 Optional automatic refresh

Off by default. Two settings on the RAG section (preference knobs, so
Settings not Services, per the r22 split precedent):

- Refresh watched sources on app start (default off).
- Refresh watched sources every N hours (default off).

An automatic refresh **only ever ingests new and changed files.** It never
deletes, under any configuration. Missing files stay counted as drift and
wait for a human. It runs at low priority, well after startup, never on the
send path, and its outcome is recorded as an Activity event (doc 04) rather
than a toast the user will miss.

## 3.5 Surfacing

The Dataset Manager card gains: watched root count, last refresh time, and
a live drift summary ("3 changed, 1 new, 1 missing") with the Refresh
button next to it. When there is no drift, say so plainly rather than
showing an empty region; "up to date, checked 20 minutes ago" is the whole
point of the feature.

## Testing

Roughly 10 to 13 tests: glob include/exclude honoured including the
shipped defaults; recursive and non-recursive; traversal and symlink
refusal on the root; plan classification for new/changed/missing/unchanged
against seeded chunk rows; hash-preferred and mtime-fallback detection with
the one-second tolerance; scan cancellation mid-walk; refresh applies new
and changed without touching missing until separately confirmed; the
over-half-removal warning; embedding-mismatch block; concurrent refresh
refusal; automatic refresh never deletes.
