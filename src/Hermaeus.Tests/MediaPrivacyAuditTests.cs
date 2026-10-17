using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class MediaPrivacyAuditTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8188", false)]
    [InlineData("https://media.example.test", true)]
    [InlineData("https://synthetic:private@example.test", true)]
    public async Task Comfy_is_disclosed_and_non_loopback_configuration_counts_as_outbound(string endpoint, bool remote)
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiEnabled = false;
        settings.Settings.Llm.ComfyUiBaseUrl = endpoint;
        var audit = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings),
            new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings));
        var before = await audit.CountOutboundDestinationsAsync();
        settings.Settings.Llm.ComfyUiEnabled = true;
        Assert.Equal(before + (remote ? 1 : 0), await audit.CountOutboundDestinationsAsync());
        var rows = await audit.ScanAsync();
        var media = Assert.Single(rows, x => x.Name == "Media generation");
        Assert.Equal(remote ? "Remote" : "Explicit action", media.Status);
        Assert.Contains("survive client cancellation", media.Detail);
        Assert.DoesNotContain("synthetic:private", media.Detail);
        if (remote) Assert.Contains("ComfyUI", rows.Single(x => x.Name == "Remote providers").Detail);
    }
}
