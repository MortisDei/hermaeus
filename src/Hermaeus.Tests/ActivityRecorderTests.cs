using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ActivityRecorderTests
{
    [Fact]
    public async Task RecordAsync_persists_a_system_kind_trace_row()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteTraceStore(settings);
        var recorder = new ActivityRecorder(new RedactionService(), store);

        await recorder.RecordAsync("services.server-start", "chat", ActivityOutcome.Succeeded, "Chat started");

        var rows = await store.GetRecentAsync(TraceKind.System);
        var row = Assert.Single(rows);
        Assert.Equal(TraceKind.System, row.Kind);
        Assert.Equal("services.server-start", row.Operation);
        Assert.Contains("Chat started", row.DetailJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordAsync_redacts_a_secret_before_persisting()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteTraceStore(settings);
        var recorder = new ActivityRecorder(new RedactionService(), store);

        await recorder.RecordAsync("models.download", "model1", ActivityOutcome.Failed,
            "Download failed", "https://example.com/model.gguf?token=sk-super-secret-key-123456789");

        var rows = await store.GetRecentAsync(TraceKind.System);
        var row = Assert.Single(rows);
        False(row.DetailJson.Contains("sk-super-secret-key-123456789", StringComparison.Ordinal),
            "a token in a recorded activity reason must be redacted before persistence");
    }

    [Fact]
    public async Task DeleteByKindAsync_removes_only_system_rows_and_leaves_other_kinds_alone()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteTraceStore(settings);
        var recorder = new ActivityRecorder(new RedactionService(), store);
        await recorder.RecordAsync("services.server-start", "chat", ActivityOutcome.Succeeded, "Chat started");
        await store.AppendAsync(new Hermaeus.Core.Models.TraceRecord { Kind = TraceKind.Chat, Operation = "send" });

        var removed = await store.DeleteByKindAsync(TraceKind.System);

        Assert.Equal(1, removed);
        Assert.Empty(await store.GetRecentAsync(TraceKind.System));
        Assert.Single(await store.GetRecentAsync(TraceKind.Chat));
    }

    [Fact]
    public async Task Clearing_activity_does_not_touch_the_durable_model_usage_rollup()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteTraceStore(settings);
        // A chat trace with a model id populates model_usage; system-kind
        // activity never carries a ModelId, so it never touches that rollup,
        // but assert the rollup survives an Activity clear either way.
        await store.AppendAsync(new Hermaeus.Core.Models.TraceRecord { Kind = TraceKind.Chat, Operation = "send", ModelId = "m1", TotalTokens = 10 });
        var recorder = new ActivityRecorder(new RedactionService(), store);
        await recorder.RecordAsync("services.server-start", "chat", ActivityOutcome.Succeeded, "Chat started");

        await store.DeleteByKindAsync(TraceKind.System);

        var usage = await store.GetModelUsageAsync(TraceKind.Chat, 30);
        var row = Assert.Single(usage);
        Assert.Equal("m1", row.ModelId);
        Assert.Equal(10, row.TotalTokens);
    }
}
