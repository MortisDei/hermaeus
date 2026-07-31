# 02. Retrieval that scales

## The problem

Retrieval holds every chunk of a dataset in memory, with its full text and
its full embedding, and touches all of it on every query. That is a
reasonable design for a few thousand chunks and it is the design the app
still has.

Two things follow from it. One is a hard ceiling that fails silently. The
other is that the per-query cost grows with the size of the corpus rather
than with the size of the answer.

The owner is about to start using RAG properly, which is what turns both of
these from theory into the experience.

## 2.1 A dataset above the cache ceiling silently answers nothing

**This is a correctness bug and it lands first, on its own.**

`StoreCache` (`RagQueryService.cs:86-107`):

```csharp
var size = EstimateCacheSize(chunks);
if (size > MaxCacheBytes)
{
    _cache.Remove(datasetId);
    var oldNode = _cacheOrder.Find(datasetId);
    if (oldNode is not null)
        _cacheOrder.Remove(oldNode);
    return;              // <- dataset is now absent from the cache
}
```

`RetrieveAsync` (`:220-233`) then does:

```csharp
if (chunks is null || chunks.Count == 0)
    await WarmCacheAsync(datasetId, ct);     // loads, then StoreCache drops it

lock (_cacheSync)
{
    chunks = _cache.GetValueOrDefault(datasetId, []);   // <- empty
    TouchCacheUnsafe(datasetId);
}
```

`chunks` is empty. `CosineScan` scores nothing. `ScoreQueryVariants` scores
nothing. The query returns no results and reports no error, and it does this
after reading every row and every embedding blob for that dataset out of
SQLite, which it will do again on the next query, and the one after that.

`MaxCacheBytes` is 128 MiB (`:44`) and `EstimateCacheSize` (`:139-146`)
counts content, three path strings, the embedding, and 256 bytes of
overhead. With nomic-embed-text-v1.5 at 768 dimensions that is roughly 3 KB
of embedding plus the text, so the ceiling arrives somewhere around 19,000
chunks. A single respectable codebase or documentation corpus reaches that.

The fix, in this item, is narrow and is not the rest of this document:

- A dataset that does not fit the cache is **still queried**, by scanning
  from the store rather than from memory. Correct and slower beats fast and
  empty.
- `RetrieveAsync` never treats "cache returned nothing" as "corpus is
  empty". Distinguish "not cached" from "cached and genuinely empty".
- When a query runs uncached, the retrieval result says so in
  `PlannerNotes`, using the same mechanism the embedding-mismatch and
  embedding-unavailable notes already use (`:255`, `:275`). The user is told
  the dataset is too large to cache and that queries will be slower, once,
  in the place they are already reading.

Write the failing test first: build a dataset whose estimated size exceeds
`MaxCacheBytes`, query it for a phrase that is certainly present, and assert
a result comes back. Watch it fail. Items 2.2 through 2.5 then make the
ceiling much harder to reach, but they are optimisation and this is a bug.

## 2.2 BM25 candidates come from an index, not from tokenising the corpus

`Bm25Scorer.Score` recomputes term frequency from `chunk.Content` for every
candidate it is given (`Bm25Scorer.cs:46`), and `RagQueryService` gives it
every chunk in the dataset, once per query variant (`:286-288`). Every query
tokenises the entire corpus, several times.

`rag.db` gains an FTS5 virtual table over chunk content, mirroring exactly
what `ConversationStore` already does for conversations
(`ConversationStore.cs:44-56`, `:240-241`, including its `LIKE` fallback at
`:255` for malformed `MATCH` syntax). Candidate generation becomes a `MATCH`
query returning at most a few hundred chunk ids.

**FTS5 generates candidates. `Bm25Scorer` still scores them.** This is the
design decision that keeps the change safe: the existing scoring function,
its `Bm25Stats`, and its tuning are untouched, so ranking among the chunks
that matter does not move. The only chunks that stop being scored are ones
FTS5 did not match at all, which share no query terms and therefore scored
essentially zero.

