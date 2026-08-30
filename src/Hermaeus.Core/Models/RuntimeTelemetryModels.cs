namespace Hermaeus.Core.Models;

public enum RuntimeTelemetryMetric
{
    ProcessWorkingSetBytes,
    ProcessGpuMemoryBytes,
    RuntimeReportedGpuMemoryBytes,
    DeviceGpuMemoryBytes
}

public enum RuntimeTelemetrySourceKind
{
    RuntimeMetrics,
    StartupAllocation,
    ProcessCounter,
    DeviceCounter,
    Unknown
}

public enum RuntimeTelemetryTrustState
{
    TrustedRuntime,
    ProcessScoped,
    DeviceTotal,
    Unknown
}

public sealed record RuntimeTelemetryRequest(
    string SeriesId,
    int ProcessId,
    DateTime ProcessStartedAtUtc,
    RuntimeIdentityV2 RuntimeIdentity,
    EmpiricalProfileFingerprintV2 Fingerprint,
    bool IncludeDeviceTotals = false);

public sealed record RuntimeTelemetrySample(
    string SeriesId,
    string ProcessInstanceId,
    RuntimeTelemetryMetric Metric,
    long? ValueBytes,
    RuntimeTelemetrySourceKind Source,
    RuntimeTelemetryTrustState Trust,
    DateTime ObservedAtUtc,
    string RuntimeStableId,
    string EvidenceCode,
    string Detail);

public sealed record RuntimeTelemetrySeries(
    string SeriesId,
    string ProcessInstanceId,
    EmpiricalProfileFingerprintV2 Fingerprint,
    DateTime StartedAtUtc,
    IReadOnlyList<RuntimeTelemetrySample> Samples)
{
    public const int MaximumSamples = 512;

    public static RuntimeTelemetrySeries Start(RuntimeTelemetryRequest request) => new(
        Validate(request).SeriesId,
        ProcessInstance(request.ProcessId, request.ProcessStartedAtUtc),
        request.Fingerprint,
        request.ProcessStartedAtUtc.ToUniversalTime(),
        []);

    public RuntimeTelemetrySeries Append(IEnumerable<RuntimeTelemetrySample> samples)
    {
        var additions = samples.ToArray();
        if (additions.Any(sample =>
            !string.Equals(sample.SeriesId, SeriesId, StringComparison.Ordinal)
            || !string.Equals(sample.ProcessInstanceId, ProcessInstanceId, StringComparison.Ordinal)
            || !string.Equals(sample.RuntimeStableId, Fingerprint.Runtime.StableId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Telemetry sample does not belong to this runtime process series.");
        }

        var combined = Samples.Concat(additions)
            .GroupBy(sample => new { sample.Metric, sample.Source, sample.ObservedAtUtc, sample.ValueBytes })
            .Select(group => group.First())
            .OrderBy(sample => sample.ObservedAtUtc)
            .TakeLast(MaximumSamples)
            .ToArray();
        return this with { Samples = combined };
    }

    public RuntimeTelemetrySample? Current(RuntimeTelemetryMetric metric) =>
        Samples.Where(sample => sample.Metric == metric && sample.ValueBytes.HasValue)
            .OrderByDescending(sample => sample.ObservedAtUtc)
            .FirstOrDefault();

    public RuntimeTelemetrySample? Peak(RuntimeTelemetryMetric metric) =>
        Samples.Where(sample => sample.Metric == metric && sample.ValueBytes.HasValue)
            .OrderByDescending(sample => sample.ValueBytes)
            .FirstOrDefault();

    public static string ProcessInstance(int processId, DateTime startedAtUtc) =>
        $"{processId}:{startedAtUtc.ToUniversalTime().Ticks}";

    private static RuntimeTelemetryRequest Validate(RuntimeTelemetryRequest request)
    {
        if (!string.Equals(request.RuntimeIdentity.StableId, request.Fingerprint.Runtime.StableId, StringComparison.Ordinal))
            throw new InvalidOperationException("Telemetry request runtime identity does not match its v2 fingerprint.");
        return request;
    }
}
