using Hermaeus.Core.Models;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class DoctorAdvisoryTests
{
    private static DoctorService NewDoctor(SettingsService settings) => new(
        settings,
        new RuntimeProfileService(settings),
        new FakeVoiceProviderRegistry(settings),
        new FakeSecretStore(),
        new SqliteRagStore(settings),
        new FakeEmbeddingService(),
        new FakeSystemInfo(),
        new PythonHealthValidator(),
        new NoOpReranker());

    [Fact]
    public async Task Blank_embedding_base_url_with_memory_enabled_yields_the_fallback_advisory()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Memory.Enabled = true;
        settings.Settings.Rag.EmbeddingBaseUrl = "";

        var doctor = NewDoctor(settings);
        var report = await doctor.ScanAsync();

        Assert.Contains(report.Checks, c => c.Key == "embedding-endpoint-fallback");
    }

    [Fact]
    public async Task Setting_the_embedding_base_url_clears_the_fallback_advisory()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Memory.Enabled = true;
        settings.Settings.Rag.EmbeddingBaseUrl = "http://localhost:39202";

        var doctor = NewDoctor(settings);
        var report = await doctor.ScanAsync();

        Assert.DoesNotContain(report.Checks, c => c.Key == "embedding-endpoint-fallback");
    }

    [Fact]
    public async Task Blank_embedding_base_url_with_memory_and_rag_disabled_does_not_yield_the_advisory()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Memory.Enabled = false;
        settings.Settings.Rag.Enabled = false;
        settings.Settings.Rag.EmbeddingBaseUrl = "";

        var doctor = NewDoctor(settings);
        var report = await doctor.ScanAsync();

        Assert.DoesNotContain(report.Checks, c => c.Key == "embedding-endpoint-fallback");
    }

    [Fact]
    public async Task Oversized_context_advisory_appears_above_threshold_and_carries_the_configured_value()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ContextSize = 32768 });

        var doctor = NewDoctor(settings);
        var report = await doctor.ScanAsync();

        var advisory = Assert.Single(report.Checks, c => c.Key.StartsWith("oversized-context-"));
        Assert.Contains("32768", advisory.Diagnostics);
        Assert.Contains("32,768", advisory.Summary);
    }

    [Fact]
    public async Task Oversized_context_advisory_absent_below_threshold()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ContextSize = 8192 });

        var doctor = NewDoctor(settings);
        var report = await doctor.ScanAsync();

        Assert.DoesNotContain(report.Checks, c => c.Key.StartsWith("oversized-context-"));
    }
}
