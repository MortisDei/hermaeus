# 01 - RAG correctness

Bugs where the RAG subsystem returns wrong results, loses data, or
crashes. Each item stands alone; implement in the order listed.

## 1.1 Parent-child retrieval is broken end to end

Evidence:

- `RagPipeline.IngestDirectoryAsync` gives each *child* chunk a
  `ParentId` and stores the parent with `ParentId = null`
  (`src/Aether.Rag/Pipeline/RagPipeline.cs:160-168`, web path
  `:269-274`). Only children are embedded; parents are stored
  unembedded by design.
- `SqliteRagStore.GetChunksAsync` filters `parent_id IS NULL`
  (`src/Aether.Rag/Storage/SqliteRagStore.cs:312-313`). That excludes
  every child and returns only parents.
- The query cache is warmed from `GetChunksAsync`
  (`src/Aether.Rag/RagQueryService.cs:63-69`), so with
  `UseParentChild` enabled at ingest: `HybridRetriever.CosineScan`
  sees zero embedded candidates (it skips `Embedding.Length == 0`),
  semantic retrieval returns nothing, BM25 scores only parents, and
  `UpgradeToParentsAsync` (`RagQueryService.cs:478-495`) upgrades
  nothing because parents have no `ParentId`.

The filter's intent was clearly "exclude parent bodies from the
candidate list"; it does the opposite.

Fix: retrieval candidates must be the embedded child chunks. The
cleanest contract is an explicit flag on the row rather than inference
from `parent_id`: add an `is_parent INTEGER NOT NULL DEFAULT 0` column
via `SqliteMigrationRunner` (additive, schema stays v1-compatible per
the storage rules), set it when storing parents, and change
`GetChunksAsync` to `WHERE ... AND is_parent = 0`. Backfill existing
rows in the migration: a row is a parent if any other row's
`parent_id` references its `id` (one `UPDATE ... WHERE id IN (SELECT
parent_id FROM rag_chunks WHERE parent_id IS NOT NULL)`).

Acceptance criteria:

- A regression test ingests a small corpus with `UseParentChild = true`
  (fake embedding service), queries it, and asserts: semantic
  candidates are non-empty, selected chunks resolve to parent content
  via `UpgradeToParentsAsync`, and no parent body appears in the BM25
  candidate list.
- A migration test over a pre-fix database file (build one in the test
  with the old shape) confirms existing parents are backfilled.
- Non-parent-child datasets behave exactly as before (existing tests
  stay green).

## 1.2 Dataset delete leaks every chunk row

`DeleteDatasetAsync` deletes only the `rag_datasets` row and relies on
`ON DELETE CASCADE` (`SqliteRagStore.cs:233-241`, schema `:98`), but no
connection ever runs `PRAGMA foreign_keys = ON` and
`SqliteConnectionStringBuilder` is built with only `DataSource` and
`Pooling` (`SqliteRagStore.cs:37-41`). SQLite defaults foreign-key
enforcement OFF, so every deleted dataset leaves all of its chunks,
embeddings, and BM25 stats in `conversations.db` forever. The
`DeleteChunksForDatasetAsync` method that would have cleaned up has no
callers.

Fix: add `Foreign Keys=True` to the connection string (Microsoft.Data.
Sqlite emits the pragma per connection). Delete explicitly anyway in
`DeleteDatasetAsync` (chunks, bm25 stats, then the dataset row, one
transaction) so correctness does not depend on a pragma, and add a
one-time vacuum-style cleanup: on store initialization, delete
`rag_chunks`/`rag_bm25_stats` rows whose `dataset_id` no longer exists,
logging the count if non-zero (the owner's DB likely carries orphans
today).

Acceptance criteria:

- Test: ingest, delete dataset, assert zero `rag_chunks` and
  `rag_bm25_stats` rows remain for that id.
- Test: pre-seed orphan rows, initialize the store, assert they are
  removed and the runtime log records the count.

## 1.3 Re-ingest serves stale chunks until restart

`RagViewModel.IngestAsync` never invalidates the query cache
(`src/Aether.ViewModels/RagViewModel.cs:274-380`), and
`RagQueryService.RetrieveAsync` only re-warms when the cache entry is
missing or empty (`RagQueryService.cs:169-182`). Add documents to an
already-queried dataset and every subsequent query runs against the
old in-memory chunk list until the app restarts (or the user happens
to click Warm cache).

Fix: `RagPipeline` cannot see the query service (dependency direction
is fine, both live in Aether.Rag, but keep coupling low): have
`RagViewModel.IngestAsync` call `_query.ClearCache(ds.Id)` after a
successful non-dry-run ingest, and also clear in
`RagQueryService.DeleteDatasetAsync` (already done) and after 1.5's
stale-source removal.

Acceptance criteria:

- Test: warm cache, ingest an additional document into the same
  dataset (fake embeddings), query again, assert the new chunk is
  retrievable without constructing a new service.

## 1.4 Embedding model switch: guard the mismatch, offer reindex

