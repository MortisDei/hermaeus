using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;

namespace Hermaeus.Services.ProcessManagement;

public sealed record ManagedRuntimeProcessIdentity(int ProcessId, DateTime StartedAtUtc);

/// <summary>
/// Manages a single llama-server (or compatible) child process.
/// Launches with configured args, health-polls /health until ready,
/// pipes stdout+stderr to a capped ring buffer, and kills cleanly on Stop/Dispose.
/// </summary>
public sealed class ServerProcessManager : IDisposable
{
    private Process? _process;
    private CancellationTokenSource? _monitorCts;
    private readonly ConcurrentQueue<string> _logRing = new();
    private readonly RedactionService? _redactor;
    private readonly IProcessJobObject _jobObject;
    private readonly IPortOwnerLookup _portOwnerLookup;
    private const int MaxLogLines = 300;

    public ServerStatus Status { get; private set; } = ServerStatus.Stopped;
    public string       ErrorMessage { get; private set; } = string.Empty;

    public event Action<ServerStatus>? StatusChanged;
    public event Action<string>?       LogLine;

    public ManagedRuntimeProcessIdentity? CurrentProcessIdentity
    {
        get
        {
            var process = _process;
            try
            {
                return process is { HasExited: false }
                    ? new ManagedRuntimeProcessIdentity(process.Id, process.StartTime.ToUniversalTime())
                    : null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
    }

    private static readonly Regex OffloadedLayersRegex =
        new(@"offloaded\s+(?<used>\d+)\s*/\s*(?<total>\d+)\s+layers", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FitLayersRegex =
        new(@"Vulkan\d+.*:\s+(?<used>\d+)\s+layers", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ServerProcessManager(RedactionService? redactor = null, IProcessJobObject? jobObject = null, IPortOwnerLookup? portOwnerLookup = null)
    {
        _redactor = redactor;
        _jobObject = jobObject ?? ProcessJobObject.Default;
        _portOwnerLookup = portOwnerLookup ?? PortOwnerLookup.Default;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task StartAsync(ServerConfig cfg, CancellationToken ct = default)
    {
        if (Status is ServerStatus.Running or ServerStatus.Starting) return;

        // Port preflight (r9 02-server-lifecycle.md 2.2): a conflicting port
        // fails instantly with the port and (best-effort) its owner named,
        // instead of launching a doomed process that exits the moment it
        // tries to bind, leaving the real cause buried in the log ring.
        if (_portOwnerLookup.IsPortListening(cfg.Port))
        {
            var owner = _portOwnerLookup.FindOwner(cfg.Port);
            ErrorMessage = owner is null
                ? $"Port {cfg.Port} is already in use. Stop that process or change this server's port."
                : $"Port {cfg.Port} is already in use by {owner.ProcessName} (PID {owner.Pid}). Stop that process or change this server's port.";
            ClearLog();
            SetStatus(ServerStatus.Error);
            Emit($"[hermaeus] ERROR: {ErrorMessage}");
            return;
        }

        // Runtime help is the source of truth for speculative decoding and
        // prompt-processing threads. A saved config may outlive an executable
        // update, so do not let an old UI assumption silently become an ignored
        // flag on a new server.
        var runtime = await LocalModelCapabilityService.ProbeRuntimeAsync(cfg.ExecutablePath, ct);
        cfg.RuntimeHelpProbed = runtime.HelpProbeSucceeded;
        cfg.RuntimeSpeculativeTypes = runtime.SpeculativeTypes;
        cfg.RuntimeSupportsPromptThreads = runtime.SupportsPromptThreads;
        cfg.RuntimeSupportsLoadMode = runtime.SupportsLoadMode;
        cfg.RuntimeSupportsCorsOrigins = runtime.SupportsCorsOrigins;
        var runtimeValidation = ValidateRuntimeOptions(cfg);
        if (runtimeValidation is not null)
        {
            ErrorMessage = runtimeValidation;
            ClearLog();
            SetStatus(ServerStatus.Error);
            Emit($"[hermaeus] ERROR: {ErrorMessage}");
            return;
        }

        // r27 03-drafting-and-proof.md 3.3: a draft model that cannot verify
        // against this target is refused here, with the cause named, rather than
        // launching a doomed process. Same precedent as the port refusal above.
        var speculative = SpeculativeDecodingValidator.Validate(cfg);
        if (speculative.IsRefusal)
        {
            ErrorMessage = speculative.Message;
            ClearLog();
            SetStatus(ServerStatus.Error);
            Emit($"[hermaeus] ERROR: {ErrorMessage}");
            return;
        }

        ErrorMessage = string.Empty;
        ClearLog();
        SetStatus(ServerStatus.Starting);
        if (speculative.HasMessage)
            Emit($"[hermaeus] Warning: {speculative.Message}");

        try
        {
            cfg = NormalizeConfig(cfg);
            _process = BuildProcess(cfg);
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) Emit(e.Data); };
            _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) Emit(e.Data); };
            _process.Exited += OnProcessExited;

            if (!_process.Start())
                throw new InvalidOperationException($"Failed to start '{cfg.ExecutablePath}'");

            if (OperatingSystem.IsWindows() && !_jobObject.TryAssign(_process))
                Emit("[hermaeus] Warning: could not attach process to the app's job object; it may survive an abnormal app exit.");

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Emit($"[hermaeus] Launched PID {_process.Id} - waiting for /health on port {cfg.Port}...");
            Emit($"[hermaeus] Model: {cfg.ModelPath}");

            // r11 4.4: restart previously replaced _monitorCts without disposing
            // the one from the prior start.
            _monitorCts?.Dispose();
            _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await WaitForHealthAsync(cfg.Port, () => _process, _monitorCts.Token);

            SetStatus(ServerStatus.Running);
            Emit($"[hermaeus] Server ready on port {cfg.Port}.");
        }
        catch (OperationCanceledException)
        {
            KillProcess();
            Emit("[hermaeus] Start cancelled.");
            SetStatus(ServerStatus.Stopped);
        }
        catch (Exception ex)
        {
            KillProcess();
            ErrorMessage = BuildErrorMessage(ex);
            SetStatus(ServerStatus.Error);
            Emit($"[hermaeus] ERROR: {ex.Message}");
        }
    }

    public void Stop()
    {
        // r14 4.4: multiple shutdown paths (window close, tray exit, dispose)
        // all call Stop; an already-stopped server logs nothing and fires no
        // status change, so the runtime log shows one Stopping/Stopped pair per
        // actual shutdown instead of three.
        if (Status == ServerStatus.Stopped && _process is null)
            return;

        Emit("[hermaeus] Stopping...");
        KillProcess();
        SetStatus(ServerStatus.Stopped);
    }

    public string GetLog() => string.Join('\n', _logRing);

    public void ClearLog()
    {
        while (_logRing.TryDequeue(out _)) { }
    }

    public void RefreshStatus()
    {
        if (Status == ServerStatus.Error)
        {
            // Check if process is actually alive
            if (_process is not null && !_process.HasExited)
            {
                // Process is running despite error state, update status
                ErrorMessage = string.Empty;
                SetStatus(ServerStatus.Running);
            }
            else if (Status == ServerStatus.Error)
            {
                // Process is not running, move to stopped state
                SetStatus(ServerStatus.Stopped);
            }
        }
    }

    public void Dispose()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        KillProcess();
    }

