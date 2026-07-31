using System;
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
    /// A dataset whose scan index does not fit the in-memory cache budget used
    /// to be dropped by StoreCache and then scanned as an empty list, so the
    /// query silently returned nothing while re-reading the whole dataset out
    /// of SQLite every time (r27 02-retrieval-that-scales.md 2.1).
    /// </summary>
    internal static class RagCacheCeilingTests
    {
        private const string Marker = "zephyrion outpost quaranta";

        public static async Task OversizedDatasetStillAnswersAPhraseThatIsPresent()
        {
            using var temp = new TempDir();
            var (store, settings) = await NewStoreAsync(temp);
            var dataset = await IngestMarkerDatasetAsync(store, temp, "oversized");

            // A budget below one chunk's fixed overhead puts any non-empty
            // dataset over the ceiling without needing a 128 MiB fixture.
            var query = NewQuery(store, settings, maxCacheBytes: 100);
            var retrieval = await query.RetrieveAsync(dataset.Id, Marker, new RagQueryOptions(TopK: 5));

            True(retrieval.Selected.Count > 0,
                "a dataset over the cache budget must still be queried from the store; before this fix the scan ran over an empty list and returned nothing, forever");
            True(retrieval.Selected.Any(s => s.Chunk.Content.Contains(Marker, StringComparison.OrdinalIgnoreCase)),
                "the chunk containing the queried phrase should be selected");
        }

        public static async Task UncachedQueryReportsTheReasonInPlannerNotes()
        {
            using var temp = new TempDir();
            var (store, settings) = await NewStoreAsync(temp);
            var dataset = await IngestMarkerDatasetAsync(store, temp, "uncached-notes");

            var query = NewQuery(store, settings, maxCacheBytes: 100);
            var retrieval = await query.RetrieveAsync(dataset.Id, Marker, new RagQueryOptions(TopK: 5));

            True(retrieval.PlannerNotes.Contains("too large to cache", StringComparison.OrdinalIgnoreCase),
                "an uncached scan should say so in PlannerNotes, where the embedding-mismatch and embedding-unavailable notes already appear");
            True(retrieval.PlannerNotes.Contains("slower", StringComparison.OrdinalIgnoreCase),
                "the note should tell the user what it costs them, not just that it happened");
        }

        public static async Task CachedDatasetDoesNotReportTheUncachedNote()
        {
            using var temp = new TempDir();
            var (store, settings) = await NewStoreAsync(temp);
            var dataset = await IngestMarkerDatasetAsync(store, temp, "cached-notes");

            var query = NewQuery(store, settings, maxCacheBytes: null);
            var retrieval = await query.RetrieveAsync(dataset.Id, Marker, new RagQueryOptions(TopK: 5));

            False(retrieval.PlannerNotes.Contains("too large to cache", StringComparison.OrdinalIgnoreCase),
                "a dataset that fits the budget should carry no cache note at all");
            True(query.GetScanIndexInfo(dataset.Id).Cached, "a dataset that fits the budget should be cached");
        }

        public static async Task GenuinelyEmptyDatasetIsDistinguishedFromAnUncachedOne()
        {
            using var temp = new TempDir();
            var (store, settings) = await NewStoreAsync(temp);

            var empty = new RagDataset { Name = "genuinely-empty" };
            await store.SaveDatasetAsync(empty);
            var populated = await IngestMarkerDatasetAsync(store, temp, "over-budget");

            var query = NewQuery(store, settings, maxCacheBytes: 100);
            await query.RetrieveAsync(empty.Id, Marker, new RagQueryOptions(TopK: 5));
            await query.RetrieveAsync(populated.Id, Marker, new RagQueryOptions(TopK: 5));

            var emptyInfo = query.GetScanIndexInfo(empty.Id);
            True(emptyInfo.Cached, "an empty dataset fits any budget and is cached, so 'no results' means the corpus really is empty");
            Equal(0, emptyInfo.ChunkCount, "an empty dataset caches zero chunks");

            var populatedInfo = query.GetScanIndexInfo(populated.Id);
            False(populatedInfo.Cached, "a dataset over the budget is reported as uncached rather than as an empty cache entry");
        }

        public static async Task ScanIndexInfoReportsTheBudgetItWasMeasuredAgainst()
        {
            using var temp = new TempDir();
            var (store, settings) = await NewStoreAsync(temp);
            var dataset = await IngestMarkerDatasetAsync(store, temp, "index-size");

            var query = NewQuery(store, settings, maxCacheBytes: null);
            await query.WarmCacheAsync(dataset.Id);

            var info = query.GetScanIndexInfo(dataset.Id);
            Equal(RagQueryService.DefaultMaxCacheBytes, info.BudgetBytes, "the reported budget should be the service's own budget");
            True(info.IndexBytes > 0, "a cached dataset should report a non-zero index size");
            True(info.ChunkCount > 0, "a cached dataset should report its chunk count");
        }

        // ── Fixtures ────────────────────────────────────────────────────────

        private static async Task<(SqliteRagStore Store, Hermaeus.Services.SettingsService Settings)> NewStoreAsync(TempDir temp)
        {
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            return (store, settings);
        }

        private static RagQueryService NewQuery(SqliteRagStore store, Hermaeus.Services.SettingsService settings, long? maxCacheBytes) =>
            new(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker(), maxCacheBytes: maxCacheBytes);

        private static async Task<RagDataset> IngestMarkerDatasetAsync(SqliteRagStore store, TempDir temp, string name)
        {
            var docs = temp.PathFor($"docs-{name}");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"),
                $"The deployment codename for this cycle is {Marker}, recorded here in full.");
            await File.WriteAllTextAsync(Path.Combine(docs, "b.txt"),
                "Unrelated notes about weather, gardening rotations and quarterly logistics.");

            var dataset = new RagDataset { Name = name };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);
            return dataset;
        }
    }
}