The dataset records `EmbeddingModel`/`EmbeddingDimensions`
(`src/Aether.Rag/Models/RagDataset.cs:27-28`, set during ingest at
`RagPipeline.cs:496-497`) and the dataset manager already computes
`ReindexRequired` (`RagViewModel.cs:106-108`), but nothing acts on it:

- Query path: `TensorPrimitives.CosineSimilarity` throws on
  mismatched vector lengths, so querying a dataset embedded at 768
  dims with a new 1024-dim query model surfaces as a raw exception
  message. Same-length different-model embeddings are worse: silent
  garbage rankings.
- Ingest path: adding documents to an existing dataset with a
  different current embedding model mixes incompatible vectors in one
  dataset (old chunks keep old embeddings because Skip-unchanged never
  re-embeds).

Fix, three parts:

1. Query guard: in `RetrieveAsync`, if the dataset's recorded
   `EmbeddingModel` is non-empty and differs from
   `Settings.Rag.EmbeddingModel` (case-insensitive), skip the semantic
   scan, use BM25-only retrieval, and put a one-line note in
   `PlannerNotes` ("semantic search skipped: dataset embedded with X,
   current model is Y; reindex to re-enable"). Never throw from a
   dimension mismatch: filter candidate embeddings to
   `Embedding.Length == queryEmbedding.Length` as a belt-and-braces
   check in `CosineScan`.
2. Ingest guard: `RagViewModel.IngestAsync` refuses to add documents
   to an existing dataset when the models differ, with a message
   naming both models, unless the user picked the new Reindex action.
3. Reindex action: a button on the dataset manager card (enabled when
   `ReindexRequired`) that re-embeds all chunks of the dataset with
   the current model in batches (reuse `EmbedChunksAsync` retry
   clamps), updates `EmbeddingModel`/`EmbeddingDimensions`, rebuilds
   BM25 stats, clears the query cache, and reports progress through
   the existing ingest progress UI. It re-embeds stored chunk content;
   it must not require the original source files.

Acceptance criteria:

- Test: dataset recorded with model A, settings say model B: retrieval
  returns BM25-ranked results, no exception, planner note present.
- Test: `CosineScan` with mixed-length embeddings ranks only the
  matching-length ones.
- Test: reindex over a fake store re-embeds every chunk, updates
  config, and subsequent retrieval uses the new vectors.
- Ingest-into-mismatched-dataset is blocked in the VM with the
  explanatory status message.

## 1.5 Deleted source files never leave a dataset

`IngestDirectoryAsync` deletes chunks only for sources present in the
current directory scan (`RagPipeline.cs:184`); a file removed from the
folder stays in the dataset forever. Health surfaces `MissingFiles`
(`RagDatasetHealthService`) but offers no way to act on it.

Fix: add an explicit, user-confirmed "Remove missing sources" action
on the dataset manager card, shown only when `MissingFiles > 0`. It
lists the missing source paths, and on confirm deletes their chunks
(`DeleteChunksForSourcesAsync`), rebuilds BM25 stats, updates
`ChunkCount`, clears the query cache, and refreshes health. Do NOT
remove anything automatically during ingest; a temporarily unmounted
drive must not silently shred a dataset (this echoes the r9 rejection
of auto-killing unrecognized processes: destructive actions are
user-clicked).

Acceptance criteria:

- Test: ingest two files, delete one on disk, run the removal service
  method, assert its chunks are gone, stats rebuilt, count updated.
- The action is confirm-gated in the VM (same
  `RequestDeleteDatasetConfirmation` pattern as dataset delete).

## 1.6 Dry-run ingest writes to the database

`IngestDirectoryAsync` calls `SaveDatasetAsync` before the batch loop
(`RagPipeline.cs:87`) regardless of `options.DryRun`, so a dry run
creates or updates the dataset row (and a new dataset id appears in
the picker after a restart even though it has zero chunks). Move the
initial save behind `if (!options.DryRun)`; the final save at
`:210-212` is already skipped by the early return.

Acceptance criteria:

- Test: dry-run ingest into a fresh dataset name leaves
  `GetDatasetsAsync` unchanged.

## 1.7 `LastIngestPath`/`LastIngestUtc` are never persisted

`RagIngestRequestBuilder.PrepareDataset` sets them on re-ingest
(`src/Aether.Rag/RagIngestRequestBuilder.cs:41-42`) and the
Add-to-dataset command pre-fills the folder from `LastIngestPath`
(`RagViewModel.cs:412`), but `SaveDatasetAsync` never writes them and
`MapDataset` never reads them (`SqliteRagStore.cs:213-231, 434-442`),
so the pre-fill only works within one session. Persist both (either
two additive columns or fold them into `config_json` by moving the
properties onto `RagDatasetConfig`; prefer the columns, the config is
user-shaped tuning). Also set them on FIRST ingest, not only re-ingest.

Acceptance criteria:

- Test: ingest, reload datasets from a fresh store instance, assert
  `LastIngestPath` and `LastIngestUtc` round-trip.
