using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hermaeus.Core.Models;

public enum LabRunStatus
{
    Starting,
    Running,
    Succeeded,
    PartiallySucceeded,
    Cancelled,
    Failed
}

public enum LabEquivalenceState
{
    Equivalent,
    Different,
    Unknown
}

public enum LabEquivalenceLevel
{
    TokenIds,
    ExactUtf8,
    Unknown
}

public enum LabCorrectnessRequirement
{
    ExactEquivalence,
    Behavioral,
    SpeedOnly
}

public enum LabRecipeKind
{
    EngineProfile,
    Context,
    KvCache,
    FlashAttention,
    CpuMoePlacement,
    ExternalDraft,
    Eagle3,
    SpeculativeDraftMaximum,
    SpeculativeDraftMinimum,
    SpeculativeProbabilityMinimum,
    SpeculativeDraftGpuLayers,
    PromptPrefixReuse
}

public sealed record LabRecipePlan(
    string Id,
    string Label,
    LabRecipeKind Kind,
    CapabilityState Availability,
    string AvailabilityDetail,
    LabConfiguration Baseline,
    IReadOnlyList<LabConfiguration> Candidates,
    int MaximumRunCount,
    bool TestsInteraction,
    IReadOnlyList<string> RequiredCapabilityIds,
    IReadOnlyList<string> RequiredMetrics,
    LabCorrectnessRequirement CorrectnessRequirement);

public sealed record LabConfiguration
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int ContextSize { get; init; }
    public int GpuLayers { get; init; }
    public int Threads { get; init; }
    public int PromptThreads { get; init; }
    public int Slots { get; init; }
    public string KvCacheTypeK { get; init; } = "f16";
    public string KvCacheTypeV { get; init; } = "f16";
    public string FlashAttention { get; init; } = "auto";
    public int CpuMoeLayers { get; init; }
    public IReadOnlyList<string> SpeculativeTypes { get; init; } = [];
    public string SpeculativeCompanionIdentity { get; init; } = string.Empty;
    public int? SpeculativeDraftGpuLayers { get; init; }
    public int? SpeculativeNMax { get; init; }
    public int? SpeculativeNMin { get; init; }
    public double? SpeculativePMin { get; init; }
    public string PromptCacheMode { get; init; } = "default";
    public string ExtraArgumentsSha256 { get; init; } = string.Empty;
}

public enum PromptReuseEvidenceLevel
{
    DirectCounter,
    ControlledTimingEffect,
    Unknown
}

public sealed record LabExperimentDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int Revision { get; init; } = 1;
    public string Name { get; init; } = string.Empty;
    public string ProtocolId { get; init; } = string.Empty;
    public int ProtocolVersion { get; init; } = 1;
    public string TargetServerId { get; init; } = string.Empty;
    public EmpiricalProfileFingerprintV2 ProfileFingerprint { get; init; } = null!;
    public LabConfiguration Baseline { get; init; } = new();
    public IReadOnlyList<LabConfiguration> Candidates { get; init; } = [];
    public IReadOnlyDictionary<string, string> ConfigurationFingerprints { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, ConfigurationIdentityV2> ConfigurationIdentities { get; init; } =
        new Dictionary<string, ConfigurationIdentityV2>(StringComparer.Ordinal);
    public string WorkloadId { get; init; } = string.Empty;
    public IReadOnlyList<string> PromptHashes { get; init; } = [];
    public string SamplingPolicy { get; init; } = "greedy";
    public int Seed { get; init; }
    public int WarmupRepetitions { get; init; }
    public int Repetitions { get; init; } = 1;
    public string OrderPolicy { get; init; } = "baseline-first";
    public int TimeoutSeconds { get; init; } = 300;
    public IReadOnlyList<string> StopConditions { get; init; } = [];
    public IReadOnlyList<string> RequiredMetrics { get; init; } = [];
    public LabCorrectnessRequirement CorrectnessRequirement { get; init; } = LabCorrectnessRequirement.ExactEquivalence;
    public IReadOnlyList<string> RequestedCapabilityIds { get; init; } = [];
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public string CanonicalJson() => LabCanonicalJson.Serialize(this);
    [System.Text.Json.Serialization.JsonIgnore]
    public string DefinitionHash => LabCanonicalJson.Hash(CanonicalJson());
}

public sealed record LabObservation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string RunId { get; init; } = string.Empty;
    public string ConfigurationId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public int Repetition { get; init; }
    public string MetricId { get; init; } = string.Empty;
    public double? Value { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public EvidenceOrigin Origin { get; init; } = EvidenceOrigin.DirectObservation;
    public string Trust { get; init; } = "Unknown";
    public string MissingReason { get; init; } = string.Empty;
    public DateTime ObservedAtUtc { get; init; } = DateTime.UtcNow;
    public string RuntimeFingerprint { get; init; } = string.Empty;
    public string ModelFingerprint { get; init; } = string.Empty;
    public string HardwareFingerprint { get; init; } = string.Empty;
    public string ConfigurationFingerprint { get; init; } = string.Empty;
}

