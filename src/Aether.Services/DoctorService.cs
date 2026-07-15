using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Embeddings;
using Aether.Rag.Storage;
using Aether.Rag.Retrieval;
using Aether.Voice;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Aether.Services;

/// <summary>
/// Runs the Doctor scan: a flat list of independent health checks grouped by
/// domain across partial-class files (Startup, Storage, Runtime, Voice, Rag,
/// System, Benchmarks) purely to keep any one file readable. This file holds
/// construction, the scan orchestration, and the tiny helpers every check uses.
/// </summary>
public sealed partial class DoctorService : IDoctorService
{
    private static readonly EmbeddingModelDownloadSpec DefaultEmbeddingDownload = new(
        "nomic-embed-text-v1.5",
        "nomic-embed-text-v1.5-Q4_K_M.gguf",
        "https://huggingface.co/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/f750a25aba2d24830d874eb4e1af468f37248a37/nomic-embed-text-v1.5.Q4_K_M.gguf",
        "d4e388894e09cf3816e8b0896d81d265b55e7a9fff9ab03fe8bf4ef5e11295ac");

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
        IBenchmarkInsightsService? benchmarkInsights = null)
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
    }

    public async Task<DoctorReport> ScanAsync(CancellationToken ct = default)
    {
        var embeddingModelCheck = CheckEmbeddingModel();

        var checks = new List<DoctorCheck>
        {
            CheckCleanShutdown(),
            await CheckDataRootAsync(ct),
            await CheckAiAssetsRootAsync(ct),
            CheckLlamaServerBinary(),
            await CheckLlamaServerUpdateAsync(ct),
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
            await CheckGpuAsync(ct),
            await CheckSecretsAsync(ct),
            CheckTraySupport()
        };

        if (!OperatingSystem.IsLinux())
            checks.Add(CheckHotkeySupport());

        var benchmarkAdvisory = await CheckBenchmarkAdvisoryAsync(ct);
        if (benchmarkAdvisory is not null)
            checks.Add(benchmarkAdvisory);

        var embeddingFallbackAdvisory = CheckEmbeddingEndpointFallbackAdvisory();
        if (embeddingFallbackAdvisory is not null)
            checks.Add(embeddingFallbackAdvisory);

        checks.AddRange(CheckOversizedContextAdvisories());

        var errorCount = checks.Count(c => c.Status == DoctorCheckStatus.Error);
        var warningCount = checks.Count(c => c.Status == DoctorCheckStatus.Warning);
        var summary = errorCount == 0 && warningCount == 0
            ? "Doctor scan found no issues."
            : $"Doctor scan found {errorCount} error(s) and {warningCount} warning(s).";

        return new DoctorReport(checks, DateTime.UtcNow, summary);
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
            var probe = Path.Combine(root, $".aether-write-test-{Guid.NewGuid():N}");
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
