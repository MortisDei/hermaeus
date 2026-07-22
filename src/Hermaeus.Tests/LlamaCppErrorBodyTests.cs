using System.Net;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// A failed chat send used to surface only the bare HTTP status
/// (EnsureSuccessStatusCode's own message), discarding llama.cpp's actual
/// error body - the one part that says *why* it failed (e.g. a mismatched
/// --mmproj for the loaded model). The error message shown to the user must
/// include that detail instead.
/// </summary>
public sealed class LlamaCppErrorBodyTests
{
    private sealed class FixedStatusHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task StreamChatAsync_surfaces_the_response_body_on_a_server_error()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppBaseUrl = "http://127.0.0.1:39903";

        var body = """{"error":{"message":"mismatched mmproj for this model","type":"server_error"}}""";
        var logs = new RuntimeLogService(settings);
        var service = new LlamaCppService(settings, logs, new HttpClient(new FixedStatusHandler(HttpStatusCode.InternalServerError, body)));

        var events = new List<LlmStreamEvent>();
        await foreach (var evt in service.StreamChatAsync("model", [new ChatMessage("user", "hi")]))
            events.Add(evt);

        var error = Assert.Single(events);
        Assert.True(error.IsFinal);
        Assert.Contains("500", error.ContentDelta, StringComparison.Ordinal);
        Assert.Contains("mismatched mmproj for this model", error.ContentDelta, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamChatAsync_error_message_is_bounded_for_a_huge_response_body()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppBaseUrl = "http://127.0.0.1:39904";

        var hugeBody = new string('x', 5_000);
        var logs = new RuntimeLogService(settings);
        var service = new LlamaCppService(settings, logs, new HttpClient(new FixedStatusHandler(HttpStatusCode.InternalServerError, hugeBody)));

        var events = new List<LlmStreamEvent>();
        await foreach (var evt in service.StreamChatAsync("model", [new ChatMessage("user", "hi")]))
            events.Add(evt);

        var error = Assert.Single(events);
        True(error.ContentDelta.Length < hugeBody.Length, "an oversized error body should be truncated, not dumped whole into the chat message");
    }
}
