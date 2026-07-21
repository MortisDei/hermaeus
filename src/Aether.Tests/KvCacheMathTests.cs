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
        // r18 04-llama-server-engine-options.md 4.2 refines q8_0 from 1.0 to 1.0625 (8.5 bits).
        Assert.Equal(1.0625, KvCacheMath.ResolveBytesPerElement("--cache-type-k q8_0", isKeyCache: true));
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
    public void ResolveBytesPerElement_maps_the_full_verified_value_set()
    {
        // r18 04-llama-server-engine-options.md 4.2: f32 is now a real mapped value (4.0), not
        // an "unrecognized" fallback to the f16 default.
        Assert.Equal(4.0, KvCacheMath.ResolveBytesPerElement("--cache-type-k f32", isKeyCache: true));
        Assert.Equal(2.0, KvCacheMath.ResolveBytesPerElement("--cache-type-k bf16", isKeyCache: true));
        Assert.Equal(0.6875, KvCacheMath.ResolveBytesPerElement("--cache-type-v q5_0", isKeyCache: false));
        Assert.Equal(0.6875, KvCacheMath.ResolveBytesPerElement("--cache-type-v q5_1", isKeyCache: false));
        Assert.Equal(0.5625, KvCacheMath.ResolveBytesPerElement("--cache-type-k iq4_nl", isKeyCache: true));
        Assert.Equal(2.0, KvCacheMath.ResolveBytesPerElement("--cache-type-k not-a-real-type", isKeyCache: true));
    }

    [Fact]
    public void ResolveBytesPerElement_three_arg_overload_prefers_extra_args_over_the_first_class_field()
    {
        // Mirrors ServerProcessManager.BuildLaunchArguments' HasArg guard: ExtraArgs already
        // setting the flag suppresses first-class emission, so the math must agree.
        Assert.Equal(1.0625, KvCacheMath.ResolveBytesPerElement("q4_0", "--cache-type-k q8_0", isKeyCache: true));
    }

    [Fact]
    public void ResolveBytesPerElement_three_arg_overload_falls_back_to_the_first_class_field()
    {
        Assert.Equal(0.5625, KvCacheMath.ResolveBytesPerElement("q4_0", extraArgs: null, isKeyCache: true));
        Assert.Equal(2.0, KvCacheMath.ResolveBytesPerElement("f16", extraArgs: null, isKeyCache: true));
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
    public void Project_applies_sliding_window_pattern_when_present()
    {
        var info = new GgufModelInfo(
            "gemma3",
            "Q4_K_M",
            BlockCount: 6,
            TrainingContextLength: 131072,
            EmbeddingLength: 6,
            HeadCount: 1,
            HeadCountKv: 1,
            KeyLength: 1,
            ValueLength: 1,
            SlidingWindow: 10,
            SlidingWindowPattern: [true, true, true, true, true, false]);

        var projection = KvCacheMath.Project(1_000_000, info, contextSize: 100, gpuLayers: -1, 1.0, 1.0);

        Assert.NotNull(projection);
        // Six dense layers would be 6 * (1 + 1) * 100 = 1200 bytes. The pattern has five
        // sliding layers capped at 10 tokens and one full layer: (5 * 10 + 100) * 2 = 300.
        Assert.Equal(300, projection!.KvBytes);
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

    /// <summary>r18 04-llama-server-engine-options.md 4.6: --swa-full allocates the full-context
    /// KV cache for sliding-window layers instead of the sliding-window-sized one.</summary>
    [Fact]
    public void Project_swa_full_skips_the_sliding_window_discount()
    {
        var info = new GgufModelInfo(
            "gemma3", "Q4_K_M",
            BlockCount: 6, TrainingContextLength: 131072, EmbeddingLength: 6, HeadCount: 1,
            HeadCountKv: 1, KeyLength: 1, ValueLength: 1,
            SlidingWindow: 10,
            SlidingWindowPattern: [true, true, true, true, true, false]);

        var withDiscount = KvCacheMath.Project(1_000_000, info, contextSize: 100, gpuLayers: -1, 1.0, 1.0, swaFull: false);
        var swaFull = KvCacheMath.Project(1_000_000, info, contextSize: 100, gpuLayers: -1, 1.0, 1.0, swaFull: true);

        Assert.NotNull(withDiscount);
        Assert.NotNull(swaFull);
        // Six dense layers at full context: 6 * (1 + 1) * 100 = 1200 bytes.
        Assert.Equal(1200, swaFull!.KvBytes);
        Assert.True(swaFull.KvBytes > withDiscount!.KvBytes);
    }

    [Fact]
    public void HasSwaFull_detects_the_flag_in_extra_args()
    {
        Assert.True(KvCacheMath.HasSwaFull("--swa-full"));
        Assert.True(KvCacheMath.HasSwaFull("--ctx-size 4096 --swa-full"));
        Assert.False(KvCacheMath.HasSwaFull("--ctx-size 4096"));
        Assert.False(KvCacheMath.HasSwaFull(null));
    }
}
