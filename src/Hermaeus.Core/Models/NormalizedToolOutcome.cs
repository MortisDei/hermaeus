using System.Text.Json.Serialization;

namespace Hermaeus.Core.Models;

/// <summary>
/// Provider-neutral semantic outcome derived from retained executor evidence.
/// Unknown is intentionally distinct from failure.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NormalizedOutcome
{
    Succeeded,
    PartiallySucceeded,
    NoEffect,
    Unavailable,
    Denied,
    Blocked,
    Failed,
    Cancelled,
    TimedOut,
    Unknown
}

/// <summary>
/// Deterministic interpretation of a tool or approval result. Raw result
/// fields remain authoritative and are stored separately.
/// </summary>
public sealed record NormalizedToolOutcome
{
    public const int CurrentDerivationVersion = 1;
    public const int MaxDetailLength = 512;

    public NormalizedOutcome Outcome { get; init; } = NormalizedOutcome.Unknown;
    public string EvidenceCode { get; init; } = "legacy-no-normalized-outcome";
    public string Detail { get; init; } = "This result predates normalized outcome evidence.";
    public DateTime DerivedAtUtc { get; init; } = DateTime.MinValue;
    public int DerivationVersion { get; init; } = CurrentDerivationVersion;

    public static NormalizedToolOutcome Create(
        NormalizedOutcome outcome,
        string evidenceCode,
        string detail,
        DateTime? derivedAtUtc = null) => new()
        {
            Outcome = outcome,
            EvidenceCode = Bound(evidenceCode, 96),
            Detail = Bound(detail, MaxDetailLength),
            DerivedAtUtc = derivedAtUtc ?? DateTime.UtcNow,
            DerivationVersion = CurrentDerivationVersion
        };

    private static string Bound(string value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
