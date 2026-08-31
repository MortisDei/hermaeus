using System.Text.Json.Serialization;

namespace Hermaeus.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdaptiveInferenceMode
{
    Fixed,
    Advise,
    AdaptAtLaunch
}

/// <summary>
/// User-owned bounds for a managed local inference launch. Zero minimum
/// context means the configured context is the floor, so the default cannot
/// silently reduce it.
/// </summary>
public sealed class AdaptiveInferenceEnvelope
{
    public AdaptiveInferenceMode Mode { get; set; } = AdaptiveInferenceMode.Fixed;
    public int MinimumContext { get; set; }
    public long MinimumGpuHeadroomBytes { get; set; } = ResourceHeadroomPolicy.DefaultDeviceStabilityBytes;
    public bool AllowGpuLayerReduction { get; set; }
    public bool AllowContextReduction { get; set; }
    public bool AllowKvPrecisionChange { get; set; }
    public bool AllowCpuMoePlacement { get; set; }
    public bool AllowMultiDevicePlacement { get; set; }
    public bool PreserveAcceleratedBackend { get; set; } = true;
    public TimeSpan PreferredEvidenceAge { get; set; } = TimeSpan.FromDays(7);

    public AdaptiveInferenceEnvelope Clone() => new()
    {
        Mode = Mode,
        MinimumContext = MinimumContext,
        MinimumGpuHeadroomBytes = MinimumGpuHeadroomBytes,
        AllowGpuLayerReduction = AllowGpuLayerReduction,
        AllowContextReduction = AllowContextReduction,
        AllowKvPrecisionChange = AllowKvPrecisionChange,
        AllowCpuMoePlacement = AllowCpuMoePlacement,
        AllowMultiDevicePlacement = AllowMultiDevicePlacement,
        PreserveAcceleratedBackend = PreserveAcceleratedBackend,
        PreferredEvidenceAge = PreferredEvidenceAge
    };

    public bool TryValidate(out string? error)
    {
        error = null;
        if (MinimumContext < 0 || MinimumContext > 131072)
        {
            error = "Adaptive minimum context must be 0 or between 1 and 131072 tokens.";
            return false;
        }

        if (MinimumGpuHeadroomBytes < 0)
        {
            error = "Adaptive minimum GPU headroom cannot be negative.";
            return false;
        }

        if (PreferredEvidenceAge <= TimeSpan.Zero || PreferredEvidenceAge > TimeSpan.FromDays(30))
        {
            error = "Preferred adaptive evidence age must be greater than zero and no more than 30 days.";
            return false;
        }

        if (!Enum.IsDefined(Mode))
        {
            error = "Adaptive inference mode is not recognized.";
            return false;
        }

        return true;
    }

    public string CanonicalValue => string.Join('|',
        Mode,
        MinimumContext,
        MinimumGpuHeadroomBytes,
        AllowGpuLayerReduction,
        AllowContextReduction,
        AllowKvPrecisionChange,
        AllowCpuMoePlacement,
        AllowMultiDevicePlacement,
        PreserveAcceleratedBackend,
        PreferredEvidenceAge);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdaptiveEvidenceState
{
    Proven,
    Inferred,
    Unknown
}

/// <summary>One bounded field in an effective-launch audit receipt.</summary>
public sealed record AdaptiveFieldObservation(
    string Field,
    string? ConfiguredValue,
    string? PlannedValue,
    string? RenderedValue,
    string? EffectiveValue,
    AdaptiveEvidenceState EvidenceState,
    string EvidenceId);

/// <summary>
/// Runtime evidence for a managed launch. A healthy endpoint is not itself
/// placement evidence, so an unrecognised or absent field remains Unknown.
/// </summary>
public sealed record EffectiveLaunchObservation(
    RuntimeIdentityV2 RuntimeIdentity,
    string ParserVersion,
    bool PropsProbeSucceeded,
    bool FitEnabled,
    long? FitTargetBytes,
    int? FitMinimumContext,
    IReadOnlyList<AdaptiveFieldObservation> Fields,
    IReadOnlyList<string> EvidenceIds,
    bool IsAuditable);
