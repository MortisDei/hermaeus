using System.Net;
using System.Text;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class CompositeLlmRoutingTests
{
    /// <summary>r11 2.4: a model id's provider tag, once learned, must survive a later scan that doesn't see that provider again (the old clear-then-rebuild wiped it, silently misrouting the next send to llama.cpp).</summary>
    [Fact]
    public async Task StreamChatAsync_still_routes_to_the_learned_provider_after_a_later_scan_does_not_see_it()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppEnabled = true;
        settings.Settings.Llm.LlamaCppBaseUrl = "http://127.0.0.1:19999";
        settings.Settings.Llm.OpenAiEnabled = true;
        settings.Settings.Llm.OpenAiApiKey = "plain-key";
        settings.Settings.Llm.OpenAiBaseUrl = "https://api.example.test";

        // First scan: llama.cpp empty, OpenAI reports gpt-4o. This learns gpt-4o -> "openai".
        var llamaHandler = new QueueHandler();
        llamaHandler.EnqueueJson(HttpStatusCode.OK, """{"data":[]}""");
        var openAiHandler = new QueueHandler();
        openAiHandler.EnqueueJson(HttpStatusCode.OK, """{"data":[{"id":"gpt-4o"}]}""");

        using var llamaHttp = new HttpClient(llamaHandler);
        using var openAiHttp = new HttpClient(openAiHandler);
        var logs = new RuntimeLogService(settings);
        var llamaCpp = new LlamaCppService(settings, logs, llamaHttp);
        var openAi = new OpenAiService(settings, new PassthroughSecretStore(), openAiHttp);
        var profiles = new RuntimeProfileService(settings);
        var ollama = new OllamaService(profiles, new HttpClient(new ThrowingHandler()));
        var composite = new CompositeLlmService(llamaCpp, openAi, ollama, settings, profiles);

        var firstScan = await composite.GetModelsAsync();
        Assert.Contains(firstScan, m => m.Id == "gpt-4o");

        // Second scan: OpenAI now reports nothing (rate-limited, cache expired, etc).
        // Under the old clear-then-rebuild scheme this would erase the learned tag.
        composite.InvalidateModelCache();
        llamaHandler.EnqueueJson(HttpStatusCode.OK, """{"data":[]}""");
        openAiHandler.EnqueueJson(HttpStatusCode.OK, """{"data":[]}""");
        var secondScan = await composite.GetModelsAsync();
        Assert.DoesNotContain(secondScan, m => m.Id == "gpt-4o");

        // gpt-4o must still route to OpenAI, not fall back to llama.cpp.
        openAiHandler.EnqueueSse("data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\ndata: [DONE]\n\n");
        var events = new List<LlmStreamEvent>();
        await foreach (var evt in composite.StreamChatAsync("gpt-4o", [new ChatMessage("user", "hi")]))
            events.Add(evt);

        Assert.Contains(events, e => e.ContentDelta == "hi");
        Assert.Equal(0, llamaHandler.RemainingChatRequests);
    }

    /// <summary>r11 2.4: a model id that was never seen in any scan must yield an explicit error event rather than being silently posted to llama.cpp.</summary>
    [Fact]
    public async Task StreamChatAsync_yields_an_error_for_a_never_seen_model_id_instead_of_posting_to_llama_cpp()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppEnabled = true;

        var llamaHandler = new ThrowingHandler();
        var llamaCpp = new LlamaCppService(settings, new RuntimeLogService(settings), new HttpClient(llamaHandler));
        var openAi = new OpenAiService(settings, new PassthroughSecretStore(), new HttpClient(new ThrowingHandler()));
        var profiles = new RuntimeProfileService(settings);
        var ollama = new OllamaService(profiles, new HttpClient(new ThrowingHandler()));
        var composite = new CompositeLlmService(llamaCpp, openAi, ollama, settings, profiles);

        var events = new List<LlmStreamEvent>();
        await foreach (var evt in composite.StreamChatAsync("saved-remote-model-id", [new ChatMessage("user", "hi")]))
            events.Add(evt);

        var single = Assert.Single(events);
        Assert.True(single.IsFinal);
        Assert.Contains("Could not determine which provider", single.ContentDelta, StringComparison.Ordinal);
    }

    /// <summary>r11 2.4: when every provider comes back empty, the empty result is itself cached briefly so repeated calls don't turn into a fresh probe every time.</summary>
    [Fact]
    public async Task GetModelsAsync_does_not_reprobe_every_call_when_all_providers_are_down()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppEnabled = true;
        settings.Settings.Llm.OpenAiEnabled = false;

        var llamaHandler = new QueueHandler();
        llamaHandler.EnqueueJson(HttpStatusCode.OK, """{"data":[]}""");

        var llamaCpp = new LlamaCppService(settings, new RuntimeLogService(settings), new HttpClient(llamaHandler));
        var openAi = new OpenAiService(settings, new PassthroughSecretStore(), new HttpClient(new ThrowingHandler()));
        var profiles = new RuntimeProfileService(settings);
        var ollama = new OllamaService(profiles, new HttpClient(new ThrowingHandler()));
        var composite = new CompositeLlmService(llamaCpp, openAi, ollama, settings, profiles);

        var first = await composite.GetModelsAsync();
        var second = await composite.GetModelsAsync();

        Assert.Empty(first);
        Assert.Empty(second);
        Assert.Equal(1, llamaHandler.TotalRequests);
    }

    [Fact]
    public async Task Llama_models_retain_local_gguf_size_and_mtime_evidence()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppBaseUrl = "http://127.0.0.1:19999";
        var modelPath = temp.PathFor("model.gguf");
        await File.WriteAllBytesAsync(modelPath, [1, 2, 3, 4]);

        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = new[] { new { id = modelPath } } }));
        handler.EnqueueJson(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler);
        using var service = new LlamaCppService(settings, new RuntimeLogService(settings), http);

        var model = Assert.Single(await service.GetModelsAsync());

        Assert.Equal(4, model.SizeBytes);
        Assert.Equal(File.GetLastWriteTimeUtc(modelPath), model.ModifiedAt);
    }

    private sealed class PassthroughSecretStore : ISecretStore
    {
        public bool IsReference(string value) => false;
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) => Task.FromResult(secret);
        public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) => Task.FromResult(valueOrReference);
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("passthrough");
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected HTTP call to {request.RequestUri} - this provider must not be reached for this scenario.");
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public int TotalRequests { get; private set; }
        public int RemainingChatRequests => _responses.Count;

        public void EnqueueJson(HttpStatusCode status, string json) =>
            _responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

        public void EnqueueSse(string body) =>
            _responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TotalRequests++;
            if (_responses.Count == 0)
                throw new InvalidOperationException($"No queued response for {request.RequestUri}");

            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
