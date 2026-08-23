using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Storage;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Voice;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hermaeus.Services;

/// <summary>
/// Runs the Doctor scan: a flat list of independent health checks grouped by
/// domain across partial-class files (Startup, Storage, Runtime, Voice, Rag,
/// System, Benchmarks) purely to keep any one file readable. This file holds
/// construction, the scan orchestration, and the tiny helpers every check uses.
/// </summary>
public sealed partial class DoctorService : IDoctorService
{
    private static readonly EmbeddingModelDownloadSpec DefaultEmbeddingDownload = new(
        "Qwen3-Embedding-0.6B",
        "Qwen3-Embedding-0.6B-Q8_0.gguf",
        "https://huggingface.co/Qwen/Qwen3-Embedding-0.6B-GGUF/resolve/370f27d7550e0def9b39c1f16d3fbaa13aa67728/Qwen3-Embedding-0.6B-Q8_0.gguf",
        "06507c7b42688469c4e7298b0a1e16deff06caf291cf0a5b278c308249c3e439");

    private readonly ISettingsService _settings;
    private readonly RuntimeProfileService _runtimes;
    private readonly IVoiceProviderRegistry _voice;
    private readonly ISecretStore _secrets;
    private readonly SqliteRagStore _ragStore;
    private readonly IEmbeddingService _embeddings;
    private readonly ISystemInfoService _systemInfo;
    private readonly PythonHealthValidator _pythonValidator;
    private readonly IReranker _reranker;
    private readonly ModelDownloadService _downloads;
    private readonly EmbeddingModelDownloadSpec _embeddingDownload;
    private readonly LlamaServerSetupService _llamaSetup;
    private readonly IRuntimeLogService? _runtimeLogs;
    private readonly AppLifecycleJournalService? _lifecycleJournal;
    private readonly IBenchmarkInsightsService? _benchmarkInsights;
    private readonly ISpeechRecognitionProviderRegistry? _sttProviders;
    private readonly IAudioCapture? _audioCapture;
    private readonly ITrayIntegrationState? _trayIntegration;

    /// <summary>
    /// Caches GitHub API release lookups (llama.cpp's update check, Hermaeus's
    /// own) for <see cref="GitHubReleaseCacheTtl"/>. Doctor runs a full scan
    /// automatically on every app startup plus every manual rescan, and
    /// GitHub's anonymous API allows only 60 requests/hour per IP; without
    /// this, a user restarting or rescanning a handful of times in an hour
    /// could exhaust that quota on background checks alone, then have an
    /// actual llama.cpp update attempt fail with a 403 rate-limit error.
    /// <see cref="DoctorService"/> is a DI singleton, so this survives across
    /// scans for the life of the app.
    /// </summary>
    private readonly Dictionary<string, (DateTimeOffset CachedAt, object? Value)> _gitHubReleaseCache = new();
    private static readonly TimeSpan GitHubReleaseCacheTtl = TimeSpan.FromHours(1);

    public DoctorService(
        ISettingsService settings,
        RuntimeProfileService runtimes,
        IVoiceProviderRegistry voice,
        ISecretStore secrets,
        SqliteRagStore ragStore,
        IEmbeddingService embeddings,
        ISystemInfoService systemInfo,
        PythonHealthValidator pythonValidator,
        IReranker reranker,
        ModelDownloadService? downloads = null,
        EmbeddingModelDownloadSpec? embeddingDownload = null,
        LlamaServerSetupService? llamaSetup = null,
        IRuntimeLogService? runtimeLogs = null,
        AppLifecycleJournalService? lifecycleJournal = null,
        IBenchmarkInsightsService? benchmarkInsights = null,
        ISpeechRecognitionProviderRegistry? sttProviders = null,
        IAudioCapture? audioCapture = null,
        ITrayIntegrationState? trayIntegration = null)
    {
        _settings = settings;
        _runtimes = runtimes;
        _voice = voice;
        _secrets = secrets;
        _ragStore = ragStore;
        _embeddings = embeddings;
        _systemInfo = systemInfo;
        _pythonValidator = pythonValidator;
        _reranker = reranker;
        _downloads = downloads ?? new ModelDownloadService();
        _embeddingDownload = embeddingDownload ?? DefaultEmbeddingDownload;
        _llamaSetup = llamaSetup ?? new LlamaServerSetupService(_downloads);
        _runtimeLogs = runtimeLogs;
        _lifecycleJournal = lifecycleJournal;
        _benchmarkInsights = benchmarkInsights;
        _sttProviders = sttProviders;
        _audioCapture = audioCapture;
        _trayIntegration = trayIntegration;
    }

