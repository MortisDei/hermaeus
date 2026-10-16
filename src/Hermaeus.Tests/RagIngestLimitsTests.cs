using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Storage;
using Hermaeus.ViewModels;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class RagIngestLimitsTests
{
    [Fact]
    public async Task Pdf_above_approved_page_limit_is_refused()
    {
        using var temp = new TempDir();
        var file = temp.PathFor("many-pages.pdf");
        WritePdf(file, 2_001);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PdfTextExtractor.ExtractAsync(file));

        Assert.Contains("page", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public async Task Local_text_preserves_BOM_and_accepts_exact_byte_and_character_limits(string encodingName)
    {
        var encoding = encodingName switch
        {
            "utf8" => new UTF8Encoding(true),
            "utf16le" => (Encoding)new UnicodeEncoding(false, true),
            "utf16be" => new UnicodeEncoding(true, true),
            "utf32le" => new UTF32Encoding(false, true),
            _ => new UTF32Encoding(true, true)
        };
        const string text = "Alpha βeta\n🙂";
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        using var source = new MemoryStream(bytes);
        var limits = new RagIngestLimits(MaxSourceBytes: bytes.Length, MaxExtractedCharacters: text.Length);

        var actual = await RagPipeline.ReadLocalTextAsync(source, "synthetic", limits);

        Assert.Equal(text, actual);
        Assert.True(source.CanRead); // Caller still owns this stream.
    }

    [Fact]
    public async Task Local_character_limit_plus_one_is_refused_without_truncation()
    {
        using var source = new MemoryStream("12345"u8.ToArray());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RagPipeline.ReadLocalTextAsync(source, "synthetic", new RagIngestLimits(MaxExtractedCharacters: 4)));

        Assert.Contains("character", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task Local_source_growth_during_read_is_refused()
    {
        using var source = new GrowingStream();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RagPipeline.ReadLocalTextAsync(source, "growing", new RagIngestLimits(MaxSourceBytes: 64)));

        Assert.Contains("byte", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void False_length_does_not_allow_reading_past_the_source_extent_limit()
    {
        using var source = new FalseLengthStream(new byte[128]);
        using var bounded = new LimitedSourceStream(source, 64, "false length", leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => bounded.Read(new byte[128], 0, 128));

        Assert.Equal(65, source.Position);
    }

    [Fact]
    public void Seeking_past_the_source_limit_is_refused()
    {
        using var source = new MemoryStream(new byte[64]);
        using var bounded = new LimitedSourceStream(source, 64, "seek", leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => bounded.Seek(65, SeekOrigin.Begin));
    }

    [Fact]
    public async Task Cancelled_source_read_does_not_consume_input()
    {
        using var source = new MemoryStream("data"u8.ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RagPipeline.ReadLocalTextAsync(source, "cancelled", new RagIngestLimits(), cancellation.Token));

        Assert.Equal(0, source.Position);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task Local_read_failure_propagates_without_returning_partial_text()
    {
        using var source = new FailingStream();

        await Assert.ThrowsAsync<IOException>(() =>
            RagPipeline.ReadLocalTextAsync(source, "failure", new RagIngestLimits()));

        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task Pdf_page_and_character_limits_include_markers_and_preserve_exact_output()
    {
        using var temp = new TempDir();
        var file = temp.PathFor("pages.pdf");
        WritePdf(file, 2, "data");
        var expected = $"[Page 1]{Environment.NewLine}data{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}[Page 2]{Environment.NewLine}data";
        var limits = new RagIngestLimits(MaxSourceBytes: new FileInfo(file).Length,
            MaxExtractedCharacters: expected.Length, MaxPdfPages: 2);

        var extracted = await PdfTextExtractor.ExtractAsync(file, limits, null);

        Assert.Equal(expected, extracted.Text);
        Assert.Equal(2, extracted.PageCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PdfTextExtractor.ExtractAsync(file, limits with { MaxExtractedCharacters = expected.Length - 1 }, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PdfTextExtractor.ExtractAsync(file, limits with { MaxPdfPages = 1 }, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PdfTextExtractor.ExtractAsync(file, limits with { MaxSourceBytes = new FileInfo(file).Length - 1 }, null));
        // Every refusal must have released the original file handle.
        using var reopened = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(reopened.CanWrite);
    }

    [Fact]
    public async Task Caller_UTF8_budget_is_enforced_before_PDF_output_accumulation()
    {
        using var temp = new TempDir();
        var file = temp.PathFor("caller.pdf");
        WritePdf(file, 2, "data");
        var original = await PdfTextExtractor.ExtractAsync(file);
        var bytes = Encoding.UTF8.GetByteCount(original.Text);

        var exact = await PdfTextExtractor.ExtractAsync(file, new FileInfo(file).Length, bytes);

        Assert.Equal(original.Text, exact.Text);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PdfTextExtractor.ExtractAsync(file, new FileInfo(file).Length, bytes - 1));
    }

    [Theory]
    [InlineData(24, true)]
    [InlineData(23, false)]
    public async Task Candidate_counts_text_and_actual_vectors_once(long stagingBytes, bool accepted)
    {
        using var temp = new TempDir();
        var (store, dataset, file, previous) = await SeedAsync(temp);
        var pipeline = new RagPipeline(store, new ControlledEmbedding(), null,
            new RagIngestLimits(MaxStagingBytes: stagingBytes));

        if (accepted)
        {
            await pipeline.IngestDirectoryAsync(dataset, Path.GetDirectoryName(file)!);
            Assert.NotEqual(previous.GenerationId, Assert.Single(await store.GetChunksAsync(dataset.Id, false)).GenerationId);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.IngestDirectoryAsync(dataset, Path.GetDirectoryName(file)!));
            await AssertUnchangedAsync(store, dataset, previous);
        }
    }

    [Theory]
    [InlineData(48, true)]
    [InlineData(47, false)]
    public async Task Candidate_budget_includes_retained_sources(long stagingBytes, bool accepted)
    {
        using var temp = new TempDir();
        var (store, dataset, file, previous) = await SeedAsync(temp);
        var next = Path.Combine(Path.GetDirectoryName(file)!, "next.txt");
        await File.WriteAllTextAsync(next, "next");
        var pipeline = new RagPipeline(store, new ControlledEmbedding(), null,
            new RagIngestLimits(MaxStagingBytes: stagingBytes));

        if (accepted)
        {
            await pipeline.IngestDirectoryAsync(dataset, Path.GetDirectoryName(file)!, explicitFiles: [next]);
            Assert.Equal(2, (await store.GetStoredChunksAsync(dataset.Id, false)).Count);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.IngestDirectoryAsync(dataset, Path.GetDirectoryName(file)!, explicitFiles: [next]));
            await AssertUnchangedAsync(store, dataset, previous);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_local_character_refusal_preserves_generation_including_dry_run(bool dryRun)
    {
        using var temp = new TempDir();
        var (store, dataset, file, previous) = await SeedAsync(temp);
        await File.WriteAllTextAsync(file, "12345");
        var pipeline = new RagPipeline(store, new ControlledEmbedding(), null,
            new RagIngestLimits(MaxExtractedCharacters: 4));

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.IngestDirectoryAsync(
            dataset, Path.GetDirectoryName(file)!, options: new IngestOptions { DryRun = dryRun }));

        await AssertUnchangedAsync(store, dataset, previous);
    }

    [Fact]
    public async Task Source_growth_before_publication_is_rechecked_without_replacing_generation()
    {
        using var temp = new TempDir();
        var (store, dataset, file, previous) = await SeedAsync(temp);
        var embedding = new ControlledEmbedding(onEmbed: () => File.WriteAllText(file, new string('x', 65)));
        var pipeline = new RagPipeline(store, embedding, null, new RagIngestLimits(MaxSourceBytes: 64));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.IngestDirectoryAsync(dataset, Path.GetDirectoryName(file)!));

        await AssertUnchangedAsync(store, dataset, previous);
        using var reopened = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(reopened.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Staging_budget_is_not_reset_at_file_batch_boundaries(bool dryRun)
    {
        using var temp = new TempDir();
        var store = await NewStoreAsync(temp);
        var directory = temp.PathFor("many");
        Directory.CreateDirectory(directory);
        for (var i = 0; i < 51; i++)
            await File.WriteAllTextAsync(Path.Combine(directory, $"{i:D2}.txt"), "data");
        var embedding = new ControlledEmbedding();
        var bytesPerChunk = dryRun ? 8 : 24;
        var pipeline = new RagPipeline(store, embedding, null,
            new RagIngestLimits(MaxStagingBytes: 51 * bytesPerChunk - 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.IngestDirectoryAsync(
            new RagDataset { Name = "batch" }, directory, options: new IngestOptions { DryRun = dryRun }));

        Assert.Empty(await store.GetDatasetsAsync());
        Assert.Equal(dryRun ? 0 : 6, embedding.BatchCalls);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(31, false)]
    public async Task Staging_budget_counts_parent_content(long bytes, bool accepted)
    {
        using var temp = new TempDir();
        var store = await NewStoreAsync(temp);
        var directory = temp.PathFor("parents");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "data.txt"), "data");
        var dataset = new RagDataset { Name = "parents", Config = new RagDatasetConfig { UseParentChild = true } };
        var pipeline = new RagPipeline(store, new ControlledEmbedding(), null, new RagIngestLimits(MaxStagingBytes: bytes));

        if (accepted)
        {
            await pipeline.IngestDirectoryAsync(dataset, directory);
            Assert.Equal(2, (await store.GetStoredChunksAsync(dataset.Id, false)).Count);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.IngestDirectoryAsync(dataset, directory));
            Assert.Empty(await store.GetDatasetsAsync());
        }
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(39, false)]
    public async Task Reindex_accounts_actual_returned_dimensions_and_preserves_generation_on_refusal(long bytes, bool accepted)
    {
        using var temp = new TempDir();
        var (store, dataset, _, previous) = await SeedAsync(temp);
        var pipeline = new RagPipeline(store, new ControlledEmbedding(actualDimensions: 8), null,
            new RagIngestLimits(MaxStagingBytes: bytes));

        if (accepted)
        {
            await pipeline.ReindexDatasetAsync(dataset);
            Assert.Equal(8, Assert.Single(await store.GetChunksAsync(dataset.Id, true)).Embedding.Length);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ReindexDatasetAsync(dataset));
            await AssertUnchangedAsync(store, dataset, previous);
        }
    }

    [Theory]
    [InlineData(48, true)]
    [InlineData(47, false)]
    public async Task Web_candidate_budget_accumulates_across_pages(long bytes, bool accepted)
    {
        using var temp = new TempDir();
        var store = await NewStoreAsync(temp);
        var dataset = new RagDataset
        {
            Name = "web-budget",
            Config = new RagDatasetConfig { EnableWebLoader = true, ExtractionMode = RagExtractionMode.WebUrl,
                WebUrlList = "https://example.test/one\nhttps://example.test/two", WebMaxPages = 2 }
        };
        using var http = new HttpClient(new FakeHttpHandler("data"));
        var pipeline = new RagPipeline(store, new ControlledEmbedding(), http, new RagIngestLimits(MaxStagingBytes: bytes));

        if (accepted)
        {
            await pipeline.IngestWebAsync(dataset);
            Assert.Equal(2, (await store.GetChunksAsync(dataset.Id, false)).Count);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.IngestWebAsync(dataset));
            Assert.Empty(await store.GetDatasetsAsync());
        }
    }

    [Fact]
    public async Task PDF_attachment_uses_shared_page_limit_without_changing_its_byte_limits()
    {
        using var temp = new TempDir();
        var file = temp.PathFor("attachment.pdf");
        WritePdf(file, 2_001);

        var attachment = Assert.Single(await ChatContextAttachment.LoadFilesAsync([file]));

        Assert.Equal(ChatContextAttachmentStatus.Skipped, attachment.Status);
        Assert.Contains("2000", attachment.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(attachment.Content);
    }

    private static async Task<SqliteRagStore> NewStoreAsync(TempDir temp)
    {
        var store = new SqliteRagStore(NewSettings(temp));
        await store.InitializeAsync();
        return store;
    }

    private static async Task<(SqliteRagStore Store, RagDataset Dataset, string File, RagChunk Previous)> SeedAsync(TempDir temp)
    {
        var store = await NewStoreAsync(temp);
        var directory = temp.PathFor("docs");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "data.txt");
        await File.WriteAllTextAsync(file, "data");
        var dataset = new RagDataset { Name = "limits" };
        await new RagPipeline(store, new ControlledEmbedding()).IngestDirectoryAsync(dataset, directory);
        return (store, dataset, file, Assert.Single(await store.GetChunksAsync(dataset.Id, false)));
    }

    private static async Task AssertUnchangedAsync(SqliteRagStore store, RagDataset dataset, RagChunk previous)
    {
        var current = Assert.Single(await store.GetChunksAsync(dataset.Id, false));
        Assert.Equal(previous.GenerationId, current.GenerationId);
        Assert.Equal(previous.SourceRevisionId, current.SourceRevisionId);
        Assert.Equal(previous.Content, current.Content);
        Assert.Single(await store.GetGenerationHistoryAsync(dataset.Id));
    }

    private sealed class ControlledEmbedding(int actualDimensions = 4, Action? onEmbed = null) : IEmbeddingService
    {
        public int Dimensions => 4; // A default/last observation is not the actual response.
        public int BatchCalls { get; private set; }
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Vector());
        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            BatchCalls++;
            onEmbed?.Invoke();
            return Task.FromResult(texts.Select(_ => Vector()).ToList());
        }
        private float[] Vector() => Enumerable.Repeat(1f, actualDimensions).ToArray();
    }

    private sealed class GrowingStream : MemoryStream
    {
        public GrowingStream() { Write("data"u8); Position = 0; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            SetLength(65);
            return read;
        }
    }

    private sealed class FalseLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override long Length => 1;
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Controlled local read failure."));
    }

    private static void WritePdf(string path, int pages, string text = "")
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < pages; i++)
        {
            var page = builder.AddPage(300, 144);
            if (text.Length > 0)
                page.AddText(text, 12, new PdfPoint(20, 100), font);
        }
        File.WriteAllBytes(path, builder.Build());
    }
}
