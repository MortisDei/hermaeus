using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>r10 03-field-follow-ups.md 3.2: llama-server's "timings" object on the final streamed chunk.</summary>
public sealed class LlamaCppServiceTimingsTests
{
    [Fact]
    public void ParseStreamEvent_captures_server_timings_from_final_chunk()
    {
        const string json = """
            {"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":17,"completion_tokens":229,"total_tokens":246},"timings":{"prompt_n":17,"prompt_ms":120.5,"predicted_n":229,"predicted_ms":8000.2}}
            """;

        var evt = LlamaCppService.ParseStreamEvent(json);

        Assert.NotNull(evt);
        Assert.NotNull(evt!.ServerTimings);
        Assert.Equal(17, evt.ServerTimings!.PromptTokens);
        Assert.Equal(120.5, evt.ServerTimings.PromptMs);
        Assert.Equal(229, evt.ServerTimings.PredictedTokens);
        Assert.Equal(8000.2, evt.ServerTimings.PredictedMs);
    }

    [Fact]
    public void ParseStreamEvent_tolerates_absent_timings()
    {
        const string json = """{"choices":[{"delta":{"content":"hi"}}]}""";

        var evt = LlamaCppService.ParseStreamEvent(json);

        Assert.NotNull(evt);
        Assert.Null(evt!.ServerTimings);
        Assert.Equal("hi", evt.ContentDelta);
    }

    // ── r19 1.2: max-token truncation must be visible, not silently discarded ──

    [Fact]
    public void ParseStreamEvent_reports_length_finish_reason()
    {
        const string json = """{"choices":[{"delta":{},"finish_reason":"length"}],"usage":{"prompt_tokens":10,"completion_tokens":4096,"total_tokens":4106}}""";

        var evt = LlamaCppService.ParseStreamEvent(json);

        Assert.NotNull(evt);
        Assert.Equal("length", evt!.FinishReason);
    }

    [Fact]
    public void ParseStreamEvent_reports_stop_finish_reason_distinctly()
    {
        const string json = """{"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":20,"total_tokens":30}}""";

        var evt = LlamaCppService.ParseStreamEvent(json);

        Assert.NotNull(evt);
        Assert.Equal("stop", evt!.FinishReason);
    }
}
