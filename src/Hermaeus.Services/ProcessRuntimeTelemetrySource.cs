using System.Diagnostics;
using System.Globalization;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

public sealed class ProcessRuntimeTelemetrySource : IRuntimeTelemetrySource
{
    private static readonly TimeSpan ProcessGpuProbeMinimumInterval = TimeSpan.FromSeconds(2);
    private const int MaximumCachedGpuProbes = 32;
    private readonly ISystemInfoService? _systemInfo;
    private readonly IRuntimeLogService? _logs;
    private readonly bool _traceNative = Environment.GetEnvironmentVariable("HERMAEUS_NATIVE_PROBE_TRACE") == "1";
    private readonly object _gpuProbeGate = new();
    private readonly Dictionary<string, GpuProbeCacheEntry> _gpuProbeCache = new(StringComparer.Ordinal);

    private sealed record GpuProbeCacheEntry(DateTimeOffset StartedAt, Task<(long? Bytes, string Source)> Probe);

    public ProcessRuntimeTelemetrySource(ISystemInfoService? systemInfo = null, IRuntimeLogService? logs = null)
    {
        _systemInfo = systemInfo;
        _logs = logs;
    }

    private void TraceNative(string message)
    {
        if (!_traceNative) return;
        try
        {
            _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info,
                RuntimeLogCategory.Service, $"Native telemetry: desktop_pid={Environment.ProcessId}; {message}"));
        }
        catch { /* Diagnostics must not change native resource ownership. */ }
    }

    public async Task<IReadOnlyList<RuntimeTelemetrySample>> CaptureAsync(
        RuntimeTelemetryRequest request,
        CancellationToken ct = default)
    {
        TraceNative($"capture-enter; series={request.SeriesId}; runtime_pid={request.ProcessId}; runtime_started_utc={request.ProcessStartedAtUtc.ToUniversalTime():O}");
        var observedAt = DateTime.UtcNow;
        var processInstance = RuntimeTelemetrySeries.ProcessInstance(request.ProcessId, request.ProcessStartedAtUtc);
        var samples = new List<RuntimeTelemetrySample>();
        try
        {
            using var process = Process.GetProcessById(request.ProcessId);
            if (process.HasExited || process.StartTime.ToUniversalTime() != request.ProcessStartedAtUtc.ToUniversalTime())
                return UnknownProcessSamples(request, processInstance, observedAt, "runtime-process-restarted", "The matching runtime process is no longer alive.");

            if (!await MatchesExpectedRuntimeAsync(process, request.RuntimeIdentity, ct))
                return UnknownProcessSamples(request, processInstance, observedAt, "runtime-executable-mismatch", "The PID is alive, but its executable does not match the recorded runtime identity.");

            process.Refresh();
            samples.Add(Sample(
                request, processInstance, RuntimeTelemetryMetric.ProcessWorkingSetBytes,
                process.WorkingSet64, RuntimeTelemetrySourceKind.ProcessCounter,
                RuntimeTelemetryTrustState.ProcessScoped, observedAt,
                "process-working-set", "Operating-system working set for the matching runtime process."));
            var (gpuMemory, gpuMemorySource) = await TryCaptureNvidiaProcessMemoryAsync(request, ct);
            samples.Add(Sample(
                request, processInstance, RuntimeTelemetryMetric.ProcessGpuMemoryBytes,
                gpuMemory, gpuMemory.HasValue ? RuntimeTelemetrySourceKind.ProcessCounter : RuntimeTelemetrySourceKind.Unknown,
                gpuMemory.HasValue ? RuntimeTelemetryTrustState.ProcessScoped : RuntimeTelemetryTrustState.Unknown,
                observedAt, gpuMemorySource,
                gpuMemory.HasValue
                    ? $"NVIDIA {gpuMemorySource} process memory for the matching runtime PID."
                    : gpuMemorySource == "process-gpu-cleanup-unconfirmed"
                        ? "The GPU probe did not produce a trustworthy counter and its root or output cleanup could not be confirmed."
                        : "No trustworthy per-process GPU memory counter is available from this source."));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return UnknownProcessSamples(request, processInstance, observedAt, "runtime-process-unavailable", "The matching runtime process could not be sampled.");
        }

        if (request.IncludeDeviceTotals && _systemInfo is not null)
        {
            var snapshot = await _systemInfo.CaptureAsync(ct);
            var known = snapshot.Gpus.Where(gpu => gpu.MemoryUsedBytes.HasValue).ToArray();
            samples.Add(Sample(
                request, processInstance, RuntimeTelemetryMetric.DeviceGpuMemoryBytes,
                known.Length == 0 ? null : known.Sum(gpu => gpu.MemoryUsedBytes),
                known.Length == 0 ? RuntimeTelemetrySourceKind.Unknown : RuntimeTelemetrySourceKind.DeviceCounter,
                known.Length == 0 ? RuntimeTelemetryTrustState.Unknown : RuntimeTelemetryTrustState.DeviceTotal,
                observedAt,
                known.Length == 0 ? "device-gpu-unavailable" : "device-gpu-total",
                known.Length == 0
                    ? "No device GPU memory counter was available."
                    : "Whole-device GPU memory total. This value is not attributed to the model process."));
        }

        return samples;
    }

    private static async Task<bool> MatchesExpectedRuntimeAsync(
        Process process, RuntimeIdentityV2 expected, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expected.ExecutableSha256))
            return true;

        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
                return false;
            var observed = await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(path, null, ct);
            return expected.IdentifiesSameRuntime(observed);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private async Task<(long? Bytes, string Source)> TryCaptureNvidiaProcessMemoryAsync(
        RuntimeTelemetryRequest request,
        CancellationToken ct)
    {
        var cacheKey = $"{request.ProcessId}:{request.ProcessStartedAtUtc.ToUniversalTime():O}:{request.RuntimeIdentity.StableId}";
        GpuProbeCacheEntry entry;
        var now = DateTimeOffset.UtcNow;
        lock (_gpuProbeGate)
        {
            if (_gpuProbeCache.TryGetValue(cacheKey, out entry!)
                && (!entry.Probe.IsCompleted || now - entry.StartedAt < ProcessGpuProbeMinimumInterval))
            {
                // A caller cancellation cancels only its wait. The shared probe
                // remains bounded and can serve the next telemetry sample.
            }
            else
            {
                entry = new GpuProbeCacheEntry(
                    now,
                    Task.Run(() => ProbeNvidiaProcessMemoryAsync(request.ProcessId, CancellationToken.None)));
                _gpuProbeCache[cacheKey] = entry;
                TrimGpuProbeCacheLocked();
            }
        }

        try
        {
            return await entry.Probe.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Do not discard an in-flight shared probe just because this
            // sample stopped waiting. Its result can still serve the next
            // bounded telemetry request.
            throw;
        }
        catch
        {
            lock (_gpuProbeGate)
            {
                if (_gpuProbeCache.TryGetValue(cacheKey, out var current)
                    && ReferenceEquals(current.Probe, entry.Probe))
                    _gpuProbeCache.Remove(cacheKey);
            }
            throw;
        }
    }

    private void TrimGpuProbeCacheLocked()
    {
        if (_gpuProbeCache.Count <= MaximumCachedGpuProbes)
            return;

        foreach (var key in _gpuProbeCache
            .OrderBy(item => item.Value.StartedAt)
            .Take(_gpuProbeCache.Count - MaximumCachedGpuProbes)
            .Select(item => item.Key)
            .ToArray())
            _gpuProbeCache.Remove(key);
    }

    private async Task<(long? Bytes, string Source)> ProbeNvidiaProcessMemoryAsync(int processId, CancellationToken ct)
    {
        var probeId = _traceNative ? Guid.NewGuid().ToString("N") : string.Empty;
        Action<string>? trace = _traceNative
            ? message => TraceNative($"probe={probeId}; runtime_pid={processId}; {message}")
            : null;
        trace?.Invoke("probe-enter");
        if (NvidiaProcessMemoryProbe.TryGetBytes(processId, out var nvmlBytes, trace))
        {
            trace?.Invoke("probe-exit; source=nvml");
            return (nvmlBytes, "nvml-process-gpu-memory");
        }
        trace?.Invoke("probe-exit; source=nvml-unavailable; fallback=nvidia-smi");

        var executable = ExecutableResolver.FindOnPath("nvidia-smi");
        if (executable is null)
            return (null, "process-gpu-unavailable");

        return await RunNvidiaSmiCommandAsync(executable,
            ["--query-compute-apps=pid,used_memory", "--format=csv,noheader,nounits"],
            processId, TimeSpan.FromSeconds(2), ct);
    }

    internal static async Task<(long? Bytes, string Source)> RunNvidiaSmiCommandAsync(
        string executable, IReadOnlyList<string> arguments, int processId,
        TimeSpan timeoutBudget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        Task<(string Output, bool Truncated)>? stdout = null;
        Task? stderr = null;
        var started = false;
        try
        {
            if (!process.Start()) return (null, "process-gpu-unavailable");
            started = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutBudget);
            stdout = ReadNvidiaOutputAsync(process.StandardOutput, timeout.Token);
            stderr = DrainNvidiaErrorAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(stdout, stderr);
            await process.WaitForExitAsync(timeout.Token);
            var output = stdout.Result;
            return process.ExitCode == 0 && !output.Truncated
                && ProcessGpuMemoryParser.TryGetBytes(output.Output, processId, out var bytes)
                ? (bytes, "nvidia-smi-process-gpu-memory")
                : (null, "process-gpu-unavailable");
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            var cleanupError = await StopNvidiaCommandAsync(process, stdout, stderr);
            if (cleanupError.Length > 0)
                throw new OperationCanceledException($"The GPU probe was cancelled. {cleanupError}", ex, ct);
            throw;
        }
        catch
        {
            var cleanupError = started ? await StopNvidiaCommandAsync(process, stdout, stderr) : string.Empty;
            return (null, cleanupError.Length > 0 ? "process-gpu-cleanup-unconfirmed" : "process-gpu-unavailable");
        }
    }

    private static async Task<(string Output, bool Truncated)> ReadNvidiaOutputAsync(
        StreamReader reader, CancellationToken ct)
    {
        const int maximumCharacters = 64 * 1024;
        var buffer = new char[2048];
        var output = new System.Text.StringBuilder();
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            var remaining = maximumCharacters - output.Length;
            output.Append(buffer, 0, Math.Min(count, remaining));
            truncated |= count > remaining;
        }
        return (output.ToString(), truncated);
    }

    private static async Task DrainNvidiaErrorAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer.AsMemory(), ct) > 0) { }
    }

    private static async Task<string> StopNvidiaCommandAsync(Process process, Task? stdout, Task? stderr)
    {
        var errors = new List<string>();
        try
        {
            // Root exit confirms neither escaped descendants nor complete
            // containment. This is best-effort cleanup of the owned probe.
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
            errors.Add($"GPU probe termination failed: {ex.Message}");
        }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await process.WaitForExitAsync(cleanup.Token); }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            errors.Add($"GPU probe root exit could not be confirmed: {ex.Message}");
        }
        var joined = Task.WhenAll(new[] { stdout, stderr }.OfType<Task>());
        try { await joined.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) when (joined.IsCanceled) { }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException)
        {
            errors.Add($"GPU probe output cleanup could not be confirmed: {ex.Message}");
            _ = joined.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return string.Join(" ", errors);
    }

    private static IReadOnlyList<RuntimeTelemetrySample> UnknownProcessSamples(
        RuntimeTelemetryRequest request,
        string processInstance,
        DateTime observedAt,
        string code,
        string detail) =>
    [
        Sample(request, processInstance, RuntimeTelemetryMetric.ProcessWorkingSetBytes, null,
            RuntimeTelemetrySourceKind.Unknown, RuntimeTelemetryTrustState.Unknown, observedAt, code, detail),
        Sample(request, processInstance, RuntimeTelemetryMetric.ProcessGpuMemoryBytes, null,
            RuntimeTelemetrySourceKind.Unknown, RuntimeTelemetryTrustState.Unknown, observedAt, code, detail)
    ];

    private static RuntimeTelemetrySample Sample(
        RuntimeTelemetryRequest request,
        string processInstance,
        RuntimeTelemetryMetric metric,
        long? value,
        RuntimeTelemetrySourceKind source,
        RuntimeTelemetryTrustState trust,
        DateTime observedAt,
        string code,
        string detail) => new(
            request.SeriesId, processInstance, metric, value, source, trust,
            observedAt, request.RuntimeIdentity.StableId, code, detail);
}

public static class ProcessGpuMemoryParser
{
    public static bool TryGetBytes(string output, int processId, out long bytes)
    {
        bytes = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                || pid != processId || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mib)
                || mib < 0)
                continue;

            bytes = checked(mib * 1024L * 1024L);
            return true;
        }
        return false;
    }
}
