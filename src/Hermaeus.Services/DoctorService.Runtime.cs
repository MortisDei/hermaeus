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

public sealed partial class DoctorService
{
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
                $"No llama-server executable is configured. Hermaeus can download the latest release for {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}) here, or you can set the path manually in Services.",
                "Download llama.cpp",
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
            ok ? resolved : $"Executable not found on disk or PATH: {resolved}. Hermaeus can download the latest release for {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}) here.",
            ok ? "Open Services" : "Download llama.cpp",
            true,
            resolved,
            "Runtime");
    }

    private async Task<DoctorCheck> CheckLlamaServerUpdateAsync(CancellationToken ct)
    {
        var server = _settings.Settings.ManagedServers.FirstOrDefault();
        var resolved = ResolveExecutable(server?.ExecutablePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return BuildCheck(
                "llama-server-update",
                "llama.cpp update check",
                DoctorCheckStatus.Info,
                "Update check skipped",
                "Configure llama-server before checking for updates.",
                "Open Services",
                true,
                "No executable path resolved.",
                "Runtime");
        }

        var local = await ReadLlamaServerVersionAsync(resolved, ct);
        if (local.BuildNumber is null)
        {
            return BuildCheck(
                "llama-server-update",
                "llama.cpp update check",
                DoctorCheckStatus.Warning,
                "llama-server version unknown",
                "The configured executable did not report a llama.cpp build. Update if this binary is old or not from llama.cpp.",
                "Open Services",
                true,
                $"Executable: {resolved}\nVersion output: {local.Raw}",
                "Runtime");
        }

        var latest = await TryGetLatestLlamaReleaseAsync(ct);
        if (latest is null)
        {
            return BuildCheck(
                "llama-server-update",
                "llama.cpp update check",
                DoctorCheckStatus.Info,
                $"Installed {local.Label}",
                "Could not reach GitHub releases, so Doctor could not compare against the latest llama.cpp build.",
                "Open Services",
                true,
                $"Executable: {resolved}\nVersion output: {local.Raw}",
                "Runtime");
        }

        var status = DoctorCheckStatus.Ready;
        var summary = $"Installed {local.Label}; latest {latest.TagName}";
        var detail = "llama-server appears current enough for the known release metadata.";
        if (latest.BuildNumber is int latestBuild && local.BuildNumber.Value < latestBuild)
        {
            status = DoctorCheckStatus.Warning;
            summary = $"llama-server may be outdated: {local.Label} < {latest.TagName}";
            detail = "Download a newer llama.cpp release or rerun Local AI setup.";
        }

        return BuildCheck(
            "llama-server-update",
            "llama.cpp update check",
            status,
            summary,
            detail,
            "Open Services",
            true,
            $"Executable: {resolved}\nVersion output: {local.Raw}\nLatest: {latest.TagName} ({latest.PublishedAt:O})",
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
                "Point Hermaeus at a models folder containing GGUF files.",
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

    /// <summary>
    /// Fires when a real GPU is present but inference is still configured for
    /// the CPU (r14 1.4): either the installed build has no GPU backend, or the
    /// chat server's effective offload is 0. Pure decision for tests.
    /// </summary>
    public static bool ShouldAdviseGpuInference(bool hasRealGpu, bool installedBuildIsCpu, int chatGpuLayers)
        => hasRealGpu && (installedBuildIsCpu || chatGpuLayers == 0);

    /// <summary>
    /// A quick, short-timeout probe of a managed server's <c>/health</c>
    /// endpoint (same convention as <see cref="ProcessManagement.ServerProcessManager"/>'s
    /// own health poll), used to gate advisories that only make sense while a
    /// model is actually loaded rather than merely configured.
    /// </summary>
    private static Task<bool> IsServerRespondingAsync(int port, CancellationToken ct) =>
        IsServerRespondingAsync($"http://127.0.0.1:{port}", ct);

    /// <summary>Same probe as the port overload, for a config-supplied base URL
    /// (e.g. RAG's EmbeddingBaseUrl) rather than a known-localhost port.</summary>
    private static async Task<bool> IsServerRespondingAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(1.5));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
            var response = await http.GetAsync($"{baseUrl.TrimEnd('/')}/health", timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<DoctorCheck?> CheckGpuInferenceAdvisoryAsync(CancellationToken ct)
    {
        var profile = await _systemInfo.GetHardwareProfileAsync(ct);
        var hasRealGpu = profile.MaxGpuVramBytes > 0 || !string.IsNullOrWhiteSpace(profile.GpuName);
        if (!hasRealGpu)
            return null;

        var chat = _settings.Settings.ManagedServers.FirstOrDefault(s => !s.EmbeddingsMode)
            ?? _settings.Settings.ManagedServers.FirstOrDefault();
        if (chat is null)
            return null;

        // Only warn about a model actually loaded at CPU speed right now, not
        // a stopped server's static configuration - the wasted-GPU condition
        // does not exist until a model is actually loaded.
        if (!await IsServerRespondingAsync(chat.Port, ct))
            return null;

        var chatGpuLayers = chat.GpuLayers;
        var resolvedExe = ResolveExecutable(chat.ExecutablePath ?? string.Empty);
        var installedBuildIsCpu = IsCpuOnlyBuild(resolvedExe);

        if (!ShouldAdviseGpuInference(hasRealGpu, installedBuildIsCpu, chatGpuLayers))
            return null;

        var reason = installedBuildIsCpu
            ? "the installed llama-server is a CPU-only build"
            : "the chat server is set to 0 GPU layers";
        return BuildCheck(
            "gpu-inference",
            "GPU inference",
            DoctorCheckStatus.Warning,
            $"GPU present but {reason}",
            $"{profile.GpuName ?? "A GPU"} was detected, but {reason}, so your prompts are read and generated at CPU speed. Install a GPU build in Services and set the chat server to offload all layers.",
            "Open Services",
            true,
            $"GPU: {profile.GpuName}\nInstalled build CPU-only: {installedBuildIsCpu}\nChat gpu-layers: {chatGpuLayers}",
            "Runtime");
    }

    /// <summary>
    /// True when no GPU backend runtime sits next to the executable (r14 1.4):
    /// CPU builds ship only ggml-cpu/ggml-base DLLs, GPU builds add
    /// ggml-cuda/ggml-vulkan (and cudart for CUDA). An empty/unresolved path is
    /// treated as CPU so the advisory nudges toward a real GPU install.
    /// </summary>
    private static bool IsCpuOnlyBuild(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return true;
        var dir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return true;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file).ToLowerInvariant();
                if (name.Contains("cuda") || name.Contains("vulkan") || name.Contains("cudart") || name.Contains("hip") || name.Contains("sycl"))
                    return false;
            }
            return true;
        }
        catch
        {
            return true;
        }
    }

    private DoctorCheck CheckUntunedGgufModels()
    {
        var models = LocalAiAssetLocator.FindGgufModels(_settings.Settings.DataManagement.LocalAiAssetsRoot);
        if (models.Count == 0)
        {
            return BuildCheck(
                "llama-tune-profiles",
                "llama.cpp tuned profiles",
                DoctorCheckStatus.Info,
                "No local GGUF models found",
                "Add GGUF models before tuning launch profiles.",
                "Open Benchmarks",
                true,
                "No GGUF files found under the AI assets root.",
                "Runtime");
        }

        var untuned = models.Where(model => FindTuneProfile(model) is null).ToList();
        if (untuned.Count == 0)
        {
            return BuildCheck(
                "llama-tune-profiles",
                "llama.cpp tuned profiles",
                DoctorCheckStatus.Ready,
                "All local GGUF models have tuned profiles",
                $"{models.Count} model(s) have matching saved tune profiles.",
                "Open Services",
                true,
                string.Join('\n', models),
                "Runtime");
        }

        return BuildCheck(
            "llama-tune-profiles",
            "llama.cpp tuned profiles",
            DoctorCheckStatus.Warning,
            $"{untuned.Count} GGUF model(s) need tuning",
            "Run Services auto-tune for each model before benchmarking or chatting.",
            "Open Services",
            true,
            string.Join('\n', untuned),
            "Runtime");
    }

    private const int LargeContextSizeThreshold = 16384;

    /// <summary>
    /// Large KV caches spill out of VRAM and make prompt processing crawl (r9
    /// 01-send-path-latency.md 1.5). Advisory only, no auto-tuning or
    /// clamping: the value may have been chosen deliberately.
    /// </summary>
    private List<DoctorCheck> CheckOversizedContextAdvisories()
    {
        return _settings.Settings.ManagedServers
            .Where(server => server.ContextSize > LargeContextSizeThreshold)
            .Select(server => BuildCheck(
                $"oversized-context-{server.Id}",
                $"{server.Name} context size",
                DoctorCheckStatus.Info,
                $"Large context configured ({server.ContextSize:N0})",
                $"{server.Name} is configured with ContextSize {server.ContextSize:N0}, above {LargeContextSizeThreshold:N0}. Large KV caches can spill out of VRAM and slow prompt processing and increase memory use.",
                "Open Services",
                true,
                $"ContextSize: {server.ContextSize}",
                "Runtime"))
            .ToList();
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

    public Task<bool> InstallLlamaServerUpdateAsync(CancellationToken ct = default)
        => InstallLlamaServerUpdateAsync(null, ct);

    public async Task<bool> InstallLlamaServerUpdateAsync(IProgress<string>? progress, CancellationToken ct = default)
        => (await InstallLlamaServerUpdateDetailedAsync(progress, ct)).Success;

    /// <summary>
    /// Installs the latest llama.cpp build, honouring the configured runtime
    /// variant (r14 1.1/3.4), verifying the new binary actually launches and
    /// falling back to the CPU build if it does not (r14 1.2). Returns the
    /// details the update flow needs to offer a prune of superseded versions
    /// (r14 3.2) and a restart-to-apply (r14 3.3).
    /// </summary>
    public async Task<LlamaUpdateOutcome> InstallLlamaServerUpdateDetailedAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        var installPath = ResolveLlamaServerInstallDirectory();
        var previousPath = ResolveExecutable(
            (_settings.Settings.ManagedServers.FirstOrDefault(s => !s.EmbeddingsMode)
                ?? _settings.Settings.ManagedServers.FirstOrDefault())?.ExecutablePath ?? string.Empty);

        var profile = await _systemInfo.GetHardwareProfileAsync(ct);
        var resolvedVariant = LlamaServerSetupService.ResolveVariant(_settings.Settings.DataManagement.LlamaRuntimeVariant, profile);

        var result = await _llamaSetup.InstallLatestAsync(installPath, resolvedVariant, progress, ct);
        if (result.Success && !string.IsNullOrWhiteSpace(result.UpdatedPath) && resolvedVariant != LlamaRuntimeVariant.Cpu)
        {
            // r14 1.2: a GPU build that cannot execute (missing driver/DLL)
            // reports no version. Fall back to the CPU build once (Cpu is
            // terminal, so no retry loop) rather than leaving a broken path.
            var probe = await ReadLlamaServerVersionAsync(result.UpdatedPath, ct);
            if (ShouldFallbackToCpu(resolvedVariant, probe.BuildNumber is not null))
            {
                _runtimeLogs?.Add(new RuntimeLogEntry(
                    DateTime.UtcNow,
                    RuntimeLogLevel.Warning,
                    RuntimeLogCategory.Service,
                    $"llama.cpp {LlamaServerSetupService.VariantLabel(resolvedVariant)} build did not launch (missing driver or runtime); falling back to the CPU build."));
                resolvedVariant = LlamaRuntimeVariant.Cpu;
                result = await _llamaSetup.InstallLatestAsync(installPath, LlamaRuntimeVariant.Cpu, progress, ct);
            }
        }

        if (!result.Success || string.IsNullOrWhiteSpace(result.UpdatedPath))
        {
            progress?.Report(result.Log);
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Error,
                RuntimeLogCategory.Service,
                $"llama.cpp update failed: {result.Log}"));
            return LlamaUpdateOutcome.Failed(result.Log);
        }

        foreach (var server in _settings.Settings.ManagedServers)
            server.ExecutablePath = result.UpdatedPath;

        await _settings.SaveAsync();
        progress?.Report(result.Log);
        _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Info,
            RuntimeLogCategory.Service,
            $"llama.cpp updated successfully: {result.UpdatedPath} (running servers keep the old build until restarted)."));

        var prunable = LlamaServerSetupService.SelectPrunableVersionDirectories(installPath, result.UpdatedPath, previousPath);
        return LlamaUpdateOutcome.Ok(result.UpdatedPath, installPath, prunable);
    }

    /// <summary>
    /// A non-CPU variant whose installed binary did not report a version failed
    /// to launch and must fall back to CPU (r14 1.2). CPU never falls back.
    /// Pure decision for tests.
    /// </summary>
    public static bool ShouldFallbackToCpu(LlamaRuntimeVariant installedVariant, bool versionProbeSucceeded)
        => installedVariant != LlamaRuntimeVariant.Cpu && !versionProbeSucceeded;

    public long PruneLlamaServerVersions(IReadOnlyList<string> versionDirectories)
    {
        var reclaimed = LlamaServerSetupService.PruneVersionDirectories(versionDirectories);
        if (reclaimed > 0)
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Info,
                RuntimeLogCategory.Service,
                $"Pruned superseded llama.cpp version directories, reclaimed {SystemInfoService.FormatBytes(reclaimed)}."));
        return reclaimed;
    }

    private string ResolveLlamaServerInstallDirectory()
    {
        var server = _settings.Settings.ManagedServers.FirstOrDefault(s => !s.EmbeddingsMode)
            ?? _settings.Settings.ManagedServers.FirstOrDefault();
        var executable = ResolveExecutable(server?.ExecutablePath ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(executable) && Path.IsPathFullyQualified(executable))
        {
            // r14 3.1: resolve the install root, not the current binary's own
            // (already versioned) directory, so updates never nest one tag deeper.
            var dir = Path.GetDirectoryName(executable);
            return string.IsNullOrEmpty(dir) ? executable : LlamaServerSetupService.ResolveInstallRoot(dir);
        }

        var root = _settings.Settings.DataManagement.LocalAiAssetsRoot.Trim();
        if (string.IsNullOrWhiteSpace(root))
            root = SettingsService.ResolveDataRoot(_settings.Settings);
        return _llamaSetup.GetDefaultInstallPath(root);
    }

    private static string ResolveExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return string.Empty;

        // Same resolver ServerProcessManager uses to launch (r11 1.3): a
        // directory, bare-name, or PATH answer here must agree with whether
        // the server can actually start.
        var resolution = ProcessManagement.ExecutableResolver.Resolve(executablePath.Trim(), "llama-server");
        return resolution.Success ? resolution.Path! : string.Empty;
    }

    private static async Task<LlamaVersionInfo> ReadLlamaServerVersionAsync(string executablePath, CancellationToken ct)
    {
        var output = await RunVersionCommandAsync(executablePath, "--version", ct);
        if (string.IsNullOrWhiteSpace(output))
            output = await RunVersionCommandAsync(executablePath, "--help", ct);

        var build = TryParseLlamaBuild(output);
        var label = build is int value ? $"b{value}" : "unknown build";
        return new LlamaVersionInfo(label, build, output.Trim());
    }

    private static async Task<string> RunVersionCommandAsync(string executablePath, string arg, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(arg);

        try
        {
            if (!process.Start())
                return string.Empty;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return $"{stdout}\n{stderr}".Trim();
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return string.Empty;
        }
    }

    private static async Task<LlamaLatestRelease?> TryGetLatestLlamaReleaseAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Hermaeus-Doctor/1.0");
            var release = await http.GetFromJsonAsync<GitHubRelease>(
                "https://api.github.com/repos/ggerganov/llama.cpp/releases/latest",
                timeout.Token);
            if (string.IsNullOrWhiteSpace(release?.TagName))
                return null;

            return new LlamaLatestRelease(
                release.TagName,
                TryParseLlamaBuild(release.TagName),
                release.PublishedAt ?? DateTimeOffset.MinValue);
        }
        catch
        {
            return null;
        }
    }

    public static int? TryParseLlamaBuild(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // GitHub release tags and older builds print "bNNNN" (e.g. "b4523").
        var match = Regex.Match(value, @"(?:^|[^a-zA-Z0-9])b(?<build>\d{3,6})(?:[^a-zA-Z0-9]|$)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups["build"].Value, out var build))
            return build;

        // Current llama-server --version output dropped the "b" prefix, e.g.
        // "version: 5750 (abcdef1)" or "build: 5750 (abcdef1)".
        match = Regex.Match(value, @"(?:version|build)\s*[:=]\s*(?<build>\d{3,6})", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["build"].Value, out build) ? build : null;
    }

    private LlamaTuneProfile? FindTuneProfile(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            return null;

        var normalized = Path.GetFullPath(modelPath);
        var file = new FileInfo(normalized);
        return _settings.Settings.LlamaTuneProfiles.FirstOrDefault(profile =>
            string.Equals(Path.GetFullPath(profile.ModelPath), normalized, StringComparison.OrdinalIgnoreCase)
            && profile.ModelSizeBytes == file.Length
            && profile.ModelModifiedAtUtc == file.LastWriteTimeUtc);
    }

    private sealed record LlamaVersionInfo(string Label, int? BuildNumber, string Raw);
    private sealed record LlamaLatestRelease(string TagName, int? BuildNumber, DateTimeOffset PublishedAt);
    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt);
}
