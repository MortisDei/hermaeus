using System.Net;
using System.Text;
using System.Text.Json;
using Hermaeus.Rag.Embeddings;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class EmbeddingRequestCoalescingTests
{
    [Fact]
    public async Task Concurrent_identical_query_embeddings_share_one_http_request()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Rag.EmbeddingBaseUrl = "http://embedding.test";
        using var handler = new BlockingEmbeddingHandler();
        using var http = new HttpClient(handler);
        using var service = new LlamaCppEmbeddingService(settings, http);

        var calls = Enumerable.Range(0, 8)
            .Select(_ => service.EmbedAsync("same query"))
            .ToArray();

        await WaitForRequestAsync(handler);
        handler.Release();
        var results = await Task.WhenAll(calls);

        Assert.Equal(1, handler.RequestCount);
        Assert.All(results, result => Assert.Equal(new[] { 1f, 2f, 3f }, result));
    }

    [Fact]
    public async Task Canceling_one_waiter_does_not_cancel_the_shared_request_for_other_waiters()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Rag.EmbeddingBaseUrl = "http://embedding.test";
        using var handler = new BlockingEmbeddingHandler();
        using var http = new HttpClient(handler);
        using var service = new LlamaCppEmbeddingService(settings, http);
        using var canceled = new CancellationTokenSource();

        var first = service.EmbedAsync("same query", canceled.Token);
        var second = service.EmbedAsync("same query");
        await WaitForRequestAsync(handler);

        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        handler.Release();

        Assert.Equal(new[] { 1f, 2f, 3f }, await second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Foreground_embedding_is_served_before_queued_background_backfill()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Rag.EmbeddingBaseUrl = "http://embedding.test";
        using var handler = new PriorityEmbeddingHandler();
        using var http = new HttpClient(handler);
        using var service = new LlamaCppEmbeddingService(settings, http);

        var firstBackground = service.EmbedBackgroundAsync("background-one");
        await WaitForAsync(() => handler.RequestCount == 1, "first background request");

        var secondBackground = service.EmbedBackgroundAsync("background-two");
        await Task.Delay(30);
        var foreground = service.EmbedAsync("foreground-query");

        handler.ReleaseFirst();
        await Task.WhenAll(firstBackground, secondBackground, foreground);

        Assert.Equal(
            ["background-one", "foreground-query", "background-two"],
            handler.Requests.ToArray());
    }

    private static async Task WaitForRequestAsync(BlockingEmbeddingHandler handler)
    {
        for (var i = 0; i < 100 && handler.RequestCount == 0; i++)
            await Task.Delay(10);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class BlockingEmbeddingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public void Release() => _release.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            await _release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[{\"index\":0,\"embedding\":[1,2,3]}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class PriorityEmbeddingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public List<string> Requests { get; } = [];

        public int RequestCount
        {
            get
            {
                lock (Requests)
                    return Requests.Count;
            }
        }

        public void ReleaseFirst() => _releaseFirst.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var input = JsonDocument.Parse(body).RootElement.GetProperty("input")[0].GetString()!;
            lock (Requests)
                Requests.Add(input);

            if (Interlocked.Increment(ref _requestCount) == 1)
                await _releaseFirst.Task.WaitAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[{\"index\":0,\"embedding\":[1,2,3]}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
