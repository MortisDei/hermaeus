using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r24 shipped Activity's coverage as an admitted subset and named the four
/// sources it had not reached. r28 doc 03 3.3 wires them. Each is one
/// RecordAsync at a point where the outcome is already known, and none of
/// them may change the behaviour of the operation it observes.
/// </summary>
public sealed class ActivityEventSourceTests
{
    private sealed record Recorded(string Operation, string SourceId, ActivityOutcome Outcome, string Title, string Reason);

    private sealed class RecordingActivity : IActivityRecorder
    {
        public List<Recorded> Rows { get; } = [];

        public Task RecordAsync(string operation, string sourceId, ActivityOutcome outcome, string title,
            string reason = "", string projectId = "", CancellationToken ct = default)
        {
            lock (Rows) Rows.Add(new Recorded(operation, sourceId, outcome, title, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingActivity : IActivityRecorder
    {
        public Task RecordAsync(string operation, string sourceId, ActivityOutcome outcome, string title,
            string reason = "", string projectId = "", CancellationToken ct = default) =>
            throw new InvalidOperationException("recorder is broken");
    }

    private static async Task<Recorded> WaitForAsync(RecordingActivity activity, string operation)
    {
        // The call sites are fire-and-forget by design, so the row may land a
        // continuation after the awaited operation returns.
        for (var i = 0; i < 100; i++)
        {
            lock (activity.Rows)
            {
                var row = activity.Rows.LastOrDefault(r => r.Operation == operation);
                if (row is not null) return row;
            }
            await Task.Delay(10);
        }

        lock (activity.Rows)
            return Assert.Single(activity.Rows, r => r.Operation == operation);
    }

    // ── memory auto-archive sweeps ──

    private static async Task<(MemoriesViewModel Vm, RecordingActivity Activity)> BuildMemoriesAsync(TempDir temp, IActivityRecorder? recorder = null)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new MemoryStore(settings);
        await store.InitializeAsync();
        var conversations = new ConversationStore(settings);
        await conversations.InitializeAsync();

        var activity = new RecordingActivity();
        return (new MemoriesViewModel(store, conversations, settings, new NoOpToasts(), recorder ?? activity), activity);
    }

    [Fact]
    public async Task A_sweep_that_archives_nothing_still_records()
    {
        using var temp = new TempDir();
        var (vm, activity) = await BuildMemoriesAsync(temp);

        await vm.InitializeCommand.ExecuteAsync(null);

        // "It ran and found nothing" and "it never ran" are the two states
        // this panel exists to separate.
        var row = await WaitForAsync(activity, "memory.auto-archive");
        Assert.Equal(ActivityOutcome.Succeeded, row.Outcome);
        Assert.Contains("0", row.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broken_recorder_does_not_break_the_sweep()
    {
        using var temp = new TempDir();
        var (vm, _) = await BuildMemoriesAsync(temp, new ThrowingActivity());

        // No throw: the recorder is describing the work, not doing it.
        await vm.InitializeCommand.ExecuteAsync(null);
    }

    // ── backup and restore ──

    private static async Task<DataManagementSettingsViewModel> BuildDataAsync(TempDir temp, RecordingActivity activity)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        // A real store, so the backup takes its SQLite online-backup path
        // rather than failing on a file that only looks like a database.
        var store = new MemoryStore(settings);
        await store.InitializeAsync();

        return new DataManagementSettingsViewModel(
            settings, new BackupService(settings), new NoOpToasts(),
            () => settings.Settings.DataManagement.DataRootDirectory, activity)
        {
            BackupDirectory = temp.PathFor("backups")
        };
    }

    [Fact]
    public async Task A_backup_records_what_it_wrote()
    {
        using var temp = new TempDir();
        var activity = new RecordingActivity();
        var vm = await BuildDataAsync(temp, activity);

        await vm.BackupDataCommand.ExecuteAsync(null);

        var row = await WaitForAsync(activity, "backup.write");
        Assert.Equal(ActivityOutcome.Succeeded, row.Outcome);
    }

    [Fact]
    public async Task A_refused_restore_records_the_reason_the_service_gave()
    {
        using var temp = new TempDir();
        var activity = new RecordingActivity();
        var vm = await BuildDataAsync(temp, activity);
        vm.RestoreBackupPath = temp.PathFor("no-such-backup.zip");

        await vm.RestoreDataCommand.ExecuteAsync(null);

        var row = await WaitForAsync(activity, "backup.restore");
        Assert.Equal(ActivityOutcome.Failed, row.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(row.Reason));
    }

    // ── the voice backend ──

    [Fact]
    public async Task A_voice_backend_that_cannot_start_records_the_failure()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.PythonPath = temp.PathFor("definitely-not-a-python-interpreter");
        var activity = new RecordingActivity();
        using var manager = new KokoroProcessManager(runtimeLogs: null, activity: activity);

        await Assert.ThrowsAnyAsync<Exception>(() => manager.StartAsync(settings.Settings));

        var row = await WaitForAsync(activity, "voice.backend-start");
        Assert.Equal(ActivityOutcome.Failed, row.Outcome);
        Assert.Equal("kokoro", row.SourceId);
    }

    [Fact]
    public async Task A_broken_recorder_does_not_change_how_a_voice_start_fails()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.PythonPath = temp.PathFor("definitely-not-a-python-interpreter");
        using var manager = new KokoroProcessManager(runtimeLogs: null, activity: new ThrowingActivity());

        // Still the process failure, not the recorder's.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => manager.StartAsync(settings.Settings));
        Assert.DoesNotContain("recorder is broken", ex.Message, StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    // ── the recorder itself ──

    [Fact]
    public async Task The_real_recorder_swallows_a_broken_trace_store()
    {
        var recorder = new ActivityRecorder(new RedactionService(), new ThrowingTraceStore());

        // No throw: r24's rule, retested here because 3.3 adds four call
        // sites that rely on it.
        await recorder.RecordAsync("models.download", "repo", ActivityOutcome.Failed, "boom", "why");
    }

    private sealed class ThrowingTraceStore : ITraceStore
    {
        public Task AppendAsync(TraceRecord trace, CancellationToken ct = default) => throw new InvalidOperationException("store is down");
        public Task<List<TraceRecord>> GetRecentAsync(TraceKind? kind = null, int limit = 50, CancellationToken ct = default) =>
            Task.FromResult(new List<TraceRecord>());
        public Task<List<ModelUsageRow>> GetModelUsageAsync(TraceKind? kind, int days, CancellationToken ct = default) =>
            Task.FromResult(new List<ModelUsageRow>());
        public Task<int> DeleteByKindAsync(TraceKind kind, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpToasts : IToastService
    {
        public event Action<ToastMessage>? ToastRaised;
        public void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500) => ToastRaised?.Invoke(new ToastMessage(title, message, kind, durationMs));
    }
}
