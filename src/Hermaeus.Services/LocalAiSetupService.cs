using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class LocalAiSetupService
{
    private static readonly Version MinSupportedXttsPython = new(3, 9);
    private static readonly Version MaxSupportedXttsPythonExclusive = new(3, 12);

    /// <summary>
    /// r11 1.6: this was an empty map, so the "verify hash for security if
    /// available" branch in DownloadGgufModelAsync was dead code and the
    /// Phi-4 download landed unverified, contradicting the security-posture
    /// skill and the r8 StarterModelCatalog precedent. SHA256 verified via
    /// the Hugging Face LFS pointer (`raw/main/&lt;file&gt;` -&gt; `oid sha256:...`),
    /// the same method StarterModelCatalog documents, checked 2026-07-16.
    /// </summary>
    internal const string Phi4ModelUrl = "https://huggingface.co/bartowski/microsoft_Phi-4-mini-reasoning-GGUF/resolve/main/microsoft_Phi-4-mini-reasoning-Q5_K_M.gguf?download=true";
    private const string Phi4ModelSha256 = "daabb3e23ca39e969ccd1ee8eec731a164f3c1754bbc70ce946a474ce4df0d7b";

    private static readonly Dictionary<string, string> ModelHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        [Phi4ModelUrl] = Phi4ModelSha256
    };

    private readonly PythonHealthValidator _pythonValidator;
    private readonly ModelDownloadService _modelDownloader;
    private readonly LlamaServerSetupService _llamaServerSetup;
    private readonly ModelManifestStore? _modelManifest;
    private readonly ISystemInfoService? _systemInfo;

    public LocalAiSetupService(
        PythonHealthValidator pythonValidator,
        ModelDownloadService? modelDownloader = null,
        LlamaServerSetupService? llamaServerSetup = null,
        ModelManifestStore? modelManifest = null,
        ISystemInfoService? systemInfo = null)
    {
        _pythonValidator = pythonValidator;
        _modelDownloader = modelDownloader ?? new ModelDownloadService();
        _llamaServerSetup = llamaServerSetup ?? new LlamaServerSetupService();
        _modelManifest = modelManifest;
        _systemInfo = systemInfo;
    }

    public async Task<LocalAiReadinessReport> ScanAsync(AppSettings settings, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var root = settings.DataManagement.LocalAiAssetsRoot.Trim();
        var layout = LocalAiAssetLocator.Detect(root);
        var items = new List<LocalAiReadinessItem>();
        var actions = new List<LocalAiSetupAction>();

        if (string.IsNullOrWhiteSpace(layout.Root) || !Directory.Exists(layout.Root))
        {
            items.Add(new("root", "AI folder", LocalAiReadinessStatus.Missing,
                "Choose an existing folder before scanning.", "Hermaeus only scans the folder you select.", true));
            return new LocalAiReadinessReport(layout.Root, items, actions,
                "Choose an existing local AI folder first.", string.Empty);
        }

        var defaultVenv = Path.Combine(layout.Root, "venv");
        var defaultScript = Path.Combine(layout.Root, "TTS", "xtts_api_server.py");
        var defaultVoices = Path.Combine(layout.Root, "TTS", "voices");
        var defaultOutput = Path.Combine(layout.Root, "TTS", "output");
        var venvPython = string.IsNullOrWhiteSpace(layout.TtsPythonPath) ? PythonPathForVenv(defaultVenv) : layout.TtsPythonPath;
        var activeVoiceProvider = NormalizeVoiceProvider(settings.Tts.VoiceProvider);
        var voicePackages = VoicePackagesFor(activeVoiceProvider);
        var voiceProviderLabel = VoiceProviderLabel(activeVoiceProvider);
        var voiceProviderNeedsPython = activeVoiceProvider is not (VoiceProvider.OpenAi or VoiceProvider.KokoroNative);

        var hasGgufModels = !string.IsNullOrWhiteSpace(layout.ModelsDirectory)
            && Directory.Exists(layout.ModelsDirectory)
            && Directory.EnumerateFiles(layout.ModelsDirectory, "*.gguf", SearchOption.AllDirectories).Any();
        items.Add(new LocalAiReadinessItem(
            "models",
            "GGUF models",
            hasGgufModels ? LocalAiReadinessStatus.Found : LocalAiReadinessStatus.NeedsAction,
            hasGgufModels ? "Found GGUF model files." : "No GGUF model files were found.",
            hasGgufModels ? "Found model files." : "Download Phi-4 mini reasoning or add your own GGUF model.",
            true));
        var voicePackagesReady = false;
        if (voiceProviderNeedsPython)
        {
            AddItem(items, "venv", "Python venv", File.Exists(venvPython) ? venvPython : string.Empty,
                "Found Python in a local venv.", "No local venv Python was found.", true);
            if (File.Exists(venvPython))
            {
                var health = await _pythonValidator.ValidateAsync(venvPython, ct);
                var status = health.IsHealthy ? LocalAiReadinessStatus.Found : LocalAiReadinessStatus.NeedsAction;
                items.Add(new LocalAiReadinessItem(
                    "python-health",
                    "Python health",
                    status,
                    health.IsHealthy ? "Python is healthy." : "Python failed health checks.",
                    health.Detail,
                    true));

                voicePackagesReady = await HasPythonModulesAsync(venvPython, voicePackages, ct);
                items.Add(new LocalAiReadinessItem(
                    "voice-packages",
                    $"{voiceProviderLabel} packages",
                    voicePackagesReady ? LocalAiReadinessStatus.Found : LocalAiReadinessStatus.NeedsAction,
                    voicePackagesReady
                        ? $"Required {voiceProviderLabel} packages are importable."
                        : $"Missing one or more {voiceProviderLabel} packages: {string.Join(", ", voicePackages)}.",
                    voicePackagesReady ? venvPython : "Install only if this provider is selected and imports fail.",
                    true));
            }
        }
        else
        {
            items.Add(new LocalAiReadinessItem(
                "voice-native",
                $"{voiceProviderLabel} assets",
                LocalAiReadinessStatus.Found,
                $"{voiceProviderLabel} needs no Python venv; use the Doctor page to install its model assets.",
                "Handled by Doctor, not the local AI setup scan.",
                false));
        }

        if (activeVoiceProvider == VoiceProvider.XttsV2)
        {
            AddItem(items, "xtts-model", "XTTS v2 model", layout.TtsModelDirectory,
                "Found XTTS v2 model files.", "XTTS v2 model files were not found.", true);
            AddItem(items, "xtts-script", "XTTS API script", layout.TtsScriptPath,
                "Found xtts_api_server.py.", "Hermaeus can create a local API script.", true);
            AddItem(items, "voices", "Voice samples", layout.TtsVoiceDirectory,
                "Found voice sample folder.", "Voice folder can be created.", false);
        }
        AddItem(items, "output", "Audio output", layout.TtsOutputDirectory,
            "Found audio output folder.", "Output folder can be created.", false);
        AddItem(items, "reranker", "RAG reranker", layout.RerankerDirectory,
            "Found reranker model folder.", "Reranker is optional for RAG quality.", false);

        if (voiceProviderNeedsPython)
        {
            if (!File.Exists(venvPython))
                actions.Add(CreateVenvAction(defaultVenv));

            var installPythonReady = File.Exists(venvPython);
            var installPython = installPythonReady
                ? venvPython
                : PythonPathForVenv(defaultVenv);
            if (!voicePackagesReady)
                actions.Add(InstallVoiceBackendAction(installPython, voicePackages, voiceProviderLabel, installPythonReady));
        }

        if (activeVoiceProvider == VoiceProvider.XttsV2 && string.IsNullOrWhiteSpace(layout.TtsScriptPath))
            actions.Add(CreateScriptAction(defaultScript));

        if (activeVoiceProvider == VoiceProvider.XttsV2 && string.IsNullOrWhiteSpace(layout.TtsVoiceDirectory))
            actions.Add(CreateDirectoryAction("voices", "Create voice sample folder", defaultVoices));
        if (string.IsNullOrWhiteSpace(layout.TtsOutputDirectory))
            actions.Add(CreateDirectoryAction("output", "Create XTTS output folder", defaultOutput));

        // Offer to download default models if no GGUF files found
        if (!hasGgufModels)
        {
            var modelsDir = Path.Combine(layout.Root, "models");
            var phi4ModelPath = Path.Combine(modelsDir, "phi-4-mini-reasoning-Q5_K_M.gguf");
            actions.Add(DownloadGgufModelAction(phi4ModelPath, Phi4ModelUrl));

            items.Add(new("default-model", "Default reasoning model", LocalAiReadinessStatus.NeedsAction,
                "Phi-4 mini reasoning model can be downloaded automatically.",
                "Hermaeus can download the Phi-4 mini reasoning GGUF model for local reasoning tasks.",
                true));
        }

        // Offer to download llama-server if not found
        var llamaServerPath = _llamaServerSetup.GetDefaultInstallPath(layout.Root);
        if (!_llamaServerSetup.IsInstalled(llamaServerPath) && !HasConfiguredLlamaServer(settings))
        {
            actions.Add(DownloadLlamaServerAction(llamaServerPath));
            items.Add(new("llama-server", "llama-server binary", LocalAiReadinessStatus.NeedsAction,
                "llama-server binary can be downloaded automatically.",
                "Hermaeus can download the llama-server binary for running local language models.",
                true));
        }

        var missingRequired = items.Count(i => i.Required && i.Status != LocalAiReadinessStatus.Found);
        var summary = missingRequired == 0
            ? $"Local AI setup is ready under {layout.Root}."
            : $"{missingRequired} required item(s) need attention under {layout.Root}.";
        var commands = string.Join(Environment.NewLine, actions.Select(a => a.CommandPreviewText));

        return new LocalAiReadinessReport(layout.Root, items, actions, summary, commands);
    }

    public async Task<LocalAiSetupResult> RunActionAsync(
        LocalAiSetupAction action,
        AppSettings settings,
        bool allowOverwrite = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"Action: {action.Title}");
        progress?.Report($"Target: {action.TargetPath}");
        progress?.Report($"Risk: {action.RiskLabel}");

        return action.Kind switch
        {
            LocalAiSetupActionKind.CreateVenv => await CreateVenvAsync(action.TargetPath, settings, progress, ct),
            LocalAiSetupActionKind.InstallXttsDependencies => await InstallXttsAsync(action.TargetPath, settings, ExtractPackages(action.CommandPreview), progress, ct),
            LocalAiSetupActionKind.CreateXttsApiScript => CreateXttsApiScript(action.TargetPath, settings, allowOverwrite, progress),
            LocalAiSetupActionKind.CreateDirectory => CreateSupportDirectory(action.TargetPath, progress),
            LocalAiSetupActionKind.DownloadGgufModel => await DownloadGgufModelAsync(action, progress, ct),
            LocalAiSetupActionKind.DownloadTtsModel => await DownloadTtsModelAsync(action, progress, ct),
            LocalAiSetupActionKind.DownloadLlamaServer => await DownloadLlamaServerAsync(action, settings, progress, ct),
            _ => new LocalAiSetupResult(false, $"Unsupported setup action: {action.Kind}")
        };
    }

    public string BuildXttsApiScript(string? modelDirectory = null, string? outputDirectory = null)
        => LocalAiSetupScriptGenerator.BuildXttsApiScript(modelDirectory, outputDirectory);

    private async Task<LocalAiSetupResult> DownloadGgufModelAsync(
        LocalAiSetupAction action,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        progress?.Report("Downloading GGUF model...");
        try
        {
            var url = action.CommandPreview.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
                return new LocalAiSetupResult(false, "No download URL specified in action.");

            Directory.CreateDirectory(Path.GetDirectoryName(action.TargetPath) ?? action.TargetPath);
            var lastPercent = -1;
            var downloadProgress = progress is null
                ? null
                : new Progress<DownloadProgress>(state =>
                {
                    var percent = (int)Math.Floor(state.PercentComplete);
                    if (percent <= lastPercent)
                        return;

                    lastPercent = percent;
                    progress.Report($"Downloading GGUF model... {percent}%");
                });
            var result = await _modelDownloader.DownloadAsync(url, action.TargetPath, progress: downloadProgress, ct: ct);
            if (!result.Success)
                return new LocalAiSetupResult(false, result.Message);

            // Verify hash for security if available
            if (ModelHashes.TryGetValue(url, out var expectedHash))
            {
                progress?.Report("Verifying model integrity...");
                var hashValid = await _modelDownloader.VerifyHashAsync(action.TargetPath, expectedHash, progress, ct);
                if (!hashValid)
                {
                    // r11 1.6: a failed verification must not leave a
                    // corrupted/tampered file in place for something else to
                    // pick up later as if it were trusted.
                    TryDeleteFile(action.TargetPath);
                    return new LocalAiSetupResult(false, "Model hash verification failed. Downloaded file may be corrupted or tampered with.");
                }
                progress?.Report("Model integrity verified.");
            }

            if (_modelManifest is not null)
            {
                var file = new FileInfo(action.TargetPath);
                await _modelManifest.UpsertAsync(new ModelManifestEntry
                {
                    FilePath = Path.GetFullPath(action.TargetPath),
                    RepoId = TryGetHuggingFaceRepoId(url) ?? string.Empty,
                    RepoFile = Path.GetFileName(new Uri(url).AbsolutePath),
                    Sha256 = ModelHashes.GetValueOrDefault(url, string.Empty),
                    SizeBytes = file.Length,
                    Source = "local-ai-setup"
                }, ct);
            }

            return new LocalAiSetupResult(true, result.Message, action.TargetPath);
        }
        catch (OperationCanceledException)
        {
            return new LocalAiSetupResult(false, "Download cancelled.");
        }
        catch (Exception ex)
        {
            return new LocalAiSetupResult(false, $"Failed to download GGUF model: {ex.Message}");
        }
    }

    internal static string? TryGetHuggingFaceRepoId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "huggingface.co", StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? $"{segments[0]}/{segments[1]}" : null;
    }

    private async Task<LocalAiSetupResult> DownloadTtsModelAsync(
        LocalAiSetupAction action,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        progress?.Report("Downloading TTS model...");
        try
        {
            var url = action.CommandPreview.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
                return new LocalAiSetupResult(false, "No download URL specified in action.");

            Directory.CreateDirectory(Path.GetDirectoryName(action.TargetPath) ?? action.TargetPath);
            var lastPercent = -1;
            var downloadProgress = progress is null
                ? null
                : new Progress<DownloadProgress>(state =>
                {
                    var percent = (int)Math.Floor(state.PercentComplete);
                    if (percent <= lastPercent)
                        return;

                    lastPercent = percent;
                    progress.Report($"Downloading TTS model... {percent}%");
                });
            var result = await _modelDownloader.DownloadAsync(url, action.TargetPath, progress: downloadProgress, ct: ct);
            return result.Success
                ? new LocalAiSetupResult(true, result.Message, action.TargetPath)
                : new LocalAiSetupResult(false, result.Message);
        }
        catch (OperationCanceledException)
        {
            return new LocalAiSetupResult(false, "Download cancelled.");
        }
        catch (Exception ex)
        {
            return new LocalAiSetupResult(false, $"Failed to download TTS model: {ex.Message}");
        }
    }

    private async Task<LocalAiSetupResult> DownloadLlamaServerAsync(
        LocalAiSetupAction action,
        AppSettings settings,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        progress?.Report("Installing llama-server binary...");
        try
        {
            var configuredVariant = settings.DataManagement.LlamaRuntimeVariant;
            var variant = configuredVariant;
            if (variant == LlamaRuntimeVariant.Auto)
            {
                variant = settings.DataManagement.InstalledLlamaRuntimeVariant;
                if (variant == LlamaRuntimeVariant.Auto)
                {
                    if (_systemInfo is null)
                    {
                        return new LocalAiSetupResult(
                            false,
                            "Cannot choose a managed llama.cpp backend because hardware detection is unavailable. Select CPU, CUDA, or Vulkan explicitly.");
                    }

                    var profile = await _systemInfo.GetHardwareProfileAsync(ct);
                    variant = LlamaServerSetupService.ResolveVariant(LlamaRuntimeVariant.Auto, profile);
                }
            }

            var result = await _llamaServerSetup.InstallLatestAsync(
                action.TargetPath,
                variant,
                progress,
                ct,
                allowAutoAcceleratedFallback: configuredVariant == LlamaRuntimeVariant.Auto);
            if (result.Success)
                settings.DataManagement.InstalledLlamaRuntimeVariant = result.SelectedVariant ?? variant;
            return result;
        }
        catch (OperationCanceledException)
        {
            return new LocalAiSetupResult(false, "Installation cancelled.");
        }
        catch (Exception ex)
        {
            return new LocalAiSetupResult(false, $"Failed to install llama-server: {ex.Message}");
        }
    }

    private static void AddItem(
        List<LocalAiReadinessItem> items,
        string key,
        string label,
        string path,
        string ready,
        string missing,
        bool required)
    {
        items.Add(new LocalAiReadinessItem(
            key,
            label,
            string.IsNullOrWhiteSpace(path) ? (required ? LocalAiReadinessStatus.Missing : LocalAiReadinessStatus.Optional) : LocalAiReadinessStatus.Found,
            string.IsNullOrWhiteSpace(path) ? missing : path,
            ready,
            required));
    }

    private static VoiceProvider NormalizeVoiceProvider(string value) => value switch
    {
        "Kokoro" => VoiceProvider.Kokoro,
        "F5Tts" or "F5-TTS" => VoiceProvider.F5Tts,
        "XttsV2" or "XTTS" or "XTTS v2" => VoiceProvider.XttsV2,
        "OpenAi" or "OpenAI" => VoiceProvider.OpenAi,
        "KokoroNative" => VoiceProvider.KokoroNative,
        _ => VoiceProvider.KokoroNative
    };

    private static string VoiceProviderLabel(VoiceProvider provider) => provider switch
    {
        VoiceProvider.XttsV2 => "XTTS v2",
        VoiceProvider.F5Tts => "F5-TTS",
        VoiceProvider.OpenAi => "OpenAI",
        VoiceProvider.KokoroNative => "Kokoro (native)",
        _ => "Kokoro"
    };

    private static IReadOnlyList<string> VoicePackagesFor(VoiceProvider provider) => provider switch
    {
        VoiceProvider.XttsV2 => LocalAiSetupConstants.XttsPackages,
        VoiceProvider.F5Tts => ["f5-tts", "soundfile"],
        VoiceProvider.OpenAi => ["openai"],
        VoiceProvider.KokoroNative => [],
        _ => LocalAiSetupConstants.KokoroPackages
    };

    private static async Task<bool> HasPythonModulesAsync(string pythonPath, IReadOnlyList<string> modules, CancellationToken ct)
    {
        if (!File.Exists(pythonPath) || modules.Count == 0)
            return false;

        var script = string.Join(";", modules.Select(module => $"import {PythonImportName(module)}"));
        var result = await RunProcessAsync(
            pythonPath,
            ["-c", script],
            Path.GetDirectoryName(pythonPath) ?? Environment.CurrentDirectory,
            progress: null,
            ct);
        return result.Success;
    }

    private static string PythonImportName(string packageName) => packageName switch
    {
        "f5-tts" => "f5_tts",
        _ => packageName.Replace("-", "_")
    };

    private static bool HasConfiguredLlamaServer(AppSettings settings) =>
        settings.ManagedServers.Any(server => LooksLikeExistingLlamaServer(server.ExecutablePath));

    private static bool LooksLikeExistingLlamaServer(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        var trimmed = executablePath.Trim();
        if (File.Exists(trimmed))
            return Path.GetFileName(trimmed).Contains("llama-server", StringComparison.OrdinalIgnoreCase);

        return ProcessManagement.ExecutableResolver.FindOnPath(trimmed) is not null
            && Path.GetFileName(trimmed).Contains("llama-server", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ExtractPackages(IReadOnlyList<string> commandPreview)
    {
        var installIndex = commandPreview
            .Select((value, index) => new { value, index })
            .FirstOrDefault(item => string.Equals(item.value, "install", StringComparison.OrdinalIgnoreCase))
            ?.index ?? -1;
        if (installIndex < 0 || installIndex + 1 >= commandPreview.Count)
            return LocalAiSetupConstants.XttsPackages;

        return commandPreview
            .Skip(installIndex + 1)
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("-", StringComparison.Ordinal))
            .ToList();
    }

    private static LocalAiSetupAction CreateVenvAction(string target) =>
        LocalAiSetupActionFactory.CreateVenvAction(target);

    private static LocalAiSetupAction InstallVoiceBackendAction(string pythonPath, IReadOnlyList<string> packages, string providerName, bool canRun) =>
        LocalAiSetupActionFactory.InstallVoiceBackendAction(pythonPath, packages, providerName, canRun);

    private static LocalAiSetupAction CreateScriptAction(string target) =>
        LocalAiSetupActionFactory.CreateScriptAction(target);

    private static LocalAiSetupAction CreateDirectoryAction(string id, string title, string target) =>
        LocalAiSetupActionFactory.CreateDirectoryAction(id, title, target);

    private static LocalAiSetupAction DownloadGgufModelAction(string modelPath, string url) =>
        LocalAiSetupActionFactory.DownloadGgufModelAction(modelPath, url);

    private static LocalAiSetupAction DownloadTtsModelAction(string modelPath, string url) =>
        LocalAiSetupActionFactory.DownloadTtsModelAction(modelPath, url);

    private static LocalAiSetupAction DownloadLlamaServerAction(string installPath) =>
        LocalAiSetupActionFactory.DownloadLlamaServerAction(installPath);

    private static async Task<LocalAiSetupResult> CreateVenvAsync(string target, AppSettings settings, IProgress<string>? progress, CancellationToken ct)
    {
        if (Directory.Exists(target) && File.Exists(PythonPathForVenv(target)))
            return new LocalAiSetupResult(true, $"Venv already exists at {target}", PythonPathForVenv(target));

        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? target);
        var pythonCommand = await ResolveCompatiblePythonCommandAsync(progress, ct);
        if (pythonCommand is null)
            return new LocalAiSetupResult(false,
                "No compatible Python interpreter was found for XTTS. Install Python 3.9, 3.10, or 3.11 and retry.");

        var result = await RunProcessAsync(
            pythonCommand.FileName,
            [.. pythonCommand.PrefixArgs, "-m", "venv", target],
            Path.GetDirectoryName(target) ?? Environment.CurrentDirectory,
            progress,
            ct);
        // Detect GPU backend on the host and update in-memory settings so UI can persist it.
        try
        {
            var detection = await DetectGpuBackendAsync();
            settings.Tts.Device = detection.Device;
            if (!string.IsNullOrWhiteSpace(detection.Warning))
                progress?.Report(detection.Warning);
            progress?.Report($"Detected GPU backend: {detection.Device}");
        }
        catch { }

        return result with { UpdatedPath = PythonPathForVenv(target) };
    }

    private sealed record GpuBackendDetection(string Device, string? Warning);

    private static async Task<GpuBackendDetection> DetectGpuBackendAsync()
    {
        // Prefer hardware-specific accelerators before falling back to CPU.
        if (ProcessManagement.ExecutableResolver.FindOnPath("nvidia-smi") is not null)
            return new GpuBackendDetection("cuda", null);
        if (ProcessManagement.ExecutableResolver.FindOnPath("rocminfo") is not null)
            return new GpuBackendDetection("rocm", null);
        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture is Architecture.Arm64)
            return new GpuBackendDetection("mps", null);

        if (OperatingSystem.IsLinux())
        {
            var intelDetected = Directory.EnumerateFiles("/sys/class/drm", "vendor", SearchOption.AllDirectories)
                .Any(path =>
                {
                    try
                    {
                        return File.ReadAllText(path).Trim().Equals("0x8086", StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (intelDetected)
            {
                return new GpuBackendDetection(
                    "cpu",
                    "Intel GPU detected, but Hermaeus cannot auto-install a matching XTTS wheel. Falling back to CPU for XTTS setup. You can still override the device manually if your backend supports it.");
            }

            var amdDetected = Directory.EnumerateFiles("/sys/class/drm", "vendor", SearchOption.AllDirectories)
                .Any(path =>
                {
                    try
                    {
                        return File.ReadAllText(path).Trim().Equals("0x1002", StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (amdDetected)
            {
                return new GpuBackendDetection(
                    "cpu",
                    "AMD GPU detected, but ROCm was not found. Falling back to CPU for XTTS setup. Install ROCm and rerun setup to use GPU acceleration.");
            }
        }

        return new GpuBackendDetection("cpu", null);
    }

    private static Task<LocalAiSetupResult> InstallXttsAsync(string pythonPath, AppSettings settings, IReadOnlyList<string> packages, IProgress<string>? progress, CancellationToken ct)
    {
        if (!File.Exists(pythonPath))
            return Task.FromResult(new LocalAiSetupResult(false, $"Python was not found at {pythonPath}. Create or choose a venv first."));

        return InstallXttsWithRepairAsync(pythonPath, settings, packages, progress, ct);
    }

    private static async Task<LocalAiSetupResult> InstallXttsWithRepairAsync(string pythonPath, AppSettings settings, IReadOnlyList<string> packages, IProgress<string>? progress, CancellationToken ct)
    {
        var workingDirectory = Path.GetDirectoryName(pythonPath) ?? Environment.CurrentDirectory;
        var preflight = await RunProcessAsync(
            pythonPath,
            ["-c", "import encodings, sys; print(sys.executable)"],
            workingDirectory,
            progress,
            ct);

        if (!preflight.Success)
        {
            progress?.Report("Detected a broken Python runtime. Hermaeus will rebuild this venv and retry installation.");
            var repair = await RebuildVenvAsync(pythonPath, progress, ct);
            if (!repair.Success)
            {
                var combinedLog = $"{preflight.Log.TrimEnd()}{Environment.NewLine}{repair.Log}";
                return new LocalAiSetupResult(false, combinedLog, repair.UpdatedPath);
            }

            pythonPath = repair.UpdatedPath ?? pythonPath;
            workingDirectory = Path.GetDirectoryName(pythonPath) ?? Environment.CurrentDirectory;
        }

        var installingXtts = packages.Any(package => string.Equals(package, "TTS", StringComparison.OrdinalIgnoreCase));
        if (installingXtts)
        {
            var isValid = await ValidatePythonForXttsAsync(pythonPath, workingDirectory, progress, ct);
            if (!isValid)
            {
                progress?.Report("Python validation failed. Attempting venv rebuild...");
                var repair = await RebuildVenvAsync(pythonPath, progress, ct);
                if (!repair.Success)
                    return repair;

                pythonPath = repair.UpdatedPath ?? pythonPath;
                workingDirectory = Path.GetDirectoryName(pythonPath) ?? Environment.CurrentDirectory;

                isValid = await ValidatePythonForXttsAsync(pythonPath, workingDirectory, progress, ct);
                if (!isValid)
                {
                    return new LocalAiSetupResult(false,
                        "XTTS requires a valid Python 3.9-3.11 installation with encodings, venv module, and proper sys.prefix. Please install Python 3.9-3.11 on your system.");
                }
            }

            try
            {
                var detection = await DetectGpuBackendForSettingsAsync(settings, ct);
                settings.Tts.Device = detection.Device;
                if (!string.IsNullOrWhiteSpace(detection.Warning))
                    progress?.Report(detection.Warning);

                progress?.Report($"Installing PyTorch for backend: {detection.Device}...");
                var torchResult = await InstallTorchForBackendAsync(pythonPath, detection.Device, workingDirectory, progress, ct);
                if (!torchResult.Success)
                    return new LocalAiSetupResult(false, $"Failed to install PyTorch: {torchResult.Log}");
            }
            catch (Exception ex)
            {
                progress?.Report($"PyTorch auto-install skipped: {ex.Message}");
            }
        }

        return await RunProcessAsync(
            pythonPath,
            ["-m", "pip", "install", ..packages],
            workingDirectory,
            progress,
            ct);

    }

    private static async Task<LocalAiSetupResult> InstallTorchForBackendAsync(string pythonPath, string backend, string workingDirectory, IProgress<string>? progress, CancellationToken ct)
    {
        // Upgrade pip/setuptools first.
        var upgrade = await RunProcessAsync(pythonPath, ["-m", "pip", "install", "--upgrade", "pip", "setuptools", "wheel"], workingDirectory, progress, ct);
        if (!upgrade.Success)
            return upgrade;

        return await RunProcessAsync(pythonPath, BuildTorchInstallArgs(backend), workingDirectory, progress, ct);
    }

    /// <summary>
    /// r11 1.8: rocm5.8 has never been a published PyTorch index (returns
    /// HTTP 403 from download.pytorch.org) so the ROCm branch always failed
    /// pip resolution; cu118, while still valid, is years old. Verified
    /// against download.pytorch.org on 2026-07-16: cu118 through cu132 and
    /// rocm6.0 through rocm7.2 all resolve; these two are the indices
    /// pytorch.org's own install-selector currently recommends. A failed
    /// install still surfaces the attempted --index-url to the user via
    /// RunProcessAsync's "Command: ..." log line, threaded through by
    /// InstallXttsWithRepairAsync's failure message. Pulled out as a pure
    /// function so a future index change is a one-line, reviewable diff with
    /// a test that pins the expected arguments per backend.
    /// </summary>
    internal const string CudaTorchIndexUrl = "https://download.pytorch.org/whl/cu128";
    internal const string RocmTorchIndexUrl = "https://download.pytorch.org/whl/rocm6.3";
    private const string CpuTorchIndexUrl = "https://download.pytorch.org/whl/cpu";

    internal static IReadOnlyList<string> BuildTorchInstallArgs(string backend)
    {
        var args = new List<string> { "-m", "pip", "install" };
        if (backend == "cuda")
        {
            args.AddRange(["torch", "torchaudio", "torchvision", "--index-url", CudaTorchIndexUrl]);
        }
        else if (backend == "rocm")
        {
            args.AddRange(["torch", "torchaudio", "torchvision", "--index-url", RocmTorchIndexUrl]);
        }
        else if (backend == "mps")
        {
            args.AddRange(["torch", "torchaudio", "torchvision"]);
        }
        else
        {
            args.AddRange(["torch", "torchaudio", "torchvision", "--index-url", CpuTorchIndexUrl]);
        }

        return args;
    }

    private static async Task<GpuBackendDetection> DetectGpuBackendForSettingsAsync(AppSettings settings, CancellationToken ct)
    {
        var device = settings.Tts.Device.Trim().ToLowerInvariant();
        if (device is "cuda" or "rocm" or "mps" or "cpu")
            return new GpuBackendDetection(device, null);

        ct.ThrowIfCancellationRequested();
        return await DetectGpuBackendAsync();
    }

    private static async Task<LocalAiSetupResult> RebuildVenvAsync(string pythonPath, IProgress<string>? progress, CancellationToken ct)
    {
        var venvRoot = GetVenvRootFromPythonPath(pythonPath);
        if (string.IsNullOrWhiteSpace(venvRoot) || !Directory.Exists(venvRoot))
            return new LocalAiSetupResult(false, $"Cannot repair Python runtime at {pythonPath}. Select a venv Python path and retry.");

        var parent = Path.GetDirectoryName(venvRoot);
        if (string.IsNullOrWhiteSpace(parent))
            return new LocalAiSetupResult(false, $"Cannot determine parent folder for venv at {venvRoot}.");

        var backup = venvRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + $".broken-{DateTime.UtcNow:yyyyMMddHHmmss}";

        Directory.Move(venvRoot, backup);
        progress?.Report($"Backed up broken venv: {backup}");

        var pythonCommand = await ResolveCompatiblePythonCommandAsync(progress, ct);
        if (pythonCommand is null)
            return new LocalAiSetupResult(false,
                "No compatible Python interpreter was found for XTTS rebuild. Install Python 3.9, 3.10, or 3.11 and retry.");

        var recreate = await RunProcessAsync(
            pythonCommand.FileName,
            [.. pythonCommand.PrefixArgs, "-m", "venv", venvRoot],
            parent,
            progress,
            ct);

        if (!recreate.Success)
            return recreate;

        var repairedPython = PythonPathForVenv(venvRoot);
        if (!File.Exists(repairedPython))
            return new LocalAiSetupResult(false, $"Venv rebuild finished but Python was not found at {repairedPython}.");

        return new LocalAiSetupResult(true, $"Rebuilt Python venv at {venvRoot}", repairedPython);
    }

    private static string GetVenvRootFromPythonPath(string pythonPath)
    {
        if (string.IsNullOrWhiteSpace(pythonPath))
            return string.Empty;

        var binDir = Path.GetDirectoryName(pythonPath);
        if (string.IsNullOrWhiteSpace(binDir))
            return string.Empty;

        return Path.GetDirectoryName(binDir) ?? string.Empty;
    }

    private static bool IsXttsCompatibleVersion(Version version) =>
        version >= MinSupportedXttsPython && version < MaxSupportedXttsPythonExclusive;

    private static Task<bool> ValidatePythonForXttsAsync(string pythonPath, string workingDirectory, IProgress<string>? progress, CancellationToken ct) =>
        ValidatePythonForXttsAsync(pythonPath, [], workingDirectory, progress, ct);

    /// <summary>
    /// r11 1.7: runs the full candidate command (FileName + PrefixArgs, e.g.
    /// "py -3.11") instead of just FileName - testing "py -3.11" used to
    /// actually validate plain "py" (the machine default). Also the only
    /// place that compares the reported version against
    /// MinSupportedXttsPython/MaxSupportedXttsPythonExclusive; the script
    /// previously reported a version but nothing ever checked it, so a 3.13
    /// interpreter passed every check.
    /// </summary>
    private static async Task<bool> ValidatePythonForXttsAsync(string pythonPath, IReadOnlyList<string> prefixArgs, string workingDirectory, IProgress<string>? progress, CancellationToken ct)
    {
        var validationScript = """
import sys
import tempfile
import os

checks = []

# 1. Execute successfully (implicit - we're running)
checks.append(("Execute", True))

# 2. Report version 3.11.x (or compatible range)
version = f"{sys.version_info[0]}.{sys.version_info[1]}"
checks.append(("Version detection", True))
print(f"HERMAEUS_VERSION={version}")

# 3. Import encodings
try:
    import encodings
    checks.append(("Import encodings", True))
except Exception as e:
    checks.append(("Import encodings", False))
    print(f"HERMAEUS_ERROR=Failed to import encodings: {e}")

# 4. Import venv
try:
    import venv
    checks.append(("Import venv", True))
except Exception as e:
    checks.append(("Import venv", False))
    print(f"HERMAEUS_ERROR=Failed to import venv: {e}")

# 5. Check sys.prefix/sys.base_prefix are valid
bad_prefixes = ["/install", ""]
base_prefix = getattr(sys, "base_prefix", sys.prefix)
if sys.prefix in bad_prefixes or base_prefix in bad_prefixes:
    checks.append(("Valid sys.prefix", False))
    print(f"HERMAEUS_ERROR=Invalid sys.prefix ({sys.prefix}) or base_prefix ({base_prefix})")
else:
    checks.append(("Valid sys.prefix", True))

# 6. Create test venv
try:
    with tempfile.TemporaryDirectory() as tmpdir:
        test_venv = os.path.join(tmpdir, "test_venv")
        builder = venv.EnvBuilder()
        builder.create(test_venv)
        test_python = os.path.join(test_venv, "bin" if not sys.platform.startswith("win") else "Scripts", "python.exe" if sys.platform.startswith("win") else "python")
        if os.path.exists(test_python):
            checks.append(("Create test venv", True))
        else:
            checks.append(("Create test venv", False))
            print(f"HERMAEUS_ERROR=Test venv created but python not found at {test_python}")
except Exception as e:
    checks.append(("Create test venv", False))
    print(f"HERMAEUS_ERROR=Failed to create test venv: {e}")

# Report all checks
for check_name, passed in checks:
    status = "PASS" if passed else "FAIL"
    print(f"HERMAEUS_CHECK={check_name}={status}")
""";

        var result = await RunProcessAsync(
            pythonPath,
            [.. prefixArgs, "-c", validationScript],
            workingDirectory,
            progress: null,
            ct);

        if (!result.Success)
        {
            progress?.Report($"Python validation failed to execute: {result.Log}");
            return false;
        }

        var checks = new Dictionary<string, bool>();
        var errors = new List<string>();
        string? reportedVersion = null;

        // TrimEntries: the log is built with StringBuilder.AppendLine, which
        // uses Environment.NewLine ("\r\n" on Windows); splitting on '\n'
        // alone left a trailing '\r' on every line, so "PASS" never equaled
        // the parsed "PASS\r" and every check silently failed regardless of
        // the interpreter (found while adding the r11 1.7 version-range test).
        foreach (var line in result.Log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("HERMAEUS_CHECK="))
            {
                var parts = line.Substring("HERMAEUS_CHECK=".Length).Split('=');
                if (parts.Length == 2)
                {
                    checks[parts[0]] = parts[1] == "PASS";
                }
            }
            else if (line.StartsWith("HERMAEUS_ERROR="))
            {
                errors.Add(line.Substring("HERMAEUS_ERROR=".Length));
            }
            else if (line.StartsWith("HERMAEUS_VERSION="))
            {
                reportedVersion = line.Substring("HERMAEUS_VERSION=".Length);
                progress?.Report($"Validating Python {reportedVersion}...");
            }
        }

        checks["Version in supported range"] = reportedVersion is not null
            && Version.TryParse(reportedVersion, out var parsedVersion)
            && IsXttsCompatibleVersion(parsedVersion);
        if (checks["Version in supported range"] == false)
            errors.Add($"Python {reportedVersion ?? "unknown"} is outside the supported range {MinSupportedXttsPython} - {MaxSupportedXttsPythonExclusive} (exclusive).");

        var allPassed = checks.Values.All(v => v);

        if (!allPassed)
        {
            progress?.Report("Python validation failed:");
            foreach (var (check, passed) in checks)
            {
                if (!passed)
                    progress?.Report($"  - {check}: FAILED");
            }
            foreach (var error in errors)
                progress?.Report($"  - {error}");
        }
        else
        {
            progress?.Report("Python validation passed all checks.");
        }

        return allPassed;
    }

    private static async Task<PythonCommand?> ResolveCompatiblePythonCommandAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var candidates = GetPythonCommandCandidates();
        var preferredCandidates = candidates.Take(candidates.Count - 1).ToList();
        var fallbackCandidate = candidates.Last();

        foreach (var candidate in preferredCandidates)
        {
            progress?.Report($"Testing {candidate.DisplayName}...");
            if (!await ValidatePythonForXttsAsync(candidate.FileName, candidate.PrefixArgs, Environment.CurrentDirectory, progress, ct))
                continue;

            progress?.Report($"Using Python for XTTS setup: {candidate.DisplayName}");
            return candidate;
        }

        progress?.Report($"Testing {fallbackCandidate.DisplayName}...");
        if (await ValidatePythonForXttsAsync(fallbackCandidate.FileName, fallbackCandidate.PrefixArgs, Environment.CurrentDirectory, progress, ct))
        {
            progress?.Report($"Preferred Python 3.9-3.11 not found. Using {fallbackCandidate.DisplayName}. This may have compatibility issues.");
            return fallbackCandidate;
        }

        progress?.Report("No compatible Python interpreter found. XTTS requires Python 3.9-3.11. Please install one of these versions.");
        return null;
    }

    private static IReadOnlyList<PythonCommand> GetPythonCommandCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                new PythonCommand("py", ["-3.11"], "py -3.11"),
                new PythonCommand("py", ["-3.10"], "py -3.10"),
                new PythonCommand("py", ["-3.9"], "py -3.9"),
                new PythonCommand("python", [], "python")
            ];
        }

        return
        [
            new PythonCommand("python3.11", [], "python3.11"),
            new PythonCommand("python3.10", [], "python3.10"),
            new PythonCommand("python3.9", [], "python3.9"),
            new PythonCommand("python3", [], "python3")
        ];
    }

    private LocalAiSetupResult CreateXttsApiScript(string target, AppSettings settings, bool allowOverwrite, IProgress<string>? progress)
    {
        if (File.Exists(target) && !allowOverwrite)
            return new LocalAiSetupResult(false, $"Refused to overwrite existing script at {target}.");

        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? Environment.CurrentDirectory);
        WriteTextAtomic(target, LocalAiSetupScriptGenerator.BuildXttsApiScript(settings.Tts.ModelDirectory, settings.Tts.OutputDirectory));
        progress?.Report($"Wrote {target}");
        return new LocalAiSetupResult(true, $"Created XTTS API script at {target}", target);
    }

    private static void WriteTextAtomic(string path, string content)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, content, Encoding.UTF8);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
            }
        }
    }

    private static LocalAiSetupResult CreateSupportDirectory(string target, IProgress<string>? progress)
    {
        Directory.CreateDirectory(target);
        progress?.Report($"Ready: {target}");
        return new LocalAiSetupResult(true, $"Folder ready at {target}", target);
    }

    private static async Task<LocalAiSetupResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var log = new StringBuilder();
        log.AppendLine($"Command: {fileName} {string.Join(" ", args.Select(QuoteIfNeeded))}");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => AppendLine(e.Data, log, progress);
        process.ErrorDataReceived += (_, e) => AppendLine(e.Data, log, progress);

        if (!process.Start())
            return new LocalAiSetupResult(false, $"Failed to start {fileName}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new LocalAiSetupResult(process.ExitCode == 0, log.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup after cancellation.
        }
    }

    private static void TryDeleteFile(string path)
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

    private static void AppendLine(string? line, StringBuilder log, IProgress<string>? progress)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (log)
            log.AppendLine(line);
        progress?.Report(line);
    }

    private static string PythonPathForVenv(string venv) =>
        Path.Combine(venv, OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python");

    private sealed record PythonCommand(string FileName, IReadOnlyList<string> PrefixArgs, string DisplayName);

    private static string QuoteIfNeeded(string value) =>
        value.Contains(" ", StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}
