using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
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

    private sealed class FakeSystemInfoWithGpu : ISystemInfoService
    {
        public Task<SystemSnapshot> CaptureAsync(CancellationToken ct = default) =>
            Task.FromResult(new SystemSnapshot { AppVersion = "test", Components = [] });

        public Task<HardwareProfile> GetHardwareProfileAsync(CancellationToken ct = default) =>
            Task.FromResult(new HardwareProfile(0, 8_000_000_000, "Fake GPU"));
    }

    /// <summary>
    /// The GPU-present-but-zero-layers advisory used to fire purely from
    /// static configuration, even for a server that was never started - a
    /// stopped server has no model loaded and cannot be "wasting" the GPU.
    /// Nothing in this test listens on the configured port, so the advisory
    /// must not appear.
    /// </summary>
    [Fact]
    public async Task Gpu_inference_advisory_does_not_fire_for_a_server_that_is_not_running()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig
        {
            Name = "Chat", Port = 48123, GpuLayers = 0, EmbeddingsMode = false
        });

        var doctor = new DoctorService(
            settings,
            new RuntimeProfileService(settings),
            new FakeVoiceProviderRegistry(settings),
            new FakeSecretStore(),
            new SqliteRagStore(settings),
            new FakeEmbeddingService(),
            new FakeSystemInfoWithGpu(),
            new PythonHealthValidator(),
            new NoOpReranker());

        var report = await doctor.ScanAsync();

        Assert.DoesNotContain(report.Checks, c => c.Key == "gpu-inference");
    }

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
