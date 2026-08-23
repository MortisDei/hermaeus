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

    [Fact]
    public void Linux_tray_support_is_ready_only_after_current_session_confirmation()
    {
        var unknown = DoctorService.BuildTraySupportCheck(
            isWindows: false,
            isMacOS: false,
            isLinux: true,
            integrationConfirmed: false,
            diagnostics: "Linux");
        var confirmed = DoctorService.BuildTraySupportCheck(
            isWindows: false,
            isMacOS: false,
            isLinux: true,
            integrationConfirmed: true,
            diagnostics: "Linux");

        Assert.Equal(DoctorCheckStatus.Info, unknown.Status);
        Assert.Equal("Tray support not confirmed", unknown.Summary);
        Assert.Equal(DoctorCheckStatus.Ready, confirmed.Status);
        Assert.Equal("Tray integration confirmed", confirmed.Summary);
    }

    [Fact]
    public void Windows_tray_support_remains_ready_without_runtime_confirmation()
    {
        var check = DoctorService.BuildTraySupportCheck(
            isWindows: true,
            isMacOS: false,
            isLinux: false,
            integrationConfirmed: false,
            diagnostics: "Windows");

        Assert.Equal(DoctorCheckStatus.Ready, check.Status);
        Assert.Equal("Tray supported", check.Summary);
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

    /// <summary>
    /// r24: GitHub's anonymous API allows only 60 requests/hour per IP, and
    /// Doctor's two update checks (llama.cpp, Hermaeus) both call it on every
    /// scan, including the automatic startup scan. A user restarting or
    /// rescanning a few times an hour could exhaust that quota on background
    /// checks alone and then have an actual llama.cpp update attempt fail
    /// with a 403. Repeated calls within the TTL must reuse the cached
    /// result instead of hitting the network again.
    /// </summary>
    [Fact]
    public async Task GetCachedGitHubReleaseAsync_reuses_a_cached_result_within_the_ttl()
    {
        var cache = new Dictionary<string, (DateTimeOffset CachedAt, object? Value)>();
        var calls = 0;

        Task<string?> Fetch(CancellationToken ct)
        {
            calls++;
            return Task.FromResult<string?>("v1.2.3");
        }

        var first = await DoctorService.GetCachedGitHubReleaseAsync(cache, "key", Fetch, TimeSpan.FromHours(1), CancellationToken.None);
        var second = await DoctorService.GetCachedGitHubReleaseAsync(cache, "key", Fetch, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Equal("v1.2.3", first);
        Assert.Equal("v1.2.3", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetCachedGitHubReleaseAsync_caches_a_failed_null_fetch_too()
    {
        var cache = new Dictionary<string, (DateTimeOffset CachedAt, object? Value)>();
        var calls = 0;

        Task<string?> Fetch(CancellationToken ct)
        {
            calls++;
            return Task.FromResult<string?>(null);
        }

        var first = await DoctorService.GetCachedGitHubReleaseAsync(cache, "key", Fetch, TimeSpan.FromHours(1), CancellationToken.None);
        var second = await DoctorService.GetCachedGitHubReleaseAsync(cache, "key", Fetch, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetCachedGitHubReleaseAsync_refetches_once_the_ttl_has_expired()
    {
        var cache = new Dictionary<string, (DateTimeOffset CachedAt, object? Value)>
        {
            ["key"] = (DateTimeOffset.UtcNow.AddHours(-2), (object?)"stale")
        };
        var calls = 0;

        Task<string?> Fetch(CancellationToken ct)
        {
            calls++;
            return Task.FromResult<string?>("fresh");
        }

        var result = await DoctorService.GetCachedGitHubReleaseAsync(cache, "key", Fetch, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Equal("fresh", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetCachedGitHubReleaseAsync_keys_are_independent()
    {
        var cache = new Dictionary<string, (DateTimeOffset CachedAt, object? Value)>();

        var llama = await DoctorService.GetCachedGitHubReleaseAsync(cache, "llama.cpp-latest-release", _ => Task.FromResult<string?>("llama"), TimeSpan.FromHours(1), CancellationToken.None);
        var hermaeus = await DoctorService.GetCachedGitHubReleaseAsync(cache, "hermaeus-latest-release", _ => Task.FromResult<string?>("hermaeus"), TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Equal("llama", llama);
        Assert.Equal("hermaeus", hermaeus);
    }

    /// <summary>
    /// The embedding backend check used to report the vague, Warning-severity
    /// "Embedding backend not reachable" for a server that was never started
    /// at all, indistinguishable from an actually-broken running server.
    /// It must now report Info-severity "No embedding server started" when
    /// nothing is listening at the resolved base URL.
    /// </summary>
    [Fact]
    public async Task Embedding_backend_check_reports_no_server_started_grey_not_a_warning()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("AI");
        var embedDir = Path.Combine(root, "Models", "embed");
        Directory.CreateDirectory(embedDir);
        File.WriteAllText(Path.Combine(embedDir, "nomic-embed-text-v1.5-Q4_K_M.gguf"), "model");

        var settings = NewSettings(temp);
        settings.Settings.DataManagement.LocalAiAssetsRoot = root;
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Rag.EmbeddingModel = "nomic-embed-text-v1.5";
        settings.Settings.Rag.EmbeddingBaseUrl = "http://127.0.0.1:1";

        var doctor = new DoctorService(
            settings,
            new RuntimeProfileService(settings),
            new FakeVoiceProviderRegistry(settings),
            new FakeSecretStore(),
            new SqliteRagStore(settings),
            new FakeEmbeddingService(),
            new FakeSystemInfo(),
            new PythonHealthValidator(),
            new NoOpReranker());

        var report = await doctor.ScanAsync();
        var check = report.Checks.Single(c => c.Key == "embeddings");

        Assert.Equal(DoctorCheckStatus.Info, check.Status);
        Assert.Equal("No embedding server started", check.Summary);
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
