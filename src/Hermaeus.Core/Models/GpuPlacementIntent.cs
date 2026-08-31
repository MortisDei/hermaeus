using System.Text.Json.Serialization;

namespace Hermaeus.Core.Models;

/// <summary>Semantic GPU placement requested for a managed llama.cpp server.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GpuPlacementKind
{
    Cpu,
    Auto,
    All,
    Exact
}

/// <summary>
/// Versioned, typed replacement for the legacy integer GPU-layer setting.
/// The intent is configuration, not a claim about what the runtime actually
/// selected. That claim requires an effective-launch observation.
/// </summary>
public sealed class GpuPlacementIntent
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public GpuPlacementKind Kind { get; set; } = GpuPlacementKind.Cpu;
    public int? ExactLayerCount { get; set; }

    public static GpuPlacementIntent Cpu() => new() { Kind = GpuPlacementKind.Cpu };
    public static GpuPlacementIntent Auto() => new() { Kind = GpuPlacementKind.Auto };
    public static GpuPlacementIntent All() => new() { Kind = GpuPlacementKind.All };

    public static GpuPlacementIntent Exact(int layerCount) => new()
    {
        Kind = GpuPlacementKind.Exact,
        ExactLayerCount = layerCount
    };

    public static bool TryFromLegacy(int gpuLayers, out GpuPlacementIntent? intent, out string? error)
    {
        intent = null;
        error = null;
        switch (gpuLayers)
        {
            case 0:
                intent = Cpu();
                return true;
            case -1:
                intent = All();
                return true;
            case > 0:
                intent = Exact(gpuLayers);
                return true;
            default:
                error = "GPU layers must be 0 for CPU, -1 for all layers, or a positive exact layer count.";
                return false;
        }
    }

    public bool TryValidate(out string? error)
    {
        error = null;
        if (SchemaVersion != CurrentSchemaVersion)
        {
            error = $"GPU placement schema version {SchemaVersion} is not supported; expected {CurrentSchemaVersion}.";
            return false;
        }

        if (Kind is not (GpuPlacementKind.Cpu or GpuPlacementKind.Auto or GpuPlacementKind.All or GpuPlacementKind.Exact))
        {
            error = "GPU placement kind is not recognized.";
            return false;
        }

        if (Kind != GpuPlacementKind.Exact)
        {
            if (ExactLayerCount is not null)
            {
                error = $"{Kind} GPU placement cannot include an exact layer count.";
                return false;
            }

            return true;
        }

        if (ExactLayerCount is not > 0)
        {
            error = "Exact GPU placement requires a positive layer count.";
            return false;
        }

        return true;
    }

    public int? LegacyGpuLayers => Kind switch
    {
        GpuPlacementKind.Cpu => 0,
        GpuPlacementKind.All => -1,
        GpuPlacementKind.Exact => ExactLayerCount,
        _ => null
    };

    public string CanonicalValue => Kind switch
    {
        GpuPlacementKind.Cpu => "cpu",
        GpuPlacementKind.Auto => "auto",
        GpuPlacementKind.All => "all",
        GpuPlacementKind.Exact => $"exact:{ExactLayerCount}",
        _ => "unknown"
    };
}