    public static async Task<ServerTuneResult> AutoTuneAsync(
        ServerConfig cfg,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        IPortOwnerLookup? portOwnerLookup = null,
        GgufModelInfo? ggufInfo = null,
        HardwareProfile? hardware = null)
    {
        var baseConfig = NormalizeConfig(new ServerConfig
        {
            Name           = cfg.Name,
            ExecutablePath = cfg.ExecutablePath,
            ModelPath      = cfg.ModelPath,
            Port           = cfg.Port,
            ContextSize    = cfg.ContextSize,
            GpuLayers      = cfg.GpuLayers,
            Threads        = cfg.Threads,
            Slots          = cfg.Slots,
            EmbeddingsMode = cfg.EmbeddingsMode,
            AutoStart      = false,
            ExtraArgs      = cfg.ExtraArgs
        });

        // r11 1.5: probes started processes and waited for /health on
        // cfg.Port without the port preflight StartAsync performs. If
        // anything was already listening there (the orphan scenario r9 was
        // built around, or an unrelated service), every candidate "reached
        // /health" instantly against the wrong process, and auto-tune
        // reported the first candidate (999 layers) as working against a
        // foreign server's log.
        var lookup = portOwnerLookup ?? PortOwnerLookup.Default;
        if (lookup.IsPortListening(baseConfig.Port))
        {
            var owner = lookup.FindOwner(baseConfig.Port);
            var detail = owner is null
                ? $"Port {baseConfig.Port} is already in use."
                : $"Port {baseConfig.Port} is already in use by {owner.ProcessName} (PID {owner.Pid}).";
            throw new InvalidOperationException($"Cannot auto-tune: {detail} Stop that process or change this server's port first.");
        }

        var threads = ChooseThreadCount(baseConfig.Threads);

        // r17 01-gguf-context-and-tuning.md 1.5: when the configured context cannot fit,
        // tune it down to something that does instead of only shedding layers. At most one
        // extra probe (all layers, the suggested context) runs before the existing layer
        // descent; if it fails for any reason, fall through unchanged.
        var suggestedContext = ggufInfo is not null && hardware is { MaxGpuVramBytes: > 0 } && File.Exists(baseConfig.ModelPath)
            ? SuggestContextSize(
                ggufInfo,
                new FileInfo(baseConfig.ModelPath).Length,
                hardware.MaxGpuVramBytes,
                baseConfig.ContextSize,
                KvCacheMath.ResolveBytesPerElement(baseConfig.KvCacheTypeK, baseConfig.ExtraArgs, isKeyCache: true),
                KvCacheMath.ResolveBytesPerElement(baseConfig.KvCacheTypeV, baseConfig.ExtraArgs, isKeyCache: false),
                KvCacheMath.HasSwaFull(baseConfig.ExtraArgs))
            : null;

        if (suggestedContext is int tunedContext)
        {
            progress?.Report($"[hermaeus] Auto-tune: configured context {baseConfig.ContextSize:N0} does not fit this GPU with this model; probing {tunedContext:N0} context with all layers first.");
            var contextProbe = new ServerConfig
            {
                Name           = baseConfig.Name,
                ExecutablePath = baseConfig.ExecutablePath,
                ModelPath      = baseConfig.ModelPath,
                Port           = baseConfig.Port,
                ContextSize    = tunedContext,
                GpuLayers      = -1,
                Threads        = threads,
                Slots          = baseConfig.Slots,
                EmbeddingsMode = baseConfig.EmbeddingsMode,
                AutoStart      = false,
                ExtraArgs      = baseConfig.ExtraArgs
            };

            var contextResult = await TryProbeAsync(contextProbe, 999, progress, ct);
            if (contextResult.Success)
                return contextResult.TuneResult! with { TunedContextSize = tunedContext };

            progress?.Report($"[hermaeus] Auto-tune: {tunedContext:N0} context probe failed; falling back to layer descent at {baseConfig.ContextSize:N0} context.");
        }

        var candidates = BuildGpuLayerCandidates(baseConfig.GpuLayers);
        var failures = new List<string>();

        progress?.Report($"[hermaeus] Auto-tune: testing GPU layer candidates {string.Join(", ", candidates)} with {threads} thread(s).");

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var probe = new ServerConfig
            {
                Name           = baseConfig.Name,
                ExecutablePath = baseConfig.ExecutablePath,
                ModelPath      = baseConfig.ModelPath,
                Port           = baseConfig.Port,
                ContextSize    = baseConfig.ContextSize,
                GpuLayers      = candidate,
                Threads        = threads,
                Slots          = baseConfig.Slots,
                EmbeddingsMode = baseConfig.EmbeddingsMode,
                AutoStart      = false,
                ExtraArgs      = baseConfig.ExtraArgs
            };

            var result = await TryProbeAsync(probe, candidate, progress, ct);
            if (result.Success)
                return result.TuneResult!;

            failures.Add(result.Error);
        }

