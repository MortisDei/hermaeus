using Aether.Services;
using Aether.Services.ProcessManagement;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class RagIngestServiceSuspensionTests
{
    [Fact]
    public async Task SuspendAsync_is_a_noop_when_no_services_are_wired_up()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var suspension = new RagIngestServiceSuspension(services: null, xtts: null, kokoro: null, settings);

        var restore = await suspension.SuspendAsync();
        var errors = await restore();

        Assert.Empty(errors);
    }

    [Fact]
    public async Task SuspendAsync_prepares_the_embedding_server_and_restores_without_errors()
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

        var suspension = new RagIngestServiceSuspension(services, xtts: null, kokoro: null, settings);

        var restore = await suspension.SuspendAsync();
        var errors = await restore();

        Assert.Empty(errors);
    }
}