That claim must be checked, not assumed. The repository has a RAG eval
harness (`RagEvalService`, `docs/rag-eval-harness.md`). Run it before and
after this item on the same dataset and record both numbers in the PR. If
retrieval quality moves measurably, the candidate cap is the first thing to
raise, and the item does not land until the numbers agree.

Schema work is additive and goes through `SqliteMigrationRunner`. Existing
datasets need a one-time backfill of the FTS table; it runs once, reports
progress through the existing ingest progress channel if it runs long, and
an install that never opens the RAG panel must not pay for it at startup.

## 2.3 The cache holds embeddings, not documents

Once 2.2 removes the need for content during BM25 scoring, the in-memory
cache no longer needs text at all. It becomes a per-dataset scan index:

- `string[]` of chunk ids, in scan order
- one contiguous `float[]` of `count * dimension`
- the dimension, and the embedding model the dataset was built with

Content, paths, titles, heading paths and the rest stay in SQLite and are
read for the small number of chunks that survive ranking (2.5).

The footprint per chunk drops from roughly 7 KB to roughly 3 KB at 768
dimensions, and it stops varying with document size, which is what made the
old estimate unpredictable. `EstimateCacheSize` becomes exact rather than
estimated, because the block's size is known arithmetic rather than a sum
over strings.

`MaxCacheBytes` and `MaxCachedDatasets` keep their current values and their
current LRU behaviour (`:115-137`). This item changes what is stored, not
the policy for evicting it.

## 2.4 The cosine scan stops sorting the whole corpus

`CosineScan` (`HybridRetriever.cs:22-41`) is:

```csharp
return chunks
    .Where(c => c.Embedding.Length > 0 && c.Embedding.Length == query.Length)
    .Select(c => new ScoredChunk(c, TensorPrimitives.CosineSimilarity(...), ScoreSource.Semantic))
    .OrderByDescending(s => s.Score)
    .Take(topK)
    .ToList();
```

For `topK` of 50 over a corpus of n, that allocates n `ScoredChunk`
instances and performs an n log n sort to select 50 of them.

Over 2.3's contiguous block it becomes a single pass with a bounded
min-heap of size `topK`: one `TensorPrimitives.CosineSimilarity` call per
chunk against a slice of the block, no per-chunk allocation, and no full
sort. `System.Numerics.Tensors` is already imported
(`HybridRetriever.cs:1`); no new package.

Two behaviours must be preserved exactly:

- **The dimension-mismatch filter stays** (`:33`, and its comment explaining
  why). With a contiguous block the whole dataset shares one dimension, so
  the check moves to the block rather than the chunk, and a query whose
  dimension differs from the block's returns no semantic results in the same
  way it does today. `RagCosineScanIgnoresMismatchedEmbeddingLengths` must
  keep passing, adapted to the new shape but asserting the same thing.
- **Ties break the same way.** A heap and a sort disagree about equal
  scores. Whatever order the new code produces, it must be deterministic, or
  the eval harness becomes noisy for reasons that have nothing to do with
  retrieval quality.

Keep the existing signature as a thin overload if any caller outside
`RagQueryService` still passes a chunk list. `DocumentRecallSource.cs:57`
does exactly that, and Recall is not in scope for this round.

## 2.5 Content is loaded for candidates, not for corpora

After fusion, `RagQueryService` holds at most a few dozen chunk ids. Those
are read from SQLite by id, in one query, and only then does a chunk carry
its text.

This is the item that makes 2.3 possible and it is where the mistake will
be if there is one: everything downstream of fusion assumes `ScoredChunk`
already has content. Parent-child upgrade, the reranker, the context
packer, citations and the trace all read `Chunk.Content`. Find every one of
them before changing the shape, not after. `RagQueryService` is 816 lines
and the packing path runs from roughly `:291` to the end.

`GetChunksAsync`'s existing `includeEmbeddings: false` projection
(`SqliteRagStore.cs:404`) is the precedent for the shape of the new read: a
by-id lookup that returns everything except the embedding blob.

## 2.6 The three send-path injections run concurrently

`ChatViewModel.SendAsync` (`:982-994`) awaits memory injection, then RAG
injection, then recall injection, one after another. The comment at `:986`
records r21 1.3's reasoning: "sequential is fine and keeps the trace
breakdown legible".