        throw new InvalidOperationException($"No llama.cpp auto-tune candidate started successfully.\n\n{string.Join("\n\n", failures)}");
    }

    private static async Task<ProbeResult> TryProbeAsync(
        ServerConfig probe,
        int requestedLayers,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        using var process = BuildProcess(probe);
        var lines = new ConcurrentQueue<string>();
        int? observedLayers = null;
        int? totalLayers = null;

        void HandleLine(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;

            var line = raw.Trim();
            lines.Enqueue(line);
            while (lines.Count > 80)
                lines.TryDequeue(out _);

            progress?.Report(line);
            var parsed = ParseGpuLayerLog(line);
            if (parsed.Used is int used)
                observedLayers = used;
            if (parsed.Total is int total)
                totalLayers = total;
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data);

        progress?.Report($"[hermaeus] Auto-tune: probing --n-gpu-layers {requestedLayers}...");

        try
        {
            if (!process.Start())
                return ProbeResult.Failed($"Failed to start '{probe.ExecutablePath}'.");

            if (OperatingSystem.IsWindows() && !ProcessJobObject.Default.TryAssign(process))
                progress?.Report("[hermaeus] Warning: could not attach auto-tune probe to the app's job object.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(requestedLayers == 0 ? 45 : 90));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            await WaitForHealthAsync(probe.Port, () => process, linked.Token);

            var layers = observedLayers ?? requestedLayers;
            var log = string.Join('\n', lines);
            progress?.Report($"[hermaeus] Auto-tune: candidate {requestedLayers} reached /health.");
            return ProbeResult.Ok(new ServerTuneResult(layers, totalLayers, probe.Threads, ParseLlamaBuildLabel(log), log));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ProbeResult.Failed($"Candidate {requestedLayers} failed: {ex.Message}\nRecent log:\n{string.Join('\n', lines)}");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }

    private static readonly int[] ContextSizeLadder =
        [2048, 4096, 8192, 12288, 16384, 24576, 32768, 49152, 65536, 98304, 131072];

    /// <summary>
    /// Pure helper (r17 01-gguf-context-and-tuning.md 1.5, revised r18
    /// 01-finish-the-open-work.md 1.3): the largest value from a fixed context ladder that is
    /// &lt;= min(131072, <paramref name="info"/>'s training context) and whose full-offload
    /// weights+KV projection fits <paramref name="vramBytes"/>. Unlike the r17 version, this is
    /// not bounded above by <paramref name="configuredContext"/>, so the result can suggest
    /// raising context when there is VRAM headroom, not only downshifting it - the caller must
    /// compare the result against <paramref name="configuredContext"/> to know which direction
    /// it is. Returns null when the configured context already fits and no larger ladder value
    /// also fits (nothing to suggest), when the KV shape facts or VRAM are unavailable, or when
    /// nothing on the ladder fits. <paramref name="fileSizeBytes"/> is required for the weights
    /// term of the projection even though it is not itself part of the GGUF header.
    /// <paramref name="bytesPerElementK"/>/<paramref name="bytesPerElementV"/> default to f16
    /// (byte-identical to pre-r18 behavior) - pass resolved values from
    /// <see cref="KvCacheMath.ResolveBytesPerElement(string,string?,bool)"/> so the ladder
    /// search reflects a configured KV cache type (r18 04-llama-server-engine-options.md 4.2).
    /// </summary>
    public static int? SuggestContextSize(
        GgufModelInfo info,
        long fileSizeBytes,
        long vramBytes,
        int configuredContext,
        double? bytesPerElementK = null,
        double? bytesPerElementV = null,
        bool swaFull = false)
    {
        if (vramBytes <= 0 || fileSizeBytes <= 0 || configuredContext <= 0)
            return null;

        var bpeK = bytesPerElementK ?? KvCacheMath.DefaultBytesPerElement;
        var bpeV = bytesPerElementV ?? KvCacheMath.DefaultBytesPerElement;

        bool Fits(int ctx)
        {
            var projection = KvCacheMath.Project(fileSizeBytes, info, ctx, gpuLayers: -1, bpeK, bpeV, swaFull);
            return projection is not null && projection.TotalBytes + KvCacheMath.GpuHeadroomBytes <= vramBytes;
        }

        var cap = info.TrainingContextLength is > 0
            ? Math.Min(131072, info.TrainingContextLength.Value)
            : 131072;

        int? best = null;
        foreach (var candidate in ContextSizeLadder)
        {
            if (candidate > cap) continue;
            if (Fits(candidate) && (best is null || candidate > best))
                best = candidate;
        }

        // If the configured context already fits, only suggest a change if we found a larger 
        // candidate on the ladder that also fits.
        if (Fits(configuredContext) && (best is null || best <= configuredContext))
            return null;

        return best;
    }

    public static IReadOnlyList<int> BuildGpuLayerCandidates(int configuredGpuLayers)
    {
        var candidates = new List<int>();
        if (configuredGpuLayers > 0)
            candidates.Add(configuredGpuLayers);

        candidates.AddRange([999, 128, 96, 64, 48, 32, 24, 16, 8, 4, 0]);
        return candidates
            .Where(x => x >= 0)
            .Distinct()
            .OrderByDescending(x => x)
            .ToList();
    }

    public static int ChooseThreadCount(int configuredThreads)
    {
        if (configuredThreads > 0)
            return configuredThreads;

        return Math.Clamp(Environment.ProcessorCount - 1, 1, 16);
    }

    public static (int? Used, int? Total) ParseGpuLayerLog(string line)
    {
        var offloaded = OffloadedLayersRegex.Match(line);
        if (offloaded.Success)
            return (int.Parse(offloaded.Groups["used"].Value), int.Parse(offloaded.Groups["total"].Value));

        var fitted = FitLayersRegex.Match(line);
        if (fitted.Success)
            return (int.Parse(fitted.Groups["used"].Value), null);

        return (null, null);
    }

    public static string ParseLlamaBuildLabel(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"(?:^|[^a-zA-Z0-9])b(?<build>\d{3,6})(?:[^a-zA-Z0-9]|$)", RegexOptions.IgnoreCase);
        return match.Success ? $"b{match.Groups["build"].Value}" : string.Empty;
    }

    // ── Process build ─────────────────────────────────────────────────────────

    private static Process BuildProcess(ServerConfig cfg)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName               = cfg.ExecutablePath,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = GetWorkingDirectory(cfg.ExecutablePath)
        };

        foreach (var arg in BuildLaunchArguments(cfg, cfg.ReasoningPreserveSupported, cfg.RuntimeSupportsLoadMode, cfg.RuntimeSupportsCorsOrigins))
            startInfo.ArgumentList.Add(arg);

        return new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
    }

    public static IReadOnlyList<string> BuildLaunchArguments(ServerConfig cfg, bool reasoningPreserveSupported = false,
        bool supportsLoadMode = false, bool supportsCorsOrigins = false)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(cfg.ModelPath))
        {
            parts.Add("-m");
            parts.Add(cfg.ModelPath);
        }

        parts.Add("--port");
        parts.Add(cfg.Port.ToString());
        parts.Add("--host");
        parts.Add("127.0.0.1");
        parts.Add("--ctx-size");
        parts.Add(cfg.ContextSize.ToString());

        if (cfg.Threads > 0)
        {
            parts.Add("--threads");
            parts.Add(cfg.Threads.ToString());
        }

        // r14 1.3: 0 keeps CPU inference (flag omitted); -1 offloads every
        // layer, which llama-server spells as a large finite count; N>0 offloads
        // exactly N.
        if (cfg.GpuLayers != 0)
        {
            parts.Add("--n-gpu-layers");
            parts.Add(cfg.GpuLayers < 0 ? "999" : cfg.GpuLayers.ToString());
        }

        var extraArgs = string.IsNullOrWhiteSpace(cfg.ExtraArgs)
            ? []
            : ExtraArgsParser.Split(cfg.ExtraArgs).ToList();

        // UseProjector is the authoritative launch gate for the configured
        // projector. ExtraArgs is an escape hatch for other runtime flags, but
        // it must not bypass an explicit projector-off choice with a second
        // --mmproj value.
        if (!cfg.UseProjector)
            RemoveProjectorArguments(extraArgs);

        bool HasArg(string flag) => extraArgs.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        if (cfg.PromptThreads > 0 && cfg.RuntimeSupportsPromptThreads && !HasArg("--threads-batch"))
        {
            parts.Add("--threads-batch");
            parts.Add(cfg.PromptThreads.ToString(CultureInfo.InvariantCulture));
        }

        // r14 2.1: single slot by default so the whole context belongs to one
        // conversation and every send reuses the same KV cache.
        if (!HasArg("--parallel"))
        {
            parts.Add("--parallel");
            parts.Add(Math.Max(1, cfg.Slots).ToString());
        }

        // r14 2.2: let edited/re-rolled prompts reuse KV chunks past the first
        // divergence instead of only the longest exact prefix.
        if (!HasArg("--cache-reuse"))
        {
            parts.Add("--cache-reuse");
            parts.Add("256");
        }

        if (cfg.EmbeddingsMode)
        {
            parts.Add("--embeddings");

            if (!HasArg("--pooling"))
            {
                parts.Add("--pooling");
                parts.Add("mean");
            }

            // r14 2.4 set this pair to a hardcoded 512 so llama-server would
            // stop logging a clamp warning at every start. That silenced the
            // warning and introduced a much worse defect, found in a real
            // runtime log: the physical batch is the largest input the server
            // will embed AT ALL, and anything bigger is refused outright with
            //   "input (N tokens) is too large to process.
            //    increase the physical batch size (current batch size: 512)"
            //
            // RAG chunks default to 1600 characters plus 320 of overlap, which
            // is 500 to 650 real tokens for prose and denser again for code, so
            // a large share of every ingest was being rejected. One owner log
            // carried 846 of those errors. Nothing surfaced it: ingestion
            // reported success for the chunks that fit and the rest went to the
            // runtime log, so the feature looked like it worked.
            //
            // The batch now follows the context size, which is the real ceiling
            // on a single embedding input anyway: if it fits the context, the
            // server can embed it. The pair stays equal so the clamp warning
            // r14 was chasing still never appears.
            var embeddingBatch = Math.Max(512, cfg.ContextSize).ToString(CultureInfo.InvariantCulture);
            if (!HasArg("-b") && !HasArg("--batch-size"))
            {
                parts.Add("-b");
                parts.Add(embeddingBatch);
            }
            if (!HasArg("-ub") && !HasArg("--ubatch-size"))
            {
                parts.Add("-ub");
                parts.Add(embeddingBatch);
            }
        }

        // r18 04-llama-server-engine-options.md 4.1: first-class engine options. Defaults match
        // today's exact command line (f16 KV cache, auto flash attention, context shift/mlock/
        // no-mmap all off) so an older saved config launches byte-identically; ExtraArgs always
        // wins over any of these, exactly like --parallel and --cache-reuse above.
        var kvCacheType = EffectiveKvCacheType(cfg);
        if (!string.Equals(kvCacheType, "f16", StringComparison.OrdinalIgnoreCase) && !HasArg("--cache-type-k"))
        {
            parts.Add("--cache-type-k");
            parts.Add(kvCacheType);
        }

        if (!string.Equals(kvCacheType, "f16", StringComparison.OrdinalIgnoreCase) && !HasArg("--cache-type-v"))
        {
            parts.Add("--cache-type-v");
            parts.Add(kvCacheType);
        }

        if (reasoningPreserveSupported
            && !HasArg("--reasoning-preserve")
            && !HasArg("--no-reasoning-preserve"))
        {
            parts.Add(cfg.PreserveReasoning ? "--reasoning-preserve" : "--no-reasoning-preserve");
        }

        // "auto" is the server's own default and emits nothing.
        if (!string.Equals(cfg.FlashAttention, "auto", StringComparison.OrdinalIgnoreCase) && !HasArg("--flash-attn") && !HasArg("-fa"))
        {
            parts.Add("--flash-attn");
            parts.Add(cfg.FlashAttention.ToLowerInvariant());
        }

        if (cfg.ContextShift && !HasArg("--context-shift") && !HasArg("--no-context-shift"))
            parts.Add("--context-shift");

        if (supportsLoadMode && !HasArg("--load-mode") && !HasArg("--mlock") && !HasArg("--mmap") && !HasArg("--no-mmap"))
        {
            if (cfg.MemoryLock)
            {
                parts.Add("--load-mode");
                parts.Add("mlock");
            }
            else if (cfg.NoMemoryMap)
            {
                parts.Add("--load-mode");
                parts.Add("none");
            }
        }
        else
        {
            if (cfg.MemoryLock && !HasArg("--mlock"))
                parts.Add("--mlock");

            if (cfg.NoMemoryMap && !HasArg("--no-mmap") && !HasArg("--mmap"))
                parts.Add("--no-mmap");
        }

        // Managed llama-server is loopback-bound, but its default wildcard CORS
        // policy still permits arbitrary web origins to read that local HTTP
        // service. Add the narrower policy only after the selected executable
        // advertises the option. External/custom servers remain untouched.
        if (supportsCorsOrigins && !HasArg("--cors-origins"))
        {
            parts.Add("--cors-origins");
            parts.Add("http://localhost,http://127.0.0.1");
        }

        // Mixture-of-Experts CPU offload. Flag names read from llama-server
        // b10215's own --help, per the r27 rule that only flags the installed
        // binary actually lists appear here:
        //   -cmoe,  --cpu-moe       keep all MoE weights in the CPU
        //   -ncmoe, --n-cpu-moe N   keep the MoE weights of the first N layers in the CPU
        // 0 emits nothing, which is the pre-0.36 command line exactly.
        if (cfg.CpuMoeLayers != 0
            && !HasArg("--cpu-moe") && !HasArg("-cmoe")
            && !HasArg("--n-cpu-moe") && !HasArg("-ncmoe"))
        {
            if (cfg.CpuMoeLayers < 0)
            {
                parts.Add("--cpu-moe");
            }
            else
            {
                parts.Add("--n-cpu-moe");
                parts.Add(cfg.CpuMoeLayers.ToString(CultureInfo.InvariantCulture));
            }
        }

        // r27 03-drafting-and-proof.md 3.2: speculative decoding, with the flag
        // names the installed binary (b10195) actually lists. --draft-max,
        // --draft-min, --draft-n, --spec-ngram-size-n and friends have been
        // REMOVED upstream: they now print "the argument has been removed" and
        // do nothing, so emitting one would look like it worked and change
        // nothing measurable. Only flags read from --help appear here.
        // r18 4.4's NgramSpeculative bool is upgraded into Speculative.Types by
        // SettingsService.NormalizeManagedServers before this ever runs.
        var speculative = cfg.Speculative;
        if (speculative is { Types.Count: > 0 } && !HasArg("--spec-type"))
        {
            var types = speculative.Types
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (types.Count > 0)
            {
                // --spec-type takes a comma-separated list, which is why this is
                // one section rather than a bool per technique.
                parts.Add("--spec-type");
                parts.Add(string.Join(",", types));

                if (speculative.RequiresDraftModel && !string.IsNullOrWhiteSpace(speculative.DraftModelPath) && !HasArg("--spec-draft-model") && !HasArg("-md") && !HasArg("--model-draft"))
                {
                    parts.Add("--spec-draft-model");
                    parts.Add(speculative.DraftModelPath);
                }

                if (speculative.RequiresDraftModel && speculative.DraftGpuLayers is { } draftLayers && draftLayers >= 0 && !HasArg("-ngld") && !HasArg("--gpu-layers-draft") && !HasArg("--spec-draft-ngl"))
                {
                    parts.Add("-ngld");
                    parts.Add(draftLayers == 0 ? "0" : draftLayers.ToString(CultureInfo.InvariantCulture));
                }

                if (speculative.NMax is { } nMax && nMax >= 0 && !HasArg("--spec-draft-n-max"))
                {
                    parts.Add("--spec-draft-n-max");
                    parts.Add(nMax.ToString(CultureInfo.InvariantCulture));
                }

                if (speculative.NMin is { } nMin && nMin >= 0 && !HasArg("--spec-draft-n-min"))
                {
                    parts.Add("--spec-draft-n-min");
                    parts.Add(nMin.ToString(CultureInfo.InvariantCulture));
                }

                if (speculative.PMin is { } pMin && pMin >= 0 && !HasArg("--spec-draft-p-min") && !HasArg("--draft-p-min"))
                {
                    parts.Add("--spec-draft-p-min");
                    parts.Add(pMin.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }
        }

        // r19 5.3: enables llama-server's multimodal chat mode (image content parts).
        if (cfg.UseProjector && !string.IsNullOrWhiteSpace(cfg.MmprojPath) && !HasArg("--mmproj"))
        {
            parts.Add("--mmproj");
            parts.Add(cfg.MmprojPath);
        }

        if (extraArgs.Count > 0)
            parts.AddRange(extraArgs);

        return parts;
    }

    private static void RemoveProjectorArguments(List<string> args)
    {
        for (var i = args.Count - 1; i >= 0; i--)
        {
            var argument = args[i];
            if (argument.StartsWith("--mmproj=", StringComparison.OrdinalIgnoreCase))
            {
                args.RemoveAt(i);
                continue;
            }

            if (!string.Equals(argument, "--mmproj", StringComparison.OrdinalIgnoreCase))
                continue;

            args.RemoveAt(i);
            if (i < args.Count && !args[i].StartsWith("-", StringComparison.Ordinal))
                args.RemoveAt(i);
        }
    }

    private static string EffectiveKvCacheType(ServerConfig cfg) =>
        !string.IsNullOrWhiteSpace(cfg.KvCacheType) && !string.Equals(cfg.KvCacheType, "f16", StringComparison.OrdinalIgnoreCase)
            ? cfg.KvCacheType
            : !string.IsNullOrWhiteSpace(cfg.KvCacheTypeK) && !string.Equals(cfg.KvCacheTypeK, "f16", StringComparison.OrdinalIgnoreCase)
                ? cfg.KvCacheTypeK
                : "f16";

    private static ServerConfig NormalizeConfig(ServerConfig cfg)
    {
        if (cfg.Port < 1 || cfg.Port > 65535)
            throw new ArgumentOutOfRangeException(nameof(cfg.Port), cfg.Port, "Port must be between 1 and 65535");

        // r11 4.5: resolution results are launch-time values, not
        // configuration edits. Mutating the caller's ServerConfig in place
        // (typically the settings object itself) silently rewrote a
        // directory or bare-name configuration to a concrete resolved path
        // in memory, which the next unrelated SaveAsync then persisted.
        // Returning a copy keeps the caller's instance byte-identical.
        return new ServerConfig
        {
            Id             = cfg.Id,
            Name           = cfg.Name,
            ExecutablePath = ResolveExecutable(cfg.ExecutablePath),
            ModelPath      = ResolveModel(cfg.ModelPath),
            Port           = cfg.Port,
            ContextSize    = cfg.ContextSize,
            GpuLayers      = cfg.GpuLayers,
            Threads        = cfg.Threads,
            PromptThreads  = cfg.PromptThreads,
            Slots          = cfg.Slots,
            EmbeddingsMode = cfg.EmbeddingsMode,
            AutoStart      = cfg.AutoStart,
            ExtraArgs      = cfg.ExtraArgs,
            MmprojPath = cfg.MmprojPath,
            UseProjector = cfg.UseProjector,
            KvCacheType = cfg.KvCacheType,
            KvCacheTypeK = cfg.KvCacheTypeK,
            KvCacheTypeV = cfg.KvCacheTypeV,
            PreserveReasoning = cfg.PreserveReasoning,
            ReasoningPreserveSupported = cfg.ReasoningPreserveSupported,
            FlashAttention = cfg.FlashAttention,
            ContextShift = cfg.ContextShift,
            MemoryLock = cfg.MemoryLock,
            NoMemoryMap = cfg.NoMemoryMap,
            CpuMoeLayers = cfg.CpuMoeLayers,
            NgramSpeculative = cfg.NgramSpeculative,
            Speculative = new SpeculativeDecodingConfig
            {
                Types = cfg.Speculative?.Types.ToList() ?? [],
                DraftModelPath = cfg.Speculative?.DraftModelPath ?? string.Empty,
                DraftGpuLayers = cfg.Speculative?.DraftGpuLayers,
                NMax = cfg.Speculative?.NMax,
                NMin = cfg.Speculative?.NMin,
                PMin = cfg.Speculative?.PMin
            },
            RuntimeHelpProbed = cfg.RuntimeHelpProbed,
            RuntimeSpeculativeTypes = cfg.RuntimeSpeculativeTypes,
            RuntimeSupportsPromptThreads = cfg.RuntimeSupportsPromptThreads,
            RuntimeSupportsLoadMode = cfg.RuntimeSupportsLoadMode,
            RuntimeSupportsCorsOrigins = cfg.RuntimeSupportsCorsOrigins
        };
    }

    private static string? ValidateRuntimeOptions(ServerConfig cfg)
    {
        if (cfg.PromptThreads > 0 && !cfg.RuntimeSupportsPromptThreads)
            return "This llama-server does not advertise --threads-batch. Remove Prompt processing threads or select a runtime that supports it.";

        var types = cfg.Speculative?.Types
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (types.Length == 0)
            return null;

        if (!cfg.RuntimeHelpProbed)
            return "Could not read the selected llama-server help, so Hermaeus will not launch speculative decoding without runtime proof.";

        var supported = new HashSet<string>(cfg.RuntimeSpeculativeTypes, StringComparer.OrdinalIgnoreCase);
        var unsupported = types.Where(type => !supported.Contains(type)).ToArray();
        return unsupported.Length == 0
            ? null
            : $"The selected llama-server does not advertise speculative type(s): {string.Join(", ", unsupported)}. Remove them or select a runtime that supports them.";
    }

    private static string ResolveExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Set the llama-server executable path first.");

        var trimmed = executablePath.Trim();
        var resolution = ExecutableResolver.Resolve(trimmed, "llama-server");
        if (resolution.Success) return resolution.Path!;

        throw resolution.Failure switch
        {
            ExecutableResolutionFailure.Ambiguous => new InvalidOperationException($"More than one llama-server executable was found inside '{trimmed}'. Select the exact file."),
            ExecutableResolutionFailure.NoneInDirectory => new FileNotFoundException($"No llama-server executable was found inside '{trimmed}'."),
            ExecutableResolutionFailure.NotOnPath => new FileNotFoundException($"'{trimmed}' was not found on PATH. Select the full llama-server executable."),
            _ => new FileNotFoundException($"The llama-server executable does not exist: '{trimmed}'.")
        };
    }

    private static string ResolveModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new InvalidOperationException("Select a .gguf model file before starting the server.");

        var trimmed = modelPath.Trim();
        if (Directory.Exists(trimmed))
        {
            var models = Directory.EnumerateFiles(trimmed, "*.gguf", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();

            return models.Length switch
            {
                1 => models[0],
                0 => throw new FileNotFoundException($"No .gguf model files were found inside '{trimmed}'."),
                _ => throw new InvalidOperationException(
                    $"More than one .gguf model was found inside '{trimmed}'. Select the exact model file, for example '{models[0]}'.")
            };
        }

        if (File.Exists(trimmed)) return trimmed;

        throw new FileNotFoundException($"The model file does not exist: '{trimmed}'.");
    }

    private static string GetWorkingDirectory(string executablePath)
    {
        if (Path.IsPathFullyQualified(executablePath) || LooksLikePath(executablePath))
            return Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;

        return Environment.CurrentDirectory;
    }

    private static bool LooksLikePath(string value) =>
        value.Contains(Path.DirectorySeparatorChar) ||
        value.Contains(Path.AltDirectorySeparatorChar);

    // ── Health poll ───────────────────────────────────────────────────────────

    private static async Task WaitForHealthAsync(
        int port,
        Func<Process?> getProcess,
        CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url      = $"http://127.0.0.1:{port}/health";
        var deadline = DateTime.UtcNow.AddMinutes(5);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var process = getProcess();
            if (process is { HasExited: true })
                throw new InvalidOperationException($"llama-server exited before it became ready. Exit code: {process.ExitCode}.");

            // r29 doc 04 4.4: both the probe and the poll interval are raced
            // against process exit. Before this, a server that died on launch
            // was still diagnosed only after the in-flight probe ran out the
            // HttpClient's 2 s timeout (which is what happens when something
            // else holds the port open but never answers) and then the 600 ms
            // interval elapsed on top. The app already had the answer and sat
            // on it, and the user waited seconds to be told the launch failed.
            using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                var exited  = WhenProcessExitsAsync(process, iterationCts.Token);
                var request = http.GetAsync(url, iterationCts.Token);
                ObserveFault(request);

                if (await Task.WhenAny(request, exited) != request)
                    continue;   // the process is gone; the top of the loop reports it

                try
                {
                    using var r = await request;
                    if (r.IsSuccessStatusCode) return;
                }
                // The HttpClient's own 2 s timeout throws OperationCanceledException too, indistinguishable
                // from a real cancellation by type alone; only a genuinely cancelled ct should escape this
                // retry loop (r9 02-server-lifecycle.md 2.4: an HTTP timeout must not masquerade as a
                // user-initiated cancel and overwrite an already-diagnosed Error state with a silent Stopped).
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }

                await Task.WhenAny(Task.Delay(600, iterationCts.Token), exited);
            }
            finally
            {
                // Abandons whichever of the probe, the exit watch and the poll
                // delay is still outstanding, so nothing survives the iteration.
                iterationCts.Cancel();
            }
        }
        throw new TimeoutException($"llama-server on port {port} did not respond within 5 minutes");
    }

    /// <summary>Completes when the process exits, or never for a null process.</summary>
    private static Task WhenProcessExitsAsync(Process? process, CancellationToken ct)
    {
        var task = process is null
            ? Task.Delay(Timeout.Infinite, ct)
            : process.WaitForExitAsync(ct);
        // Abandoned at the end of every poll iteration; observe the resulting
        // cancellation so it is not an unobserved task exception.
        ObserveFault(task);
        return task;
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void OnProcessExited(object? sender, EventArgs e)
    {
        // r11 4.4: reads via the sender captured at event-raise time, not the
        // _process field, which KillProcess() may be disposing concurrently
        // on another thread (process-crash class); ExitCode access on an
        // already-disposed Process is swallowed rather than left to throw
        // ObjectDisposedException on a threadpool thread.
        var code = TryGetExitCode(sender as Process);
        Emit($"[hermaeus] Process exited with code {code}.");
        if (Status == ServerStatus.Running)
        {
            SetStatus(code == 0 ? ServerStatus.Stopped : ServerStatus.Error);
        }
        else if (Status == ServerStatus.Starting)
        {
            // The health-wait loop would eventually notice HasExited on its
            // next poll, but reacting to the exit event directly (r9
            // 02-server-lifecycle.md 2.4) reports the failure immediately
            // instead of leaving Starting stuck for up to one poll interval.
            ErrorMessage = BuildErrorMessage(
                new InvalidOperationException($"llama-server exited before it became ready. Exit code: {code}."));
            SetStatus(ServerStatus.Error);
        }
    }

    private void KillProcess()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        _process = null;
    }

    private static int TryGetExitCode(Process? process)
    {
        if (process is null) return -1;
        try { return process.ExitCode; }
        catch (ObjectDisposedException) { return -1; }
        catch (InvalidOperationException) { return -1; }
    }

    private void SetStatus(ServerStatus s)
    {
        Status = s;
        StatusChanged?.Invoke(s);
    }

    private void Emit(string line)
    {
        line = _redactor?.Redact(line) ?? line;
        _logRing.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
        while (_logRing.Count > MaxLogLines)
            _logRing.TryDequeue(out _);
        LogLine?.Invoke(line);
    }

    private string BuildErrorMessage(Exception ex)
    {
        var recent = _logRing.TakeLast(4).ToArray();
        if (recent.Length == 0) return ex.Message;

        return $"{ex.Message}\n\nRecent log:\n{string.Join('\n', recent)}";
    }
}

public sealed record ServerTuneResult(
    int GpuLayers,
    int? TotalLayers,
    int Threads,
    string LlamaServerVersion,
    string RecentLog,
    int? TunedContextSize = null);

internal sealed record ProbeResult(bool Success, ServerTuneResult? TuneResult, string Error)
{
    public static ProbeResult Ok(ServerTuneResult result) => new(true, result, string.Empty);
    public static ProbeResult Failed(string error) => new(false, null, error);
}
