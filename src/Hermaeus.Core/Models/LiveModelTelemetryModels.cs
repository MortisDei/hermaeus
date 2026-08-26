namespace Hermaeus.Core.Models;

public enum LiveTelemetryValueSemantics
{
    Current,
    Average,
    Peak
}

public sealed record LiveTelemetryMetric(
    string Id,
    double? Value,
    string Unit,
    LiveTelemetryValueSemantics Semantics,
    RuntimeTelemetrySourceKind Source,
    RuntimeTelemetryTrustState Trust,
    string MissingReason = "");

public enum RuntimeHealthConditionKind
{
    LowVramHeadroom,
    UnexpectedGpuSpill,
    MemoryAbovePrediction,
    ContextNearLimit,
    SustainedPerformanceCollapse,
    RuntimeUnavailable
}

public enum RuntimeHealthSeverity
{
    Warning,
    Critical
}

public sealed record RuntimeHealthCondition(
    RuntimeHealthConditionKind Kind,
    RuntimeHealthSeverity Severity,
    string ObservedFact,
    string EvidenceQuality,
    string Action,
    bool IsActive = true);

public sealed record LiveModelTelemetrySnapshot(
    RuntimeIdentityV2? RuntimeIdentity,
    ModelIdentityV2? ModelIdentity,
    string RuntimeStatus,
    DateTime CapturedAtUtc,
    IReadOnlyList<LiveTelemetryMetric> Metrics,
    IReadOnlyList<RuntimeHealthCondition> HealthConditions)
{
    public static LiveModelTelemetrySnapshot Unknown(string status, DateTime? capturedAtUtc = null) => new(
        null, null, status, (capturedAtUtc ?? DateTime.UtcNow).ToUniversalTime(), [], []);
}

public sealed record RuntimeHealthInput(
    string RuntimeModelIdentity,
    double? VramHeadroomBytes = null,
    bool? ExpectedGpuResident = null,
    bool? SpillObserved = null,
    double? ObservedMemoryBytes = null,
    double? PredictedMemoryBytes = null,
    double? ContextUsed = null,
    double? ContextLimit = null,
    double? CurrentDecodeTokensPerSecond = null,
    double? CompatibleBaselineDecodeTokensPerSecond = null,
    TimeSpan? PerformanceCollapseDuration = null,
    int PerformanceSampleCount = 0,
    bool RuntimeHealthy = true,
    bool RuntimeStatusKnown = true,
    RuntimeTelemetryTrustState EvidenceTrust = RuntimeTelemetryTrustState.Unknown);
