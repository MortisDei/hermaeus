using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Storage;

namespace Aether.Services;

public sealed class DoctorService : IDoctorService
{
    private readonly ISettingsService _settings;
    private readonly IRuntimeProfileService _runtimes;
    private readonly IVoiceProviderRegistry _voice;
    private readonly ISecretStore _secrets;
    private readonly SqliteRagStore _ragStore;
    private readonly IEmbeddingService _embeddings;
    private readonly ISystemInfoService _systemInfo;
    private readonly PythonHealthValidator _pythonValidator;

    public DoctorService(
        ISettingsService settings,
        IRuntimeProfileService runtimes,
        IVoiceProviderRegistry voice,
        ISecretStore secrets,
        SqliteRagStore ragStore,
        IEmbeddingService embeddings,
        ISystemInfoService systemInfo,
        PythonHealthValidator pythonValidator)
    {
        _settings = settings;
        _runtimes = runtimes;
        _voice = voice;
        _secrets = secrets;
        _ragStore = ragStore;
        _embeddings = embeddings;
        _systemInfo = systemInfo;
        _pythonValidator = pythonValidator;
    }

    public async Task<DoctorReport> ScanAsync(CancellationToken ct = default)
    {
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
            await CheckEmbeddingBackendAsync(ct),
            CheckRerankerAssets(),
            await CheckGpuAsync(ct),
            await CheckSecretsAsync(ct),
            CheckTraySupport(),
            CheckHotkeySupport()
        };

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
        var root = _settings.Settings.LocalAiAssetsRoot.Trim();
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
        var layout = LocalAiAssetLocator.Detect(_settings.Settings.LocalAiAssetsRoot);
        var dir = string.IsNullOrWhiteSpace(layout.ModelsDirectory) ? _settings.Settings.LocalAiAssetsRoot.Trim() : layout.ModelsDirectory;
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
        var python = _settings.Settings.TtsPythonPath.Trim();
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

    private DoctorCheck CheckRerankerAssets()
    {
        if (!_settings.Settings.RagRerankerEnabled)
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
        var modelPath = Path.Combine(modelDir, "model_O4.onnx");
        var vocabPath = Path.Combine(modelDir, "vocab.txt");
        var ok = File.Exists(modelPath) && File.Exists(vocabPath);
        return BuildCheck(
            "reranker",
            "Reranker assets",
            ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            ok ? "Reranker assets present" : "Reranker assets missing",
            ok ? modelDir : "Download or point Aether at the reranker model folder.",
            "Open Settings",
            true,
            modelDir,
            "RAG");
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
        if (!string.IsNullOrWhiteSpace(_settings.Settings.RagRerankerModelPath))
            return Path.GetFullPath(_settings.Settings.RagRerankerModelPath);

        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        return Path.Combine(root, "models", "rerank", "ms-marco-MiniLM-L6-v2");
    }
}
