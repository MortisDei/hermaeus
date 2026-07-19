using Aether.Services.ProcessManagement;

namespace Aether.Services;

/// <summary>Weights and KV-cache byte projection at a given context size and offload level,
/// before either headroom constant (GPU or RAM) is added - callers add whichever headroom
/// matches the budget they are checking.</summary>
public sealed record KvProjection(long WeightsBytes, long KvBytes)
{
    public long TotalBytes => WeightsBytes + KvBytes;
}

/// <summary>
/// Deterministic KV-cache-cost estimate, pure and next to <see cref="ModelFitEstimator"/>.
/// Always an estimate, never a measurement: known overestimate for sliding-window-attention
/// models (the gemma family), which need less KV than this formula predicts because it
/// assumes full dense attention across the whole context. Acceptable for a warning threshold
/// (r17 01-gguf-context-and-tuning.md 1.2).
/// </summary>
public static class KvCacheMath
{
    public const double DefaultBytesPerElement = 2.0; // f16 cache
    public const long GpuHeadroomBytes = 1_610_612_736; // 1.5 GiB: compute buffers + display overhead

    /// <summary>Replaces the old combined 1.2 weights multiplier now that KV is computed
    /// separately: this covers only rounding/allocation overhead beyond the raw weights file
    /// size.</summary>
    private const double WeightsMultiplier = 1.05;

    /// <summary>Reads <c>--cache-type-k</c>/<c>--cache-type-v</c> from a server's ExtraArgs.
    /// <c>q8_0</c> halves cache bytes to 1.0x; <c>q4_0</c>/<c>q4_1</c> shrink it to 0.5625x;
    /// anything else (including the flag being absent) keeps the f16 default.</summary>
    public static double ResolveBytesPerElement(string? extraArgs, bool isKeyCache)
    {
        if (string.IsNullOrWhiteSpace(extraArgs))
            return DefaultBytesPerElement;

        var flag = isKeyCache ? "--cache-type-k" : "--cache-type-v";
        var tokens = ExtraArgsParser.Split(extraArgs).ToList();
        var index = tokens.FindIndex(t => string.Equals(t, flag, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= tokens.Count)
            return DefaultBytesPerElement;

        return tokens[index + 1].Trim().ToLowerInvariant() switch
        {
            "q8_0" => 1.0,
            "q4_0" or "q4_1" => 0.5625,
            _ => DefaultBytesPerElement
        };
    }

    /// <summary>Bytes of KV cache per token across every layer, at full offload:
    /// block_count * head_count_kv * (key_length * bytesPerElementK + value_length *
    /// bytesPerElementV). Null when the GGUF header lacked the shape facts required
    /// (block_count, head_count_kv, key_length, value_length all must be &gt; 0).</summary>
    public static double? KvBytesPerToken(GgufModelInfo info, double bytesPerElementK, double bytesPerElementV)
    {
        if (info.BlockCount is not > 0 || info.HeadCountKv is not > 0 || info.KeyLength is not > 0 || info.ValueLength is not > 0)
            return null;

        return info.BlockCount.Value * (double)info.HeadCountKv.Value *
            ((info.KeyLength.Value * bytesPerElementK) + (info.ValueLength.Value * bytesPerElementV));
    }

    /// <summary>Offloaded-layer fraction used to scale both the weights and KV terms:
    /// llama.cpp keeps the KV of offloaded layers on the GPU alongside their weights, so
    /// both terms shrink together. 1.0 for full offload (negative, or &gt;= block count),
    /// 0.0 for none on GPU, otherwise layers / block count.</summary>
    public static double OffloadFraction(int gpuLayers, int blockCount)
    {
        if (blockCount <= 0) return 1.0;
        if (gpuLayers < 0 || gpuLayers >= blockCount) return 1.0;
        if (gpuLayers <= 0) return 0.0;
        return gpuLayers / (double)blockCount;
    }

    /// <summary>Full weights+KV projection at <paramref name="contextSize"/> and
    /// <paramref name="gpuLayers"/> offload, before headroom. Null when KV shape facts are
    /// missing from <paramref name="info"/>.</summary>
    public static KvProjection? Project(
        long fileSizeBytes,
        GgufModelInfo info,
        int contextSize,
        int gpuLayers,
        double bytesPerElementK,
        double bytesPerElementV)
    {
        var kvPerToken = KvBytesPerToken(info, bytesPerElementK, bytesPerElementV);
        if (kvPerToken is null)
            return null;

        var fraction = info.BlockCount is > 0 ? OffloadFraction(gpuLayers, info.BlockCount.Value) : 1.0;
        var weights = (long)(fileSizeBytes * WeightsMultiplier * fraction);
        var kv = (long)(kvPerToken.Value * Math.Max(0, contextSize) * fraction);
        return new KvProjection(weights, kv);
    }
}
