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
/// Always an estimate, never a measurement: uses GGUF sliding-window metadata when present
/// so interleaved attention is not charged as dense attention on every layer.
/// </summary>
public static class KvCacheMath
{
    public const double DefaultBytesPerElement = 2.0; // f16 cache

    /// <summary>Compute buffers + display overhead beyond the weights+KV projection. The single
    /// copy of this constant (r18 01-finish-the-open-work.md 1.4 collapsed a second literal copy
    /// that used to live on <see cref="ModelFitEstimator"/>). Was 1.5 GiB at r17; corrected once
    /// to 512 MiB against a real false-warning report (Gemma E4B QAT, ~4.8 GB actual VRAM use at
    /// 64k context). Still a rough estimate, not a measurement - if it needs correcting again,
    /// prefer deriving it from something measurable (the compute-buffer size llama.cpp itself
    /// reports at startup) over a second guess.</summary>
    public const long GpuHeadroomBytes = 536_870_912;

    /// <summary>Replaces the old combined 1.2 weights multiplier now that KV is computed
    /// separately: this covers only rounding/allocation overhead beyond the raw weights file
    /// size.</summary>
    private const double WeightsMultiplier = 1.05;

    /// <summary>Reads <c>--cache-type-k</c>/<c>--cache-type-v</c> from a server's ExtraArgs.
    /// Superseded as of r18 by the <see cref="ResolveBytesPerElement(string,string?,bool)"/>
    /// overload that also reads the first-class <c>ServerConfig.KvCacheTypeK/V</c> fields;
    /// kept for callers with only an ExtraArgs string on hand.</summary>
    public static double ResolveBytesPerElement(string? extraArgs, bool isKeyCache)
    {
        if (string.IsNullOrWhiteSpace(extraArgs))
            return DefaultBytesPerElement;

        var flag = isKeyCache ? "--cache-type-k" : "--cache-type-v";
        var tokens = ExtraArgsParser.Split(extraArgs).ToList();
        var index = tokens.FindIndex(t => string.Equals(t, flag, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= tokens.Count)
            return DefaultBytesPerElement;

        return BytesPerElementFor(tokens[index + 1]);
    }

    /// <summary>
    /// r18 04-llama-server-engine-options.md 4.2: resolves bytes-per-element from whichever
    /// actually wins at launch - ExtraArgs' own <c>--cache-type-k</c>/<c>-v</c> if present
    /// (matching <c>ServerProcessManager.BuildLaunchArguments</c>'s <c>HasArg</c> guard, which
    /// suppresses the first-class field whenever ExtraArgs already sets the flag), otherwise the
    /// first-class <paramref name="kvCacheType"/> field (defaulting to f16, byte-identical to
    /// pre-r18 behavior when never touched).
    /// </summary>
    public static double ResolveBytesPerElement(string kvCacheType, string? extraArgs, bool isKeyCache)
    {
        var flag = isKeyCache ? "--cache-type-k" : "--cache-type-v";
        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            var tokens = ExtraArgsParser.Split(extraArgs).ToList();
            var index = tokens.FindIndex(t => string.Equals(t, flag, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index + 1 < tokens.Count)
                return BytesPerElementFor(tokens[index + 1]);
        }

        return BytesPerElementFor(kvCacheType);
    }

    /// <summary>
    /// Full verified value set (r18 04-llama-server-engine-options.md 4.2), derived from each
    /// format's bits-per-weight over 8: f32 4.0, f16/bf16 2.0, q8_0 1.0625 (8.5 bits, refining
    /// r17's 1.0), q5_0/q5_1 0.6875, q4_0/q4_1/iq4_nl 0.5625. Unrecognized strings (including an
    /// empty one) keep the f16 default.
    /// </summary>
    private static double BytesPerElementFor(string? cacheType) => (cacheType ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "f32" => 4.0,
        "f16" or "bf16" => 2.0,
        "q8_0" => 1.0625,
        "q5_0" or "q5_1" => 0.6875,
        "q4_0" or "q4_1" or "iq4_nl" => 0.5625,
        _ => DefaultBytesPerElement
    };

    /// <summary>Dense-attention bytes of KV cache per token across every layer, at full offload:
    /// block_count * head_count_kv * (key_length * bytesPerElementK + value_length *
    /// bytesPerElementV). Null when the GGUF header lacked the shape facts required
    /// (block_count, head_count_kv, key_length, value_length all must be &gt; 0). This is
    /// the dense fallback and does not apply sliding-window reductions.</summary>
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

    /// <summary>
    /// r18 04-llama-server-engine-options.md 4.6: <c>--swa-full</c> makes llama-server allocate
    /// the full-context-sized KV cache for sliding-window-attention layers instead of the
    /// sliding-window-sized one <see cref="ProjectKvBytes"/> otherwise models - the engine-side
    /// counterpart of r17's "sliding-window models are a known overestimate [of how much fits]"
    /// caveat. Checked the same way <see cref="ResolveBytesPerElement(string?,bool)"/> scans
    /// ExtraArgs for a flag.
    /// </summary>
    public static bool HasSwaFull(string? extraArgs) =>
        !string.IsNullOrWhiteSpace(extraArgs)
        && ExtraArgsParser.Split(extraArgs).Any(t => string.Equals(t, "--swa-full", StringComparison.OrdinalIgnoreCase));

    /// <summary>Full weights+KV projection at <paramref name="contextSize"/> and
    /// <paramref name="gpuLayers"/> offload, before headroom. Null when KV shape facts are
    /// missing from <paramref name="info"/>. <paramref name="swaFull"/> (default false, matching
    /// pre-r18 behavior) skips the sliding-window discount, matching <c>--swa-full</c>.</summary>
    public static KvProjection? Project(
        long fileSizeBytes,
        GgufModelInfo info,
        int contextSize,
        int gpuLayers,
        double bytesPerElementK,
        double bytesPerElementV,
        bool swaFull = false)
    {
        var kvPerToken = KvBytesPerToken(info, bytesPerElementK, bytesPerElementV);
        if (kvPerToken is null)
            return null;

        var fraction = info.BlockCount is > 0 ? OffloadFraction(gpuLayers, info.BlockCount.Value) : 1.0;
        var weights = (long)(fileSizeBytes * WeightsMultiplier * fraction);
        var kv = (long)(ProjectKvBytes(info, kvPerToken.Value, contextSize, swaFull) * fraction);
        return new KvProjection(weights, kv);
    }

    private static double ProjectKvBytes(GgufModelInfo info, double denseKvBytesPerToken, int contextSize, bool swaFull = false)
    {
        var ctx = Math.Max(0, contextSize);
        if (ctx == 0)
            return 0;

        if (info.BlockCount is not > 0 ||
            info.SlidingWindow is not > 0 ||
            info.SlidingWindowPattern is not { Count: > 0 } pattern ||
            swaFull)
        {
            return denseKvBytesPerToken * ctx;
        }

        var layerBytesPerToken = denseKvBytesPerToken / info.BlockCount.Value;
        var slidingContext = Math.Min(ctx, info.SlidingWindow.Value);
        double tokenSlots = 0;

        for (var layer = 0; layer < info.BlockCount.Value; layer++)
        {
            var isSliding = pattern[layer % pattern.Count];
            tokenSlots += isSliding ? slidingContext : ctx;
        }

        return layerBytesPerToken * tokenSlots;
    }
}
