using Aether.Core.Services;
using Aether.Services;
using Xunit;

namespace Aether.Tests;

public sealed class ChatTraceServiceTests
{
    [Fact]
    public async Task PersistAsync_then_LoadRecentAsync_round_trips_a_trace()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        await settings.LoadAsync();
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var runtimeLogs = new RuntimeLogService(settings);
        var traces = new ChatTraceService(runtimeLogs, new SqliteTraceStore(settings));

        var entry = new ChatTraceEntry(
            Id: "trace-1",
            Timestamp: DateTime.UtcNow,
            ModelId: "model-a",
            Provider: "llama.cpp",
            Runtime: "llama.cpp",
            SystemPrompt: "Be concise.",
            AttachmentCount: 2,
            EstimatedTokens: 123,
            ProviderUsage: new ChatTokenUsage(50, 20, 70),
            FirstTokenMs: 100,
            TotalLatencyMs: 400,
            ErrorDetails: string.Empty);

        await traces.PersistAsync(entry, conversationId: "conv-1");
        var recent = await traces.LoadRecentAsync();

        var loaded = Assert.Single(recent);
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal(entry.ModelId, loaded.ModelId);
        Assert.Equal(entry.Provider, loaded.Provider);
        Assert.Equal(entry.SystemPrompt, loaded.SystemPrompt);
        Assert.Equal(entry.AttachmentCount, loaded.AttachmentCount);
        Assert.Equal(70, loaded.ProviderUsage?.TotalTokens);
    }

    [Fact]
    public async Task PersistAsync_and_LoadRecentAsync_are_no_ops_without_a_trace_store()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        await settings.LoadAsync();
        var runtimeLogs = new RuntimeLogService(settings);
        var traces = new ChatTraceService(runtimeLogs);

        await traces.PersistAsync(
            new ChatTraceEntry("t", DateTime.UtcNow, "m", "p", "r", "s", 0, 0, null, 0, 0, string.Empty),
            conversationId: "conv-1");
        var recent = await traces.LoadRecentAsync();

        Assert.Empty(recent);
    }
}
