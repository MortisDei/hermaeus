using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r12 01-settings-lifecycle.md 1.2, 1.5, 1.6, 1.7: the settings save path
/// applies edits onto a deep copy instead of the live shared settings
/// object, so a failed save cannot leak a partial edit into some later,
/// unrelated save.
/// </summary>
public sealed class SettingsViewModelSaveLifecycleTests
{
    [Fact]
    public async Task Failed_save_leaves_live_settings_unchanged_so_a_later_unrelated_save_does_not_persist_it()
    {
        using var temp = new TempDir();
        var oldRoot = temp.PathFor("old-root");
        Directory.CreateDirectory(oldRoot);
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "conversations.db"), "data");
        // A second, non-conflicting file makes this a partial conflict - the
        // one real ambiguous case that must still refuse. A conflict on
        // every migratable file is a repoint, not a refusal (see
        // BackupMigrationTests.DataRootMigrationRepointsWithoutMovingWhenTargetAlreadyHasEveryFile).
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "memories.db"), "memory-data");

        var newRoot = temp.PathFor("new-root");
        Directory.CreateDirectory(newRoot);
        await File.WriteAllTextAsync(Path.Combine(newRoot, "conversations.db"), "conflicting existing file");

        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = oldRoot;

        var candidate = settings.Settings.Clone();
        candidate.DataManagement.DataRootDirectory = newRoot;
        candidate.Llm.DefaultSystemPrompt = "should not persist";
        await ThrowsAsync<IOException>(() => settings.SaveAsync(candidate, previousDataRootDirectory: oldRoot));

        Assert.Equal(oldRoot, settings.Settings.DataManagement.DataRootDirectory);
        Assert.Equal(string.Empty, settings.Settings.Llm.DefaultSystemPrompt);

        // An unrelated save path (any direct ISettingsService.SaveAsync call)
        // persists the live object as-is; it must not carry the failed edit.
        await settings.SaveAsync();

        var reloaded = new SettingsService(temp.PathFor("settings/settings.json"));
        await reloaded.LoadAsync();
        Assert.Equal(string.Empty, reloaded.Settings.Llm.DefaultSystemPrompt);
        Assert.Equal(oldRoot, reloaded.Settings.DataManagement.DataRootDirectory);
    }

    [Fact]
    public async Task Successful_save_migrates_data_and_clears_the_settings_error()
    {
        using var temp = new TempDir();
        var oldRoot = temp.PathFor("old-root");
        Directory.CreateDirectory(oldRoot);
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "conversations.db"), "data");
        var newRoot = temp.PathFor("new-root");

        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = oldRoot;
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());
        vm.Data.DataRootDirectory = newRoot;
        vm.Data.RequestDataRootMigrationConfirmation = _ => Task.FromResult(true);

        await vm.Data.ConfirmDataRootMigrationCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.SettingsError);
        Assert.Equal(newRoot, settings.Settings.DataManagement.DataRootDirectory);
        Assert.True(File.Exists(Path.Combine(newRoot, "conversations.db")));
    }

    [Fact]
    public async Task Unchanged_data_root_does_not_request_migration_confirmation()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());
        var confirmations = 0;
        vm.Data.RequestDataRootMigrationConfirmation = _ =>
        {
            confirmations++;
            return Task.FromResult(true);
        };

        await vm.Data.ConfirmDataRootMigrationCommand.ExecuteAsync(null);

        Assert.Equal(0, confirmations);
    }

    [Fact]
    public async Task Changed_data_root_requires_confirmation_and_cancel_restores_the_old_root()
    {
        using var temp = new TempDir();
        var oldRoot = temp.PathFor("old-root");
        Directory.CreateDirectory(oldRoot);
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "conversations.db"), "data");
        var newRoot = temp.PathFor("new-root");
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = oldRoot;
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());
        var confirmations = 0;
        vm.Data.RequestDataRootMigrationConfirmation = plan =>
        {
            confirmations++;
            Assert.Equal(Path.GetFullPath(oldRoot), plan.PreviousDataRoot);
            Assert.Equal(Path.GetFullPath(newRoot), plan.CurrentDataRoot);
            return Task.FromResult(false);
        };

        vm.Data.DataRootDirectory = newRoot;
        await vm.Data.ConfirmDataRootMigrationCommand.ExecuteAsync(null);

        Assert.Equal(1, confirmations);
        Assert.Equal(oldRoot, settings.Settings.DataManagement.DataRootDirectory);
        Assert.Equal(oldRoot, vm.Data.DataRootDirectory);
        Assert.False(vm.Data.DataRootMigrationPending);
        Assert.True(File.Exists(Path.Combine(oldRoot, "conversations.db")));
    }

    [Fact]
    public async Task Confirmed_data_root_change_uses_existing_migration_and_persists_at_commit_boundary()
    {
        using var temp = new TempDir();
        var oldRoot = temp.PathFor("old-root");
        Directory.CreateDirectory(oldRoot);
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "conversations.db"), "data");
        var newRoot = temp.PathFor("new-root");
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = oldRoot;
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());
        vm.Data.RequestDataRootMigrationConfirmation = _ => Task.FromResult(true);

        vm.Data.DataRootDirectory = newRoot;
        await vm.Data.ConfirmDataRootMigrationCommand.ExecuteAsync(null);

        Assert.Equal(newRoot, settings.Settings.DataManagement.DataRootDirectory);
        Assert.Equal(newRoot, vm.Data.DataRootDirectory);
        Assert.True(File.Exists(Path.Combine(newRoot, "conversations.db")));
        Assert.False(File.Exists(Path.Combine(oldRoot, "conversations.db")));
        Assert.False(vm.Data.DataRootMigrationPending);
    }

    [Fact]
    public async Task Ordinary_autosave_does_not_migrate_an_unconfirmed_data_root()
    {
        using var temp = new TempDir();
        var oldRoot = temp.PathFor("old-root");
        Directory.CreateDirectory(oldRoot);
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "conversations.db"), "data");
        var newRoot = temp.PathFor("new-root");
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = oldRoot;
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());

        vm.Data.DataRootDirectory = newRoot;
        vm.Llm.DefaultSystemPrompt = "ordinary edit";
        await WaitForAsync(
            () => settings.Settings.Llm.DefaultSystemPrompt == "ordinary edit",
            "ordinary settings autosave");

        Assert.Equal(oldRoot, settings.Settings.DataManagement.DataRootDirectory);
        Assert.Equal("ordinary edit", settings.Settings.Llm.DefaultSystemPrompt);
        Assert.True(File.Exists(Path.Combine(oldRoot, "conversations.db")));
        Assert.False(File.Exists(Path.Combine(newRoot, "conversations.db")));
        Assert.Contains("after confirmation", vm.Data.DataMigrationPreview);
    }

    [Fact]
    public void Reload_clears_a_pending_data_root_change_and_stale_save_wording()
    {
        using var temp = new TempDir();
        var oldRoot = temp.PathFor("old-root");
        Directory.CreateDirectory(oldRoot);
        var newRoot = temp.PathFor("new-root");
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = oldRoot;
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());

        vm.Data.DataRootDirectory = newRoot;
        Assert.True(vm.Data.DataRootMigrationPending);
        Assert.DoesNotContain("Save will move", vm.Data.DataMigrationPreview);

        vm.Reload();

        Assert.Equal(oldRoot, vm.Data.DataRootDirectory);
        Assert.False(vm.Data.DataRootMigrationPending);
        Assert.Equal("The current data folder is active.", vm.Data.DataMigrationPreview);
    }

    [Fact]
    public async Task Trust_rescan_never_mutates_the_live_settings_object()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());

        vm.Tts.TtsPythonPath = temp.PathFor("edited-but-unsaved-python.exe");
        var before = settings.Settings.Tts.PythonPath;

        await vm.Trust.RescanTrustCommand.ExecuteAsync(null);

        Assert.Equal(before, settings.Settings.Tts.PythonPath);
        Assert.NotEqual(vm.Tts.TtsPythonPath, settings.Settings.Tts.PythonPath);
    }

    [Fact]
    public void Reload_clears_stale_Trust_and_LocalAiSetup_error_text()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());
        vm.Trust.SettingsError = "stale trust error";
        vm.LocalAiSetup.SettingsError = "stale setup error";

        vm.Reload();

        Assert.Equal(string.Empty, vm.Trust.SettingsError);
        Assert.Equal(string.Empty, vm.LocalAiSetup.SettingsError);
    }

    [Fact]
    public async Task SaveAsync_completes_without_an_artificial_delay_tail()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await vm.SaveAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1500, $"SaveAsync should return promptly, took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Normal_preference_is_persisted_after_debounced_change_without_save_command()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());

        vm.Llm.DefaultSystemPrompt = "persisted automatically";

        await WaitForAsync(
            () => settings.Settings.Llm.DefaultSystemPrompt == "persisted automatically",
            "debounced settings save", timeoutMs: 5000);
        var reloaded = new SettingsService(temp.PathFor("settings/settings.json"));
        await reloaded.LoadAsync();

        Assert.Equal("persisted automatically", reloaded.Settings.Llm.DefaultSystemPrompt);
    }

    [Fact]
    public async Task Completed_autosave_can_be_followed_by_reload()
    {
        using var temp = new TempDir();
        var fixture = NewControlledFixture(temp);

        fixture.Vm.Llm.DefaultSystemPrompt = "completed";
        var pending = await fixture.Delay.WaitForCallAsync(0);
        fixture.Delay.Complete(pending);
        await fixture.Completed.WaitForCountAsync(1);

        fixture.Vm.Reload();

        Assert.Equal("Saved", fixture.Vm.PersistenceStatus);
        Assert.Equal(1, fixture.Settings.SaveCount);
        fixture.Vm.Shutdown();
    }

    [Fact]
    public async Task Replacing_an_autosave_cancels_the_older_delay_and_saves_only_the_latest_edit()
    {
        using var temp = new TempDir();
        var fixture = NewControlledFixture(temp);

        fixture.Vm.Llm.DefaultSystemPrompt = "first";
        var first = await fixture.Delay.WaitForCallAsync(0);
        fixture.Vm.Llm.DefaultSystemPrompt = "second";
        var second = await fixture.Delay.WaitForCallAsync(1);

        await first.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Delay.Complete(first);
        await fixture.Completed.WaitForCountAsync(1);
        fixture.Delay.Complete(second);
        await fixture.Completed.WaitForCountAsync(2);

        Assert.Equal(1, fixture.Settings.SaveCount);
        Assert.Equal("second", fixture.Settings.Settings.Llm.DefaultSystemPrompt);
        fixture.Vm.Shutdown();
    }

    [Fact]
    public async Task Reload_cancels_a_pending_autosave_without_saving_it()
    {
        using var temp = new TempDir();
        var fixture = NewControlledFixture(temp);

        fixture.Vm.Llm.DefaultSystemPrompt = "cancelled";
        var pending = await fixture.Delay.WaitForCallAsync(0);
        fixture.Vm.Reload();
        await pending.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Delay.Complete(pending);
        await fixture.Completed.WaitForCountAsync(1);

        Assert.Equal(0, fixture.Settings.SaveCount);
        Assert.Equal(string.Empty, fixture.Settings.Settings.Llm.DefaultSystemPrompt);
        fixture.Vm.Shutdown();
    }

    [Fact]
    public async Task Shutdown_cancels_a_pending_autosave_without_leaking_or_saving_it()
    {
        using var temp = new TempDir();
        var fixture = NewControlledFixture(temp);

        fixture.Vm.Llm.DefaultSystemPrompt = "shutdown";
        var pending = await fixture.Delay.WaitForCallAsync(0);
        fixture.Vm.Shutdown();
        await pending.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Delay.Complete(pending);
        await fixture.Completed.WaitForCountAsync(1);

        Assert.Equal(0, fixture.Settings.SaveCount);
        Assert.Equal(string.Empty, fixture.Settings.Settings.Llm.DefaultSystemPrompt);
    }

    [Fact]
    public async Task A_stale_completion_cannot_clear_or_dispose_the_newer_autosave_source()
    {
        using var temp = new TempDir();
        var fixture = NewControlledFixture(temp);

        fixture.Vm.Llm.DefaultSystemPrompt = "first";
        var first = await fixture.Delay.WaitForCallAsync(0);
        fixture.Vm.Llm.DefaultSystemPrompt = "second";
        var second = await fixture.Delay.WaitForCallAsync(1);
        await first.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));

        fixture.Delay.Complete(first);
        await fixture.Completed.WaitForCountAsync(1);

        // This edit must be able to cancel the still-owned second source. A
        // stale completion that clears or disposes it makes this setter throw.
        fixture.Vm.Llm.DefaultSystemPrompt = "third";
        var third = await fixture.Delay.WaitForCallAsync(2);
        await second.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Delay.Complete(second);
        await fixture.Completed.WaitForCountAsync(2);
        fixture.Delay.Complete(third);
        await fixture.Completed.WaitForCountAsync(3);

        Assert.Equal(1, fixture.Settings.SaveCount);
        Assert.Equal("third", fixture.Settings.Settings.Llm.DefaultSystemPrompt);
        fixture.Vm.Shutdown();
    }

    [Fact]
    public void TtsPythonPath_round_trips_through_reload_without_the_dead_secret_reference_guard()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Tts.PythonPath = "secret:some-reference-shaped-value";
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());

        Assert.Equal("secret:some-reference-shaped-value", vm.Tts.TtsPythonPath);
    }

    private static ControlledFixture NewControlledFixture(TempDir temp)
    {
        var rawSettings = NewSettings(temp);
        rawSettings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var settings = new CountingSettingsService(rawSettings);
        var delay = new ControlledDelay();
        var completed = new CompletionTracker();
        var vm = NewSettingsViewModel(
            settings,
            new FakeSecretStore(),
            autoSaveDelay: delay.DelayAsync,
            autoSaveLifecycleCompleted: completed.Record);
        return new ControlledFixture(vm, settings, delay, completed);
    }

    private sealed record ControlledFixture(
        SettingsViewModel Vm,
        CountingSettingsService Settings,
        ControlledDelay Delay,
        CompletionTracker Completed);

    private sealed class ControlledDelay
    {
        private readonly object _gate = new();
        private readonly List<PendingDelay> _pending = [];
        private TaskCompletionSource<bool> _callAdded = NewSignal();

        public Task DelayAsync(TimeSpan _, CancellationToken token)
        {
            var pending = new PendingDelay();
            token.Register(static state =>
            {
                var item = (PendingDelay)state!;
                item.CancellationObserved.TrySetResult(true);
            }, pending);
            lock (_gate)
            {
                _pending.Add(pending);
                _callAdded.TrySetResult(true);
                _callAdded = NewSignal();
            }
            return pending.Completion.Task;
        }

        public async Task<PendingDelay> WaitForCallAsync(int index)
        {
            while (true)
            {
                Task signal;
                lock (_gate)
                {
                    if (_pending.Count > index)
                        return _pending[index];
                    signal = _callAdded.Task;
                }
                await signal;
            }
        }

        public void Complete(PendingDelay pending) => pending.Completion.TrySetResult(true);

        private static TaskCompletionSource<bool> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingDelay
    {
        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CompletionTracker
    {
        private readonly object _gate = new();
        private int _count;
        private TaskCompletionSource<bool>? _waiter;
        private int _waiterTarget;

        public void Record()
        {
            lock (_gate)
            {
                _count++;
                if (_waiter is not null && _count >= _waiterTarget)
                {
                    _waiter.TrySetResult(true);
                    _waiter = null;
                }
            }
        }

        public Task WaitForCountAsync(int target)
        {
            lock (_gate)
            {
                if (_count >= target)
                    return Task.CompletedTask;

                _waiterTarget = target;
                _waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _waiter.Task.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }
    }

    private sealed class CountingSettingsService(ISettingsService inner) : ISettingsService
    {
        private int _saveCount;

        public AppSettings Settings => inner.Settings;
        public int SaveCount => Volatile.Read(ref _saveCount);
        public event EventHandler? SettingsChanged
        {
            add => inner.SettingsChanged += value;
            remove => inner.SettingsChanged -= value;
        }

        public Task LoadAsync() => inner.LoadAsync();

        public Task<SettingsSaveResult> SaveAsync(string? previousDataRootDirectory = null)
        {
            Interlocked.Increment(ref _saveCount);
            return inner.SaveAsync(previousDataRootDirectory);
        }

        public Task<SettingsSaveResult> SaveAsync(AppSettings settings, string? previousDataRootDirectory = null)
        {
            Interlocked.Increment(ref _saveCount);
            return inner.SaveAsync(settings, previousDataRootDirectory);
        }

        public DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory) =>
            inner.PreviewDataRootMigration(previousDataRootDirectory, nextDataRootDirectory);
    }
}
