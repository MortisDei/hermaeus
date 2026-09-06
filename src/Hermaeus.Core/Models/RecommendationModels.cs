using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermaeus.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecommendationKind
{
    RuntimeConfiguration,
    DefaultModel,
    WorkloadPlacement,
    Retest,
    ResourceConflict
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecommendationEligibility
{
    Actionable,
    ReviewOnly,
    InsufficientEvidence,
    Contradicted,
    Stale
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecommendationStatus
{
    Current,
    Accepted,
    Dismissed,
    Superseded,
    Expired
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecommendationDecisionKind
{
    Apply,
    Dismiss,
    Undo
}

public sealed record RecommendationPatch
{
    public string TargetDomain { get; init; } = string.Empty;
    public string CanonicalJson { get; init; } = "{}";
    public string Sha256 { get; init; } = string.Empty;

    public static RecommendationPatch Create(string targetDomain, string json)
    {
        var domain = ValidateToken(targetDomain, nameof(targetDomain), 96);
        var canonical = ExperienceJson.CanonicalizeJson(json);
        using var document = JsonDocument.Parse(canonical);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A recommendation patch must be a JSON object.", nameof(json));
        RejectUnsafePatch(document.RootElement);
        return new RecommendationPatch
        {
            TargetDomain = domain,
            CanonicalJson = canonical,
            Sha256 = ExperienceJson.Hash(canonical)
        };
    }

    private static void RejectUnsafePatch(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name.Trim();
            if (name.Length is 0 or > 96 || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || name.Contains("password", StringComparison.OrdinalIgnoreCase)
                || name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || name.Contains("api_key", StringComparison.OrdinalIgnoreCase)
                || name.Contains("apikey", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Recommendation patches cannot contain secret fields.", nameof(element));

            if (property.Value.ValueKind == JsonValueKind.Object)
                RejectUnsafePatch(property.Value);
            else if (property.Value.ValueKind == JsonValueKind.Array)
                foreach (var item in property.Value.EnumerateArray())
                    RejectUnsafeValue(item);
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString() ?? string.Empty;
                if (Path.IsPathRooted(value) || value.Contains('/') || value.Contains('\\'))
                    throw new ArgumentException("Recommendation patches cannot contain paths.", nameof(element));
            }
        }

        static void RejectUnsafeValue(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                RejectUnsafePatch(value);
                return;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                    RejectUnsafeValue(item);
                return;
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? string.Empty;
                if (Path.IsPathRooted(text) || text.Contains('/') || text.Contains('\\'))
                    throw new ArgumentException("Recommendation patches cannot contain paths.", nameof(element));
            }
        }
    }

    private static string ValidateToken(string value, string field, int maximum)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maximum || trimmed.Any(character =>
            !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new ArgumentException($"Recommendation {field} must be a bounded identifier.", field);
        return trimmed;
    }
}

public sealed record RecommendationEvidenceReference(
    string EvidenceId,
    string EvidenceKind,
    bool Required,
    CapabilityState State,
    DateTime ObservedAtUtc,
    TimeSpan? MaximumAge = null);

public sealed record RecommendationCondition(string Code, string Value);

public sealed record RecommendationTradeoff(string Code, string Value);

public sealed record ConfigurationRecommendation
{
    public string Id { get; init; } = string.Empty;
    public int SchemaVersion { get; init; } = 1;
    public RecommendationKind Kind { get; init; }
    public string TargetIdentity { get; init; } = string.Empty;
    public string CurrentConfigurationIdentity { get; init; } = string.Empty;
    public RecommendationPatch ProposedPatch { get; init; } = new();
    public IReadOnlyList<RecommendationEvidenceReference> Evidence { get; init; } = [];
    public IReadOnlyList<RecommendationCondition> Conditions { get; init; } = [];
    public IReadOnlyList<RecommendationTradeoff> Tradeoffs { get; init; } = [];
    public RecommendationEligibility Eligibility { get; init; }
    public string RuleId { get; init; } = string.Empty;
    public int DerivationVersion { get; init; } = 1;
    public string ReasonCode { get; init; } = string.Empty;
    public DateTime EvaluatedAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public RecommendationStatus Status { get; init; } = RecommendationStatus.Current;

    public static ConfigurationRecommendation Create(
        RecommendationKind kind,
        string targetIdentity,
        string currentConfigurationIdentity,
        RecommendationPatch patch,
        IEnumerable<RecommendationEvidenceReference> evidence,
        IEnumerable<RecommendationCondition> conditions,
        IEnumerable<RecommendationTradeoff> tradeoffs,
        RecommendationEligibility eligibility,
        string ruleId,
        int derivationVersion,
        string reasonCode,
        DateTime evaluatedAtUtc,
        DateTime? expiresAtUtc = null)
    {
        var target = ValidateOpaque(targetIdentity, nameof(targetIdentity));
        var current = ValidateOpaque(currentConfigurationIdentity, nameof(currentConfigurationIdentity));
        var rule = ValidateOpaque(ruleId, nameof(ruleId));
        var reason = ValidateOpaque(reasonCode, nameof(reasonCode));
        if (derivationVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(derivationVersion));
        ArgumentNullException.ThrowIfNull(patch);
        var normalizedEvidence = evidence?.ToArray() ?? throw new ArgumentNullException(nameof(evidence));
        var normalizedConditions = conditions?.ToArray() ?? throw new ArgumentNullException(nameof(conditions));
        var normalizedTradeoffs = tradeoffs?.ToArray() ?? throw new ArgumentNullException(nameof(tradeoffs));
        if (normalizedEvidence.Length > 32 || normalizedConditions.Length > 32 || normalizedTradeoffs.Length > 32)
            throw new InvalidOperationException("A recommendation contains too many bounded evidence or trade-off entries.");
        var evaluated = evaluatedAtUtc.ToUniversalTime();
        var id = IdentityHash.Compute(
            kind.ToString(), target, current, patch.TargetDomain, patch.Sha256,
            rule, derivationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new ConfigurationRecommendation
        {
            Id = id,
            Kind = kind,
            TargetIdentity = target,
            CurrentConfigurationIdentity = current,
            ProposedPatch = patch,
            Evidence = normalizedEvidence,
            Conditions = normalizedConditions,
            Tradeoffs = normalizedTradeoffs,
            Eligibility = eligibility,
            RuleId = rule,
            DerivationVersion = derivationVersion,
            ReasonCode = reason,
            EvaluatedAtUtc = evaluated,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc?.ToUniversalTime()
        };
    }

    private static string ValidateOpaque(string value, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 256 || trimmed.Any(char.IsWhiteSpace)
            || trimmed.Contains('/') || trimmed.Contains('\\'))
            throw new ArgumentException($"Recommendation {field} must be a path-free opaque identity.", field);
        return trimmed;
    }
}

public sealed record RecommendationQuery
{
    public RecommendationKind? Kind { get; init; }
    public string? TargetIdentity { get; init; }
    public string? CurrentConfigurationIdentity { get; init; }
    public RecommendationEligibility? Eligibility { get; init; }
    public RecommendationStatus? Status { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed record RecommendationDecisionRecord(
    string Id,
    string RecommendationId,
    RecommendationDecisionKind Decision,
    string Actor,
    string? ExpectedCurrentConfigurationIdentity,
    string ResultCode,
    DateTime CreatedAtUtc);

public sealed record RecommendationRollbackRecord(
    string Id,
    string RecommendationId,
    string PreImageJson,
    string PreImageHash,
    string PostApplyConfigurationIdentity,
    DateTime CreatedAtUtc,
    bool Consumed);

public static class RecommendationEligibilityRules
{
    public static RecommendationEligibility Evaluate(
        bool targetIdentityComplete,
        bool targetExists,
        bool requiredEvidenceRevoked,
        bool contradicted,
        bool requiredEvidenceExpired,
        bool minimumFactsComplete,
        bool actionable)
    {
        if (!targetIdentityComplete || !targetExists)
            return RecommendationEligibility.Stale;
        if (requiredEvidenceRevoked)
            return RecommendationEligibility.InsufficientEvidence;
        if (contradicted)
            return RecommendationEligibility.Contradicted;
        if (requiredEvidenceExpired)
            return RecommendationEligibility.Stale;
        if (!minimumFactsComplete)
            return RecommendationEligibility.InsufficientEvidence;
        return actionable ? RecommendationEligibility.Actionable : RecommendationEligibility.ReviewOnly;
    }
}

public sealed record RecommendationProposal(
    RecommendationKind Kind,
    string TargetIdentity,
    string CurrentConfigurationIdentity,
    RecommendationPatch ProposedPatch,
    IReadOnlyList<RecommendationEvidenceReference> Evidence,
    IReadOnlyList<RecommendationCondition> Conditions,
    IReadOnlyList<RecommendationTradeoff> Tradeoffs,
    string RuleId,
    int DerivationVersion,
    string ReasonCode,
    DateTime EvaluatedAtUtc,
    bool TargetIdentityComplete,
    bool TargetExists,
    bool RequiredEvidenceRevoked,
    bool Contradicted,
    bool RequiredEvidenceExpired,
    bool MinimumFactsComplete,
    bool Actionable,
    DateTime? ExpiresAtUtc = null);

public sealed record RecommendationEvidenceAssessment(
    bool RequiredFactsComplete,
    bool RequiredEvidenceExpired);

public static class RecommendationEvidenceRules
{
    public static readonly TimeSpan DefaultMaximumAge = TimeSpan.FromDays(30);

    public static RecommendationEvidenceAssessment Assess(
        IReadOnlyList<RecommendationEvidenceReference> evidence,
        DateTime evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var evaluated = evaluatedAtUtc.ToUniversalTime();
        var required = evidence.Where(value => value.Required).ToArray();
        if (required.Length == 0)
            return new(false, false);

        var expired = required.Any(value =>
        {
            var observed = value.ObservedAtUtc.ToUniversalTime();
            var maximumAge = value.MaximumAge ?? DefaultMaximumAge;
            return observed > evaluated || evaluated - observed > maximumAge;
        });
        var complete = required.All(value => value.State == CapabilityState.Available);
        return new(complete, expired);
    }
}