public sealed record LabOutputEvidence(
    string ConfigurationId,
    string CaseId,
    int Repetition,
    IReadOnlyList<int>? TokenIds,
    string Utf8Sha256,
    string BoundedText);

public sealed record LabEquivalenceResult(
    LabEquivalenceState State,
    LabEquivalenceLevel Level,
    string BaselineHash,
    string CandidateHash,
    string Detail,
    string BoundedDiff);

public sealed record LabMetricSummary(
    string MetricId,
    string Unit,
    double? Median,
    double? Minimum,
    double? Maximum,
    int Repetitions,
    string Source);

public sealed record LabComparison
{
    public string BaselineConfigurationId { get; init; } = string.Empty;
    public string CandidateConfigurationId { get; init; } = string.Empty;
    public bool IsControlled { get; init; }
    public IReadOnlyList<string> FingerprintDifferences { get; init; } = [];
    public IReadOnlyList<LabMetricSummary> BaselineMetrics { get; init; } = [];
    public IReadOnlyList<LabMetricSummary> CandidateMetrics { get; init; } = [];
    public LabEquivalenceResult Equivalence { get; init; } = new(
        LabEquivalenceState.Unknown, LabEquivalenceLevel.Unknown, string.Empty, string.Empty,
        "No correctness evidence was supplied.", string.Empty);
    public bool CorrectnessPassed { get; init; }
    public bool CanShowHeadlineDelta { get; init; }
    public string RefusalReason { get; init; } = string.Empty;
}

public sealed record LabRunSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string DefinitionHash { get; init; } = string.Empty;
    public LabExperimentDefinition Definition { get; init; } = new();
    public LabRunStatus Status { get; init; } = LabRunStatus.Starting;
    public DateTime StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public int? TemporaryPort { get; init; }
    public string RuntimeOwnershipId { get; init; } = string.Empty;
    public int? RuntimeProcessId { get; init; }
    public DateTime? RuntimeProcessStartedAtUtc { get; init; }
    public IReadOnlyList<LabObservation> Observations { get; init; } = [];
    public IReadOnlyList<LabOutputEvidence> Outputs { get; init; } = [];
    public IReadOnlyList<LabComparison> Comparisons { get; init; } = [];
    public IReadOnlyList<string> Failures { get; init; } = [];
    public string StartEvidenceId { get; init; } = string.Empty;
    public string CompletionEvidenceId { get; init; } = string.Empty;
}

public sealed record LabRunEvidenceSlice(
    string RunId,
    string DefinitionHash,
    string ConfigurationId,
    IReadOnlyList<LabObservation> Observations,
    IReadOnlyList<LabOutputEvidence> Outputs,
    int ChunkIndex = 0);

public sealed record LabComparisonDecision(
    string BaselineConfigurationId,
    string CandidateConfigurationId,
    bool IsControlled,
    IReadOnlyList<string> FingerprintDifferences,
    LabEquivalenceResult Equivalence,
    bool CorrectnessPassed,
    bool CanShowHeadlineDelta,
    string RefusalReason);

public sealed record LabRunCompletionSummary(
    string RunId,
    string DefinitionHash,
    LabRunStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<string> Failures,
    IReadOnlyList<LabComparisonDecision> Comparisons,
    IReadOnlyList<string> EvidenceSliceIds,
    IReadOnlyList<LabConfiguration>? Configurations = null,
    IReadOnlyList<LabComparison>? DetailedComparisons = null,
    string? ExperimentName = null,
    string? ModelIdentityLabel = null);

public sealed record LabApplyEvidence(
    string RunId,
    string DefinitionHash,
    string ReviewId,
    string TargetServerId,
    string CandidateConfigurationId,
    IReadOnlyList<LabApplyChange> Changes);

public sealed record LabApplyChange(string Field, string CurrentValue, string ProposedValue);

public sealed record LabApplyReview
{
    public string ReviewId { get; init; } = Guid.NewGuid().ToString("N");
    public string RunId { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string TargetServerId { get; init; } = string.Empty;
    public string CandidateConfigurationId { get; init; } = string.Empty;
    public string ExpectedCurrentConfigurationHash { get; init; } = string.Empty;
    public string ExpectedRuntimeFingerprint { get; init; } = string.Empty;
    public string ExpectedModelFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<LabApplyChange> Changes { get; init; } = [];
    public bool CanApply { get; init; }
    public string RefusalReason { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public static class LabCanonicalJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, Options);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
