using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>r18 04-llama-server-engine-options.md 4.3: hardware-tier engine-option
/// recommendation, distilled from the owner-supplied tuning guide's cheat sheet.</summary>
public sealed class EngineOptionPresetTests
{
    [Fact]
    public void Recommends_32k_and_q8_0_for_16gb_and_above()
    {
        var result = EngineOptionPresets.Recommend(16_000_000_000, trainingContextLength: null);

        Assert.Equal(32768, result.ContextSize);
        Assert.Equal("q8_0", result.KvCacheType);
    }

    [Fact]
    public void Recommends_16k_and_q8_0_for_8gb()
    {
        var result = EngineOptionPresets.Recommend(8_000_000_000, trainingContextLength: null);

        Assert.Equal(16384, result.ContextSize);
        Assert.Equal("q8_0", result.KvCacheType);
    }

    [Fact]
    public void Recommends_8k_and_q4_0_for_6gb()
    {
        var result = EngineOptionPresets.Recommend(6_000_000_000, trainingContextLength: null);

        Assert.Equal(8192, result.ContextSize);
        Assert.Equal("q4_0", result.KvCacheType);
    }

    [Fact]
    public void Falls_back_to_f16_when_no_gpu_vram_is_reported()
    {
        var result = EngineOptionPresets.Recommend(0, trainingContextLength: null);

        Assert.Equal("f16", result.KvCacheType);
    }

    [Fact]
    public void Caps_the_suggested_context_at_the_models_training_context()
    {
        var result = EngineOptionPresets.Recommend(16_000_000_000, trainingContextLength: 8192);

        Assert.Equal(8192, result.ContextSize);
    }
}
