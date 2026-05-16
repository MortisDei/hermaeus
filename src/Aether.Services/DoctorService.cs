using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Embeddings;
using Aether.Rag.Storage;
using Aether.Rag.Retrieval;

namespace Aether.Services;

public sealed class DoctorService : IDoctorService
{
    private const string DefaultEmbeddingModelName = "nomic-embed-text-v1.5";
    private const string DefaultEmbeddingFileName = "nomic-embed-text-v1.5-Q4_K_M.gguf";
    private const string DefaultEmbeddingDownloadUrl = "https://huggingface.co/bartowski/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5-Q4_K_M.gguf";

    private readonly ISettingsService _settings;
    private readonly IRuntimeProfileService _runtimes;
    private readonly IVoiceProviderRegistry _voice;
    private readonly ISecretStore _secrets;
    private readonly SqliteRagStore _ragStore;
    private readonly IEmbeddingService _embeddings;
    private readonly ISystemInfoService _systemInfo;
    private readonly PythonHealthValidator _pythonValidator;
    private readonly IReranker _reranker;
    private readonly ModelDownloadService _downloads;

    public DoctorService(
        ISettingsService settings,
        IRuntimeProfileService runtimes,
        IVoiceProviderRegistry voice,
        ISecretStore secrets,
        SqliteRagStore ragStore,
        IEmbeddingService embeddings,
        ISystemInfoService systemInfo,
        PythonHealthValidator pythonValidator,
        IReranker reranker)
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
        _downloads = new ModelDownloadService();
    }

    public async Task<DoctorReport> ScanAsync(CancellationToken ct = default)
    {
        var embeddingModelCheck = CheckEmbeddingModel();

        var checks = new List<DoctorCheck>
        {
            await CheckDataRootAsync(ct),
            await CheckAiAssetsRootAsync(ct),
            CheckLlamaServerBinary(),
            CheckGgufModels(),
            await CheckOllamaAsync(ct),
            await CheckVoiceBackendAsync(ct),
            await CheckPythonAsync(ct),
            await CheckRagDbAsync(ct),
            embeddingModelCheck,
            embeddingModelCheck.Status == DoctorCheckStatus.Ready
                ? await CheckEmbeddingBackendAsync(ct)
                : CheckEmbeddingBackendSkipped(embeddingModelCheck),
            CheckRerankerAssets(),
            await CheckGpuAsync(ct),
            await CheckSecretsAsync(ct),
            CheckTraySupport()
        };

        if (!OperatingSystem.IsLinux())
            checks.Add(CheckHotkeySupport());

        var errorCount = checks.Count(c => c.Status == DoctorCheckStatus.Error);
        var warningCount = checks.Count(c => c.Status == DoctorCheckStatus.Warning);
        var summary = errorCount == 0 && warningCount == 0
            ? "Doctor scan found no issues."
            : $"Doctor scan found {errorCount} error(s) and {warningCount} warning(s).";

        return new DoctorReport(checks, DateTime.UtcNow, summary);
    }

    private async Task<DoctorCheck> CheckDataRootAsync(CancellationToken ct)
    {
        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        var (ok, detail) = await TryWriteAsync(root, ct);
        return BuildCheck(
            "data-root",
            "Data root writable",
            ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Error,
            ok ? "Data root is writable" : "Data root is not writable",
            detail,
            "Open Settings",
            true,
            detail,
            "Storage");
    }

    private async Task<DoctorCheck> CheckAiAssetsRootAsync(CancellationToken ct)
    {
        var root = _settings.Settings.DataManagement.LocalAiAssetsRoot.Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            return BuildCheck(
                "ai-root",
                "AI assets root",
                DoctorCheckStatus.Warning,
                "AI assets root not set",
                "Choose a local AI assets folder in Settings.",
                "Open Settings",
                true,
                "AI assets root is empty.",
                "Storage");
        }

        var full = Path.GetFullPath(root);
        var exists = Directory.Exists(full);
        var (ok, detail) = exists ? await TryWriteAsync(full, ct) : (false, "Folder does not exist.");

        return BuildCheck(
            "ai-root",
            "AI assets root writable",
            ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            ok ? "AI assets root is writable" : "AI assets root needs attention",
            exists ? detail : "Folder does not exist.",
            "Open Settings",
            true,
            detail,
            "Storage");
    }

    private DoctorCheck CheckLlamaServerBinary()
    {
        var server = _settings.Settings.ManagedServers.FirstOrDefault();
        if (server is null || string.IsNullOrWhiteSpace(server.ExecutablePath))
        {
            return BuildCheck(
                "llama-server",
                "llama-server found",
                DoctorCheckStatus.Error,
                "llama-server not configured",
                "Set the llama-server executable path in Services.",
                "Open Services",
                true,
                "No managed server executable configured.",
                "Runtime");
        }

        var path = server.ExecutablePath.Trim();
        var resolved = ResolveExecutable(path);
        var ok = !string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved);
        return BuildCheck(
            "llama-server",
            "llama-server found",
            ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Error,
            ok ? "llama-server available" : "llama-server missing",
            ok ? resolved : "Executable not found on disk or PATH.",
            "Open Services",
            true,
            resolved,
            "Runtime");
    }

    private DoctorCheck CheckGgufModels()
    {
        var layout = LocalAiAssetLocator.Detect(_settings.Settings.DataManagement.LocalAiAssetsRoot);
        var dir = string.IsNullOrWhiteSpace(layout.ModelsDirectory) ? _settings.Settings.DataManagement.LocalAiAssetsRoot.Trim() : layout.ModelsDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return BuildCheck(
                "gguf-models",
                "GGUF models found",
                DoctorCheckStatus.Warning,
                "No model folder detected",
                "Point Aether at a models folder containing GGUF files.",
                "Open Settings",
                true,
                dir,
                "Runtime");
        }

        var hasGguf = Directory.EnumerateFiles(dir, "*.gguf", SearchOption.AllDirectories).Any();
        return BuildCheck(
            "gguf-models",
            "GGUF models found",
            hasGguf ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            hasGguf ? "GGUF models detected" : "No GGUF models found",
            hasGguf ? dir : "Add GGUF models to your AI assets root.",
            "Open Settings",
            true,
            dir,
            "Runtime");
    }

    private async Task<DoctorCheck> CheckOllamaAsync(CancellationToken ct)
    {
        var profiles = _runtimes.Profiles.Where(p => p.Enabled && p.Kind == RuntimeKind.Ollama).ToList();
        if (profiles.Count == 0)
        {
            return BuildCheck(
                "ollama",
                "Ollama reachable",
                DoctorCheckStatus.Info,
                "Ollama not enabled",
                "Enable an Ollama runtime profile to check connectivity.",
                "Open Services",
                true,
                "No enabled Ollama profiles.",
                "Runtime");
        }

        foreach (var profile in profiles)
        {
            var health = await _runtimes.CheckHealthAsync(profile, ct);
            if (health.IsHealthy)
            {
                return BuildCheck(
                    "ollama",
                    "Ollama reachable",
                    DoctorCheckStatus.Ready,
                    "Ollama is reachable",
                    health.Message,
                    "Open Services",
                    true,
                    health.Message,
                    "Runtime");
            }
        }

        var first = profiles[0];
        return BuildCheck(
            "ollama",
            "Ollama reachable",
            DoctorCheckStatus.Warning,
            "Ollama is not reachable",
            $"Last check failed for {first.Name}.",
            "Open Services",
            true,
            first.BaseUrl,
            "Runtime");
    }

    private async Task<DoctorCheck> CheckVoiceBackendAsync(CancellationToken ct)
    {
        var provider = _voice.GetActiveVoiceProvider();
        var health = await provider.HealthCheckAsync(ct);
        var status = health.Status switch
        {
            VoiceHealthStatus.Healthy => DoctorCheckStatus.Ready,
            VoiceHealthStatus.Warning => DoctorCheckStatus.Warning,
            _ => DoctorCheckStatus.Error
        };

        return BuildCheck(
            "voice-backend",
            "Voice backend health",
            status,
            health.Summary,
            health.Detail,
            "Open Settings",
            true,
            $"Provider: {provider.DisplayName}\n{health.Detail}",
            "Voice");
    }

    private async Task<DoctorCheck> CheckPythonAsync(CancellationToken ct)
    {
        var python = _settings.Settings.Tts.PythonPath.Trim();
        var report = await _pythonValidator.ValidateAsync(python, ct);
        var status = report.IsHealthy ? DoctorCheckStatus.Ready : DoctorCheckStatus.Error;
        if (!report.IsHealthy && report.Issues.Any(i => i.Code == "version"))
            status = DoctorCheckStatus.Warning;

        return BuildCheck(
            "python",
            "Python 3.11 for voice",
            status,
            report.Summary,
            report.Detail,
            "Open Settings",
            true,
            report.Diagnostics,
            "Voice");
    }

    private async Task<DoctorCheck> CheckRagDbAsync(CancellationToken ct)
    {
        try
        {
            await _ragStore.InitializeAsync();
            var datasets = await _ragStore.GetDatasetsAsync(ct);
            return BuildCheck(
                "rag-db",
                "RAG database health",
                DoctorCheckStatus.Ready,
                "RAG database ready",
                $"Datasets: {datasets.Count}",
                "Open RAG",
                true,
                $"Datasets: {datasets.Count}",
                "RAG");
        }
        catch (Exception ex)
        {
            return BuildCheck(
                "rag-db",
                "RAG database health",
                DoctorCheckStatus.Error,
                "RAG database error",
                ex.Message,
                "Open RAG",
                true,
                ex.ToString(),
                "RAG");
        }
    }

    private async Task<DoctorCheck> CheckEmbeddingBackendAsync(CancellationToken ct)
    {
        try
        {
            var embedding = await _embeddings.EmbedAsync("doctor", ct);
            var ok = embedding.Length > 0;
            return BuildCheck(
                "embeddings",
                "Embedding backend health",
                ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
                ok ? "Embedding backend responded" : "Embedding backend returned empty response",
                ok ? $"Dimensions: {embedding.Length}" : "No embedding returned.",
                "Open Services",
                true,
                ok ? $"Dimensions: {embedding.Length}" : "No embedding returned.",
                "RAG");
        }
        catch (Exception ex)
        {
            return BuildCheck(
                "embeddings",
                "Embedding backend health",
                DoctorCheckStatus.Warning,
                "Embedding backend not reachable",
                ex.Message,
                "Open Services",
                true,
                ex.ToString(),
                "RAG");
        }
    }

    private DoctorCheck CheckEmbeddingModel()
    {
        var embeddingModel = _settings.Settings.Rag.EmbeddingModel.Trim();
        var search = FindInstalledEmbeddingModel(embeddingModel);
        if (search.Found)
        {
            return BuildCheck(
                "embedding-model",
                "Embedding model availability",
                DoctorCheckStatus.Ready,
                "Embedding model found",
                search.Path,
                "Open Services",
                true,
                search.Path,
                "RAG");
        }

        return BuildCheck(
            "embedding-model",
            "Embedding model availability",
            DoctorCheckStatus.Warning,
            "No embedding model found",
            "Download a dedicated embedding GGUF model for RAG indexing.",
            "Download embedding model",
            true,
            string.IsNullOrWhiteSpace(search.SearchedIn)
                ? "No candidate model directories were found."
                : $"Searched in: {search.SearchedIn}",
            "RAG");
    }

    private DoctorCheck CheckEmbeddingBackendSkipped(DoctorCheck modelCheck) =>
        BuildCheck(
            "embeddings",
            "Embedding backend health",
            DoctorCheckStatus.Info,
            "Embedding backend check skipped",
            "Install or select a dedicated embedding model before checking backend health.",
            "Details",
            false,
            modelCheck.Diagnostics,
            "RAG");

    private DoctorCheck CheckRerankerAssets()
    {
        if (!_settings.Settings.Rag.RerankerEnabled)
        {
            return BuildCheck(
                "reranker",
                "Reranker assets",
                DoctorCheckStatus.Info,
                "Reranker disabled",
                "Enable reranker in Settings to improve RAG quality.",
                "Open Settings",
                true,
                "Reranker disabled.",
                "RAG");
        }

        var modelDir = ResolveRerankerDirectory();
        var vocabPath = Path.Combine(modelDir, "vocab.txt");
        // Accept any ONNX file in the reranker directory (some mirrors/name variants use different filenames)
        var ok = File.Exists(vocabPath) && Directory.Exists(modelDir) && Directory.EnumerateFiles(modelDir, "*.onnx", SearchOption.TopDirectoryOnly).Any();
        return BuildCheck(
            "reranker",
            "Reranker assets",
            ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            ok ? "Reranker assets present" : "Reranker assets missing",
            ok ? modelDir : "Download or point Aether at the reranker model folder.",
            ok ? "Open Settings" : "Install Reranker",
            true,
            modelDir,
            "RAG");
    }

    public async Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default)
    {
        if (_reranker is Aether.Rag.Retrieval.OnnxCrossEncoderReranker onnx)
        {
            return await onnx.InstallAssetsAsync(null, ct);
        }

        return false;
    }

    public async Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        if (_reranker is Aether.Rag.Retrieval.OnnxCrossEncoderReranker onnx)
        {
            return await onnx.InstallAssetsAsync(progress, ct);
        }

        return false;
    }

    public Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default)
        => InstallEmbeddingModelAsync(null, ct);

    public async Task<bool> InstallEmbeddingModelAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        var destinationDirectory = ResolveEmbeddingModelDirectory();
        Directory.CreateDirectory(destinationDirectory);

        var destinationPath = Path.Combine(destinationDirectory, DefaultEmbeddingFileName);
        progress?.Report($"Downloading embedding model to {destinationPath}...");

        var downloadProgress = progress is null
            ? null
            : new Progress<DownloadProgress>(p => progress.Report($"Downloading embedding model... {p.PercentComplete:F1}%"));

        var result = await _downloads.DownloadAsync(DefaultEmbeddingDownloadUrl, destinationPath, downloadProgress, ct);
        if (!result.Success)
        {
            progress?.Report(result.Message);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_settings.Settings.Rag.EmbeddingModel)
            || !LooksLikeEmbeddingModelName(_settings.Settings.Rag.EmbeddingModel))
            _settings.Settings.Rag.EmbeddingModel = DefaultEmbeddingModelName;

        var embeddingServer = _settings.Settings.ManagedServers.FirstOrDefault(s => s.EmbeddingsMode);
        if (embeddingServer is not null && string.IsNullOrWhiteSpace(embeddingServer.ModelPath))
            embeddingServer.ModelPath = destinationPath;

        await _settings.SaveAsync();
        progress?.Report($"Embedding model ready at {destinationPath}");
        return true;
    }

    private async Task<DoctorCheck> CheckGpuAsync(CancellationToken ct)
    {
        var snapshot = await _systemInfo.CaptureAsync(ct);
        var gpu = snapshot.Gpus.FirstOrDefault();
        if (gpu is null)
        {
            return BuildCheck(
                "gpu",
                "GPU visibility",
                DoctorCheckStatus.Warning,
                "No GPU detected",
                "GPU probe returned no devices.",
                "Open System",
                true,
                "No GPU detected.",
                "System");
        }

        return BuildCheck(
            "gpu",
            "GPU visibility",
            DoctorCheckStatus.Ready,
            $"GPU: {gpu.Name}",
            gpu.Status,
            "Open System",
            true,
            $"GPU: {gpu.Name}\n{gpu.Status}",
            "System");
    }

    private async Task<DoctorCheck> CheckSecretsAsync(CancellationToken ct)
    {
        try
        {
            var backend = await _secrets.BackendLabelAsync(ct);
            return BuildCheck(
                "secrets",
                "Secrets backend",
                DoctorCheckStatus.Ready,
                "Secrets backend ready",
                backend,
                "Open Settings",
                true,
                backend,
                "Security");
        }
        catch (Exception ex)
        {
            return BuildCheck(
                "secrets",
                "Secrets backend",
                DoctorCheckStatus.Warning,
                "Secrets backend unavailable",
                ex.Message,
                "Open Settings",
                true,
                ex.ToString(),
                "Security");
        }
    }

    private DoctorCheck CheckTraySupport()
    {
        var supported = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        return BuildCheck(
            "tray",
            "Tray support",
            supported ? DoctorCheckStatus.Info : DoctorCheckStatus.Warning,
            supported ? "Tray likely supported" : "Tray not supported",
            supported ? "Depends on the desktop environment." : "Tray icons are not supported on this OS.",
            "Details",
            false,
            Environment.OSVersion.ToString(),
            "System");
    }

    private DoctorCheck CheckHotkeySupport()
    {
        var supported = OperatingSystem.IsWindows();
        return BuildCheck(
            "hotkeys",
            "Hotkey support",
            supported ? DoctorCheckStatus.Info : DoctorCheckStatus.Warning,
            supported ? "System-wide hotkeys supported" : "System-wide hotkeys unavailable",
            supported ? "Windows only for now." : "Global hotkeys are disabled on this OS.",
            "Details",
            false,
            Environment.OSVersion.ToString(),
            "System");
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

    private static string ResolveExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return string.Empty;

        var trimmed = executablePath.Trim();
        if (Path.IsPathFullyQualified(trimmed))
            return File.Exists(trimmed) ? trimmed : string.Empty;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, trimmed);
            if (File.Exists(candidate)) return candidate;
        }

        return string.Empty;
    }

    private string ResolveRerankerDirectory()
    {
        return OnnxCrossEncoderReranker.ResolveModelDirectory(_settings.Settings);
    }

    private (bool Found, string Path, string SearchedIn) FindInstalledEmbeddingModel(string embeddingModel)
    {
        var directories = GetEmbeddingCandidateDirectories();
        var markers = BuildEmbeddingMarkers(embeddingModel);

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                // Prefer marker-matched files. Do not treat arbitrary chat GGUFs as embedding models.
                var all = Directory.EnumerateFiles(dir, "*.gguf", SearchOption.AllDirectories).ToList();
                var matches = all.Where(path => markers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(path => path.Length)
                    .ToList();

                if (matches.Count > 0)
                    return (true, matches[0], string.Join(", ", directories));
            }
            catch
            {
                // Keep scanning other directories.
            }
        }

        return (false, string.Empty, string.Join(", ", directories));
    }

    private string ResolveEmbeddingModelDirectory()
    {
        var directories = GetEmbeddingCandidateDirectories();
        var existing = directories.FirstOrDefault(Directory.Exists);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        var dataRoot = SettingsService.ResolveDataRoot(_settings.Settings);
        return Path.Combine(dataRoot, "models");
    }

    private List<string> GetEmbeddingCandidateDirectories()
    {
        var settings = _settings.Settings;
        var aiRoot = settings.DataManagement.LocalAiAssetsRoot.Trim();
        var layout = LocalAiAssetLocator.Detect(aiRoot);
        var dataRoot = SettingsService.ResolveDataRoot(settings);

        return new[]
        {
            layout.ModelsDirectory,
            // Include the root folder itself in case users point directly at a model folder
            string.IsNullOrWhiteSpace(aiRoot) ? string.Empty : Path.GetFullPath(aiRoot),
            string.IsNullOrWhiteSpace(aiRoot) ? string.Empty : Path.Combine(Path.GetFullPath(aiRoot), "models"),
            string.IsNullOrWhiteSpace(aiRoot) ? string.Empty : Path.Combine(Path.GetFullPath(aiRoot), "Models"),
            Path.Combine(dataRoot, "models")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    private static List<string> BuildEmbeddingMarkers(string embeddingModel)
    {
        var markers = new List<string>
        {
            "embed",
            "embedding",
            "nomic",
            "bge",
            "e5",
            "gte"
        };

        if (!string.IsNullOrWhiteSpace(embeddingModel) && LooksLikeEmbeddingModelName(embeddingModel))
        {
            markers.Add(embeddingModel.Trim());
            markers.Add(embeddingModel.Trim().Replace('/', '-'));
        }

        return markers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool LooksLikeEmbeddingModelName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        return text.Contains("embed", StringComparison.OrdinalIgnoreCase)
               || text.Contains("embedding", StringComparison.OrdinalIgnoreCase)
               || text.Contains("nomic", StringComparison.OrdinalIgnoreCase)
               || text.Contains("bge", StringComparison.OrdinalIgnoreCase)
               || text.Contains("e5", StringComparison.OrdinalIgnoreCase)
               || text.Contains("gte", StringComparison.OrdinalIgnoreCase);
    }
}
