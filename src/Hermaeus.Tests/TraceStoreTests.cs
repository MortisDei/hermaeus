using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class TraceStoreTests
{
    [Fact]
    public async Task Append_and_read_roundtrips_the_envelope()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteTraceStore(settings);

        var trace = new TraceRecord
        {
            Kind = TraceKind.Rag,
            SourceId = "dataset-1",
            ModelId = "model-a",
            Operation = "rag-query",
            FirstTokenMs = 12,
            TotalLatencyMs = 345,
            PromptTokens = 100,
            CompletionTokens = 20,
            TotalTokens = 120,
            Error = "",
            DetailJson = """{"question":"q"}"""
        };
        await store.AppendAsync(trace);

        var read = await store.GetRecentAsync(TraceKind.Rag);
        var got = Assert.Single(read);
        Assert.Equal(trace.Id, got.Id);
        Assert.Equal(TraceKind.Rag, got.Kind);
        Assert.Equal("dataset-1", got.SourceId);
        Assert.Equal("model-a", got.ModelId);
        Assert.Equal(345, got.TotalLatencyMs);
        Assert.Equal(120, got.TotalTokens);
        Assert.Equal("""{"question":"q"}""", got.DetailJson);
    }

    [Fact]
    public async Task Kind_filter_separates_projections_and_null_returns_all()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteTraceStore(settings);

        await store.AppendAsync(new TraceRecord { Kind = TraceKind.Chat, SourceId = "c1" });
        await store.AppendAsync(new TraceRecord { Kind = TraceKind.Agent, SourceId = "t1" });
        await store.AppendAsync(new TraceRecord { Kind = TraceKind.Chat, SourceId = "c2" });

        Assert.Equal(2, (await store.GetRecentAsync(TraceKind.Chat)).Count);
        Assert.Single(await store.GetRecentAsync(TraceKind.Agent));
        Assert.Empty(await store.GetRecentAsync(TraceKind.Rag));
        Assert.Equal(3, (await store.GetRecentAsync()).Count);
    }

    [Fact]
    public async Task Recent_traces_are_newest_first()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteTraceStore(settings);

        var older = new TraceRecord { Kind = TraceKind.Chat, CreatedAt = DateTime.UtcNow.AddMinutes(-5) };
        var newer = new TraceRecord { Kind = TraceKind.Chat, CreatedAt = DateTime.UtcNow };
        await store.AppendAsync(older);
        await store.AppendAsync(newer);

        var read = await store.GetRecentAsync(TraceKind.Chat);
        Assert.Equal(newer.Id, read[0].Id);
        Assert.Equal(older.Id, read[1].Id);
    }

    [Fact]
    public async Task Retention_prunes_per_kind_without_touching_other_kinds()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteTraceStore(settings);

        var baseTime = DateTime.UtcNow.AddHours(-1);
        await store.AppendAsync(new TraceRecord { Kind = TraceKind.Agent, CreatedAt = baseTime });
        for (var i = 0; i < SqliteTraceStore.MaxTracesPerKind + 5; i++)
            await store.AppendAsync(new TraceRecord { Kind = TraceKind.Chat, CreatedAt = baseTime.AddSeconds(i) });

        var chat = await store.GetRecentAsync(TraceKind.Chat, limit: SqliteTraceStore.MaxTracesPerKind * 2);
        Assert.Equal(SqliteTraceStore.MaxTracesPerKind, chat.Count);
        Assert.Single(await store.GetRecentAsync(TraceKind.Agent));
    }
}
