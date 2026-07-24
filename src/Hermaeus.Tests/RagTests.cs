using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hermaeus.Rag;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using static Hermaeus.Tests.Helpers;
using static Hermaeus.Tests.PdfHelpers;

namespace Hermaeus.Tests
{
    internal static class RagTests
    {
        public static async Task RagWebIngestStripsHtmlAndStoresChunks()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            using var http = new HttpClient(new FakeHttpHandler("""
                <html>
                  <head>
                    <title>Example Title</title>
                    <script>secretScript()</script>
                    <style>.hidden{display:none}</style>
                  </head>
                  <body><main>Visible local-first documentation page.</main></body>
                </html>
                """));
            var pipeline = new RagPipeline(store, new FakeEmbeddingService(), http);
            var dataset = new RagDataset
            {
                Name = "web",
                Config = new RagDatasetConfig
                {
                    EnableWebLoader = true,
                    ExtractionMode = RagExtractionMode.WebUrl,
                    WebUrlList = "https://example.test/docs",
                    WebMaxPages = 1
                }
            };

            await pipeline.IngestWebAsync(dataset);
            var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            True(chunks.Count > 0, "web ingest should store chunks");
            True(chunks[0].Content.Contains("Visible local-first documentation page", StringComparison.Ordinal),
                "stored chunk should include visible page text");
            False(chunks[0].Content.Contains("secretScript", StringComparison.Ordinal),
                "stored chunk should not include script text");
            Equal("Example Title", chunks[0].SourceTitle, "web title should be stored as source title");
            Equal(chunks.Count, dataset.ChunkCount, "dataset chunk count should match stored chunks");
        }

        public static async Task RagDigitalPdfTextExtracts()
        {
            using var temp = new TempDir();
            var pdf = temp.PathFor("paper.pdf");
            WriteSimplePdf(pdf, "Digital PDF alpha beta");

            var extracted = await PdfTextExtractor.ExtractAsync(pdf);
            True(extracted.HasText, "digital PDF should have extractable text");
            True(extracted.Text.Contains("Digital PDF alpha beta", StringComparison.Ordinal), "PDF text should be extracted");
        }

