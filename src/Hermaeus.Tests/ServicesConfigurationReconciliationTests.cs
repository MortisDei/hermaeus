using Hermaeus.Core.Models;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ServicesConfigurationReconciliationTests
{
    [Fact]
    public async Task External_save_updates_clean_fields_but_preserves_dirty_editor_fields()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var services = NewServicesViewModel(settings);
        var server = services.Servers.First();

        server.ContextSize = 8192;
        Assert.Contains("contextSize", server.DirtyConfigurationFields);
        var baseRevision = server.BaseConfigurationRevision;

        var external = settings.Settings.Clone();
        external.ManagedServers[0].Threads = 9;
        await settings.SaveAsync(external);

        await WaitForAsync(() => server.Threads == 9
            && !server.DirtyConfigurationFields.Contains("threads")
            && server.BaseConfigurationRevision != baseRevision,
            "dirty server editor reconciliation");

        Assert.Equal(8192, server.ContextSize);
        Assert.Equal(9, server.Threads);
        Assert.Contains("contextSize", server.DirtyConfigurationFields);
        Assert.DoesNotContain("threads", server.DirtyConfigurationFields);
        Assert.NotEqual(baseRevision, server.BaseConfigurationRevision);
        Assert.Equal(4096, settings.Settings.ManagedServers[0].ContextSize);

        await server.SaveConfigCommand.ExecuteAsync(null);

        Assert.Equal(8192, settings.Settings.ManagedServers[0].ContextSize);
        Assert.Equal(9, settings.Settings.ManagedServers[0].Threads);
        Assert.Empty(server.DirtyConfigurationFields);
        Assert.False(server.HasUnsavedChanges);
    }

    [Fact]
    public async Task External_save_refreshes_a_clean_editor_without_marking_it_dirty()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var services = NewServicesViewModel(settings);
        var server = services.Servers.First();

        var external = settings.Settings.Clone();
        external.ManagedServers[0].ContextSize = 16384;
        external.ManagedServers[0].GpuPlacement = GpuPlacementIntent.Exact(12);
        external.ManagedServers[0].GpuLayers = 12;
        await settings.SaveAsync(external);

        await WaitForAsync(() => server.ContextSize == 16384
            && server.GpuLayers == 12
            && string.Equals(server.GpuPlacementSelection, "Exact", StringComparison.Ordinal)
            && !server.HasUnsavedChanges,
            "clean server editor reconciliation");

        Assert.Equal(16384, server.ContextSize);
        Assert.Equal("Exact", server.GpuPlacementSelection);
        Assert.Equal(12, server.GpuLayers);
        Assert.Empty(server.DirtyConfigurationFields);
        Assert.False(server.HasUnsavedChanges);
    }
}
