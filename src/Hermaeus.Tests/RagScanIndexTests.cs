using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hermaeus.Rag;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests
{
    /// <summary>
    /// r27 02-retrieval-that-scales.md 2.2 through 2.5. Retrieval used to hold
    /// every chunk of a dataset in memory with its full text, tokenise the whole
    /// corpus once per query variant for BM25, and allocate a scored record per
    /// chunk and sort all of them to select fifty.
    /// </summary>
    internal static class RagScanIndexTests
    {
        // ── 2.2 FTS5 candidate generation ───────────────────────────────────

        public static async Task FtsCandidateGenerationReturnsChunksContainingTheQueryTerms()
        {
            using var temp = new TempDir();
            var (store, _) = await NewStoreAsync(temp);
            var dataset = await IngestAsync(store, temp, "fts-candidates", new Dictionary<string, string>
            {
                ["marker.txt"] = "The deployment codename is zephyrion outpost quaranta.",
                ["other.txt"] = "Unrelated notes about gardening rotations and weather."
            });

            var ids = await store.SearchChunkIdsAsync(dataset.Id, "zephyrion", 50);
            var chunks = await store.GetChunksByIdsAsync(ids);

            True(chunks.Count > 0, "FTS candidate generation should return the chunk containing the term");
            True(chunks.All(c => c.Content.Contains("zephyrion", StringComparison.OrdinalIgnoreCase)),
                "every returned candidate should actually contain the query term");
            False(chunks.Any(c => c.SourceFile == "other.txt"),
                "a chunk sharing no query term should not be a candidate");
        }

        public static async Task MalformedMatchInputFallsBackRatherThanThrowing()
        {
            using var temp = new TempDir();
            var (store, _) = await NewStoreAsync(temp);
            var dataset = await IngestAsync(store, temp, "fts-malformed", new Dictionary<string, string>
            {
                ["a.txt"] = "quoted \"phrase\" and a NEAR( token in the corpus."
            });

            // Mirrors ConversationStore's existing case: unbalanced quotes and
            // bare FTS5 operators must not surface as a SqliteException.
            var ids = await store.SearchChunkIdsAsync(dataset.Id, "\"unbalanced AND NEAR( *", 50);
            True(ids is not null, "a malformed MATCH must fall back rather than throw");
        }

        public static async Task TheFtsBackfillIsIdempotentAndDoesNotDuplicateRows()
        {
            using var temp = new TempDir();
            var (store, _) = await NewStoreAsync(temp);
            var dataset = await IngestAsync(store, temp, "fts-backfill", new Dictionary<string, string>
            {
                ["a.txt"] = "alpha bravo charlie delta echo."
            });

            var first = await store.SearchChunkIdsAsync(dataset.Id, "bravo", 50);
            var second = await store.SearchChunkIdsAsync(dataset.Id, "bravo", 50);

            Equal(first.Count, second.Count, "searching twice should not grow the index");
            Equal(first.Count, first.Distinct(StringComparer.Ordinal).Count(), "candidate ids should be unique");
        }

        public static async Task ReIngestingTheSameChunkReplacesItsSearchRowRatherThanAddingOne()
        {
            using var temp = new TempDir();
            var (store, _) = await NewStoreAsync(temp);
            var docs = temp.PathFor("docs-reingest");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "alpha bravo charlie.");

            var dataset = new RagDataset { Name = "fts-reingest" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs, options: new IngestOptions { DuplicatePolicy = IngestDuplicatePolicy.Replace });
            var before = await store.SearchChunkIdsAsync(dataset.Id, "bravo", 50);

            await pipeline.IngestDirectoryAsync(dataset, docs, options: new IngestOptions { DuplicatePolicy = IngestDuplicatePolicy.Replace });
            var after = await store.SearchChunkIdsAsync(dataset.Id, "bravo", 50);

            Equal(before.Count, after.Count, "re-ingesting the same content must not duplicate search rows");
        }

        public static async Task DeletingADatasetRemovesItsSearchRows()
        {
            using var temp = new TempDir();
            var (store, _) = await NewStoreAsync(temp);
            var dataset = await IngestAsync(store, temp, "fts-delete", new Dictionary<string, string>
            {
                ["a.txt"] = "alpha bravo charlie."
            });
            True((await store.SearchChunkIdsAsync(dataset.Id, "bravo", 50)).Count > 0, "the dataset should be searchable before deletion");

            await store.DeleteDatasetAsync(dataset.Id);

            Equal(0, (await store.SearchChunkIdsAsync(dataset.Id, "bravo", 50)).Count,
                "a deleted dataset should leave no search rows behind");
        }

        /// <summary>
        /// The claim 2.2 rests on, checked rather than assumed: the only chunks
        /// FTS5 stops handing to Bm25Scorer are ones that share no query term
        /// and therefore scored essentially zero. Scoring the FTS candidate set
        /// must produce the same ranking as scoring the whole corpus did.
        /// </summary>
        public static async Task ScoringFtsCandidatesRanksIdenticallyToScoringTheWholeCorpus()
        {
            using var temp = new TempDir();
            var (store, _) = await NewStoreAsync(temp);
            var files = new Dictionary<string, string>();
            for (var i = 0; i < 30; i++)
            {
                files[$"doc-{i:D2}.txt"] =
                    $"Document {i} covers deployment topology, cache eviction and retrieval latency. " +
                    (i % 3 == 0 ? "It also mentions the zephyrion outpost codename explicitly. " : "") +
                    (i % 5 == 0 ? "Quarterly gardening rotations and weather notes appear here too. " : "") +
                    string.Join(' ', Enumerable.Repeat($"filler term {i}", 6));
            }

            var dataset = await IngestAsync(store, temp, "candidate-equivalence", files);
            var stats = await store.GetBm25StatsAsync(dataset.Id);
            True(stats is not null, "ingest should have written BM25 stats");

            var wholeCorpus = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            var scorer = new Bm25Scorer();

            foreach (var query in new[] { "zephyrion outpost", "cache eviction", "gardening rotations", "retrieval latency" })
            {
                var fullRanking = scorer.Score(query, wholeCorpus, stats!, Bm25Scorer.BuildTfIndex(wholeCorpus))
                    .Where(s => s.Score > 0f)
                    .OrderByDescending(s => s.Score)
                    .ThenBy(s => s.Chunk.Id, StringComparer.Ordinal)
                    .Select(s => s.Chunk.Id)
                    .ToList();

                var candidateIds = await store.SearchChunkIdsAsync(dataset.Id, query, 400);
                var candidates = await store.GetChunksByIdsAsync(candidateIds);
                var candidateRanking = scorer.Score(query, candidates, stats!, Bm25Scorer.BuildTfIndex(candidates))
                    .Where(s => s.Score > 0f)
                    .OrderByDescending(s => s.Score)
                    .ThenBy(s => s.Chunk.Id, StringComparer.Ordinal)
                    .Select(s => s.Chunk.Id)
                    .ToList();

                Equal(string.Join(",", fullRanking), string.Join(",", candidateRanking),
                    $"FTS candidate generation must not change which chunks BM25 ranks, or in what order, for '{query}'");
            }
        }

        // ── 2.3 The cache holds embeddings, not documents ───────────────────

        public static Task ScanIndexSizeIsExactArithmeticOverCountAndDimension()
        {
            var index = new RagScanIndex(["a", "b", "c"], new float[3 * 768], 768, "nomic");

            Equal(RagScanIndex.ByteSizeFor(3, 768), index.ByteSize,
                "a loaded index and the projection used by the RAG panel must agree");
            True(index.ByteSize >= 3L * 768 * sizeof(float), "the block itself is the floor of the size");
            // Exact rather than estimated: the size no longer varies with
            // document length, which is what made the old ceiling unpredictable.
            Equal(RagScanIndex.ByteSizeFor(6, 768), RagScanIndex.ByteSizeFor(3, 768) * 2,
                "size should be linear in chunk count, with no content term");
            return Task.CompletedTask;
        }

        public static async Task TheScanIndexCarriesEveryEmbeddedChunkAsOneContiguousBlock()
        {
            using var temp = new TempDir();
            var (store, settings) = await NewStoreAsync(temp);
            var dataset = await IngestAsync(store, temp, "scan-block", new Dictionary<string, string>
            {
                ["a.txt"] = "alpha bravo charlie.",
                ["b.txt"] = "delta echo foxtrot."
            });

            var index = await store.GetScanIndexAsync(dataset.Id, settings.Settings.Rag.EmbeddingModel);
            var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: true);

            Equal(chunks.Count, index.Count, "the index should carry every embedded chunk");
            Equal(index.Count * index.Dimension, index.Block.Length, "the block must be exactly count times dimension, never jagged");
            True(index.ChunkIds.All(id => chunks.Any(c => c.Id == id)), "every index id should belong to the dataset");
        }

        public static async Task LruEvictionStillEvictsOldestFirstAndRespectsTheBudget()
        {
            using var temp = new TempDir();
            var (store, settings) = await NewStoreAsync(temp);
            var first = await IngestAsync(store, temp, "lru-first", new Dictionary<string, string> { ["a.txt"] = "alpha bravo." });
            var second = await IngestAsync(store, temp, "lru-second", new Dictionary<string, string> { ["b.txt"] = "charlie delta." });

            // A budget that fits exactly one dataset's index forces an eviction
            // on the second warm, without changing the eviction policy itself.
            var oneIndex = (await store.GetScanIndexAsync(first.Id, string.Empty)).ByteSize;
            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker(),
                maxCacheBytes: oneIndex);

            await query.WarmCacheAsync(first.Id);
            True(query.GetScanIndexInfo(first.Id).Cached, "the first dataset should be cached");

            await query.WarmCacheAsync(second.Id);
            True(query.GetScanIndexInfo(second.Id).Cached, "the most recently used dataset should be cached");
            False(query.GetScanIndexInfo(first.Id).Cached, "the oldest dataset should be evicted first");
        }

        // ── 2.4 The bounded top-K scan ──────────────────────────────────────

        public static Task TopKSelectionMatchesTheFullSortForAFixedFixture()
        {
            var index = FixedIndex(count: 200, dimension: 4);
            var query = new[] { 1f, 0f, 0f, 0f };

            var bounded = HybridRetriever.CosineScan(query, index, topK: 10);
            var fullSort = Enumerable.Range(0, index.Count)
                .Select(i => (Id: index.ChunkIds[i], Score: Cosine(query, index.RowAt(i).ToArray()), Index: i))
                .OrderByDescending(e => e.Score)
                .ThenBy(e => e.Index)
                .Take(10)
                .Select(e => e.Id)
                .ToList();

            Equal(10, bounded.Count, "the bounded scan should return exactly topK results");
            Equal(string.Join(",", fullSort), string.Join(",", bounded.Select(b => b.ChunkId)),
                "the bounded min-heap must select the same set, in the same order, as sorting the whole corpus");
            return Task.CompletedTask;
        }

        public static Task AQueryWhoseDimensionDiffersFromTheBlockReturnsNoSemanticResults()
        {
            var index = FixedIndex(count: 20, dimension: 4);

            // With a contiguous block the whole dataset shares one dimension, so
            // the mismatch check moves to the block. It must still refuse rather
            // than throw out of TensorPrimitives.CosineSimilarity.
            Equal(0, HybridRetriever.CosineScan(new[] { 1f, 0f, 0f }, index, topK: 5).Count,
                "a query of the wrong dimension should return no semantic results");
            Equal(0, HybridRetriever.CosineScan([1f, 0f, 0f, 0f], RagScanIndex.Empty, topK: 5).Count,
                "an empty index should return no semantic results");
            return Task.CompletedTask;
        }

        public static Task TieOrderingIsDeterministicAcrossRuns()
        {
            // Every row identical: every score ties, so only the tie-break rule
            // decides the output. It must be the same rule every time or the
            // eval harness becomes noisy for reasons unrelated to quality.
            var block = new float[40 * 4];
            for (var i = 0; i < 40; i++)
            {
                block[i * 4] = 1f;
            }

            var ids = Enumerable.Range(0, 40).Select(i => $"chunk-{i:D2}").ToArray();
            var index = new RagScanIndex(ids, block, 4, "test");

            var runs = Enumerable.Range(0, 5)
                .Select(_ => string.Join(",", HybridRetriever.CosineScan([1f, 0f, 0f, 0f], index, topK: 8).Select(s => s.ChunkId)))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Equal(1, runs.Count, "repeated scans over an all-ties fixture must produce one stable ordering");
            Equal("chunk-00,chunk-01,chunk-02,chunk-03,chunk-04,chunk-05,chunk-06,chunk-07", runs[0],
                "ties should break on scan position, lowest first");
            return Task.CompletedTask;
        }

        // ── 2.5 Content for candidates, not corpora ─────────────────────────

        public static async Task CandidateContentIsLoadedForExactlyTheRequestedIdsAndNoOthers()
        {
            using var temp = new TempDir();
            var (store, _) = await NewStoreAsync(temp);
            var dataset = await IngestAsync(store, temp, "by-id", new Dictionary<string, string>
            {
                ["a.txt"] = "alpha bravo charlie.",
                ["b.txt"] = "delta echo foxtrot.",
                ["c.txt"] = "golf hotel india."
            });

            var all = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            var wanted = all.Take(2).Select(c => c.Id).ToList();
            var loaded = await store.GetChunksByIdsAsync(wanted);

            Equal(wanted.Count, loaded.Count, "exactly the requested ids should come back");
            True(loaded.All(c => wanted.Contains(c.Id)), "no chunk outside the requested set should be loaded");
            True(loaded.All(c => c.Content.Length > 0), "candidates must carry their content");
            Equal(0, (await store.GetChunksByIdsAsync([])).Count, "an empty id set should not query at all");
        }

        public static async Task CitationsParentUpgradeAndTheTraceStillCarryContent()
        {
            using var temp = new TempDir();
            var (store, settings) = await NewStoreAsync(temp);
            var docs = temp.PathFor("docs-parent");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"),
                "apple banana carrot delta echo foxtrot golf hotel india juliet kilo lima mike november");

            var dataset = new RagDataset
            {
                Name = "content-downstream",
                Config = new RagDatasetConfig { UseParentChild = true, TargetChunkChars = 20, ParentChunkChars = 200, OverlapChars = 0 }
            };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var retrieval = await query.RetrieveAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 3, UseParentChild: true));

            True(retrieval.Selected.Count > 0, "selection should be non-empty");
            True(retrieval.Selected.All(s => s.Chunk.Content.Length > 0),
                "everything downstream of fusion reads Chunk.Content; the selected chunks must still carry it");
            True(retrieval.SemanticCandidates.All(s => s.Chunk.Content.Length > 0),
                "semantic candidates must carry content once they leave retrieval");
            True(retrieval.Selected.Any(s => s.Chunk.Content.Length > 20),
                "parent upgrade should still resolve to the larger parent body");
        }

        public static async Task RetrievalStillAnswersAcrossBothSignalsAfterTheScanRework()
        {
            using var temp = new TempDir();
            var (store, settings) = await NewStoreAsync(temp);
            var dataset = await IngestAsync(store, temp, "both-signals", new Dictionary<string, string>
            {
                ["marker.txt"] = "The deployment codename is zephyrion outpost quaranta.",
                ["filler.txt"] = "Quarterly logistics notes covering gardening rotations."
            });

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var retrieval = await query.RetrieveAsync(dataset.Id, "zephyrion outpost", new RagQueryOptions(TopK: 5));

            True(retrieval.Bm25Candidates.Count > 0, "keyword candidates should come back from the FTS-backed path");
            True(retrieval.Selected.Any(s => s.Chunk.Content.Contains("zephyrion", StringComparison.OrdinalIgnoreCase)),
                "the chunk containing the query terms should be selected");
        }

        // ── Fixtures ────────────────────────────────────────────────────────

        private static RagScanIndex FixedIndex(int count, int dimension)
        {
            var block = new float[count * dimension];
            for (var i = 0; i < count; i++)
            {
                // A deterministic spread of directions, so scores differ and the
                // ordering is a real ordering rather than an accident.
                block[(i * dimension) + 0] = 1f;
                block[(i * dimension) + 1] = i / (float)count;
                block[(i * dimension) + 2] = (count - i) / (float)count;
                block[(i * dimension) + 3] = 0.5f;
            }

            return new RagScanIndex(
                Enumerable.Range(0, count).Select(i => $"chunk-{i:D3}").ToArray(),
                block,
                dimension,
                "test");
        }

        private static float Cosine(float[] a, float[] b)
        {
            var dot = 0f; var na = 0f; var nb = 0f;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            return dot / (MathF.Sqrt(na) * MathF.Sqrt(nb));
        }

        private static async Task<(SqliteRagStore Store, Hermaeus.Services.SettingsService Settings)> NewStoreAsync(TempDir temp)
        {
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            return (store, settings);
        }

        private static async Task<RagDataset> IngestAsync(SqliteRagStore store, TempDir temp, string name, Dictionary<string, string> files)
        {
            var docs = temp.PathFor($"docs-{name}");
            Directory.CreateDirectory(docs);
            foreach (var (file, content) in files)
                await File.WriteAllTextAsync(Path.Combine(docs, file), content);

            var dataset = new RagDataset { Name = name };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);
            return dataset;
        }
    }
}
