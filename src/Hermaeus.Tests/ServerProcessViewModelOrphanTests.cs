using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ServerProcessViewModelOrphanTests
{
    private sealed class FakePortOwnerLookup(PortOwnerInfo? owner) : IPortOwnerLookup
    {
        public bool IsPortListening(int port) => owner is not null;
        public PortOwnerInfo? FindOwner(int port) => owner;
    }

    private static ServerProcessViewModel NewVm(TempDir temp, string exePath, int port, PortOwnerInfo? owner)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var config = new ServerConfig { Name = "Chat", ExecutablePath = exePath, Port = port };
        var detector = new OrphanServerDetector(new FakePortOwnerLookup(owner));
        return new ServerProcessViewModel(config, settings, new RedactionService(), new TrustService(), new FakeToasts(), new RuntimeLogService(settings), detector);
    }

    [Fact]
    public async Task RefreshOrphanStatusAsync_shows_a_stoppable_banner_for_its_own_leftover_binary()
    {
        using var temp = new TempDir();
        var exe = @"C:\hermaeus\llama-server.exe";
        var vm = NewVm(temp, exe, 8080, new PortOwnerInfo(4321, "llama-server", exe));

        await vm.RefreshOrphanStatusAsync();

        Assert.True(vm.HasOrphan);
        Assert.True(vm.CanStopOrphan);
        Assert.Contains("4321", vm.OrphanBannerText);
    }

    [Fact]
    public async Task RefreshOrphanStatusAsync_shows_information_only_for_an_unrelated_process()
    {
        using var temp = new TempDir();
        var vm = NewVm(temp, @"C:\hermaeus\llama-server.exe", 8080,
            new PortOwnerInfo(9999, "other", @"C:\other\other.exe"));

        await vm.RefreshOrphanStatusAsync();

        Assert.True(vm.HasOrphan);
        Assert.False(vm.CanStopOrphan);
    }

    [Fact]
    public async Task RefreshOrphanStatusAsync_clears_the_banner_when_the_port_is_free()
    {
        using var temp = new TempDir();
        var vm = NewVm(temp, @"C:\hermaeus\llama-server.exe", 8080, owner: null);

        await vm.RefreshOrphanStatusAsync();

        Assert.False(vm.HasOrphan);
        Assert.Equal(string.Empty, vm.OrphanBannerText);
    }
}
