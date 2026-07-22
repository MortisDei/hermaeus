using System.Net;
using System.Text;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class LlamaCppContextLengthTests
{
    /// <summary>r11 2.5: the probed context length must be re-fetched when the hosted model changes, not served forever from a baseUrl-only cache. Restarting the managed server with a different model keeps the same 127.0.0.1:port, so a stale cache silently fed the previous model's window into token-budget math.</summary>
    [Fact]
    public async Task GetModelsAsync_reprobes_context_length_when_the_hosted_model_id_changes()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppBaseUrl = "http://127.0.0.1:28080";

        var handler = new QueueingHandler();
        // First model hosted: "model-a", n_ctx 4096.
        handler.Enqueue(HttpStatusCode.OK, """{"data":[{"id":"model-a"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"n_ctx":4096}""");

        var logs = new RuntimeLogService(settings);
        var service = new LlamaCppService(settings, logs, new HttpClient(handler));

        var first = await service.GetModelsAsync();
        Assert.Equal(4096, first.Single().ProbedContextLength);

        // Server restarted with a different model at the same base URL, larger context.
        handler.Enqueue(HttpStatusCode.OK, """{"data":[{"id":"model-b"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"n_ctx":8192}""");

        var second = await service.GetModelsAsync();
        Assert.Equal(8192, second.Single().ProbedContextLength);
    }

    /// <summary>Re-probing the same model id a second time must still hit the cache and skip the /props call.</summary>
    [Fact]
    public async Task GetModelsAsync_reuses_the_cached_context_length_for_the_same_model_id()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppBaseUrl = "http://127.0.0.1:28081";

        var handler = new QueueingHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"data":[{"id":"model-a"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"n_ctx":4096}""");
        handler.Enqueue(HttpStatusCode.OK, """{"data":[{"id":"model-a"}]}"""); // second /v1/models call, same model id

        var service = new LlamaCppService(settings, new RuntimeLogService(settings), new HttpClient(handler));

        var first = await service.GetModelsAsync();
        var second = await service.GetModelsAsync();

        Assert.Equal(4096, first.Single().ProbedContextLength);
        Assert.Equal(4096, second.Single().ProbedContextLength);
        Assert.Equal(0, handler.Remaining);
    }

    private sealed class QueueingHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();
        public int Remaining => _responses.Count;

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException($"No queued response for {request.RequestUri}");

            var (status, body) = _responses.Dequeue();
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(response);
        }
    }
}
