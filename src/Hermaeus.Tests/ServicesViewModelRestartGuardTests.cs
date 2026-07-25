using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// docs/review/03-performance.md item 3.6: audits every restart call site
/// for a "skip if the model hasn't changed" guard. ServerProcessViewModel's
/// guard already existed and is confirmed here; ChatViewModel and
/// Hermaeus.LocalApi have no restart call sites at all (nothing to guard).
/// </summary>
public sealed class ServicesViewModelRestartGuardTests
{
    [Fact]
    public async Task SelectModelAndRestartAsyncSkipsRestartWhenModelPathIsUnchangedAndServerIsRunning()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var services = NewServicesViewModel(settings);

        var server = services.Servers.First();
        var samePath = System.IO.Path.GetFullPath(
            string.IsNullOrWhiteSpace(server.ModelPath) ? temp.PathFor("model.gguf") : server.ModelPath);
        server.ModelPath = samePath;
        server.Status = ServerStatus.Running;

        await server.SelectModelAndRestartAsync(samePath);

        Assert.Equal(ServerStatus.Running, server.Status);
    }

    [Fact]
    public async Task SelectChatModelAndRestartAsyncSkipsWhenTheModelFileDoesNotExist()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var services = NewServicesViewModel(settings);

        var server = services.Servers.First();
        var originalStatus = server.Status;

        await services.SelectChatModelAndRestartAsync(temp.PathFor("does-not-exist.gguf"));

        Assert.Equal(originalStatus, server.Status);
    }

    /// <summary>
    /// A real bug report: an llama.cpp update rewrites every managed
    /// server's ExecutablePath on the underlying config directly, including
    /// stopped servers, but each row's own bound property only ever synced
    /// from config at construction. SyncAllExecutablePathsFromConfig must
    /// pick up that change for every row, whether it was ever started or
    /// not, without starting anything.
    /// </summary>
    [Fact]
    public void SyncAllExecutablePathsFromConfigUpdatesEveryRowIncludingStoppedOnes()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var services = NewServicesViewModel(settings);

        foreach (var server in services.Servers)
            Assert.Equal(ServerStatus.Stopped, server.Status);

        var newPath = temp.PathFor("llama-server-new/llama-server.exe");
        foreach (var config in settings.Settings.ManagedServers)
            config.ExecutablePath = newPath;

        services.SyncAllExecutablePathsFromConfig();

        foreach (var server in services.Servers)
        {
            Assert.Equal(newPath, server.ExecutablePath);
            Assert.Equal(ServerStatus.Stopped, server.Status);
        }
    }
}
