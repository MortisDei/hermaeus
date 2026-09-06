using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public enum ModelFitTier { Unknown, FitsGpu, FitsPartial, TooLarge }

public sealed record ModelFitResult(ModelFitTier Tier, string Reason);

/// <summary>
/// What Unsloth shows at download time, for what is already on disk (r13 02-model-library.md
/// 2.5). This compatibility facade retains the public result shape while the
/// versioned <see cref="ModelFitPredictor"/> owns the calculation.
/// </summary>
public static class ModelFitEstimator
{
    public static ModelFitResult Estimate(long fileSizeBytes, HardwareProfile hw)
        => ModelFitPredictor.EstimatePreDownload(fileSizeBytes, hw);

    /// <summary>
    /// KV-cache-aware overload (r17 01-gguf-context-and-tuning.md 1.3): when
    /// <paramref name="info"/> is null (no local GGUF header could be read), this is
    /// byte-identical to <see cref="Estimate(long, HardwareProfile)"/>. When present, the
    /// weights+KV projection at <paramref name="contextSize"/> and full offload (this answers
    /// "could this model fit at all", not "does the currently configured GpuLayers fit" - that
    /// is a Services-card concern, not a library-card one) replaces the flat 1.2 multiplier,
    /// and the Reason states the split so users see why a small file can still be too large at
    /// huge context.
    /// </summary>
    public static ModelFitResult Estimate(long fileSizeBytes, HardwareProfile hw, GgufModelInfo? info, int contextSize, string kvCacheType = "f16")
    {
        if (fileSizeBytes <= 0)
            return new ModelFitResult(ModelFitTier.Unknown, "Model file size is unavailable; fit cannot be estimated.");

        if (info is null)
            return Estimate(fileSizeBytes, hw);

        if (hw.TotalRamBytes <= 0 && hw.MaxGpuVramBytes <= 0)
            return new ModelFitResult(ModelFitTier.Unknown, string.Empty);

        var prediction = ModelFitPredictor.Predict(new ModelFitPredictionRequest(
            Fingerprint: null,
            ModelFileBytes: fileSizeBytes,
            ContextSize: Math.Max(1, contextSize),
            GpuLayers: -1,
            Slots: 1,
            KvCacheTypeK: kvCacheType,
            KvCacheTypeV: kvCacheType,
            KvCacheTypeKState: CapabilityState.Available,
            KvCacheTypeVState: CapabilityState.Available,
            SwaFull: false,
            CpuMoeLayers: 0,
            Hardware: hw,
            Companions: []), info);
        return prediction.UnknownComponents.Count > 0
            ? Estimate(fileSizeBytes, hw)
            : new ModelFitResult(prediction.Tier, $"KV cache projection: {ModelFitPredictor.FormatBreakdown(prediction)}");
    }

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
