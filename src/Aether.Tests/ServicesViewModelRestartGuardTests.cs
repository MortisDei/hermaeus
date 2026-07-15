using Aether.Core.Models;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// docs/review/03-performance.md item 3.6: audits every restart call site
/// for a "skip if the model hasn't changed" guard. ServerProcessViewModel's
/// guard already existed and is confirmed here; ChatViewModel and
/// Aether.LocalApi have no restart call sites at all (nothing to guard).
/// </summary>
public sealed class ServicesViewModelRestartGuardTests
{
    [Fact]
    public async Task SelectModelAndRestartAsyncSkipsRestartWhenModelPathIsUnchangedAndServerIsRunning()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var runtimeLogs = new RuntimeLogService(settings);
        var services = new ServicesViewModel(
            settings,
            new RuntimeProfileService(settings),
            new FakeToasts(),
            new RedactionService(),
            new TrustService(),
            runtimeLogs);

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
        var runtimeLogs = new RuntimeLogService(settings);
        var toasts = new FakeToasts();
        var services = new ServicesViewModel(
            settings,
            new RuntimeProfileService(settings),
            toasts,
            new RedactionService(),
            new TrustService(),
            runtimeLogs);

        var server = services.Servers.First();
        var originalStatus = server.Status;

        await services.SelectChatModelAndRestartAsync(temp.PathFor("does-not-exist.gguf"));

        Assert.Equal(originalStatus, server.Status);
    }
}
