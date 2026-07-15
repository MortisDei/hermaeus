using Aether.Core.Models;
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
}