    public async Task<DoctorReport> ScanAsync(CancellationToken ct = default)
    {
        var embeddingModelCheck = CheckEmbeddingModel();

        var checks = new List<DoctorCheck>
        {
            CheckCleanShutdown(),
            await CheckDataRootAsync(ct),
            await CheckAiAssetsRootAsync(ct),
            await CheckLlamaServerBinaryAsync(ct),
            await CheckLlamaServerUpdateAsync(ct),
            await CheckAppUpdateAsync(ct),
            CheckGgufModels(),
            CheckUntunedGgufModels(),
            await CheckOllamaAsync(ct),
            await CheckVoiceBackendAsync(ct),
            await CheckPythonAsync(ct),
            await CheckRagDbAsync(ct),
            embeddingModelCheck,
            await CheckEmbeddingModelVersionAsync(embeddingModelCheck, ct),
            embeddingModelCheck.Status == DoctorCheckStatus.Ready
                ? await CheckEmbeddingBackendAsync(ct)
                : CheckEmbeddingBackendSkipped(embeddingModelCheck),
            CheckRerankerAssets(),
            CheckNativeKokoroAssets(),
            await CheckSpeechRecognitionAsync(ct),
            CheckMicrophoneAsync(),
            await CheckGpuAsync(ct),
            await CheckSecretsAsync(ct),
            CheckTraySupport()
        };

        if (!OperatingSystem.IsLinux())
            checks.Add(CheckHotkeySupport());

        // r25 doc 03 3.6: only appears when a superseded model is actually on disk.
        var supersededSpeechModel = CheckSupersededSpeechModel();
        if (supersededSpeechModel is not null)
            checks.Add(supersededSpeechModel);

        var benchmarkAdvisory = await CheckBenchmarkAdvisoryAsync(ct);
        if (benchmarkAdvisory is not null)
            checks.Add(benchmarkAdvisory);

        var embeddingFallbackAdvisory = CheckEmbeddingEndpointFallbackAdvisory();
        if (embeddingFallbackAdvisory is not null)
            checks.Add(embeddingFallbackAdvisory);

        checks.AddRange(CheckOversizedContextAdvisories());
        // r27 03 3.7: only appears when a draft-* type is configured with a missing or empty draft path.
        checks.AddRange(CheckDraftModelAdvisories());
        // r28 doc 02 2.5: only appears when drafting is configured and the
        // last Speed Check for the model recorded that it never engaged.
        var draftEngagement = await CheckDraftEngagementAdvisoryAsync(ct);
        if (draftEngagement is not null)
            checks.Add(draftEngagement);

        var gpuInferenceAdvisory = await CheckGpuInferenceAdvisoryAsync(ct);
        if (gpuInferenceAdvisory is not null)
            checks.Add(gpuInferenceAdvisory);

        var errorCount = checks.Count(c => c.Status == DoctorCheckStatus.Error);
        var warningCount = checks.Count(c => c.Status == DoctorCheckStatus.Warning);
        var summary = errorCount == 0 && warningCount == 0
            ? "Doctor scan found no issues."
            : $"Doctor scan found {errorCount} error(s) and {warningCount} warning(s).";

        return new DoctorReport(checks, DateTime.UtcNow, summary);
    }

    private Task<T?> GetCachedGitHubReleaseAsync<T>(string cacheKey, Func<CancellationToken, Task<T?>> fetch, CancellationToken ct) =>
        GetCachedGitHubReleaseAsync(_gitHubReleaseCache, cacheKey, fetch, GitHubReleaseCacheTtl, ct);

    /// <summary>Fetches through <paramref name="cache"/>, keyed by
    /// <paramref name="cacheKey"/>. A failed fetch (null) is cached too, so a
    /// rate-limited or offline moment does not retry on every subsequent scan
    /// within the TTL either. Static and parameterized (rather than an
    /// instance method reading <see cref="_gitHubReleaseCache"/> directly) so
    /// the caching behaviour itself is unit-testable without a real network
    /// call.</summary>
    internal static async Task<T?> GetCachedGitHubReleaseAsync<T>(
        Dictionary<string, (DateTimeOffset CachedAt, object? Value)> cache,
        string cacheKey,
        Func<CancellationToken, Task<T?>> fetch,
        TimeSpan ttl,
        CancellationToken ct)
    {
        if (cache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.CachedAt < ttl)
            return (T?)cached.Value;

        var result = await fetch(ct);
        cache[cacheKey] = (DateTimeOffset.UtcNow, result);
        return result;
    }

    private static DoctorCheck BuildCheck(
        string key,
        string title,
        DoctorCheckStatus status,
        string summary,
        string detail,
        string fixLabel,
        bool canFix,
        string diagnostics,
        string category)
        => new(key, title, status, summary, detail, fixLabel, canFix, diagnostics, category);

    private static async Task<(bool Ok, string Detail)> TryWriteAsync(string root, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".hermaeus-write-test-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "ok", ct);
            File.Delete(probe);
            return (true, root);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