They are independent, and each already carries its own stopwatch
(`recallMs`, `selectMs`, `lessonMs`, `ragMs`, `recallInjectionMs`), so
running them concurrently costs nothing in legibility: each timer still
measures its own task. The pre-stream wait becomes the slowest of the three
rather than their sum.

Requirements:

- The order in which sources are appended to `asst.Sources` must stay
  memory, then RAG, then recall, regardless of which finishes first. The
  user sees a stable ordering; concurrency is an implementation detail.
- One injection failing must not cancel the others. Each already degrades
  independently (RAG has an explicit "semantic search unavailable" path);
  that stays true.
- The context receipt built from these (r25 doc 02) must be byte-identical
  for the same inputs. If it is not, the concurrency changed something it
  should not have.

## 2.7 The ceiling is visible before you hit it

The RAG panel shows, per dataset, whether it is cached, and its scan-index
size against the budget. Not a warning, not a recommendation: the same
factual register as the existing dataset health information.

This exists because 2.1's `PlannerNotes` line tells you *after* a slow
query. A user ingesting a large corpus should be able to see the number
going up while they do it.

## Tests

| Area | Test |
| --- | --- |
| 2.1 | A dataset over `MaxCacheBytes` returns results for a phrase that is present (write first, watch fail) |
| 2.1 | An uncached query reports the reason in `PlannerNotes` |
| 2.1 | A genuinely empty dataset is distinguished from an uncached one |
| 2.2 | FTS5 candidate generation returns chunks containing the query terms |
| 2.2 | Malformed `MATCH` input falls back rather than throwing (mirror `ConversationStore`'s existing case) |
| 2.2 | The FTS backfill is idempotent and does not duplicate rows |
| 2.2 | `Bm25Scorer` scoring is unchanged for a fixed candidate set (existing tests must pass untouched) |
| 2.3 | Scan-index size is exact arithmetic over count and dimension |
| 2.3 | LRU eviction still evicts oldest first and respects both limits |
| 2.4 | Top-K selection matches the previous implementation's result set for a fixed fixture |
| 2.4 | A query whose dimension differs from the block returns no semantic results |
| 2.4 | Tie ordering is deterministic across runs |
| 2.5 | Candidate content is loaded for exactly the fused ids, and no others |
| 2.5 | Citations, parent upgrade and the trace still carry content |
| 2.6 | Sources are ordered memory, RAG, recall regardless of completion order |
| 2.6 | One injection throwing does not prevent the other two |
| 2.6 | The context receipt is identical to the sequential version for fixed inputs |

Plus the eval-harness before/after numbers from 2.2, recorded in the PR
body rather than as a test.

## What this doc explicitly does not do

- **No approximate nearest neighbour index (HNSW, IVF, or similar).** An
  exact SIMD scan over a contiguous block handles corpora far beyond
  anything this app has seen, and an ANN index adds a build step, a tuning
  surface, a recall/speed tradeoff to explain, and a second thing that can
  disagree with the database. When an exact scan is measurably too slow on a
  real corpus, that measurement is the argument for revisiting this.
- **No vector extension for SQLite.** New native dependency, standing rule
  against new packages, and it would replace a scan that is not the
  bottleneck.
- **No change to chunking, chunk size, or the ingest pipeline's structure.**
  `RagPipeline` already batches embeddings, skips unchanged files, reports
  progress, and supports dry runs. It is not what is slow.
- **No replacement of `Bm25Scorer` with FTS5's own `bm25()` ranking.** 2.2
  uses FTS5 for candidate generation precisely so that scoring, and
  therefore ranking quality, does not move. Swapping the scoring function is
  a retrieval-quality change wearing a performance change's clothes.
- **No change to the RRF fusion weights, the boost factors, or
  `MaxBoostFactor`.** r10 tuned those deliberately (`HybridRetriever.cs:74-98`).
  This round makes retrieval faster, not different.
- **No raising of `MaxCacheBytes` as the fix for 2.1.** A bigger number
  moves the cliff; it does not remove it.
- **No background pre-warming of every dataset at startup.** Doc 01 is
  removing work from startup, not adding it.
