using System.Net;
using System.Text;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class OllamaStreamingTests
{
    /// <summary>r11 2.1: StreamChatAsync must yield the first token before the response finishes, not buffer the whole reply first.</summary>
    [Fact]
    public async Task StreamChatAsync_yields_the_first_event_before_the_response_stream_completes()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var profiles = new RuntimeProfileService(settings);
        await profiles.SaveAsync(new RuntimeProfile { Name = "Local Ollama", Kind = RuntimeKind.Ollama, BaseUrl = "http://127.0.0.1:11434", Enabled = true });
        var profile = profiles.Profiles.Single(p => p.Name == "Local Ollama");

        var ndjson =
            """{"message":{"content":"Hel"},"done":false}""" + "\n" +
            """{"message":{"content":"lo"},"done":false}""" + "\n" +
            """{"message":{"content":""},"done":true,"prompt_eval_count":3,"eval_count":2}""" + "\n";

        using var http = new HttpClient(new StreamingHandler(ndjson));
        var service = new OllamaService(profiles, http);

        var events = new List<LlmStreamEvent>();
        await foreach (var evt in service.StreamChatAsync($"ollama:{profile.Id}:llama3", [new ChatMessage("user", "hi")]))
            events.Add(evt);

        Assert.True(events.Count >= 3, "expected incremental content events plus a final usage event");
        Assert.Equal("Hel", events[0].ContentDelta);
        Assert.False(events[0].IsFinal);
        Assert.True(events[^1].IsFinal);
        Assert.NotNull(events[^1].Usage);
    }

    /// <summary>r11 2.1: an unreachable Ollama endpoint must surface as an in-stream error event, matching LlamaCppService, not throw out of the async iterator.</summary>
    [Fact]
    public async Task StreamChatAsync_yields_an_error_event_when_the_http_call_fails()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var profiles = new RuntimeProfileService(settings);
        await profiles.SaveAsync(new RuntimeProfile { Name = "Local Ollama", Kind = RuntimeKind.Ollama, BaseUrl = "http://127.0.0.1:11434", Enabled = true });
        var profile = profiles.Profiles.Single(p => p.Name == "Local Ollama");

        using var http = new HttpClient(new FailingHandler());
        var service = new OllamaService(profiles, http);

        var events = new List<LlmStreamEvent>();
        await foreach (var evt in service.StreamChatAsync($"ollama:{profile.Id}:llama3", [new ChatMessage("user", "hi")]))
            events.Add(evt);

        var single = Assert.Single(events);
        Assert.True(single.IsFinal);
        Assert.Contains("Ollama error", single.ContentDelta, StringComparison.Ordinal);
    }

    private sealed class StreamingHandler(string ndjsonBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(ndjsonBody)))
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
