using System.Net;
using System.Net.Http.Headers;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Storage;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class RagWebIngestBoundsTests
{
    private const int WebByteLimit = 2 * 1024 * 1024;

    [Theory]
    [InlineData(-1)]
    [InlineData(WebByteLimit)]
    public async Task Exact_existing_web_limit_is_accepted_without_truncating_text(long declaredLength)
    {
        using var temp = new TempDir();
        var store = await NewStoreAsync(temp);
        var dataset = NewDataset();
        using var body = new ControlledBodyStream(WebByteLimit);
        using var http = NewHttp(body, declaredLength);

        await new RagPipeline(store, new FakeEmbeddingService(), http).IngestWebAsync(dataset);

        Assert.Equal(WebByteLimit, body.BytesRead);
        Assert.True(body.Disposed);
        var chunk = Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));
        Assert.Equal("Complete source text.", chunk.Content);
        Assert.Single(await store.GetGenerationHistoryAsync(dataset.Id));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(WebByteLimit, false)]
    [InlineData(-1, true)]
    public async Task Oversized_actual_body_reads_at_most_limit_plus_one_and_preserves_generation(
        long declaredLength, bool dryRun)
    {
        using var temp = new TempDir();
        var (store, dataset, previous) = await SeedAsync(temp);
        using var body = new ControlledBodyStream(WebByteLimit + 32_768);
        using var http = NewHttp(body, declaredLength);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RagPipeline(store, new FakeEmbeddingService(), http).IngestWebAsync(
                dataset, options: new IngestOptions { DryRun = dryRun }));

        Assert.Contains("too large", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WebByteLimit + 1, body.BytesRead);
        Assert.True(body.Disposed);
        await AssertUnchangedAsync(store, dataset, previous);
    }

    [Fact]
    public async Task Unending_body_is_refused_after_limit_plus_one_instead_of_waiting_for_eof()
    {
        using var temp = new TempDir();
        var (store, dataset, previous) = await SeedAsync(temp);
        // The guard turns an unbounded implementation into a deterministic failure,
        // without allocating an unlimited fixture or hanging the suite.
        using var body = new ControlledBodyStream(long.MaxValue, guardAfter: WebByteLimit + 1);
        using var http = NewHttp(body);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RagPipeline(store, new FakeEmbeddingService(), http).IngestWebAsync(dataset));

        Assert.Equal(WebByteLimit + 1, body.BytesRead);
        Assert.True(body.Disposed);
        await AssertUnchangedAsync(store, dataset, previous);
    }

    [Fact]
    public async Task Oversized_content_length_is_refused_before_reading_the_body()
    {
        using var temp = new TempDir();
        var (store, dataset, previous) = await SeedAsync(temp);
        using var body = new ControlledBodyStream(WebByteLimit + 1);
        using var http = NewHttp(body, WebByteLimit + 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RagPipeline(store, new FakeEmbeddingService(), http).IngestWebAsync(dataset));

        Assert.Equal(0, body.BytesRead);
        Assert.True(body.Disposed);
        await AssertUnchangedAsync(store, dataset, previous);
    }

    [Fact]
    public async Task Cancellation_during_body_read_disposes_stream_and_preserves_generation()
    {
        using var temp = new TempDir();
        var (store, dataset, previous) = await SeedAsync(temp);
        using var body = new ControlledBodyStream(128, waitAfterEnd: true);
        using var http = NewHttp(body);
        using var cancellation = new CancellationTokenSource();

        var ingest = new RagPipeline(store, new FakeEmbeddingService(), http)
            .IngestWebAsync(dataset, ct: cancellation.Token);
        await body.WaitingForCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ingest);

        Assert.Equal(128, body.BytesRead);
        Assert.True(body.Disposed);
        await AssertUnchangedAsync(store, dataset, previous);
    }

    [Fact]
    public async Task Body_read_failure_disposes_stream_and_preserves_generation()
    {
        using var temp = new TempDir();
        var (store, dataset, previous) = await SeedAsync(temp);
        using var body = new ControlledBodyStream(128, failAfterEnd: true);
        using var http = NewHttp(body);

        await Assert.ThrowsAsync<IOException>(() =>
            new RagPipeline(store, new FakeEmbeddingService(), http).IngestWebAsync(dataset));

        Assert.Equal(128, body.BytesRead);
        Assert.True(body.Disposed);
        await AssertUnchangedAsync(store, dataset, previous);
    }

    private static async Task<SqliteRagStore> NewStoreAsync(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        return store;
    }

    private static RagDataset NewDataset() => new()
    {
        Name = "web-bounds",
        Config = new RagDatasetConfig
        {
            EnableWebLoader = true,
            ExtractionMode = RagExtractionMode.WebUrl,
            WebUrlList = "https://example.test/document",
            WebMaxPages = 1
        }
    };

    private static async Task<(SqliteRagStore Store, RagDataset Dataset, RagChunk Previous)> SeedAsync(TempDir temp)
    {
        var store = await NewStoreAsync(temp);
        var dataset = NewDataset();
        using var http = new HttpClient(new FakeHttpHandler("Original published source."));
        await new RagPipeline(store, new FakeEmbeddingService(), http).IngestWebAsync(dataset);
        return (store, dataset, Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false)));
    }

    private static async Task AssertUnchangedAsync(SqliteRagStore store, RagDataset dataset, RagChunk previous)
    {
        var current = Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));
        Assert.Equal(previous.GenerationId, current.GenerationId);
        Assert.Equal(previous.SourceRevisionId, current.SourceRevisionId);
        Assert.Equal(previous.Content, current.Content);
        Assert.Equal(previous.GenerationId, Assert.Single(await store.GetDatasetsAsync()).CurrentGenerationId);
        Assert.Single(await store.GetGenerationHistoryAsync(dataset.Id));
    }

    private static HttpClient NewHttp(Stream body, long declaredLength = -1) =>
        new(new BodyHandler(body, declaredLength));

    private sealed class BodyHandler(Stream body, long declaredLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new StreamContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            if (declaredLength >= 0)
                content.Headers.ContentLength = declaredLength;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class ControlledBodyStream(
        long length,
        long guardAfter = long.MaxValue,
        bool waitAfterEnd = false,
        bool failAfterEnd = false) : Stream
    {
        private static readonly byte[] Prefix = "Complete source text."u8.ToArray();
        public long BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource WaitingForCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (BytesRead >= guardAfter)
                throw new IOException("Fixture detected a read beyond the permitted limit plus one.");
            if (BytesRead >= length)
            {
                if (failAfterEnd)
                    throw new IOException("Controlled body read failure.");
                if (waitAfterEnd)
                {
                    WaitingForCancellation.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                return 0;
            }

            var count = (int)Math.Min(buffer.Length, Math.Min(length - BytesRead, guardAfter - BytesRead));
            buffer.Span[..count].Fill((byte)' ');
            for (var i = 0; i < count && BytesRead + i < Prefix.Length; i++)
                buffer.Span[i] = Prefix[BytesRead + i];
            BytesRead += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
