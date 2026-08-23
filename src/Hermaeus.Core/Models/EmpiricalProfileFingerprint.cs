using System.Security.Cryptography;
using System.Text;

namespace Hermaeus.Core.Models;

/// <summary>
/// The material model and inference settings behind an empirical observation.
/// It identifies a measured configuration, not a capability score or a model
/// selection recommendation.
/// </summary>
public sealed class EmpiricalProfileFingerprint
{
    public string ModelIdentity { get; set; } = string.Empty;
    public string ModelHash { get; set; } = string.Empty;
    public string Quantization { get; set; } = string.Empty;
    public string RuntimeKind { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public int? ContextSize { get; set; }
    public int? GpuLayers { get; set; }
    public int? Threads { get; set; }
    public int? PromptThreads { get; set; }
    public string KvCacheTypeK { get; set; } = string.Empty;
    public string KvCacheTypeV { get; set; } = string.Empty;
    public string FlashAttention { get; set; } = string.Empty;
    public string SpeculativeTypes { get; set; } = string.Empty;
    public string SpeculativeDraftModel { get; set; } = string.Empty;
    public int? SpeculativeNMax { get; set; }
    public int? SpeculativeNMin { get; set; }
    public double? SpeculativePMin { get; set; }
    public int? SpeculativeDraftGpuLayers { get; set; }

    /// <summary>
    /// Stable, opaque key over the fields above. It lets observations group by
    /// the exact known configuration without treating an unset field as a
    /// guessed default.
    /// </summary>
    public string StableId => ComputeStableId(this);

    public static EmpiricalProfileFingerprint From(BenchmarkRunMetadata metadata, string modelIdentity) => new()
    {
        ModelIdentity = modelIdentity,
        ModelHash = metadata.ModelHash,
        Quantization = metadata.Quantization,
        RuntimeKind = metadata.RuntimeKind,
        RuntimeVersion = metadata.RuntimeVersion,
        ContextSize = metadata.ContextSize,
        GpuLayers = metadata.GpuLayers,
        Threads = metadata.Threads,
        PromptThreads = metadata.PromptThreads,
        KvCacheTypeK = metadata.KvCacheTypeK,
        KvCacheTypeV = metadata.KvCacheTypeV,
        FlashAttention = metadata.FlashAttention,
        SpeculativeTypes = metadata.SpeculativeTypes,
        SpeculativeDraftModel = metadata.SpeculativeDraftModel,
        SpeculativeNMax = metadata.SpeculativeNMax,
        SpeculativeNMin = metadata.SpeculativeNMin,
        SpeculativePMin = metadata.SpeculativePMin,
        SpeculativeDraftGpuLayers = metadata.SpeculativeDraftGpuLayers
    };

    private static string ComputeStableId(EmpiricalProfileFingerprint value)
    {
        var fields = new[]
        {
            value.ModelIdentity, value.ModelHash, value.Quantization,
            value.RuntimeKind, value.RuntimeVersion, value.ContextSize?.ToString() ?? "",
            value.GpuLayers?.ToString() ?? "", value.Threads?.ToString() ?? "",
            value.PromptThreads?.ToString() ?? "", value.KvCacheTypeK,
            value.KvCacheTypeV, value.FlashAttention, value.SpeculativeTypes,
            value.SpeculativeDraftModel, value.SpeculativeNMax?.ToString() ?? "",
            value.SpeculativeNMin?.ToString() ?? "",
            value.SpeculativePMin?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            value.SpeculativeDraftGpuLayers?.ToString() ?? ""
        };
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", fields)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
