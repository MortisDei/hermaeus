using Hermaeus.Core.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ChatContextUsageCalculatorTests
{
    [Fact]
    public void ResolveContextWindowLimit_prefers_the_model_override()
    {
        var limit = ChatContextUsageCalculator.ResolveContextWindowLimit(8192, 4096, 2048);
        Assert.Equal(8192, limit);
    }

    [Fact]
    public void ResolveContextWindowLimit_falls_back_to_the_managed_server_then_settings()
    {
        Assert.Equal(4096, ChatContextUsageCalculator.ResolveContextWindowLimit(null, 4096, 2048));
        Assert.Equal(2048, ChatContextUsageCalculator.ResolveContextWindowLimit(null, null, 2048));
    }

    [Theory]
    [InlineData(500, 1000, "None")]
    [InlineData(850, 1000, "Warning")]
    [InlineData(960, 1000, "Critical")]
    public void Compute_classifies_warning_level_from_percent_used(int totalTokens, int limit, string expectedLevel)
    {
        var usage = new ChatTokenUsage(totalTokens, 0, totalTokens);
        var result = ChatContextUsageCalculator.Compute(usage, limit, "Estimated");

        Assert.Equal(expectedLevel, result.WarningLevel);
        Assert.Equal(expectedLevel == "Critical", result.IsCritical);
        Assert.Equal(expectedLevel == "Warning", result.IsWarning);
    }

    [Fact]
    public void TruncateHistoryToContextWindow_drops_oldest_messages_first()
    {
        var messages = Enumerable.Range(0, 100)
            .Select(i => new ChatMessage("user", $"message {i} with some padding text to inflate the estimated token count"))
            .ToList();

        var truncated = ChatContextUsageCalculator.TruncateHistoryToContextWindow(messages, contextWindow: 200);

        Assert.NotEmpty(truncated);
        Assert.True(truncated.Count < messages.Count, "a small context window should drop some history");
        Assert.Equal(messages[^1].Content, truncated[^1].Content);
    }
}
