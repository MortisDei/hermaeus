using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class ServicesViewModelTests
{
    private static ServerProcessViewModel NewServerVm(TempDir temp, int contextSize)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var config = new ServerConfig { Name = "Chat", ContextSize = contextSize };
        return new ServerProcessViewModel(config, settings, new RedactionService(), new TrustService(), new FakeToasts(), new RuntimeLogService(settings));
    }

    [Fact]
    public void Oversized_context_note_visible_above_threshold()
    {
        using var temp = new TempDir();
        var vm = NewServerVm(temp, 32768);

        Assert.True(vm.HasOversizedContext);
        Assert.Contains("32,768", vm.OversizedContextNote);
    }

    [Fact]
    public void Oversized_context_note_absent_below_threshold()
    {
        using var temp = new TempDir();
        var vm = NewServerVm(temp, 8192);

        Assert.False(vm.HasOversizedContext);
    }

    [Fact]
    public void Oversized_context_note_updates_when_the_field_changes()
    {
        using var temp = new TempDir();
        var vm = NewServerVm(temp, 8192);
        Assert.False(vm.HasOversizedContext);

        vm.ContextSize = 32768;

        Assert.True(vm.HasOversizedContext);
        Assert.Contains("32,768", vm.OversizedContextNote);
    }

    private static ServicesViewModel NewServicesVm(TempDir temp, out ISettingsService settings)
    {
        settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new ServicesViewModel(settings, new RuntimeProfileService(settings), new FakeToasts(), new RedactionService(), new TrustService(), new RuntimeLogService(settings));
    }

    /// <summary>
    /// ServicesViewModel.Rebuild runs from ISettingsService.SettingsChanged
    /// via RunOnUi; under xUnit's AsyncTestSyncContext, RunOnUi's captured
    /// context does not always match the context active by the time the
    /// event fires deep inside SaveAsync's own await chain, so the posted
    /// Rebuild can land after the awaited SaveAsync call already returned.
    /// Poll briefly instead of asserting immediately.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
    }

    // ── r12 01-settings-lifecycle.md 1.4: Rebuild must diff, not churn ──

    [Fact]
    public async Task Saving_an_unrelated_setting_does_not_fire_ServerAvailabilityChanged()
    {
        using var temp = new TempDir();
        var vm = NewServicesVm(temp, out var settings);
        var fired = 0;
        vm.ServerAvailabilityChanged += (_, _) => fired++;

        // Simulate an unrelated save (e.g. a UI font-size tweak): nothing
        // about the managed servers changed, so this should not touch the
        // Services panel at all.
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        await settings.SaveAsync();
        await Task.Delay(100);

        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Changing_a_managed_server_port_fires_ServerAvailabilityChanged()
    {
        using var temp = new TempDir();
        var vm = NewServicesVm(temp, out var settings);
        var fired = 0;
        vm.ServerAvailabilityChanged += (_, _) => fired++;

        settings.Settings.ManagedServers[0].Port = 50000;
        await settings.SaveAsync();
        await WaitForAsync(() => fired > 0);

        Assert.True(fired > 0, "changing a managed server's port must fire ServerAvailabilityChanged");
    }

    [Fact]
    public async Task Rebuild_reuses_the_existing_row_instead_of_replacing_it()
    {
        using var temp = new TempDir();
        var vm = NewServicesVm(temp, out var settings);
        var originalChatRow = vm.Servers.First(s => !s.EmbeddingsMode);
        originalChatRow.LogExpanded = true;

        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        await settings.SaveAsync();
        await Task.Delay(100);

        var afterRebuild = vm.Servers.First(s => !s.EmbeddingsMode);
        Assert.Same(originalChatRow, afterRebuild);
        Assert.True(afterRebuild.LogExpanded, "reused rows must keep their UI state (e.g. expanded logs)");
    }

    [Fact]
    public async Task Removing_a_managed_server_disposes_its_view_model()
    {
        using var temp = new TempDir();
        var vm = NewServicesVm(temp, out var settings);
        var chatRow = vm.Servers.First(s => !s.EmbeddingsMode);

        settings.Settings.ManagedServers.RemoveAll(s => s.Id == chatRow.Id);
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", Port = 39201 });
        await settings.SaveAsync();
        await WaitForAsync(() => chatRow.IsDisposed);

        Assert.True(chatRow.IsDisposed, "a row whose config was removed must be disposed, not just dropped");
        Assert.DoesNotContain(vm.Servers, s => s.Id == chatRow.Id);
    }
}
