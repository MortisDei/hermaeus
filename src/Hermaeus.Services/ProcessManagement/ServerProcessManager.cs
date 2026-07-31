using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;

namespace Hermaeus.Services.ProcessManagement;

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

        foreach (var arg in BuildLaunchArguments(cfg))
            startInfo.ArgumentList.Add(arg);

        return new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
    }

    public static IReadOnlyList<string> BuildLaunchArguments(ServerConfig cfg)
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

        bool HasArg(string flag) => extraArgs.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

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

            // r14 2.4: llama-server clamps n_batch down to n_ubatch (512) for
            // embeddings and logs a warning pair every start; set a coherent
            // pair up front so the start is clean.
            if (!HasArg("-b") && !HasArg("--batch-size"))
            {
                parts.Add("-b");
                parts.Add("512");
            }
            if (!HasArg("-ub") && !HasArg("--ubatch-size"))
            {
                parts.Add("-ub");
                parts.Add("512");
            }
        }

        // r18 04-llama-server-engine-options.md 4.1: first-class engine options. Defaults match
        // today's exact command line (f16 KV cache, auto flash attention, context shift/mlock/
        // no-mmap all off) so an older saved config launches byte-identically; ExtraArgs always
        // wins over any of these, exactly like --parallel and --cache-reuse above.
        if (!string.Equals(cfg.KvCacheTypeK, "f16", StringComparison.OrdinalIgnoreCase) && !HasArg("--cache-type-k"))
        {
            parts.Add("--cache-type-k");
            parts.Add(cfg.KvCacheTypeK);
        }

        if (!string.Equals(cfg.KvCacheTypeV, "f16", StringComparison.OrdinalIgnoreCase) && !HasArg("--cache-type-v"))
        {
            parts.Add("--cache-type-v");
            parts.Add(cfg.KvCacheTypeV);
        }

        // "auto" is the server's own default and emits nothing.
        if (!string.Equals(cfg.FlashAttention, "auto", StringComparison.OrdinalIgnoreCase) && !HasArg("--flash-attn") && !HasArg("-fa"))
        {
            parts.Add("--flash-attn");
            parts.Add(cfg.FlashAttention.ToLowerInvariant());
        }

        if (cfg.ContextShift && !HasArg("--context-shift") && !HasArg("--no-context-shift"))
            parts.Add("--context-shift");

        if (cfg.MemoryLock && !HasArg("--mlock"))
            parts.Add("--mlock");

        if (cfg.NoMemoryMap && !HasArg("--no-mmap") && !HasArg("--mmap"))
            parts.Add("--no-mmap");

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
        if (!string.IsNullOrWhiteSpace(cfg.MmprojPath) && !HasArg("--mmproj"))
        {
            parts.Add("--mmproj");
            parts.Add(cfg.MmprojPath);
        }

        if (extraArgs.Count > 0)
            parts.AddRange(extraArgs);

        return parts;
    }

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
            Slots          = cfg.Slots,
            EmbeddingsMode = cfg.EmbeddingsMode,
            AutoStart      = cfg.AutoStart,
            ExtraArgs      = cfg.ExtraArgs
        };
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

            try
            {
                var r = await http.GetAsync(url, ct);
                if (r.IsSuccessStatusCode) return;
            }
            // The HttpClient's own 2 s timeout throws OperationCanceledException too, indistinguishable
            // from a real cancellation by type alone; only a genuinely cancelled ct should escape this
            // retry loop (r9 02-server-lifecycle.md 2.4: an HTTP timeout must not masquerade as a
            // user-initiated cancel and overwrite an already-diagnosed Error state with a silent Stopped).
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }

            await Task.Delay(600, ct);
        }
        throw new TimeoutException($"llama-server on port {port} did not respond within 5 minutes");
    }

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
