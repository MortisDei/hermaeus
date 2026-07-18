using Aether.Core.Models;
using Aether.Services;
using Xunit;

namespace Aether.Tests;

// r13 02-model-library.md 2.5: fits-on-your-hardware chip. Pure estimator, tier boundaries.
public sealed class ModelFitEstimatorTests
{
    private const long OneGb = 1024L * 1024 * 1024;

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
        var exactVram = weighted + 1_610_612_736; // GpuHeadroomBytes

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
}
