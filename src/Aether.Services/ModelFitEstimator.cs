using Aether.Core.Models;

namespace Aether.Services;

public enum ModelFitTier { Unknown, FitsGpu, FitsPartial, TooLarge }

public sealed record ModelFitResult(ModelFitTier Tier, string Reason);

/// <summary>
/// What Unsloth shows at download time, for what is already on disk (r13 02-model-library.md
/// 2.5). The multiplier/headroom constants below are a deliberate rough estimate for KV cache
/// and compute buffers beyond raw file size, not a measured science.
/// </summary>
public static class ModelFitEstimator
{
    private const double WeightsMultiplier = 1.2;
    private const long GpuHeadroomBytes = 1_610_612_736; // 1.5 GiB
    private const long RamHeadroomBytes = 2_147_483_648; // 2 GiB

    public static ModelFitResult Estimate(long fileSizeBytes, HardwareProfile hw)
    {
        if (hw.TotalRamBytes <= 0 && hw.MaxGpuVramBytes <= 0)
            return new ModelFitResult(ModelFitTier.Unknown, string.Empty);

        var weighted = (long)(fileSizeBytes * WeightsMultiplier);

        if (hw.MaxGpuVramBytes > 0 && weighted + GpuHeadroomBytes <= hw.MaxGpuVramBytes)
            return new ModelFitResult(ModelFitTier.FitsGpu, $"~{FormatGb(fileSizeBytes)} model fits fully in {FormatGb(hw.MaxGpuVramBytes)} VRAM.");

        if (hw.TotalRamBytes > 0 && weighted + RamHeadroomBytes <= hw.TotalRamBytes)
        {
            return hw.MaxGpuVramBytes > 0
                ? new ModelFitResult(ModelFitTier.FitsPartial, $"~{FormatGb(fileSizeBytes)} model vs {FormatGb(hw.MaxGpuVramBytes)} VRAM: needs partial CPU offload.")
                : new ModelFitResult(ModelFitTier.FitsPartial, $"~{FormatGb(fileSizeBytes)} model runs on CPU/RAM only: no GPU detected.");
        }

        return new ModelFitResult(ModelFitTier.TooLarge, $"~{FormatGb(fileSizeBytes)} model exceeds available VRAM and RAM headroom.");
    }

    private static string FormatGb(long bytes) => $"{bytes / 1024d / 1024 / 1024:0.0} GB";

    /// <summary>Short chip text for a tier; empty for Unknown so callers render nothing rather
    /// than guessing. Shared between the Models page cards, the HF browser file list, and the
    /// wizard's starter-model tier so all three surfaces describe fit identically.</summary>
    public static string Label(ModelFitTier tier) => tier switch
    {
        ModelFitTier.FitsGpu => "Fits GPU",
        ModelFitTier.FitsPartial => "Partial offload",
        ModelFitTier.TooLarge => "Too large",
        _ => string.Empty
    };
}
