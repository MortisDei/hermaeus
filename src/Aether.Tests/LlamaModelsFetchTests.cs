using Aether.Core.Services;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// llama.cpp model discovery hygiene: an explicit prompt cache in the request
/// body, and quiet handling of a stopped managed server (state gating plus
/// once-per-down-state log coalescing) so it never spams the runtime log.
/// </summary>
public sealed class LlamaModelsFetchTests
{
    [Fact]
    public void BuildChatPayload_sets_cache_prompt_true_in_snake_case()
    {
        var payload = LlamaCppService.BuildChatPayload("m", [], LlmChatOptions.Default, 256);
        var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
        });
        Assert.Contains("\"cache_prompt\":true", json);
    }

    /// <summary>r17 02-benchmark-truth.md 2.6: DisablePromptCache (benchmark-only in practice)
    /// must flip cache_prompt off; the chat path never sets it, so the default above stays true.</summary>
    [Fact]
    public void BuildChatPayload_sets_cache_prompt_false_when_disable_prompt_cache_is_set()
    {
        var options = LlmChatOptions.Default with { DisablePromptCache = true };
        var payload = LlamaCppService.BuildChatPayload("m", [], options, 256);
        var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
        });
        Assert.Contains("\"cache_prompt\":false", json);
    }

    [Fact]
    public async Task GetModelsAsync_logs_once_per_down_state_not_per_call()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppBaseUrl = "http://127.0.0.1:39901"; // unique, never listening

        var logs = new RuntimeLogService(settings);
        var service = new LlamaCppService(settings, logs, new HttpClient(new AlwaysFailsHandler()));

        for (var i = 0; i < 5; i++)
            Assert.Empty(await service.GetModelsAsync());

        var lines = logs.GetEntries().Count(e => e.Message.Contains("models unavailable", StringComparison.Ordinal));
        Assert.Equal(1, lines);
    }

    [Fact]
    public async Task GetModelsAsync_skips_http_and_log_when_server_known_stopped()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.LlamaCppBaseUrl = "http://127.0.0.1:39902";

        var logs = new RuntimeLogService(settings);
        var handler = new AlwaysFailsHandler();
        var service = new LlamaCppService(settings, logs, new HttpClient(handler))
        {
            IsBaseUrlKnownStopped = _ => true
        };

        Assert.Empty(await service.GetModelsAsync());
        Assert.Equal(0, handler.Calls);
        Assert.DoesNotContain(logs.GetEntries(), e => e.Message.Contains("models unavailable", StringComparison.Ordinal));
    }

    private sealed class AlwaysFailsHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
        }
    }
}
