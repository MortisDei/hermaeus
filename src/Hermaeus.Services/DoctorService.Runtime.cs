using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Storage;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Voice;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Hermaeus.Services;

public sealed partial class DoctorService
{
    private const int MaxProbeOutputCharacters = 8_000;

    private async Task<DoctorCheck> CheckLlamaServerBinaryAsync(CancellationToken ct)
    {
        var configuredPath = (_settings.Settings.ManagedServers.FirstOrDefault(s => !s.EmbeddingsMode)
            ?? _settings.Settings.ManagedServers.FirstOrDefault())?.ExecutablePath ?? string.Empty;
        var resolved = ResolveManagedLlamaExecutable(configuredPath);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return BuildCheck(
                "llama-server",
                "llama-server usable",
                DoctorCheckStatus.Error,
                "llama-server not found",
                $"No usable configured or managed llama-server executable was found. Hermaeus can download the latest release for {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}) here, or you can set the path manually in Services.",
                "Download llama.cpp",
                true,
                $"Configured path: {configuredPath}\nManaged install root: {ResolveLlamaServerInstallDirectory()}",
                "Runtime");
        }

        var exists = !string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved);
        if (!exists)
        {
            return BuildCheck(
                "llama-server",
                "llama-server usable",
                DoctorCheckStatus.Error,
                "llama-server missing",
                $"Executable not found on disk or PATH: {resolved}. Hermaeus can download the latest release for {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}) here.",
                "Download llama.cpp",
                true,
                resolved,
                "Runtime");
        }

        var probe = await ReadLlamaServerVersionAsync(resolved, ct);
        if (!probe.Started)
        {
            return BuildCheck(
                "llama-server",
                "llama-server usable",
                DoctorCheckStatus.Error,
                "llama-server exists but cannot execute",
                probe.Error,
                "Download llama.cpp",
                true,
                $"Executable: {resolved}\n{probe.Error}",
                "Runtime");
        }

        if (probe.FailureKind is LlamaProbeFailureKind.NonZeroExit or LlamaProbeFailureKind.TimedOut)
        {
            return BuildCheck(
                "llama-server",
                "llama-server usable",
                DoctorCheckStatus.Error,
                DescribeLlamaProbeFailure(probe),
                "The executable started but did not complete a valid probe. Reinstall the managed llama.cpp package or correct its companion libraries.",
                "Download llama.cpp",
                true,
                FormatProbeEvidence(resolved, "--version", probe),
                "Runtime");
        }

        var healthy = probe.BuildNumber is not null;
        return BuildCheck(
            "llama-server",
            "llama-server usable",
            healthy ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            healthy ? $"llama-server executed successfully ({probe.Label})" : DescribeLlamaProbeFailure(probe),
            healthy ? resolved : "The executable ran successfully but did not report a recognizable llama.cpp build identifier.",
            "Open Services",
            true,
            FormatProbeEvidence(resolved, "--version", probe),
            "Runtime");
    }

    private async Task<DoctorCheck> CheckLlamaServerUpdateAsync(CancellationToken ct)
    {
        var server = _settings.Settings.ManagedServers.FirstOrDefault(s => !s.EmbeddingsMode)
            ?? _settings.Settings.ManagedServers.FirstOrDefault();
        var resolved = ResolveManagedLlamaExecutable(server?.ExecutablePath ?? string.Empty);
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
                "Installed identity is known. Latest release: Unknown because GitHub release metadata was unavailable. Comparison: Unknown; no update or current-state claim is made.",
                "Open Services",
                true,
                $"Executable: {resolved}\nVersion output: {local.Raw}\nInstalled: {local.Label}\nLatest: Unknown\nComparison: Unknown",
                "Runtime");
        }

        var comparison = CompareLlamaBuilds(local.BuildNumber.Value, latest.BuildNumber);
        var status = comparison == LlamaVersionComparison.Outdated
            ? DoctorCheckStatus.Warning
            : comparison == LlamaVersionComparison.Incomparable
                ? DoctorCheckStatus.Info
                : DoctorCheckStatus.Ready;
        var latestLabel = latest.FromSharedCache
            ? $"{latest.TagName} (cached {latest.MetadataObservedAt:u})"
            : latest.TagName;
        var summary = comparison == LlamaVersionComparison.Incomparable
            ? $"Installed {local.Label}; latest {latestLabel} (not comparable)"
            : $"Installed {local.Label}; latest {latestLabel}";
        var detail = comparison switch
        {
            LlamaVersionComparison.Outdated => "Download a newer llama.cpp release or rerun Local AI setup.",
            LlamaVersionComparison.Incomparable => "The installed and upstream identifiers use different schemes, so Doctor cannot determine whether this build is current.",
            _ => "The installed llama.cpp build is current for the comparable release metadata."
        };

        return BuildCheck(
            "llama-server-update",
            "llama.cpp update check",
            status,
            summary,
            detail,
            "Open Services",
            true,
            $"Executable: {resolved}\nVersion output: {local.Raw}\nLatest: {latest.TagName} ({latest.PublishedAt:O})\nMetadata source: {(latest.FromSharedCache ? $"shared cache at {latest.MetadataObservedAt:O}" : "live release lookup")}",
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
    /// chat server is explicitly configured for CPU placement. Typed Auto is
    /// not treated as CPU merely because its legacy integer is zero. Pure
    /// decision for tests.
    /// </summary>
    public static bool ShouldAdviseGpuInference(bool hasRealGpu, bool installedBuildIsCpu, GpuPlacementIntent? placement)
        => hasRealGpu && (installedBuildIsCpu || placement?.Kind == GpuPlacementKind.Cpu);

    /// <summary>
    /// Compatibility overload for callers that still have only the legacy
    /// integer form. New runtime decisions must use the typed overload above.
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
    /// (e.g. RAG's EmbeddingBaseUrl) rather than a known-localhost port.
    ///
    /// Reported a running embedding server as "not started". Two causes, both fixed
    /// here:
    ///
    /// 1. **"localhost" resolves to IPv6 ::1 first on Windows**, while llama-server
    ///    binds IPv4 127.0.0.1 only. A base URL of http://localhost:39202 therefore
    ///    failed to connect even with the server live and serving. The port overload
    ///    above never hit this because it hardcodes 127.0.0.1. A localhost host is
    ///    now retried against 127.0.0.1 before concluding anything.
    /// 2. **Only 2xx counted as responding.** A server still loading its model
    ///    answers /health with 503; that is a started server, and reporting it as
    ///    "not started" sends the user to go start the thing that is already running.
    ///    Any HTTP response now means something is listening.
    /// </summary>
    private static async Task<bool> IsServerRespondingAsync(string baseUrl, CancellationToken ct)
    {
        if (await ProbeAsync(baseUrl, ct))
            return true;

        var loopback = RewriteLocalhostToLoopback(baseUrl);
        return loopback is not null && await ProbeAsync(loopback, ct);
    }

    /// <summary>Returns the same URL with a "localhost" host swapped for 127.0.0.1, or
    /// null when the host was not localhost and there is nothing to retry.</summary>
    internal static string? RewriteLocalhostToLoopback(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/'), UriKind.Absolute, out var uri))
            return null;
        if (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            return null;

        return new UriBuilder(uri) { Host = "127.0.0.1" }.Uri.ToString().TrimEnd('/');
    }

    private static async Task<bool> ProbeAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            // Any status answers the question this probe is actually asking, which is
            // "is a server listening", not "is it healthy".
            await http.GetAsync($"{baseUrl.TrimEnd('/')}/health", timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

        if (!chat.TryGetGpuPlacement(out var placement, out _))
            return null;

        var resolvedExe = ResolveExecutable(chat.ExecutablePath ?? string.Empty);
        var installedBuildIsCpu = IsCpuOnlyBuild(resolvedExe);

        if (!ShouldAdviseGpuInference(hasRealGpu, installedBuildIsCpu, placement))
            return null;

        var reason = installedBuildIsCpu
            ? "the installed llama-server is a CPU-only build"
            : "the chat server is explicitly set to CPU placement";
        return BuildCheck(
            "gpu-inference",
            "GPU inference",
            DoctorCheckStatus.Warning,
            $"GPU present but {reason}",
            $"{profile.GpuName ?? "A GPU"} was detected, but {reason}. Install a GPU build in Services and choose Auto, All, or an exact layer count if you want accelerated inference. Effective placement remains Unknown until the running runtime reports it.",
            "Open Services",
            true,
            $"GPU: {profile.GpuName}\nInstalled build CPU-only: {installedBuildIsCpu}\nConfigured placement: {placement?.CanonicalValue ?? "Unknown"}\nEffective placement: Unknown until /props evidence is available",
            "Runtime");
    }

    /// <summary>
    /// True when no GPU backend runtime sits next to the executable (r14 1.4):
    /// CPU builds ship only ggml-cpu/ggml-base shared libraries, GPU builds add
    /// ggml-cuda/ggml-vulkan (and cudart for CUDA). An empty/unresolved path is
    /// treated as CPU so the advisory nudges toward a real GPU install.
    /// </summary>
    internal static bool IsCpuOnlyBuild(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return true;
        var dir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return true;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (!IsSharedLibrary(name))
                    continue;

                var lower = name.ToLowerInvariant();
                if (lower.Contains("cuda") || lower.Contains("vulkan") || lower.Contains("cudart") || lower.Contains("hip") || lower.Contains("sycl"))
                    return false;
            }
            return true;
        }
        catch
        {
            return true;
        }

        static bool IsSharedLibrary(string name) =>
            name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".so", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// r27 03-drafting-and-proof.md 3.7: a server configured with a
    /// <c>draft-*</c> speculative type but no draft model path, or a draft path
    /// that no longer exists on disk. Both are configurations that will fail at
    /// start, and Doctor's job is to say so before the user finds out by
    /// starting. Only appears when there is something to report.
    /// </summary>
    private List<DoctorCheck> CheckDraftModelAdvisories()
    {
        var checks = new List<DoctorCheck>();
        foreach (var server in _settings.Settings.ManagedServers)
        {
            var speculative = server.Speculative;
            if (speculative is not { RequiresDraftModel: true })
                continue;

            var path = speculative.DraftModelPath?.Trim() ?? string.Empty;
            var types = string.Join(", ", speculative.Types);

            if (path.Length == 0)
            {
                checks.Add(BuildCheck(
                    $"draft-model-{server.Id}",
                    $"{server.Name} draft model",
                    DoctorCheckStatus.Warning,
                    "Speculative decoding needs a draft model",
                    $"{server.Name} is set to {types}, which drafts from a second model file, but no draft model is selected. The server will refuse to start.",
                    "Open Services",
                    true,
                    $"Speculative types: {types}; draft model path: (empty)",
                    "Runtime"));
                continue;
            }

            if (!File.Exists(path))
            {
                checks.Add(BuildCheck(
                    $"draft-model-{server.Id}",
                    $"{server.Name} draft model",
                    DoctorCheckStatus.Warning,
                    "Draft model file is missing",
                    $"{server.Name} is set to {types} with a draft model at '{path}', which is not on disk. The server will refuse to start.",
                    "Open Services",
                    true,
                    $"Speculative types: {types}; draft model path: {path}",
                    "Runtime"));
            }
        }

        return checks;
    }

    /// <summary>
    /// r28 doc 02 2.5: speculative decoding is on and the most recent Speed
    /// Check for the default model recorded zero drafted tokens, which means
    /// the last comparison was between two identical configurations.
    ///
    /// Deterministic: it compares a setting against a recorded number. It runs
    /// nothing, does not diagnose why, and proposes no fix. "Never measured"
    /// is reported as its own state and never as "measured and found dead".
    /// </summary>
    private async Task<DoctorCheck?> CheckDraftEngagementAdvisoryAsync(CancellationToken ct)
    {
        if (_benchmarkInsights is null)
            return null;

        var modelId = _settings.Settings.Llm.DefaultModel;
        var server = _settings.Settings.ManagedServers.FirstOrDefault(s => !s.EmbeddingsMode && s.Speculative is { Types.Count: > 0 });
        if (server is null || string.IsNullOrWhiteSpace(modelId))
            return null;

        BenchmarkRun? latest;
        try
        {
            latest = await _benchmarkInsights.GetLatestSpeedCheckRunAsync(modelId, ct);
        }
        catch
        {
            // Benchmark storage being unreadable is not this check's business
            // to report; the storage checks already cover that.
            return null;
        }

        var finding = DraftEngagementAdvisory.Evaluate(server.Speculative, latest);
        var types = string.Join(", ", server.Speculative!.Types);

        return finding.State switch
        {
            DraftEngagementState.ConfiguredButNeverEngaged => BuildCheck(
                $"draft-engagement-{server.Id}",
                $"{server.Name} drafting engagement",
                DoctorCheckStatus.Warning,
                "Drafting is configured but did not engage on the last measured run",
                $"{server.Name} is set to {types}, and the most recent Speed Check for {modelId} recorded 0 drafted tokens. "
                    + "That run compared the setting against itself.",
                "Open Services",
                true,
                $"Speculative types: {types}; last Speed Check drafted tokens: 0",
                "Runtime"),

            DraftEngagementState.ConfiguredButNotReported => BuildCheck(
                $"draft-engagement-{server.Id}",
                $"{server.Name} drafting engagement",
                DoctorCheckStatus.Warning,
                "The last Speed Check ran against a server that was not drafting",
                $"{server.Name} is set to {types}, and the most recent Speed Check for {modelId} came back with no draft counters at all. "
                    + "llama-server reports them whenever speculative decoding is active, so the server that answered was started without it. "
                    + "Changing this setting does not restart a running server; restart it from Services and run the check again.",
                "Open Services",
                true,
                $"Speculative types: {types}; last Speed Check draft counters: (none reported)",
                "Runtime"),

            DraftEngagementState.NeverMeasured => BuildCheck(
                $"draft-engagement-{server.Id}",
                $"{server.Name} drafting engagement",
                DoctorCheckStatus.Info,
                "Drafting has not been measured on this model",
                $"{server.Name} is set to {types}, and {modelId} has no Speed Check run that reported draft counters. "
                    + "Whether drafting engages here is unmeasured rather than known.",
                "Open Benchmarks",
                false,
                $"Speculative types: {types}; last Speed Check drafted tokens: (none recorded)",
                "Runtime"),

            _ => null
        };
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
    /// variant and recording the selected backend separately, verifying the new binary
    /// actually launches without silently changing backend. Returns the
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
        var configuredVariant = _settings.Settings.DataManagement.LlamaRuntimeVariant;
        var resolvedVariant = LlamaServerSetupService.ResolveUpdateVariant(
            configuredVariant,
            _settings.Settings.DataManagement.InstalledLlamaRuntimeVariant,
            profile);

        var result = await _llamaSetup.InstallLatestAsync(
            installPath,
            resolvedVariant,
            progress,
            ct,
            allowAutoAcceleratedFallback: configuredVariant == LlamaRuntimeVariant.Auto);
        var installedVariant = result.SelectedVariant ?? resolvedVariant;
        if (result.Success && !string.IsNullOrWhiteSpace(result.UpdatedPath) && installedVariant != LlamaRuntimeVariant.Cpu)
        {
            // A GPU build that cannot execute (missing driver/DLL) is not safe
            // to replace with CPU: that would report success while materially
            // changing the selected backend. Leave the new path unconfigured
            // and require an explicit backend choice instead.
            var probe = await ReadLlamaServerVersionAsync(result.UpdatedPath, ct);
            var expectedBuild = TryParseLlamaBuild(result.VerifiedReleaseTag);
            var identityVerified = IsLlamaUpdateIdentityVerified(
                probe.BuildNumber,
                expectedBuild,
                IsVerifiedLlamaArtifact(result, expectedBuild));
            if (ShouldRejectGpuRuntime(installedVariant, probe.Started, probe.ExitCode, identityVerified))
            {
                var probeEvidence = FormatProbeEvidence(result.UpdatedPath, "--version", probe);
                result = result with
                {
                    Success = false,
                    Log = $"llama.cpp {LlamaServerSetupService.VariantLabel(installedVariant)} build was downloaded, but {DescribeLlamaProbeFailure(probe)}. "
                    + "The update was refused so a working GPU backend cannot be silently replaced with CPU. "
                    + "Check the backend's driver/runtime requirements or explicitly choose CPU."
                    + Environment.NewLine + "Probe diagnostics:" + Environment.NewLine + probeEvidence
                };
            }
        }

        if (!result.Success || string.IsNullOrWhiteSpace(result.UpdatedPath))
        {
            progress?.Report(SummarizeProgress(result.Log));
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Error,
                RuntimeLogCategory.Service,
                $"llama.cpp update failed: {result.Log}"));
            return LlamaUpdateOutcome.Failed(result.Log);
        }

        foreach (var server in _settings.Settings.ManagedServers)
            server.ExecutablePath = result.UpdatedPath;
        _settings.Settings.DataManagement.InstalledLlamaRuntimeVariant = installedVariant;

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
    /// A non-CPU variant is refused unless its executable starts and exits
    /// zero, then identity is established either by recognizable version text
    /// or by the separately verified release-tag and SHA256 artifact evidence.
    /// A successful process launch with neither evidence remains unsafe to
    /// install over a working GPU backend.
    /// </summary>
    public static bool ShouldRejectGpuRuntime(LlamaRuntimeVariant installedVariant, bool versionProbeSucceeded)
        => ShouldRejectGpuRuntime(installedVariant, probeStarted: true, exitCode: 0, versionProbeSucceeded);

    public static bool ShouldRejectGpuRuntime(
        LlamaRuntimeVariant installedVariant,
        bool probeStarted,
        int? exitCode,
        bool versionProbeSucceeded)
        => installedVariant != LlamaRuntimeVariant.Cpu
            && ClassifyLlamaProbe(probeStarted, exitCode, versionProbeSucceeded) != LlamaProbeFailureKind.None;

    internal static bool IsLlamaUpdateIdentityVerified(
        int? reportedBuild,
        int? expectedBuild,
        bool verifiedArtifact)
    {
        if (expectedBuild is not null && reportedBuild is not null && reportedBuild != expectedBuild)
            return false;

        return reportedBuild is not null || verifiedArtifact;
    }

    private static bool IsVerifiedLlamaArtifact(LocalAiSetupResult result, int? expectedBuild)
    {
        var sha256 = result.VerifiedArtifactSha256;
        return expectedBuild is not null
            && sha256 is { Length: 64 }
            && sha256.All(Uri.IsHexDigit);
    }

    public long PruneLlamaServerVersions(IReadOnlyList<string> versionDirectories)
    {
        var installRoot = ResolveLlamaServerInstallDirectory();
        var protectedExecutables = _settings.Settings.ManagedServers
            .Select(server => ResolveExecutable(server.ExecutablePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        var reclaimed = LlamaServerSetupService.PruneVersionDirectories(
            installRoot, versionDirectories, protectedExecutables);
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

    private string ResolveManagedLlamaExecutable(string configuredPath)
    {
        var configured = ResolveExecutable(configuredPath);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return LlamaServerSetupService.ResolveInstalledExecutable(ResolveLlamaServerInstallDirectory())
            ?? string.Empty;
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
        var result = await RunVersionCommandAsync(executablePath, "--version", ct);

        var build = TryParseLlamaBuild(result.Output);
        var label = build is int value ? $"b{value}" : "unknown build";
        return new LlamaVersionInfo(
            label,
            build,
            result.Output.Trim(),
            result.Started,
            result.ExitCode,
            result.Stdout,
            result.Stderr,
            result.Error,
            ClassifyLlamaProbe(result.Started, result.ExitCode, build is not null));
    }

    private static async Task<LlamaCommandResult> RunVersionCommandAsync(string executablePath, string arg, CancellationToken ct)
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
                return new LlamaCommandResult(false, null, string.Empty, string.Empty, "The operating system refused to start the executable.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var stdoutTask = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var stderrTask = ReadBoundedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(timeout.Token);
            return new LlamaCommandResult(true, process.ExitCode, stdoutTask.Result, stderrTask.Result, string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return new LlamaCommandResult(true, null, string.Empty, string.Empty, "The executable probe timed out after 3 seconds.");
        }
        catch (Exception ex)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return new LlamaCommandResult(false, null, string.Empty, string.Empty, ex.Message);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[2048];
        var output = new System.Text.StringBuilder();
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            var remaining = MaxProbeOutputCharacters - output.Length;
            if (remaining > 0)
                output.Append(buffer, 0, Math.Min(read, remaining));
            if (!truncated && read > remaining)
            {
                truncated = true;
                output.Append(" [truncated]");
            }
        }

        return output.ToString();
    }

    internal static LlamaProbeFailureKind ClassifyLlamaProbe(
        bool probeStarted,
        int? exitCode,
        bool buildIdentityVerified)
    {
        if (!probeStarted)
            return LlamaProbeFailureKind.CouldNotStart;
        if (exitCode is null)
            return LlamaProbeFailureKind.TimedOut;
        if (exitCode != 0)
            return LlamaProbeFailureKind.NonZeroExit;
        return buildIdentityVerified ? LlamaProbeFailureKind.None : LlamaProbeFailureKind.IdentityUnverified;
    }

    private static string DescribeLlamaProbeFailure(LlamaVersionInfo probe) => probe.FailureKind switch
    {
        LlamaProbeFailureKind.CouldNotStart => "the executable could not be started",
        LlamaProbeFailureKind.TimedOut => "the executable started but the probe timed out",
        LlamaProbeFailureKind.NonZeroExit => $"the executable started but exited with code {probe.ExitCode}",
        LlamaProbeFailureKind.IdentityUnverified => "the executable started and returned exit code 0, but no recognizable llama.cpp build identifier was found",
        _ => "the executable probe completed successfully"
    };

    private static string FormatProbeEvidence(string executable, string arguments, LlamaVersionInfo probe) => string.Join(
        Environment.NewLine,
        $"Executable: {executable}",
        $"Arguments: {arguments}",
        $"Started: {probe.Started}",
        $"Exit code: {probe.ExitCode?.ToString() ?? "unknown"}",
        $"Validation: {probe.FailureKind}",
        $"Stdout: {(string.IsNullOrWhiteSpace(probe.Stdout) ? "<empty>" : probe.Stdout)}",
        $"Stderr: {(string.IsNullOrWhiteSpace(probe.Stderr) ? "<empty>" : probe.Stderr)}",
        $"Error: {(string.IsNullOrWhiteSpace(probe.Error) ? "<none>" : probe.Error)}");

    private static string SummarizeProgress(string log)
    {
        const string marker = "Probe diagnostics:";
        var markerIndex = log.IndexOf(marker, StringComparison.Ordinal);
        return markerIndex >= 0 ? log[..markerIndex].Trim() : log;
    }

    internal static LlamaVersionComparison CompareLlamaBuilds(int installedBuild, int? latestBuild) =>
        latestBuild is null
            ? LlamaVersionComparison.Incomparable
            : installedBuild < latestBuild.Value
                ? LlamaVersionComparison.Outdated
                : LlamaVersionComparison.Current;

    private Task<LlamaLatestRelease?> TryGetLatestLlamaReleaseAsync(CancellationToken ct)
    {
        var shared = LlamaServerSetupService.LastSuccessfulRelease;
        if (shared is { } cached)
        {
            return Task.FromResult<LlamaLatestRelease?>(new(
                cached.Download.TagName,
                TryParseLlamaBuild(cached.Download.TagName),
                cached.Download.PublishedAt ?? cached.CachedAt,
                cached.CachedAt,
                true));
        }

        return GetCachedGitHubReleaseAsync("llama.cpp-latest-compatible-release", FetchLatestLlamaReleaseAsync, ct);
    }

    private async Task<LlamaLatestRelease?> FetchLatestLlamaReleaseAsync(CancellationToken ct)
    {
        try
        {
            var release = await _llamaSetup.GetLatestDownloadInfoAsync(ct);
            return new LlamaLatestRelease(
                release.TagName,
                TryParseLlamaBuild(release.TagName),
                release.PublishedAt ?? DateTimeOffset.MinValue,
                DateTimeOffset.UtcNow,
                false);
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

        // Current llama-server output uses both "build: 5750" and
        // "(build 10509, commit ...)" forms. The latter is what b10509 emits.
        match = Regex.Match(value, @"(?:version|build)\s*[:=]?\s*(?<build>\d{3,6})", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["build"].Value, out build) ? build : null;
    }

    private LlamaTuneProfile? FindTuneProfile(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            return null;

        var normalized = Path.GetFullPath(modelPath);
        var file = new FileInfo(normalized);
        return _settings.Settings.LlamaTuneProfiles.FirstOrDefault(profile =>
            ModelPathSafety.AreSameLocalPath(profile.ModelPath, normalized)
            && profile.ModelSizeBytes == file.Length
            && profile.ModelModifiedAtUtc == file.LastWriteTimeUtc);
    }

    private sealed record LlamaCommandResult(bool Started, int? ExitCode, string Stdout, string Stderr, string Error)
    {
        public string Output => string.Join(Environment.NewLine,
            new[] { Stdout, Stderr }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        public bool Success => Started && ExitCode == 0;
    }

    private sealed record LlamaVersionInfo(
        string Label,
        int? BuildNumber,
        string Raw,
        bool Started,
        int? ExitCode,
        string Stdout,
        string Stderr,
        string Error,
        LlamaProbeFailureKind FailureKind);
    private sealed record LlamaLatestRelease(
        string TagName,
        int? BuildNumber,
        DateTimeOffset PublishedAt,
        DateTimeOffset MetadataObservedAt,
        bool FromSharedCache);
}

internal enum LlamaProbeFailureKind
{
    None,
    CouldNotStart,
    TimedOut,
    NonZeroExit,
    IdentityUnverified
}

internal enum LlamaVersionComparison
{
    Current,
    Outdated,
    Incomparable
}
