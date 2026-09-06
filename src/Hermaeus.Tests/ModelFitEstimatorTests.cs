using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

// r13 02-model-library.md 2.5: fits-on-your-hardware chip. Pure estimator, tier boundaries.
public sealed class ModelFitEstimatorTests
{
    private const long OneGb = 1024L * 1024 * 1024;

    [Fact]
    public void Estimate_returns_unknown_when_model_size_is_missing()
    {
        var result = ModelFitEstimator.Estimate(0, new HardwareProfile(32 * OneGb, 8 * OneGb, "Test GPU"));

        Assert.Equal(ModelFitTier.Unknown, result.Tier);
        Assert.Contains("unavailable", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0.0 GB", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_size_only_estimate_is_owned_by_the_versioned_predictor()
    {
        var hardware = new HardwareProfile(32 * OneGb, 8 * OneGb, "Test GPU");

        var legacyFacade = ModelFitEstimator.Estimate(4 * OneGb, hardware);
        var versionedProjection = ModelFitPredictor.EstimatePreDownload(4 * OneGb, hardware);

        Assert.Equal(versionedProjection, legacyFacade);
    }

    [Fact]
    public void Estimate_returns_unknown_when_model_size_is_missing_with_metadata()
    {
        var result = ModelFitEstimator.Estimate(0, new HardwareProfile(32 * OneGb, 8 * OneGb, "Test GPU"), Shape(), 8192);

        Assert.Equal(ModelFitTier.Unknown, result.Tier);
        Assert.Contains("unavailable", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0.0 GB", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_returns_unknown_when_hardware_has_no_data()
    {
        var result = ModelFitEstimator.Estimate(4 * OneGb, new HardwareProfile(0, 0, null));

        Assert.Equal(ModelFitTier.Unknown, result.Tier);
        Assert.Equal(string.Empty, result.Reason);
    }

    [Fact]
    public void Estimate_fits_gpu_when_weighted_size_plus_headroom_is_within_vram()
    {
        // 4 GB * 1.2 + 1.5 GB headroom = 6.3 GB, fits comfortably in 8 GB VRAM.
        var hw = new HardwareProfile(TotalRamBytes: 32 * OneGb, MaxGpuVramBytes: 8 * OneGb, GpuName: "Test GPU");

        var result = ModelFitEstimator.Estimate(4 * OneGb, hw);

        Assert.Equal(ModelFitTier.FitsGpu, result.Tier);
        Assert.Contains("VRAM", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_partial_offload_when_too_big_for_vram_but_fits_ram()
    {
        // 9 GB * 1.2 + 1.5 GB = 12.3 GB, exceeds 8 GB VRAM; 9*1.2+2=12.8 GB fits 32 GB RAM.
        var hw = new HardwareProfile(TotalRamBytes: 32 * OneGb, MaxGpuVramBytes: 8 * OneGb, GpuName: "Test GPU");

        var result = ModelFitEstimator.Estimate(9 * OneGb, hw);

        Assert.Equal(ModelFitTier.FitsPartial, result.Tier);
        Assert.Contains("partial CPU offload", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_too_large_when_it_exceeds_both_vram_and_ram_headroom()
    {
        var hw = new HardwareProfile(TotalRamBytes: 8 * OneGb, MaxGpuVramBytes: 4 * OneGb, GpuName: "Test GPU");

        var result = ModelFitEstimator.Estimate(20 * OneGb, hw);

        Assert.Equal(ModelFitTier.TooLarge, result.Tier);
    }

    [Fact]
    public void Estimate_partial_offload_with_no_gpu_still_evaluates_ram()
    {
        var hw = new HardwareProfile(TotalRamBytes: 32 * OneGb, MaxGpuVramBytes: 0, GpuName: null);

        var result = ModelFitEstimator.Estimate(4 * OneGb, hw);

        Assert.Equal(ModelFitTier.FitsPartial, result.Tier);
        Assert.Contains("no GPU detected", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_is_exact_at_the_gpu_boundary()
    {
        const long fileSize = 5 * OneGb;
        var weighted = (long)(fileSize * 1.2);
        var exactVram = weighted + KvCacheMath.GpuHeadroomBytes;

        var fitsExactly = ModelFitEstimator.Estimate(fileSize, new HardwareProfile(0, exactVram, "gpu"));
        var justUnder = ModelFitEstimator.Estimate(fileSize, new HardwareProfile(0, exactVram - 1, "gpu"));

        Assert.Equal(ModelFitTier.FitsGpu, fitsExactly.Tier);
        Assert.NotEqual(ModelFitTier.FitsGpu, justUnder.Tier);
    }

    [Fact]
    public void Label_is_empty_for_unknown_and_short_for_known_tiers()
    {
        Assert.Equal(string.Empty, ModelFitEstimator.Label(ModelFitTier.Unknown));
        Assert.Equal("Fits GPU", ModelFitEstimator.Label(ModelFitTier.FitsGpu));
        Assert.Equal("Partial offload", ModelFitEstimator.Label(ModelFitTier.FitsPartial));
        Assert.Equal("Too large", ModelFitEstimator.Label(ModelFitTier.TooLarge));
    }

    // r17 01-gguf-context-and-tuning.md 1.3: KV-cache-aware overload.
    private static GgufModelInfo Shape() => new("llama", "Q4_K_M", 32, 8192, 4096, 32, 8, 128, 128);

    [Fact]
    public void Estimate_with_null_info_is_byte_identical_to_the_legacy_overload()
    {
        var hw = new HardwareProfile(TotalRamBytes: 32 * OneGb, MaxGpuVramBytes: 8 * OneGb, GpuName: "Test GPU");

        var legacy = ModelFitEstimator.Estimate(4 * OneGb, hw);
        var withNullInfo = ModelFitEstimator.Estimate(4 * OneGb, hw, null, 8192);

        Assert.Equal(legacy, withNullInfo);
    }

    [Fact]
    public void Estimate_with_info_fits_gpu_at_small_context_and_not_at_huge_context()
    {
        // A 4 GB model on an 8 GB card: comfortable at 8k context, but the KV cache at 65k
        // context (32 layers * 8 kv heads * 256 dims * 2 bytes/token = 131072 bytes/token)
        // adds ~8.4 GB on its own, which cannot fit in 8 GB VRAM nor comfortably in RAM headroom
        // alongside the weights depending on machine RAM - assert only the GPU-fit boundary,
        // which is unambiguous.
        var hw = new HardwareProfile(TotalRamBytes: 128 * OneGb, MaxGpuVramBytes: 8 * OneGb, GpuName: "Test GPU");
        var info = Shape();

        var small = ModelFitEstimator.Estimate(4 * OneGb, hw, info, 8192);
        var huge = ModelFitEstimator.Estimate(4 * OneGb, hw, info, 65536);

        Assert.Equal(ModelFitTier.FitsGpu, small.Tier);
        Assert.Contains("KV cache", small.Reason, StringComparison.Ordinal);
        Assert.NotEqual(ModelFitTier.FitsGpu, huge.Tier);
        Assert.Contains("KV cache", huge.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_with_info_falls_back_to_legacy_when_shape_facts_are_missing()
    {
        var hw = new HardwareProfile(TotalRamBytes: 32 * OneGb, MaxGpuVramBytes: 8 * OneGb, GpuName: "Test GPU");
        var incomplete = new GgufModelInfo("llama", "Q4_K_M", null, null, null, null, null, null, null);

        var withIncompleteInfo = ModelFitEstimator.Estimate(4 * OneGb, hw, incomplete, 8192);
        var legacy = ModelFitEstimator.Estimate(4 * OneGb, hw);

        Assert.Equal(legacy, withIncompleteInfo);
    }

    [Fact]
    public void Estimate_with_info_returns_unknown_when_hardware_has_no_data()
    {
        var result = ModelFitEstimator.Estimate(4 * OneGb, new HardwareProfile(0, 0, null), Shape(), 8192);

        Assert.Equal(ModelFitTier.Unknown, result.Tier);
    }
}
