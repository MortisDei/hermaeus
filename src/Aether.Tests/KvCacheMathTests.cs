using Aether.Services;
using Xunit;

namespace Aether.Tests;

// r17 01-gguf-context-and-tuning.md 1.2: pure KV-cache-cost estimate.
public sealed class KvCacheMathTests
{
    private static GgufModelInfo Shape(int blockCount = 32, int headCountKv = 8, int keyLength = 128, int valueLength = 128) =>
        new("llama", "Q4_K_M", blockCount, 8192, 4096, 32, headCountKv, keyLength, valueLength);

    [Fact]
    public void ResolveBytesPerElement_defaults_to_f16_when_extra_args_is_empty()
    {
        Assert.Equal(2.0, KvCacheMath.ResolveBytesPerElement(null, isKeyCache: true));
        Assert.Equal(2.0, KvCacheMath.ResolveBytesPerElement("", isKeyCache: false));
    }

    [Fact]
    public void ResolveBytesPerElement_maps_cache_type_overrides_per_side()
    {
        Assert.Equal(1.0, KvCacheMath.ResolveBytesPerElement("--cache-type-k q8_0", isKeyCache: true));
        Assert.Equal(0.5625, KvCacheMath.ResolveBytesPerElement("--cache-type-v q4_0", isKeyCache: false));
        Assert.Equal(0.5625, KvCacheMath.ResolveBytesPerElement("--cache-type-k q4_1", isKeyCache: true));
    }

    [Fact]
    public void ResolveBytesPerElement_ignores_the_other_sides_flag()
    {
        // A K-side override should not affect a V-side lookup and vice versa.
        Assert.Equal(2.0, KvCacheMath.ResolveBytesPerElement("--cache-type-k q8_0", isKeyCache: false));
        Assert.Equal(2.0, KvCacheMath.ResolveBytesPerElement("--cache-type-v q8_0", isKeyCache: true));
    }

    [Fact]
    public void ResolveBytesPerElement_keeps_default_for_unrecognized_values()
    {
        Assert.Equal(2.0, KvCacheMath.ResolveBytesPerElement("--cache-type-k f32", isKeyCache: true));
    }

    [Fact]
    public void KvBytesPerToken_matches_hand_computed_arithmetic()
    {
        // block_count 32, kv heads 8, key/value 128 dims each, f16 (2 bytes) on both sides:
        // 32 * 8 * (128*2 + 128*2) = 131072 bytes/token.
        var bytes = KvCacheMath.KvBytesPerToken(Shape(), bytesPerElementK: 2.0, bytesPerElementV: 2.0);

        Assert.Equal(131072d, bytes);
    }

    [Fact]
    public void KvBytesPerToken_applies_independent_k_and_v_byte_sizes()
    {
        // K quantized to q8_0 (1.0x), V stays f16 (2.0x): 32*8*(128*1 + 128*2) = 98304.
        var bytes = KvCacheMath.KvBytesPerToken(Shape(), bytesPerElementK: 1.0, bytesPerElementV: 2.0);

        Assert.Equal(98304d, bytes);
    }

    [Theory]
    [InlineData(0, 8, 128, 128)]
    [InlineData(32, 0, 128, 128)]
    [InlineData(32, 8, 0, 128)]
    [InlineData(32, 8, 128, 0)]
    public void KvBytesPerToken_is_null_when_any_shape_fact_is_missing(int blockCount, int headCountKv, int keyLength, int valueLength)
    {
        var info = new GgufModelInfo("llama", "Q4_K_M", blockCount == 0 ? null : blockCount, 8192, 4096, 32,
            headCountKv == 0 ? null : headCountKv, keyLength == 0 ? null : keyLength, valueLength == 0 ? null : valueLength);

        Assert.Null(KvCacheMath.KvBytesPerToken(info, 2.0, 2.0));
    }

    [Fact]
    public void OffloadFraction_is_full_for_negative_or_at_least_block_count()
    {
        Assert.Equal(1.0, KvCacheMath.OffloadFraction(-1, 32));
        Assert.Equal(1.0, KvCacheMath.OffloadFraction(32, 32));
        Assert.Equal(1.0, KvCacheMath.OffloadFraction(999, 32));
    }

    [Fact]
    public void OffloadFraction_is_zero_when_no_layers_are_offloaded()
    {
        Assert.Equal(0.0, KvCacheMath.OffloadFraction(0, 32));
    }

    [Fact]
    public void OffloadFraction_is_the_layer_ratio_for_partial_offload()
    {
        Assert.Equal(0.5, KvCacheMath.OffloadFraction(16, 32));
        Assert.Equal(0.25, KvCacheMath.OffloadFraction(8, 32));
    }

    [Fact]
    public void Project_full_offload_matches_hand_computed_weights_and_kv()
    {
        const long fileSize = 4_000_000_000; // 4 GB
        var projection = KvCacheMath.Project(fileSize, Shape(), contextSize: 1024, gpuLayers: -1, 2.0, 2.0);

        Assert.NotNull(projection);
        Assert.Equal((long)(fileSize * 1.05), projection!.WeightsBytes);
        Assert.Equal(131072L * 1024, projection.KvBytes);
    }

    [Fact]
    public void Project_scales_both_weights_and_kv_by_the_offload_fraction()
    {
        const long fileSize = 4_000_000_000;
        var full = KvCacheMath.Project(fileSize, Shape(), contextSize: 1024, gpuLayers: -1, 2.0, 2.0)!;
        var half = KvCacheMath.Project(fileSize, Shape(), contextSize: 1024, gpuLayers: 16, 2.0, 2.0)!;

        Assert.Equal(full.WeightsBytes / 2, half.WeightsBytes);
        Assert.Equal(full.KvBytes / 2, half.KvBytes);
    }

    [Fact]
    public void Project_is_zero_weights_and_kv_when_nothing_is_offloaded()
    {
        var projection = KvCacheMath.Project(4_000_000_000, Shape(), contextSize: 4096, gpuLayers: 0, 2.0, 2.0);

        Assert.NotNull(projection);
        Assert.Equal(0, projection!.WeightsBytes);
        Assert.Equal(0, projection.KvBytes);
    }

    [Fact]
    public void Project_returns_null_when_kv_shape_facts_are_missing()
    {
        var info = new GgufModelInfo("llama", "Q4_K_M", null, 8192, 4096, 32, null, null, null);

        Assert.Null(KvCacheMath.Project(4_000_000_000, info, 4096, -1, 2.0, 2.0));
    }
}
