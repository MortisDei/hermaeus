namespace Hermaeus.Core.Models;

/// <summary>
/// Trust state for evidence that binds a workload to the runtime process that
/// produced it. Workload completion and evidence verification are deliberately
/// separate facts.
/// </summary>
public enum RuntimeEvidenceStatus
{
    Verified,
    Inconclusive,
    Unverified,
    Mismatch
}

/// <summary>
/// Shared runtime-authority receipt used by Lab and Benchmark observations.
/// Requested, resolved, launched, effective, and telemetry identity are kept as
/// separate fields so one cannot be silently substituted for another.
/// </summary>
public sealed record RuntimeEvidenceEnvelope
{
    public const string CurrentSchema = "runtime-evidence-v1";

    public string Schema { get; init; } = CurrentSchema;
    public string EnvelopeId { get; init; } = Guid.NewGuid().ToString("N");
    public string Workflow { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string CandidateId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public string RequestedConfigurationStableId { get; init; } = string.Empty;
    public string ResolvedConfigurationStableId { get; init; } = string.Empty;
    public string LaunchedConfigurationStableId { get; init; } = string.Empty;
    public string EffectiveConfigurationStableId { get; init; } = string.Empty;
    public RuntimeIdentityV2? RuntimeIdentity { get; init; }
    public ModelIdentityV2? ModelIdentity { get; init; }
    public ConfigurationIdentityV2? RequestedConfiguration { get; init; }
    public ConfigurationIdentityV2? ResolvedConfiguration { get; init; }
    public RuntimeLaunchProcessEvidence? Process { get; init; }
    public EffectiveLaunchObservation? EffectiveLaunch { get; init; }
    public IReadOnlyDictionary<string, string> EffectiveFields { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<string> TelemetryProcessInstanceIds { get; init; } = [];
    public RuntimeEvidenceStatus Status { get; init; } = RuntimeEvidenceStatus.Unverified;
    public IReadOnlyList<string> Reasons { get; init; } = [];

    public bool ComparisonEligible => Status == RuntimeEvidenceStatus.Verified;
    public bool RecommendationEligible => ComparisonEligible;
    public bool ApplyEligible => ComparisonEligible;
}
