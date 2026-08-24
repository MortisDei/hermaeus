using System.Diagnostics;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class ProcessRuntimeTelemetrySource : IRuntimeTelemetrySource
{
    private readonly ISystemInfoService? _systemInfo;

    public ProcessRuntimeTelemetrySource(ISystemInfoService? systemInfo = null) => _systemInfo = systemInfo;

    public async Task<IReadOnlyList<RuntimeTelemetrySample>> CaptureAsync(
        RuntimeTelemetryRequest request,
        CancellationToken ct = default)
    {
        var observedAt = DateTime.UtcNow;
        var processInstance = RuntimeTelemetrySeries.ProcessInstance(request.ProcessId, request.ProcessStartedAtUtc);
        var samples = new List<RuntimeTelemetrySample>();
        try
        {
            using var process = Process.GetProcessById(request.ProcessId);
            if (process.HasExited || process.StartTime.ToUniversalTime() != request.ProcessStartedAtUtc.ToUniversalTime())
                return UnknownProcessSamples(request, processInstance, observedAt, "runtime-process-restarted", "The matching runtime process is no longer alive.");

            process.Refresh();
            samples.Add(Sample(
                request, processInstance, RuntimeTelemetryMetric.ProcessWorkingSetBytes,
                process.WorkingSet64, RuntimeTelemetrySourceKind.ProcessCounter,
                RuntimeTelemetryTrustState.ProcessScoped, observedAt,
                "process-working-set", "Operating-system working set for the matching runtime process."));
            samples.Add(Sample(
                request, processInstance, RuntimeTelemetryMetric.ProcessGpuMemoryBytes,
                null, RuntimeTelemetrySourceKind.Unknown, RuntimeTelemetryTrustState.Unknown,
                observedAt, "process-gpu-unavailable", "No trustworthy per-process GPU memory counter is available from this source."));
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
