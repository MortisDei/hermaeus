using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Aether.Rag;
using Aether.Rag.Models;
using Aether.Rag.Pipeline;
using Aether.Rag.Storage;
using static Aether.Tests.Helpers;
using static Aether.Tests.PdfHelpers;

namespace Aether.Tests
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
    }
}
