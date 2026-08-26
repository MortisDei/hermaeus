using Hermaeus.Services;
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
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());

        vm.Data.DataRootDirectory = newRoot;
        vm.Llm.DefaultSystemPrompt = "should not persist";

        await vm.SaveAsync();

        Assert.NotEqual(string.Empty, vm.SettingsError);
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

        await vm.SaveAsync();

        Assert.Equal(string.Empty, vm.SettingsError);
        Assert.Equal(newRoot, settings.Settings.DataManagement.DataRootDirectory);
        Assert.True(File.Exists(Path.Combine(newRoot, "conversations.db")));
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
    public void TtsPythonPath_round_trips_through_reload_without_the_dead_secret_reference_guard()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Tts.PythonPath = "secret:some-reference-shaped-value";
        var vm = NewSettingsViewModel(settings, new FakeSecretStore());

        Assert.Equal("secret:some-reference-shaped-value", vm.Tts.TtsPythonPath);
    }
}