        public static async Task RagDirectoryIngestIncludesPdfs()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "notes.md"), "markdown source alpha");
            WriteSimplePdf(Path.Combine(docs, "paper.pdf"), "pdf source beta gamma");

            var dataset = new RagDataset { Name = "docs" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            True(chunks.Any(c => c.SourceFile == "paper.pdf" && c.Content.Contains("pdf source beta gamma", StringComparison.Ordinal)),
                "PDF content should be chunked and stored");
            True(chunks.Any(c => c.SourceFile == "notes.md"), "markdown content should still ingest");
        }

        public static async Task RagDirectoryIngestReportsOverallProgress()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            for (var i = 0; i < 55; i++)
                await File.WriteAllTextAsync(Path.Combine(docs, $"notes-{i:D2}.md"), $"markdown source {i} alpha beta gamma");

            var dataset = new RagDataset { Name = "progress" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var reports = new List<IngestProgress>();
            await pipeline.IngestDirectoryAsync(dataset, docs, new InlineProgress(reports.Add));

            var overall = reports.Where(p => p.OverallTotal > 0).ToList();
            True(overall.Count > 0, "directory ingest should report overall progress");
            True(overall.Zip(overall.Skip(1), (a, b) => b.OverallDone >= a.OverallDone).All(x => x),
                "overall progress should never reset between file batches");
            True(reports.Any(p => p.Stage == "Embedding"
                && p.OverallDetail.Contains("File batch", StringComparison.Ordinal)
                && p.Detail.Contains("embedding batch", StringComparison.Ordinal)),
                "embedding progress should identify both the file batch and embedding batch");
        }

        public static async Task RagDirectoryDryRunReportsWithoutPersisting()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "preview.md"), "dry run preview alpha beta");

            var dataset = new RagDataset { Name = "preview" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var report = await pipeline.IngestDirectoryAsync(dataset, docs, options: new IngestOptions { DryRun = true, DuplicatePolicy = IngestDuplicatePolicy.ReportOnly });

            var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            Equal(0, chunks.Count, "dry-run ingest should not persist chunks");

            // r10 01-rag-correctness.md 1.6: a dry run must not create the
            // dataset row either, or a zero-chunk dataset appears in the
            // picker after restart.
            var datasets = await store.GetDatasetsAsync();
            False(datasets.Any(d => d.Name == "preview"), "dry-run ingest should not create a dataset row");
            True(report.Documents.Count > 0, "dry-run report should include document entries");
            True(report.Documents.Any(d => d.Path.Contains("preview.md", StringComparison.Ordinal)), "dry-run report should include the source path");
        }

        public static async Task RagDirectorySkipUnchangedAvoidsDuplicateChunks()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "keep.md"), "unchanged source alpha beta");

            var dataset = new RagDataset { Name = "skip-unchanged" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs, options: new IngestOptions { DuplicatePolicy = IngestDuplicatePolicy.Replace });
            var before = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);

            var report = await pipeline.IngestDirectoryAsync(dataset, docs, options: new IngestOptions { DuplicatePolicy = IngestDuplicatePolicy.SkipIfUnchanged });
            var after = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);

            Equal(before.Count, after.Count, "unchanged ingest should not duplicate chunks");
            True(report.Documents.Any(d => d.Status == DocumentIngestStatus.SkippedUnchanged), "report should mention skipped unchanged source");
        }

        public static async Task RagEmptyPdfWarnsAndContinues()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "notes.txt"), "plain text survives with enough content to chunk");
            WriteSimplePdf(Path.Combine(docs, "scan.pdf"), string.Empty);

            var dataset = new RagDataset { Name = "docs" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var report = await pipeline.IngestDirectoryAsync(dataset, docs);

            var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            True(chunks.Any(c => c.SourceFile == "notes.txt"), "text file should still ingest when PDF has no text");
            False(chunks.Any(c => c.SourceFile == "scan.pdf"), "empty PDF should not store chunks");
            True(report.Health is not null
                && report.Health.Warnings.Any(line => line.Contains("No extractable PDF text", StringComparison.Ordinal)),
                "empty PDF should surface a health warning");
        }

        public static async Task RagIngestCancellationDuringEmbedding()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "file1.txt"), string.Concat(Enumerable.Repeat("alpha ", 100)));
            await File.WriteAllTextAsync(Path.Combine(docs, "file2.txt"), string.Concat(Enumerable.Repeat("beta ", 100)));

            var dataset = new RagDataset { Name = "cancel-embed" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());

            // Cancellation during embedding phase
            var cts = new CancellationTokenSource();
            var task = Task.Run(async () =>
            {
                var progress = new Progress<IngestProgress>(p =>
                {
                    // Cancel after first batch embedded
                    if (p.Stage == "Embedding" && p.Done > 10)
                        cts.Cancel();
                });

                try
                {
                    await pipeline.IngestDirectoryAsync(dataset, docs, progress, cts.Token);
                }
                catch (OperationCanceledException) { }
            });

            await task;
            var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            // Cancellation should prevent storage, or at least partially stored
            True(chunks.Count < 500, "cancellation during embedding should reduce final chunk count significantly");
        }

        public static async Task RagDirectoryIngestPersistsCompletedFileBatches()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            for (var i = 0; i < 60; i++)
                await File.WriteAllTextAsync(Path.Combine(docs, $"file-{i:D2}.txt"), $"batch checkpoint source {i} alpha beta gamma");

            var dataset = new RagDataset { Name = "checkpointed-ingest" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            using var cts = new CancellationTokenSource();
            var progress = new InlineProgress(p =>
            {
                if (p.Stage == "Chunking" && p.Done > 50)
                    cts.Cancel();
            });

            try
            {
                await pipeline.IngestDirectoryAsync(dataset, docs, progress, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }

            var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            True(chunks.Any(c => c.SourceFile == "file-00.txt"), "completed file batches should be persisted before later cancellation");
            False(chunks.Any(c => c.SourceFile == "file-59.txt"), "cancelled later batches should not be fully persisted");
        }

        public static async Task RagIngestCancellationDuringStorage()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();
            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "file1.txt"), string.Concat(Enumerable.Repeat("gamma ", 100)));
            await File.WriteAllTextAsync(Path.Combine(docs, "file2.txt"), string.Concat(Enumerable.Repeat("delta ", 100)));

            var dataset = new RagDataset { Name = "cancel-store" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());

            // Cancellation during storage phase
            var cts = new CancellationTokenSource();
            var task = Task.Run(async () =>
            {
                var progress = new Progress<IngestProgress>(p =>
                {
                    // Cancel during storing phase
                    if (p.Stage == "Storing")
                        cts.Cancel();
                });

                try
                {
                    await pipeline.IngestDirectoryAsync(dataset, docs, progress, cts.Token);
                }
                catch (OperationCanceledException) { }
            });

            await task;
            var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            // Cancellation during storage should prevent or reduce chunk persistence
            True(chunks.Count < 500, "cancellation during storage should prevent or reduce chunk persistence");
        }

        public static async Task RagIngestClampsOversizedEmbeddingInputs()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "long.txt"), new string('a', 8000));

            var dataset = new RagDataset { Name = "long-embedding" };
            var strictEmbed = new MaxLengthEmbeddingService(maxChars: 700);
            var pipeline = new RagPipeline(store, strictEmbed);

            await pipeline.IngestDirectoryAsync(dataset, docs);

            var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: true);
            True(chunks.Count > 0, "ingest should store chunks even when source text is very long");
            True(chunks.All(c => c.Embedding.Length > 0), "all stored chunks should have embeddings");
            True(strictEmbed.FailedAttempts > 0, "ingest should retry when the embedding backend rejects an oversized input");
        }

        // ── 2.1 Embedding input clamp vs chunk size (r10 02-rag-quality.md) ──

        public static async Task RagEmbeddingInputIncludesFinalSentenceOfDefaultSizedChunkWithLongSourcePath()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor(string.Join('/', Enumerable.Range(0, 10).Select(i => $"very-long-directory-segment-number-{i}")));
            Directory.CreateDirectory(docs);

            const string finalSentence = "The final distinguishing sentence lives at the very end of this default-sized chunk.";
            var filler = string.Join(' ', Enumerable.Repeat("Filler prose about the RAG pipeline and its embedding behaviour during ingest.", 18));
            var content = filler + " " + finalSentence; // single paragraph => one chunk, no blank lines
            True(content.Length is > 1200 and < 1900, "fixture content should be close to the 1600-char default chunk target");

            await File.WriteAllTextAsync(Path.Combine(docs, "long.txt"), content);

            var dataset = new RagDataset { Name = "long-chunk-input" };
            var recording = new RecordingEmbeddingService();
            var pipeline = new RagPipeline(store, recording);
            await pipeline.IngestDirectoryAsync(dataset, docs);

            True(recording.Inputs.Count > 0, "ingest should have called the embedding service");
            True(recording.Inputs.Any(i => i.Contains(finalSentence, StringComparison.Ordinal)),
                "the embedding input for a default-sized chunk with a long source path must include its final sentence, not just the first part");
        }

        public static async Task RagChunkSizeGuardWarnsWhenTargetChunkCharsExceedsTheClamp()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            // 512-token clamp * 4 chars/token = 2048 chars; an oversized custom config should be flagged.
            var dataset = new RagDataset { Name = "oversized-chunks", Config = new RagDatasetConfig { TargetChunkChars = 4000 } };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var report = await pipeline.IngestDirectoryAsync(dataset, docs);

            True(report.Health is not null && report.Health.Warnings.Any(w => w.Contains("4000", StringComparison.Ordinal) && w.Contains("2048", StringComparison.Ordinal)),
                "an oversized chunk config should produce a health warning naming both the configured and clamp-implied sizes");
        }

        public static async Task RagRaisedEmbeddingClampImprovesRecallOnLongChunkFixture()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);

            const string marker = "zephyrion outpost quaranta xylophone";
            var filler = string.Join(' ', Enumerable.Repeat("The quarterly report covers general operational updates and routine status notes.", 18));
            var content = filler + $" The unique deployment codename for this cycle is {marker}, noted here at the very end.";
            True(content.Length is > 1200 and < 1900, "fixture content should be close to the 1600-char default chunk target");
            await File.WriteAllTextAsync(Path.Combine(docs, "long-chunk.txt"), content);

            for (var i = 0; i < 5; i++)
                await File.WriteAllTextAsync(Path.Combine(docs, $"other-{i}.txt"), $"Unrelated topic number {i} about weather and gardening rotations.");

            var dataset = new RagDataset { Name = "long-chunk-recall" };
            var embed = new HashingBagOfWordsEmbeddingService();
            var pipeline = new RagPipeline(store, embed);
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var query = new RagQueryService(store, embed, new FakeLlm(), settings, new NoOpReranker());
            var retrieval = await query.RetrieveAsync(dataset.Id, marker, new RagQueryOptions(TopK: 5));

            // Inspect SemanticCandidates specifically, not the BM25-fused Selected
            // list: BM25 matches the marker via literal keyword overlap against
            // the full (never-clamped) stored content regardless of this fix. And
            // with only 6 documents in this fixture, CosineScan's top-50 cutoff
            // never excludes anyone, so mere presence in SemanticCandidates is
            // not meaningful either: rank by score and require the marker chunk
            // to come out first, since every other chunk shares zero query terms
            // and can only score near it by coincidence.
            var rankedBySemanticScore = retrieval.SemanticCandidates.OrderByDescending(s => s.Score).ToList();
            True(rankedBySemanticScore.Count > 0 && rankedBySemanticScore[0].Chunk.Content.Contains(marker, StringComparison.Ordinal),
                "with the raised embedding clamp, the marker chunk should rank first by cosine score, since it is the only chunk whose embedded input actually contains the marker's distinctive tokens");
        }

        private sealed class RecordingEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
        {
            public List<string> Inputs { get; } = [];
            public int Dimensions => 4;

            public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            {
                Inputs.Add(text);
                return Task.FromResult(new[] { 1f, 0f, 0f, 0f });
            }

            public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            {
                Inputs.AddRange(texts);
                return Task.FromResult(texts.Select(_ => new[] { 1f, 0f, 0f, 0f }).ToList());
            }
        }

        /// <summary>
        /// A deterministic bag-of-words embedding: unlike FakeEmbeddingService (which
        /// hashes only text length), cosine similarity here genuinely reflects shared
        /// vocabulary, so it can prove whether a specific token was actually inside the
        /// (possibly clamped) embedding input rather than just the raw content length.
        /// </summary>
        private sealed class HashingBagOfWordsEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
        {
            // A wide bucket count keeps hash collisions between unrelated words
            // rare; presence (not frequency) per distinct word keeps a handful
            // of heavily repeated filler words from swamping the signal from
            // rare distinctive tokens that appear once.
            private const int Dims = 2048;
            public int Dimensions => Dims;

            public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Embed(text));

            public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
                Task.FromResult(texts.Select(Embed).ToList());

            private static float[] Embed(string text)
            {
                var vector = new float[Dims];
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+"))
                {
                    if (!seen.Add(m.Value)) continue;
                    var bucket = (int)((uint)m.Value.GetHashCode() % Dims);
                    vector[bucket] += 1f;
                }

                var norm = MathF.Sqrt(vector.Sum(v => v * v));
                if (norm > 0)
                    for (var i = 0; i < vector.Length; i++)
                        vector[i] /= norm;
                return vector;
            }
        }

        // ── 2.5 Lightweight dataset health projection (r10 02-rag-quality.md) ──

        public static async Task RagChunkHealthInfoMatchesIngestedSourcesWithoutLoadingContent()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");
            await File.WriteAllTextAsync(Path.Combine(docs, "b.txt"), "delta echo foxtrot");

            var dataset = new RagDataset { Name = "health-projection" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var info = await query.GetChunkHealthInfoForDatasetAsync(dataset.Id);
            var fullChunks = await query.GetChunksForDatasetAsync(dataset.Id, includeEmbeddings: false);

            Equal(fullChunks.Count, info.Count, "the lightweight projection should return one row per chunk, same as the full-chunk path");
            True(info.Any(i => i.SourcePath.EndsWith("a.txt", StringComparison.OrdinalIgnoreCase)), "the projection should carry the real source path");

            var healthFromProjection = RagDatasetHealthService.Compute(info);
            var healthFromFullChunks = RagDatasetHealthService.Compute(fullChunks);
            Equal(healthFromFullChunks.SourceCount, healthFromProjection.SourceCount, "health computed from the projection should match health computed from full chunks");
        }

        // ── 2.6 Eval harness gaps (r10 02-rag-quality.md) ────────────────────

        public static async Task RagEvalRetrievalModeShouldRefuseCasePassesOnEmptyDatasetAndFailsWhenAnswerable()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var evalPath = temp.PathFor("gold.json");
            var evalSet = new Hermaeus.Rag.Models.RagEvalSet
            {
                Name = "gold",
                Cases = [new Hermaeus.Rag.Models.RagEvalCase { Id = "refuse-1", Question = "anything at all", ShouldRefuse = true }]
            };
            await File.WriteAllTextAsync(evalPath, System.Text.Json.JsonSerializer.Serialize(evalSet));

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());

            // Empty dataset: the preflight gate has nothing to match, so it refuses.
            var emptyDataset = new RagDataset { Name = "empty-eval" };
            await store.SaveDatasetAsync(emptyDataset);
            var emptyRun = await eval.RunAsync(emptyDataset.Id, evalPath, fullAnswer: false);
            True(emptyRun.Results.Single().Passed,
                "a should_refuse case over an empty dataset should pass in retrieval-only mode (it used to hard-fail every should_refuse case)");

            // Answerable dataset: BM25 finds a literal term match, so the gate does not refuse.
            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "anything at all is answered right here in detail.");
            var answerableDataset = new RagDataset { Name = "answerable-eval" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(answerableDataset, docs);
            var answerableRun = await eval.RunAsync(answerableDataset.Id, evalPath, fullAnswer: false);
            False(answerableRun.Results.Single().Passed, "a should_refuse case over a dataset that actually answers it should fail");
        }

        public static async Task RagEvalCancellationStopsBetweenCasesAndSkipsExport()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var dataDir = temp.PathFor("data");
            settings.Settings.DataManagement.DataRootDirectory = dataDir;
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            var dataset = new RagDataset { Name = "cancel-eval" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var evalPath = temp.PathFor("gold.json");
            var evalSet = new Hermaeus.Rag.Models.RagEvalSet
            {
                Name = "gold",
                Cases = Enumerable.Range(0, 5).Select(i => new Hermaeus.Rag.Models.RagEvalCase { Id = $"case-{i}", Question = "apple" }).ToList()
            };
            await File.WriteAllTextAsync(evalPath, System.Text.Json.JsonSerializer.Serialize(evalSet));

            using var cts = new CancellationTokenSource();
            // Cancels synchronously from inside the 2nd case's embedding call, on
            // the same call stack as the eval loop, so the test is deterministic
            // rather than racing a Progress<T> callback dispatched elsewhere.
            var cancelingEmbed = new CancelAfterNCallsEmbeddingService(cts, cancelAfterCalls: 2);
            var query = new RagQueryService(store, cancelingEmbed, new FakeLlm(), settings, new NoOpReranker());
            var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());

            await ThrowsAsync<OperationCanceledException>(() => eval.RunAsync(dataset.Id, evalPath, fullAnswer: false, ct: cts.Token));

            var evalRunsDir = Path.Combine(dataDir, "eval-runs");
            True(!Directory.Exists(evalRunsDir) || Directory.GetDirectories(evalRunsDir).Length == 0,
                "a cancelled eval must not write any run export (export only happens on completion)");
        }

        private sealed class CancelAfterNCallsEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
        {
            private readonly CancellationTokenSource _cts;
            private readonly int _cancelAfterCalls;
            private int _calls;

            public CancelAfterNCallsEmbeddingService(CancellationTokenSource cts, int cancelAfterCalls)
            {
                _cts = cts;
                _cancelAfterCalls = cancelAfterCalls;
            }

            public int Dimensions => 4;

            public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            {
                _calls++;
                if (_calls >= _cancelAfterCalls)
                    _cts.Cancel();
                return Task.FromResult(new[] { 1f, 0f, 0f, 0f });
            }

            public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
                Task.FromResult(texts.Select(_ => new[] { 1f, 0f, 0f, 0f }).ToList());
        }

        // ── 2.2 Refusal preflight scoring (r10 02-rag-quality.md) ───────────

        public static async Task RagQueryDoesNotRefuseOnStrongSemanticMatchWithZeroTokenOverlap()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            // Corpus vocabulary shares zero words with the question below.
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"),
                "The voice output subsystem converts synthesized audio into speaker playback.");

            var dataset = new RagDataset { Name = "vocab-mismatch" };
            // Always returns the same vector: simulates a real embedding model
            // finding the question and the answer chunk semantically identical
            // (cosine 1.0) even though they share no vocabulary at all.
            var embed = new ConstantEmbeddingService();
            var pipeline = new RagPipeline(store, embed);
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var query = new RagQueryService(store, embed, new FakeLlm(), settings, new NoOpReranker());
            var sawSources = false;
            var sawRefusalToken = false;
            await foreach (var evt in query.StreamQueryAsync(dataset.Id, "how do I make it talk?", new RagQueryOptions(TopK: 3)))
            {
                if (evt.Kind == RagStreamEventKind.Sources) sawSources = true;
                if (evt.Kind == RagStreamEventKind.Token && evt.Text.Contains("do not have enough grounded context", StringComparison.OrdinalIgnoreCase))
                    sawRefusalToken = true;
            }

            True(sawSources, "a strong semantic match should still emit a sources event");
            False(sawRefusalToken,
                "a strong cosine match with zero token overlap between question and context must not be refused (the old preflight scored question/context overlap directly and would have refused this)");
        }

        public static async Task RagQueryRefusesOnEmptyDatasetAndEmitsSourcesAndReason()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var dataset = new RagDataset { Name = "empty" };
            await store.SaveDatasetAsync(dataset);

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());

            var sawSources = false;
            var sawRefusalToken = false;
            string? refusalReasonFromTrace = null;
            await foreach (var evt in query.StreamQueryAsync(dataset.Id, "anything at all", new RagQueryOptions(TopK: 3)))
            {
                switch (evt.Kind)
                {
                    case RagStreamEventKind.Sources: sawSources = true; break;
                    case RagStreamEventKind.Trace: refusalReasonFromTrace = evt.Trace!.RefusalReason; break;
                    case RagStreamEventKind.Token:
                        if (evt.Text.Contains("do not have enough grounded context", StringComparison.OrdinalIgnoreCase))
                            sawRefusalToken = true;
                        break;
                }
            }

            True(sawRefusalToken, "a dataset with no chunks should refuse");
            True(sawSources, "the sources event should still be emitted on refusal, even when empty");
            True(!string.IsNullOrWhiteSpace(refusalReasonFromTrace), "the refusal reason should be recorded in the trace");
        }

        private sealed class ConstantEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
        {
            public int Dimensions => 4;
            public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(new[] { 1f, 0f, 0f, 0f });
            public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
                Task.FromResult(texts.Select(_ => new[] { 1f, 0f, 0f, 0f }).ToList());
        }

        // ── 1.1 Parent-child retrieval (r10 01-rag-correctness.md) ──────────

        public static async Task RagParentChildRetrievalResolvesChildEmbeddingsToParentContent()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"),
                "apple banana carrot delta echo foxtrot golf hotel india juliet kilo lima mike november");

            var dataset = new RagDataset
            {
                Name = "parent-child",
                Config = new RagDatasetConfig { UseParentChild = true, TargetChunkChars = 20, ParentChunkChars = 200, OverlapChars = 0 }
            };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            // GetChunksAsync deliberately excludes is_parent rows (that's the fix under
            // test), so parent ids are read directly off the table instead.
            var parentIds = new HashSet<string>();
            await using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(temp.PathFor("data"), "conversations.db")}"))
            {
                await c.OpenAsync();
                var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT id FROM rag_chunks WHERE dataset_id = $ds AND is_parent = 1";
                cmd.Parameters.AddWithValue("$ds", dataset.Id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) parentIds.Add(r.GetString(0));
            }
            True(parentIds.Count > 0, "ingest with UseParentChild should have produced at least one parent chunk");

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var retrieval = await query.RetrieveAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 3, UseParentChild: true));

            True(retrieval.SemanticCandidates.Count > 0,
                "semantic candidates must be non-empty for a parent-child dataset: children are embedded, parents are not");
            True(retrieval.Selected.Count > 0, "selected context should be non-empty");
            True(retrieval.Selected.Any(s => s.Chunk.Content.Length > 20),
                "selected chunks should resolve to the larger parent content via UpgradeToParentsAsync, not the small child chunk");
            True(retrieval.Bm25Candidates.All(b => !parentIds.Contains(b.Chunk.Id)),
                "no parent body chunk should ever appear in the BM25 candidate list");
        }

        public static async Task RagParentChildMigrationBackfillsIsParentOnExistingRows()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var dataDir = temp.PathFor("data");
            Directory.CreateDirectory(dataDir);
            settings.Settings.DataManagement.DataRootDirectory = dataDir;
            var dbPath = Path.Combine(dataDir, "conversations.db");

            // Build a pre-fix database: rag_chunks has no is_parent column, and a
            // parent row is identified only by being the target of another row's parent_id.
            await using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await c.OpenAsync();
                var cmd = c.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE rag_datasets (id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE, description TEXT NOT NULL DEFAULT '', chunk_count INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, config_json TEXT NOT NULL DEFAULT '{}');
                    CREATE TABLE rag_chunks (id TEXT PRIMARY KEY, dataset_id TEXT NOT NULL, source_file TEXT NOT NULL, source_path TEXT NOT NULL DEFAULT '', source_hash TEXT NOT NULL DEFAULT '', source_modified_utc TEXT, source_title TEXT NOT NULL, content TEXT NOT NULL, chunk_index INTEGER NOT NULL DEFAULT 0, chunk_total INTEGER NOT NULL DEFAULT 1, parent_id TEXT, token_count INTEGER NOT NULL DEFAULT 0, embedding BLOB, created_at TEXT NOT NULL);
                    INSERT INTO rag_datasets (id, name, created_at) VALUES ('ds1', 'legacy', '2026-01-01T00:00:00Z');
                    INSERT INTO rag_chunks (id, dataset_id, source_file, source_title, content, created_at) VALUES ('parent1', 'ds1', 'a.txt', 'a', 'parent body text', '2026-01-01T00:00:00Z');
                    INSERT INTO rag_chunks (id, dataset_id, source_file, source_title, content, parent_id, created_at) VALUES ('child1', 'ds1', 'a.txt', 'a', 'child text', 'parent1', '2026-01-01T00:00:00Z');";
                await cmd.ExecuteNonQueryAsync();
            }

            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var chunks = await store.GetChunksAsync("ds1", includeEmbeddings: false);
            Equal(1, chunks.Count, "post-migration retrieval candidates should exclude the backfilled parent row");
            Equal("child1", chunks[0].Id, "the surviving candidate row should be the child, not the parent");

            var parent = await store.GetParentChunkAsync("parent1");
            True(parent is not null && parent.IsParent, "the backfilled parent row should be flagged is_parent");
        }

        // ── 1.2 Dataset delete must not leak chunk/BM25 rows (r10 01-rag-correctness.md) ──

        public static async Task RagDeleteDatasetRemovesAllChunkAndBm25Rows()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var dataDir = temp.PathFor("data");
            settings.Settings.DataManagement.DataRootDirectory = dataDir;
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            var dataset = new RagDataset { Name = "to-delete" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var chunksBefore = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            True(chunksBefore.Count > 0, "ingest should have stored chunks before delete");
            True(await store.GetBm25StatsAsync(dataset.Id) is not null, "ingest should have stored BM25 stats before delete");

            await store.DeleteDatasetAsync(dataset.Id);

            var dbPath = Path.Combine(dataDir, "conversations.db");
            await using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await c.OpenAsync();
            Equal(0L, await ScalarCountAsync(c, "rag_chunks", dataset.Id), "no rag_chunks rows should remain for a deleted dataset");
            Equal(0L, await ScalarCountAsync(c, "rag_bm25_stats", dataset.Id), "no rag_bm25_stats rows should remain for a deleted dataset");
        }

        public static async Task RagStoreInitializationCleansUpPreexistingOrphanRows()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var dataDir = temp.PathFor("data");
            Directory.CreateDirectory(dataDir);
            settings.Settings.DataManagement.DataRootDirectory = dataDir;
            var dbPath = Path.Combine(dataDir, "conversations.db");

            // Seed rows for a dataset that no longer has a rag_datasets row,
            // as if it survived a pre-fix DeleteDatasetAsync that relied on a
            // foreign-key pragma no connection ever actually enabled.
            await using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await c.OpenAsync();
                var cmd = c.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE rag_datasets (id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE, description TEXT NOT NULL DEFAULT '', chunk_count INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, config_json TEXT NOT NULL DEFAULT '{}');
                    CREATE TABLE rag_chunks (id TEXT PRIMARY KEY, dataset_id TEXT NOT NULL, source_file TEXT NOT NULL, source_path TEXT NOT NULL DEFAULT '', source_hash TEXT NOT NULL DEFAULT '', source_modified_utc TEXT, source_title TEXT NOT NULL, content TEXT NOT NULL, chunk_index INTEGER NOT NULL DEFAULT 0, chunk_total INTEGER NOT NULL DEFAULT 1, parent_id TEXT, token_count INTEGER NOT NULL DEFAULT 0, embedding BLOB, created_at TEXT NOT NULL);
                    CREATE TABLE rag_bm25_stats (dataset_id TEXT PRIMARY KEY, stats_json TEXT NOT NULL, updated_at TEXT NOT NULL);
                    INSERT INTO rag_chunks (id, dataset_id, source_file, source_title, content, created_at) VALUES ('orphan-chunk', 'gone-dataset', 'a.txt', 'a', 'orphaned content', '2026-01-01T00:00:00Z');
                    INSERT INTO rag_bm25_stats (dataset_id, stats_json, updated_at) VALUES ('gone-dataset', '{}', '2026-01-01T00:00:00Z');";
                await cmd.ExecuteNonQueryAsync();
            }

            var logs = new CollectingRuntimeLog();
            var store = new SqliteRagStore(settings, logs);
            await store.InitializeAsync();

            await using var verify = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await verify.OpenAsync();
            Equal(0L, await ScalarCountAsync(verify, "rag_chunks", "gone-dataset"), "orphaned chunk rows should be removed on initialization");
            Equal(0L, await ScalarCountAsync(verify, "rag_bm25_stats", "gone-dataset"), "orphaned BM25 stats rows should be removed on initialization");
            True(logs.Entries.Any(e => e.Message.Contains("orphaned", StringComparison.OrdinalIgnoreCase)),
                "cleanup should log the count of removed orphaned rows");
        }

        // ── 1.3 Re-ingest must clear the query cache (r10 01-rag-correctness.md) ──

        public static async Task RagReIngestClearsQueryCacheSoNewChunksAreRetrievableImmediately()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());
            var toasts = new FakeToasts();
            var logs = new RuntimeLogService(settings);

            var vm = new RagViewModel(query, pipeline, eval, toasts, logs, settings)
            {
                NewDatasetName = "reingest-test",
                IngestPath = docs,
                IngestDryRun = false
            };
            await vm.IngestCommand.ExecuteAsync(null);

            var dataset = (await query.GetDatasetsAsync()).First(d => d.Name == "reingest-test");

            // Warm the cache explicitly, as clicking "Warm cache" (or an earlier query) would.
            await query.WarmCacheAsync(dataset.Id);

            // Add a second document to the SAME dataset.
            await File.WriteAllTextAsync(Path.Combine(docs, "b.txt"), "zulu unique keyword marker");
            var item = new RagDatasetManagerItemViewModel(dataset, string.Empty);
            vm.AddToDatasetCommand.Execute(item);
            vm.IngestPath = docs; // AddToDataset pre-fills from LastIngestPath, which may not cover the new file yet.
            await vm.IngestCommand.ExecuteAsync(null);

            var retrieval = await query.RetrieveAsync(dataset.Id, "marker", new RagQueryOptions(TopK: 5));
            True(retrieval.Selected.Any(s => s.Chunk.Content.Contains("marker", StringComparison.OrdinalIgnoreCase)),
                "the newly ingested chunk should be retrievable immediately, without constructing a new RagQueryService");
        }

        // ── 1.7 LastIngestPath/LastIngestUtc persistence (r10 01-rag-correctness.md) ──

        public static async Task RagLastIngestPathAndUtcRoundTripThroughAFreshStoreInstance()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            // RagPipeline itself does not set LastIngestPath/LastIngestUtc; that's
            // RagIngestRequestBuilder's job (called by RagViewModel.IngestAsync
            // before the pipeline runs), so build the dataset the same way.
            var dataset = RagIngestRequestBuilder.PrepareDataset(
                existing: null, newDatasetName: "persist-ingest-path", enableWebLoader: false,
                ingestPath: docs, webUrlList: string.Empty, webMaxPages: 5,
                useParentChild: false, embeddingModel: "");
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var freshStore = new SqliteRagStore(settings);
            var reloaded = (await freshStore.GetDatasetsAsync()).First(d => d.Id == dataset.Id);

            Equal(docs, reloaded.LastIngestPath, "LastIngestPath should round-trip through a fresh store instance");
            True(reloaded.LastIngestUtc.HasValue, "LastIngestUtc should round-trip through a fresh store instance");
        }

        // ── 3.6 AddToDataset renamed-target trap (r12 03-runtime-vm-correctness.md) ──

        public static async Task RagAddToDatasetIngestsIntoARenamedTargetAsANewDatasetInstead()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());
            var vm = new RagViewModel(query, pipeline, eval, new FakeToasts(), new RuntimeLogService(settings), settings)
            {
                NewDatasetName = "dataset-a",
                IngestPath = docs
            };
            await vm.IngestCommand.ExecuteAsync(null);

            var datasetA = (await query.GetDatasetsAsync()).First(d => d.Name == "dataset-a");
            await vm.RefreshDatasetManagerCommand.ExecuteAsync(null);
            var item = vm.DatasetManagerItems.First(i => i.Id == datasetA.Id);

            vm.AddToDatasetCommand.Execute(item);
            Equal("dataset-a", vm.NewDatasetName, "AddToDataset should pre-fill the target's own name");

            // User edits the name box, intending to create a different dataset.
            vm.NewDatasetName = "dataset-b";
            await File.WriteAllTextAsync(Path.Combine(docs, "b.txt"), "zulu unique marker");
            await vm.IngestCommand.ExecuteAsync(null);

            var all = await query.GetDatasetsAsync();
            True(all.Any(d => d.Name == "dataset-b"), "editing the name after AddToDataset should create a new dataset");
            var reloadedA = all.First(d => d.Id == datasetA.Id);
            var chunksA = await store.GetChunksAsync(reloadedA.Id, includeEmbeddings: false);
            False(chunksA.Any(c => c.Content.Contains("zulu", StringComparison.OrdinalIgnoreCase)),
                "the original dataset must not have received the new document");
        }

        public static async Task RagAddToDatasetWithoutEditingTheNameStillIngestsIntoTheSameDataset()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());
            var vm = new RagViewModel(query, pipeline, eval, new FakeToasts(), new RuntimeLogService(settings), settings)
            {
                NewDatasetName = "dataset-a",
                IngestPath = docs
            };
            await vm.IngestCommand.ExecuteAsync(null);
            var datasetA = (await query.GetDatasetsAsync()).First(d => d.Name == "dataset-a");
            await vm.RefreshDatasetManagerCommand.ExecuteAsync(null);
            var item = vm.DatasetManagerItems.First(i => i.Id == datasetA.Id);

            vm.AddToDatasetCommand.Execute(item);
            await File.WriteAllTextAsync(Path.Combine(docs, "b.txt"), "zulu unique marker");
            await vm.IngestCommand.ExecuteAsync(null);

            var all = await query.GetDatasetsAsync();
            Equal(1, all.Count(d => d.Name == "dataset-a"), "unmodified AddToDataset must still land in the same dataset, not create a duplicate");
            var chunksA = await store.GetChunksAsync(datasetA.Id, includeEmbeddings: false);
            True(chunksA.Any(c => c.Content.Contains("zulu", StringComparison.OrdinalIgnoreCase)),
                "the new document should be ingested into the existing target dataset");
        }

        // ── 3.7 Reindex must not flip the recorded model before the pipeline commits (r12 03-runtime-vm-correctness.md) ──

        /// <summary>
        /// Cancels synchronously from inside the same call stack as the reindex
        /// loop's own embedding call, once armed. The original version of this
        /// test triggered the cancel from a PropertyChanged handler reacting to
        /// Progress&lt;T&gt;-marshaled VM state; Progress&lt;T&gt; always posts through
        /// the captured SynchronizationContext instead of running inline, so
        /// that cancel raced the reindex loop's own progress with no ordering
        /// guarantee - it landed reliably under Windows CI's scheduling but not
        /// Linux CI's, which is why this only failed on the ubuntu-latest leg.
        /// </summary>
        private sealed class CancelOnNextEmbedCallEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
        {
            private readonly Action _cancel;
            public bool Armed { get; set; }
            public int Dimensions => 4;

            public CancelOnNextEmbedCallEmbeddingService(Action cancel) => _cancel = cancel;

            public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            {
                Fire();
                return Task.FromResult(new[] { 1f, text.Length % 7, text.Length % 11, 0.5f });
            }

            public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            {
                Fire();
                return Task.FromResult(texts.Select(t => new[] { 1f, t.Length % 7, t.Length % 11, 0.5f }).ToList());
            }

            private void Fire()
            {
                if (!Armed) return;
                Armed = false;
                _cancel();
            }
        }

        public static async Task RagViewModelReindexCancelledMidRunLeavesDatasetOnTheOldModel()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            for (var i = 0; i < 20; i++)
                await File.WriteAllTextAsync(Path.Combine(docs, $"f{i}.txt"), string.Concat(Enumerable.Repeat($"keyword{i} ", 200)));

            RagViewModel? vmRef = null;
            var embedding = new CancelOnNextEmbedCallEmbeddingService(() => vmRef!.StopIngestCommand.Execute(null));
            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var pipeline = new RagPipeline(store, embedding);
            var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());
            var vm = new RagViewModel(query, pipeline, eval, new FakeToasts(), new RuntimeLogService(settings), settings)
            {
                NewDatasetName = "reindex-cancel-test",
                IngestPath = docs
            };
            vmRef = vm;
            await vm.IngestCommand.ExecuteAsync(null);
            var dataset = (await query.GetDatasetsAsync()).First(d => d.Name == "reindex-cancel-test");
            var originalModel = dataset.Config.EmbeddingModel;

            settings.Settings.Rag.EmbeddingModel = "new-model";
            await vm.RefreshDatasetManagerCommand.ExecuteAsync(null);
            var item = vm.DatasetManagerItems.First(i => i.Id == dataset.Id);
            True(item.ReindexRequired, "the dataset should need reindexing once the current embedding model changes");

            embedding.Armed = true;
            await vm.ReindexDatasetCommand.ExecuteAsync(item);

            var reloaded = (await query.GetDatasetsAsync()).First(d => d.Id == dataset.Id);
            Equal(originalModel, reloaded.Config.EmbeddingModel, "a cancelled reindex must leave the dataset reporting its old embedding model");
            await vm.RefreshDatasetManagerCommand.ExecuteAsync(null);
            var reloadedItem = vm.DatasetManagerItems.First(i => i.Id == dataset.Id);
            True(reloadedItem.ReindexRequired, "ReindexRequired must remain true after a cancelled reindex");
        }

        // ── 1.4 Embedding model mismatch guard + reindex (r10 01-rag-correctness.md) ──

        public static Task RagCosineScanIgnoresMismatchedEmbeddingLengths()
        {
            var query = new float[] { 1f, 0f, 0f, 0f };
            var matching = new RagChunk { Id = "match", Embedding = new float[] { 1f, 0f, 0f, 0f } };
            var mismatched = new RagChunk { Id = "mismatch", Embedding = new float[] { 1f, 0f, 0f } };

            var results = HybridRetriever.CosineScan(query, [matching, mismatched], topK: 10);

            Equal(1, results.Count, "CosineScan should silently drop candidates whose embedding length does not match the query's");
            Equal("match", results[0].Chunk.Id, "the matching-length chunk should be the only result");
            return Task.CompletedTask;
        }

        public static async Task RagRetrievalFallsBackToBm25OnlyWhenEmbeddingModelMismatches()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            settings.Settings.Rag.EmbeddingModel = "model-a";
            var dataset = new RagDataset { Name = "mismatch", Config = new RagDatasetConfig { EmbeddingModel = "model-a" } };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            // The current settings model changed after this dataset was embedded.
            settings.Settings.Rag.EmbeddingModel = "model-b";

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var retrieval = await query.RetrieveAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 5));

            Equal(0, retrieval.SemanticCandidates.Count, "semantic scan should be skipped entirely on an embedding model mismatch");
            True(retrieval.Selected.Count > 0, "BM25-only retrieval should still return results");
            True(retrieval.PlannerNotes.Contains("semantic search skipped", StringComparison.OrdinalIgnoreCase),
                "planner notes should explain why semantic search was skipped");
            True(retrieval.PlannerNotes.Contains("model-a", StringComparison.Ordinal) && retrieval.PlannerNotes.Contains("model-b", StringComparison.Ordinal),
                "planner notes should name both the dataset's and the current embedding model");
        }

        // ── r21 2.1: embedding-failure BM25 fallback ─────────────────────────

        public static async Task RagRetrievalFallsBackToBm25OnlyWhenEmbeddingServiceThrows()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            var dataset = new RagDataset { Name = "embed-down" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var logs = new CollectingRuntimeLog();
            var throwingEmbed = new ToggleFailEmbeddingService { ShouldThrow = true };
            var query = new RagQueryService(store, throwingEmbed, new FakeLlm(), settings, new NoOpReranker(), logs);
            var retrieval = await query.RetrieveAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 5));

            Equal(0, retrieval.SemanticCandidates.Count, "semantic scan should be skipped when the embedding call throws");
            True(retrieval.Selected.Count > 0, "BM25-only retrieval should still return results for a keyword-matching corpus");
            True(retrieval.PlannerNotes.Contains("semantic search unavailable", StringComparison.OrdinalIgnoreCase),
                "planner notes should explain that semantic search was unavailable");
            True(logs.Entries.Any(e => e.Level == Hermaeus.Core.Models.RuntimeLogLevel.Warning && e.Category == Hermaeus.Core.Models.RuntimeLogCategory.Rag),
                "an embedding failure should log exactly one Warning in the Rag category");
        }

        public static async Task RagRetrievalCancellationDuringEmbeddingIsNotSwallowedIntoFallback()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            var dataset = new RagDataset { Name = "embed-cancel" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var query = new RagQueryService(store, new CancellingEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            using var cts = new CancellationTokenSource();
            await ThrowsAsync<OperationCanceledException>(() => query.RetrieveAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 5), cts.Token));
        }

        public static async Task RagRetrievalRecoversToFullSemanticAfterATransientEmbeddingFailure()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            var dataset = new RagDataset { Name = "embed-recovers" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var toggling = new ToggleFailEmbeddingService { ShouldThrow = true };
            var query = new RagQueryService(store, toggling, new FakeLlm(), settings, new NoOpReranker());

            var failed = await query.RetrieveAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 5));
            Equal(0, failed.SemanticCandidates.Count, "the first query should have degraded to BM25-only");

            toggling.ShouldThrow = false;
            var recovered = await query.RetrieveAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 5));
            True(recovered.SemanticCandidates.Count > 0,
                "a later query on the same RagQueryService instance should be fully semantic again once the embedding server is back, proving no failure state is cached");
            False(recovered.PlannerNotes.Contains("semantic search unavailable", StringComparison.OrdinalIgnoreCase),
                "recovered planner notes should not still mention the earlier failure");
        }

        // ── r21 2.3: single-dataset read seam ─────────────────────────────────

        public static async Task RagGetDatasetAsyncReturnsSingleDatasetOrNullWhenAbsent()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var dataset = new RagDataset { Name = "single-read" };
            await store.SaveDatasetAsync(dataset);

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());

            var found = await query.GetDatasetAsync(dataset.Id);
            True(found is not null && found.Id == dataset.Id, "GetDatasetAsync should return the matching dataset");

            var missing = await query.GetDatasetAsync("does-not-exist");
            True(missing is null, "GetDatasetAsync should return null for an unknown dataset id");
        }

        private sealed class ToggleFailEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
        {
            public bool ShouldThrow { get; set; }
            public int Dimensions => 4;

            public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            {
                if (ShouldThrow)
                    throw new HttpRequestException("connection refused");
                return Task.FromResult(new[] { 1f, text.Length % 7, text.Length % 11, 0.5f });
            }

            public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
                Task.FromResult(texts.Select(t => new[] { 1f, t.Length % 7, t.Length % 11, 0.5f }).ToList());
        }

        private sealed class CancellingEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
        {
            public int Dimensions => 4;

            public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
                throw new OperationCanceledException(ct);

            public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
                Task.FromResult(texts.Select(t => new[] { 1f, t.Length % 7, t.Length % 11, 0.5f }).ToList());
        }

        public static async Task RagReindexReEmbedsChunksAndUpdatesConfig()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            var dataset = new RagDataset { Name = "reindex-me", Config = new RagDatasetConfig { EmbeddingModel = "old-model" } };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var before = await store.GetChunksAsync(dataset.Id, includeEmbeddings: true);
            True(before.Count > 0, "ingest should have stored chunks");

            dataset.Config.EmbeddingModel = "new-model";
            var newEmbed = new DistinctEmbeddingService();
            var reindexPipeline = new RagPipeline(store, newEmbed);
            var count = await reindexPipeline.ReindexDatasetAsync(dataset);

            Equal(before.Count, count, "reindex should re-embed every chunk");
            var after = await store.GetChunksAsync(dataset.Id, includeEmbeddings: true);
            True(after.All(c => c.Embedding.SequenceEqual(DistinctEmbeddingService.Vector)),
                "reindex should overwrite every chunk's embedding with the new model's output");
            True(after.All(c => c.Content.Length > 0), "reindex must work from stored chunk content only, and that content must survive unchanged");

            var reloaded = (await store.GetDatasetsAsync()).First(d => d.Id == dataset.Id);
            Equal("new-model", reloaded.Config.EmbeddingModel, "dataset config should record the new embedding model after reindex");

            settings.Settings.Rag.EmbeddingModel = "new-model";
            var query = new RagQueryService(store, newEmbed, new FakeLlm(), settings, new NoOpReranker());
            var retrieval = await query.RetrieveAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 5));
            True(retrieval.SemanticCandidates.Count > 0, "retrieval after reindex should find semantic candidates using the new vectors");
        }

        public static async Task RagIngestIntoMismatchedDatasetIsBlockedWithExplanation()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");

            settings.Settings.Rag.EmbeddingModel = "model-a";
            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());
            var toasts = new FakeToasts();
            var logs = new RuntimeLogService(settings);
            var vm = new RagViewModel(query, pipeline, eval, toasts, logs, settings)
            {
                NewDatasetName = "mismatch-vm",
                IngestPath = docs,
                IngestDryRun = false
            };
            await vm.IngestCommand.ExecuteAsync(null);
            var dataset = (await query.GetDatasetsAsync()).First(d => d.Name == "mismatch-vm");
            var chunksBefore = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);

            settings.Settings.Rag.EmbeddingModel = "model-b";
            var item = new RagDatasetManagerItemViewModel(dataset, "model-b");
            vm.AddToDatasetCommand.Execute(item);
            await File.WriteAllTextAsync(Path.Combine(docs, "b.txt"), "delta echo foxtrot");
            vm.IngestPath = docs;
            await vm.IngestCommand.ExecuteAsync(null);

            True(vm.IsError, "ingest into a mismatched-model dataset should be blocked");
            True(vm.StatusMessage.Contains("model-a", StringComparison.Ordinal) && vm.StatusMessage.Contains("model-b", StringComparison.Ordinal),
                "the block message should name both the dataset's and the current embedding model");

            var chunksAfter = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            Equal(chunksBefore.Count, chunksAfter.Count, "no new chunks should be added when the ingest is blocked");
        }

        // ── 1.5 Remove missing sources (r10 01-rag-correctness.md) ──────────

        public static async Task RagRemoveMissingSourcesDeletesChunksAndRebuildsStats()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            var keptPath = Path.Combine(docs, "kept.txt");
            var goingAwayPath = Path.Combine(docs, "going-away.txt");
            await File.WriteAllTextAsync(keptPath, "apple banana carrot");
            await File.WriteAllTextAsync(goingAwayPath, "delta echo foxtrot");

            var dataset = new RagDataset { Name = "missing-sources" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var before = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            True(before.Any(c => c.SourcePath == goingAwayPath), "the file about to be deleted should have chunks before removal");

            File.Delete(goingAwayPath);

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var remaining = await query.RemoveMissingSourcesAsync(dataset.Id, [goingAwayPath]);

            var after = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            Equal(remaining, after.Count, "returned remaining count should match the store");
            False(after.Any(c => c.SourcePath == goingAwayPath), "chunks for the removed source should be gone");
            True(after.Any(c => c.SourcePath == keptPath), "chunks for the still-present source should survive");

            var stats = await store.GetBm25StatsAsync(dataset.Id);
            True(stats is not null && stats.TotalDocuments == after.Count, "BM25 stats should be rebuilt to match the surviving chunks");

            var reloaded = (await store.GetDatasetsAsync()).First(d => d.Id == dataset.Id);
            Equal(after.Count, reloaded.ChunkCount, "dataset ChunkCount should be updated after removal");
        }

        public static async Task RagRemoveMissingSourcesIsBlockedWithoutConfirmation()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            var goingAwayPath = Path.Combine(docs, "going-away.txt");
            await File.WriteAllTextAsync(goingAwayPath, "delta echo foxtrot");

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());
            var toasts = new FakeToasts();
            var logs = new RuntimeLogService(settings);
            var vm = new RagViewModel(query, pipeline, eval, toasts, logs, settings)
            {
                NewDatasetName = "missing-sources-vm",
                IngestPath = docs,
                IngestDryRun = false
            };
            await vm.IngestCommand.ExecuteAsync(null);
            var dataset = (await query.GetDatasetsAsync()).First(d => d.Name == "missing-sources-vm");

            File.Delete(goingAwayPath);
            await vm.RefreshDatasetManagerCommand.ExecuteAsync(null);
            var item = vm.DatasetManagerItems.First(i => i.Id == dataset.Id);
            True(item.MissingFiles > 0, "health should report the deleted file as missing");

            // No RequestRemoveMissingSourcesConfirmation wired up: the command must not act.
            await vm.RemoveMissingSourcesCommand.ExecuteAsync(item);

            var after = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
            True(after.Any(c => c.SourcePath == goingAwayPath), "without confirmation, the missing source's chunks must not be removed");
        }

        private sealed class DistinctEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
        {
            public static readonly float[] Vector = [9f, 9f, 9f, 9f];
            public int Dimensions => 4;
            public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Vector);
            public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
                Task.FromResult(texts.Select(_ => Vector).ToList());
        }

        private static async Task<long> ScalarCountAsync(Microsoft.Data.Sqlite.SqliteConnection c, string table, string datasetId)
        {
            var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE dataset_id = $ds";
            cmd.Parameters.AddWithValue("$ds", datasetId);
            return (long)(await cmd.ExecuteScalarAsync())!;
        }

        private sealed class CollectingRuntimeLog : Hermaeus.Core.Services.IRuntimeLogService
        {
            public List<Hermaeus.Core.Models.RuntimeLogEntry> Entries { get; } = [];
            public event Action<Hermaeus.Core.Models.RuntimeLogEntry>? LogAdded;
            public void Add(Hermaeus.Core.Models.RuntimeLogEntry entry) { Entries.Add(entry); LogAdded?.Invoke(entry); }
            public IReadOnlyList<Hermaeus.Core.Models.RuntimeLogEntry> GetEntries() => Entries;
            public void ClearInMemory() => Entries.Clear();
            public string GetLogDirectory() => string.Empty;
            public string GetLogFilePath() => string.Empty;
        }

        private sealed class MaxLengthEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
        {
            private readonly int _maxChars;
            public int FailedAttempts { get; private set; }

            public MaxLengthEmbeddingService(int maxChars)
            {
                _maxChars = maxChars;
            }

            public int Dimensions => 4;

            public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            {
                if (text.Length > _maxChars)
                {
                    FailedAttempts++;
                    throw new InvalidOperationException($"input too large for test embedding service: {text.Length} chars");
                }

                return Task.FromResult(new[] { 1f, text.Length % 17, text.Length % 31, 0.25f });
            }

            public async Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            {
                var result = new List<float[]>(texts.Count);
                foreach (var text in texts)
                    result.Add(await EmbedAsync(text, ct));
                return result;
            }
        }

        private sealed class InlineProgress : IProgress<IngestProgress>
        {
            private readonly Action<IngestProgress> _handler;

            public InlineProgress(Action<IngestProgress> handler)
            {
                _handler = handler;
            }

            public void Report(IngestProgress value) => _handler(value);
        }
    }
}
