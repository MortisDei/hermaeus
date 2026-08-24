using System.Globalization;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public enum FitPlacement { Gpu, SystemRam, Split, Unknown }

public sealed record FitComponent(
    string Name,
    long? Bytes,
    long? GpuBytes,
    long? SystemRamBytes,
    FitPlacement Placement,
    EvidenceOrigin Origin,
    string Explanation);

public sealed record ModelFitInputs(
    long ModelFileBytes,
    int ContextSize,
    int GpuLayers,
    int Slots,
    string KvCacheTypeK,
    string KvCacheTypeV,
    bool SwaFull,
    int CpuMoeLayers,
    long AvailableGpuBytes,
    long AvailableSystemRamBytes);

public sealed record FitCompanionInput(
    string Name,
    long FileBytes,
    FitPlacement Placement,
    EvidenceOrigin Origin,
    string Explanation);

public sealed record ModelFitPredictionRequest(
    EmpiricalProfileFingerprintV2? Fingerprint,
    long ModelFileBytes,
    int ContextSize,
    int GpuLayers,
    int Slots,
    string KvCacheTypeK,
    string KvCacheTypeV,
    CapabilityState KvCacheTypeKState,
    CapabilityState KvCacheTypeVState,
    bool SwaFull,
    int CpuMoeLayers,
    HardwareProfile Hardware,
    IReadOnlyList<FitCompanionInput> Companions,
    long? RuntimeOverheadBytes = null,
    long GpuPolicyHeadroomBytes = 0,
    long SystemRamPolicyHeadroomBytes = 2_147_483_648);

public sealed record ModelFitPrediction(
    int PredictionVersion,
    EmpiricalProfileFingerprintV2? Fingerprint,
    ModelFitInputs Inputs,
    FitComponent WeightPlacement,
    IReadOnlyList<FitComponent> KvAllocation,
    FitComponent RuntimeAndComputeOverhead,
    IReadOnlyList<FitComponent> CompanionAllocations,
    IReadOnlyList<FitComponent> Headroom,
    long? GpuRequiredBytes,
    long? SystemRamRequiredBytes,
    IReadOnlyList<string> UnknownComponents,
    ModelFitTier Tier,
    string Explanation);

public static class ModelFitPredictor
{
    private const double WeightAllocationMultiplier = 1.05;

    public static ModelFitPrediction Predict(ModelFitPredictionRequest request, GgufModelInfo? info)
    {
        var unknown = new List<string>();
        if (request.ModelFileBytes <= 0)
            unknown.Add("Model weight size");
        var blockCount = info?.BlockCount;
        var fraction = ResolveOffloadFraction(request.GpuLayers, blockCount, unknown);
        var allocatedWeights = (long)(Math.Max(0, request.ModelFileBytes) * WeightAllocationMultiplier);
        var weights = Component(
            "Model weights",
            allocatedWeights,
            fraction,
            EvidenceOrigin.DeterministicCalculation,
            info is null
                ? "GGUF file size plus 5% allocation overhead fallback; tensor placement metadata is unavailable."
                : "GGUF file size plus 5% allocation overhead, placed by the selected GPU-layer fraction.");

        if (request.CpuMoeLayers != 0)
            unknown.Add("MoE expert tensor placement");

        var kv = BuildKv(request, info, fraction, unknown);
        var runtimeOverheadBytes = request.RuntimeOverheadBytes ?? KvCacheMath.GpuHeadroomBytes;
        var overheadOnGpu = fraction is > 0;
        var overhead = new FitComponent(
            "Runtime and compute overhead",
            runtimeOverheadBytes,
            overheadOnGpu ? runtimeOverheadBytes : 0,
            overheadOnGpu ? 0 : runtimeOverheadBytes,
            overheadOnGpu ? FitPlacement.Gpu : FitPlacement.SystemRam,
            request.RuntimeOverheadBytes.HasValue ? EvidenceOrigin.DirectObservation : EvidenceOrigin.DeterministicCalculation,
            request.RuntimeOverheadBytes.HasValue
                ? "Runtime-scoped observed allocation overhead."
                : "512 MiB analytical fallback retained from the existing GPU Fit model.");

        var companions = request.Companions.Select(input => input.Placement switch
        {
            FitPlacement.Gpu => new FitComponent(input.Name, input.FileBytes, input.FileBytes, 0, input.Placement, input.Origin, input.Explanation),
            FitPlacement.SystemRam => new FitComponent(input.Name, input.FileBytes, 0, input.FileBytes, input.Placement, input.Origin, input.Explanation),
            _ => new FitComponent(input.Name, input.FileBytes, null, null, FitPlacement.Unknown, input.Origin, input.Explanation)
        }).ToArray();
        if (companions.Any(component => component.Placement == FitPlacement.Unknown))
            unknown.Add("Companion model placement");

        var headroom = new[]
        {
            new FitComponent("GPU policy headroom", request.GpuPolicyHeadroomBytes, request.GpuPolicyHeadroomBytes, 0,
                FitPlacement.Gpu, EvidenceOrigin.DeterministicCalculation,
                request.GpuPolicyHeadroomBytes == 0 ? "No additional GPU policy reserve." : "Explicit reserved GPU capacity."),
            new FitComponent("System RAM policy headroom", request.SystemRamPolicyHeadroomBytes, 0, request.SystemRamPolicyHeadroomBytes,
                FitPlacement.SystemRam, EvidenceOrigin.DeterministicCalculation,
                "Explicit system RAM reserve for the desktop and runtime working space.")
        };

        var materialUnknown = unknown.Count > 0;
        long? gpuRequired = materialUnknown ? null : SumGpu([weights, .. kv, overhead, .. companions, .. headroom]);
        long? ramRequired = materialUnknown ? null : SumRam([weights, .. kv, overhead, .. companions, .. headroom]);
        var tier = ResolveTier(gpuRequired, ramRequired, request.Hardware);
        var explanation = materialUnknown
            ? $"Unknown: {string.Join(", ", unknown.Distinct(StringComparer.Ordinal))}. Totals are withheld instead of using false precision."
            : BuildExplanation(gpuRequired!.Value, ramRequired!.Value, request.Hardware, tier);

        return new ModelFitPrediction(
            1,
            request.Fingerprint,
            new ModelFitInputs(
                request.ModelFileBytes, request.ContextSize, request.GpuLayers, Math.Max(1, request.Slots),
                request.KvCacheTypeK, request.KvCacheTypeV, request.SwaFull, request.CpuMoeLayers,
                request.Hardware.MaxGpuVramBytes, request.Hardware.TotalRamBytes),
            weights, kv, overhead, companions, headroom, gpuRequired, ramRequired,
            unknown.Distinct(StringComparer.Ordinal).ToArray(), tier, explanation);
    }

    public static string FormatBreakdown(ModelFitPrediction prediction)
    {
        var gpu = prediction.GpuRequiredBytes.HasValue ? FormatBytes(prediction.GpuRequiredBytes.Value) : "Unknown";
        var ram = prediction.SystemRamRequiredBytes.HasValue ? FormatBytes(prediction.SystemRamRequiredBytes.Value) : "Unknown";
        var components = new[] { prediction.WeightPlacement }
            .Concat(prediction.KvAllocation)
            .Concat([prediction.RuntimeAndComputeOverhead])
            .Concat(prediction.CompanionAllocations)
            .Concat(prediction.Headroom)
            .Select(component => $"{component.Name}: {(component.Bytes.HasValue ? FormatBytes(component.Bytes.Value) : "Unknown")} ({component.Placement})");
        return $"GPU Fit {ModelFitEstimator.Label(prediction.Tier).DefaultIfEmpty("Unknown")} at {prediction.Inputs.ContextSize.ToString("N0", CultureInfo.InvariantCulture)} context, "
            + $"{prediction.Inputs.Slots} slot(s), KV {prediction.Inputs.KvCacheTypeK}/{prediction.Inputs.KvCacheTypeV}: GPU {gpu}; RAM {ram}. "
            + $"{string.Join("; ", components)}. {prediction.Explanation}";
    }

    private static IReadOnlyList<FitComponent> BuildKv(
        ModelFitPredictionRequest request,
        GgufModelInfo? info,
        double? offloadFraction,
        ICollection<string> unknown)
    {
        if (info is null || offloadFraction is null)
        {
            unknown.Add("KV shape or placement");
            return [Unknown("KV key cache"), Unknown("KV value cache")];
        }

        var keyBytes = ResolveKvBytes(request.KvCacheTypeK, request.KvCacheTypeKState, "K", unknown);
        var valueBytes = ResolveKvBytes(request.KvCacheTypeV, request.KvCacheTypeVState, "V", unknown);
        if (!keyBytes.HasValue || !valueBytes.HasValue)
            return [Unknown("KV key cache"), Unknown("KV value cache")];

        var full = KvCacheMath.ProjectAllocation(info, request.ContextSize, -1, keyBytes.Value, valueBytes.Value, request.SwaFull);
        if (full is null)
        {
            unknown.Add("KV tensor shape");
            return [Unknown("KV key cache"), Unknown("KV value cache")];
        }

        return
        [
            Component("KV key cache", full.KeyBytes, offloadFraction, EvidenceOrigin.DeterministicCalculation,
                $"{request.KvCacheTypeK} key cache at {request.ContextSize.ToString("N0", CultureInfo.InvariantCulture)} total context across {Math.Max(1, request.Slots)} slot(s)."),
            Component("KV value cache", full.ValueBytes, offloadFraction, EvidenceOrigin.DeterministicCalculation,
                $"{request.KvCacheTypeV} value cache at {request.ContextSize.ToString("N0", CultureInfo.InvariantCulture)} total context across {Math.Max(1, request.Slots)} slot(s).")
        ];
    }

    private static double? ResolveKvBytes(string type, CapabilityState state, string cache, ICollection<string> unknown)
    {
        var bytes = KvCacheMath.TryResolveKnownBytesPerElement(type);
        if (!bytes.HasValue)
        {
            unknown.Add($"{cache} KV format {type}");
            return null;
        }
        if (KvCacheMath.RequiresRuntimeAdvertisement(type) && state != CapabilityState.Available)
        {
            unknown.Add($"{cache} KV runtime support for {type}");
            return null;
        }
        return bytes;
    }

    private static double? ResolveOffloadFraction(int gpuLayers, int? blockCount, ICollection<string> unknown)
    {
        if (gpuLayers == 0) return 0;
        if (gpuLayers < 0) return 1;
        if (blockCount is > 0) return KvCacheMath.OffloadFraction(gpuLayers, blockCount.Value);
        unknown.Add("Partial GPU layer placement");
        return null;
    }

    private static FitComponent Component(string name, long totalBytes, double? gpuFraction, EvidenceOrigin origin, string explanation)
    {
        if (!gpuFraction.HasValue)
            return Unknown(name) with { Bytes = totalBytes, Origin = origin, Explanation = explanation };
        var gpu = (long)(totalBytes * gpuFraction.Value);
        var ram = totalBytes - gpu;
        var placement = gpu == 0 ? FitPlacement.SystemRam : ram == 0 ? FitPlacement.Gpu : FitPlacement.Split;
        return new FitComponent(name, totalBytes, gpu, ram, placement, origin, explanation);
    }

    private static FitComponent Unknown(string name) => new(
        name, null, null, null, FitPlacement.Unknown, EvidenceOrigin.DeterministicCalculation, "Required facts are unavailable.");

    private static long SumGpu(IEnumerable<FitComponent> components) => components.Sum(component => component.GpuBytes ?? 0);
    private static long SumRam(IEnumerable<FitComponent> components) => components.Sum(component => component.SystemRamBytes ?? 0);

    private static ModelFitTier ResolveTier(long? gpu, long? ram, HardwareProfile hardware)
    {
        if (!gpu.HasValue || !ram.HasValue)
            return ModelFitTier.Unknown;
        if (ram > 0 && hardware.TotalRamBytes <= 0)
            return ModelFitTier.Unknown;
        if (hardware.MaxGpuVramBytes > 0 && gpu <= hardware.MaxGpuVramBytes && ram <= hardware.TotalRamBytes)
            return ModelFitTier.FitsGpu;
        if (gpu > 0 && hardware.MaxGpuVramBytes <= 0 && hardware.TotalRamBytes <= 0)
            return ModelFitTier.Unknown;
        if (hardware.TotalRamBytes > 0 && ram + gpu <= hardware.TotalRamBytes)
            return ModelFitTier.FitsPartial;
        return ModelFitTier.TooLarge;
    }

    private static string BuildExplanation(long gpu, long ram, HardwareProfile hardware, ModelFitTier tier) =>
        $"Deterministic prediction requires {FormatBytes(gpu)} GPU and {FormatBytes(ram)} system RAM against "
        + $"{FormatBytes(hardware.MaxGpuVramBytes)} GPU and {FormatBytes(hardware.TotalRamBytes)} system RAM; tier {tier}.";

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024 / 1024:0.00} GiB";

    private static string DefaultIfEmpty(this string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}

public sealed record GpuFitComparison(
    bool Compatible,
    string CompatibilityDetail,
    long? PredictedGpuBytes,
    long? ObservedPeakGpuBytes,
    long? GpuDiscrepancyBytes,
    double? GpuDiscrepancyPercent,
    long? PredictedSystemRamBytes,
    long? ObservedPeakSystemRamBytes,
    long? SystemRamDiscrepancyBytes,
    double? SystemRamDiscrepancyPercent)
{
    public string DisplaySummary => !Compatible
        ? $"Incompatible observation: {CompatibilityDetail}"
        : $"GPU discrepancy {Format(GpuDiscrepancyBytes, GpuDiscrepancyPercent)}; RAM discrepancy {Format(SystemRamDiscrepancyBytes, SystemRamDiscrepancyPercent)}.";

    private static string Format(long? bytes, double? percent) => bytes.HasValue && percent.HasValue
        ? $"{bytes.Value / 1024d / 1024:0.0} MiB ({percent.Value:+0.0;-0.0;0.0}%)"
        : "Unknown";
}

public static class GpuFitComparisonService
{
    public static GpuFitComparison Compare(ModelFitPrediction prediction, RuntimeTelemetrySeries series)
    {
        if (prediction.Fingerprint is null || !prediction.Fingerprint.IsExactlyCompatibleWith(series.Fingerprint))
            return Incompatible("The v2 runtime, model, hardware, or configuration fingerprint differs.");

        var gpu = BestGpuPeak(series);
        var ram = series.Peak(RuntimeTelemetryMetric.ProcessWorkingSetBytes);
        var observedGpu = IsComparableGpu(gpu) ? gpu!.ValueBytes : null;
        var observedRam = ram is { Trust: RuntimeTelemetryTrustState.ProcessScoped or RuntimeTelemetryTrustState.TrustedRuntime }
            ? ram.ValueBytes : null;
        return new GpuFitComparison(
            true,
            "Exact v2 fingerprint match.",
            prediction.GpuRequiredBytes,
            observedGpu,
            Difference(observedGpu, prediction.GpuRequiredBytes),
            Percent(observedGpu, prediction.GpuRequiredBytes),
            prediction.SystemRamRequiredBytes,
            observedRam,
            Difference(observedRam, prediction.SystemRamRequiredBytes),
            Percent(observedRam, prediction.SystemRamRequiredBytes));
    }

    private static RuntimeTelemetrySample? BestGpuPeak(RuntimeTelemetrySeries series) =>
        series.Samples
            .Where(sample => sample.Metric is RuntimeTelemetryMetric.RuntimeReportedGpuMemoryBytes or RuntimeTelemetryMetric.ProcessGpuMemoryBytes)
            .Where(sample => sample.ValueBytes.HasValue)
            .OrderBy(sample => sample.Trust == RuntimeTelemetryTrustState.TrustedRuntime ? 0 : 1)
            .ThenByDescending(sample => sample.ValueBytes)
            .FirstOrDefault();

    private static bool IsComparableGpu(RuntimeTelemetrySample? sample) =>
        sample is { Trust: RuntimeTelemetryTrustState.TrustedRuntime or RuntimeTelemetryTrustState.ProcessScoped };

    private static long? Difference(long? observed, long? predicted) =>
        observed.HasValue && predicted.HasValue ? observed.Value - predicted.Value : null;

    private static double? Percent(long? observed, long? predicted) =>
        observed.HasValue && predicted is > 0 ? (observed.Value - predicted.Value) * 100d / predicted.Value : null;

    private static GpuFitComparison Incompatible(string detail) => new(
        false, detail, null, null, null, null, null, null, null, null);
}

public sealed record RuntimeTelemetryMetricSummary(
    RuntimeTelemetryMetric Metric,
    long? MinimumBytes,
    long? MaximumBytes,
    double? MeanBytes,
    long? CurrentBytes,
    int SampleCount);

public sealed record GpuFitObservationEvidence(
    string SeriesId,
    string ProcessInstanceId,
    EmpiricalProfileFingerprintV2 Fingerprint,
    DateTime StartedAtUtc,
    IReadOnlyList<RuntimeTelemetryMetricSummary> Summaries,
    IReadOnlyList<RuntimeTelemetrySample> RetainedSamples,
    int TotalSampleCount)
{
    public static GpuFitObservationEvidence From(RuntimeTelemetrySeries series)
    {
        var summaries = series.Samples.GroupBy(sample => sample.Metric).Select(group =>
        {
            var values = group.Where(sample => sample.ValueBytes.HasValue).Select(sample => sample.ValueBytes!.Value).ToArray();
            return new RuntimeTelemetryMetricSummary(
                group.Key,
                values.Length == 0 ? null : values.Min(),
                values.Length == 0 ? null : values.Max(),
                values.Length == 0 ? null : values.Average(),
                series.Current(group.Key)?.ValueBytes,
                group.Count());
        }).ToArray();

        var retained = series.Samples.Take(8)
            .Concat(series.Samples.GroupBy(sample => sample.Metric).SelectMany(group =>
                new[]
                {
                    group.OrderBy(sample => sample.ValueBytes ?? long.MinValue).First(),
                    group.OrderByDescending(sample => sample.ValueBytes ?? long.MinValue).First(),
                    group.OrderByDescending(sample => sample.ObservedAtUtc).First()
                }))
            .Distinct()
            .OrderBy(sample => sample.ObservedAtUtc)
            .Take(64)
            .ToArray();
        return new GpuFitObservationEvidence(
            series.SeriesId, series.ProcessInstanceId, series.Fingerprint,
            series.StartedAtUtc, summaries, retained, series.Samples.Count);
    }

    public RuntimeTelemetrySeries ToSeries() => new(
        SeriesId, ProcessInstanceId, Fingerprint, StartedAtUtc, RetainedSamples);
}

public sealed record GpuFitEvidencePayload(
    ModelFitPrediction Prediction,
    GpuFitObservationEvidence Observation,
    GpuFitComparison Comparison);

public sealed record GpuFitComparisonEntry(
    string ExperienceId,
    DateTime CreatedAtUtc,
    GpuFitComparison Comparison);

public sealed record GpuFitComparisonSet(
    IReadOnlyList<GpuFitComparisonEntry> Compatible,
    IReadOnlyList<GpuFitComparisonEntry> Incompatible);

public sealed class GpuFitExperienceService
{
    private readonly IEmpiricalExperienceStore _store;
    private readonly GpuFitExperienceCodec _codec = new();

    public GpuFitExperienceService(IEmpiricalExperienceStore store) => _store = store;

    public Task<EmpiricalExperience> RecordAsync(
        ModelFitPrediction prediction,
        RuntimeTelemetrySeries series,
        CancellationToken ct = default)
    {
        if (prediction.Fingerprint is null || !prediction.Fingerprint.IsExactlyCompatibleWith(series.Fingerprint))
            throw new InvalidOperationException("GPU Fit evidence requires an exact v2 fingerprint match.");

        var comparison = GpuFitComparisonService.Compare(prediction, series);
        var observation = GpuFitObservationEvidence.From(series);
        var context = new GpuFitExperienceContext(prediction.Fingerprint.Configuration.StableId, ExperienceJson.Canonicalize(prediction));
        var action = new GpuFitExperienceAction(series.SeriesId, ExperienceJson.Canonicalize(new GpuFitEvidencePayload(prediction, observation, comparison)));
        var draft = new EmpiricalExperienceDraft
        {
            Domain = EmpiricalExperienceDomains.GpuFitObservation,
            ContextJson = _codec.EncodeContext(context),
            ActionJson = _codec.EncodeAction(action),
            RuntimeFingerprint = prediction.Fingerprint.Runtime.StableId,
            ModelFingerprint = prediction.Fingerprint.Model.StableId,
            Outcome = NormalizedToolOutcome.Create(
                comparison.GpuDiscrepancyBytes.HasValue || comparison.SystemRamDiscrepancyBytes.HasValue
                    ? NormalizedOutcome.Succeeded
                    : NormalizedOutcome.Unknown,
                "gpu-fit-comparison-v1",
                comparison.DisplaySummary),
            Provenance =
            [
                new EmpiricalExperienceProvenance(
                    series.SeriesId,
                    new SourceReference(
                        ProvenanceKind.Lab,
                        "Runtime telemetry observation series",
                        series.SeriesId,
                        EvidenceOrigin: EvidenceOrigin.DirectObservation))
            ]
        };
        return _store.AddAsync(draft, ct);
    }

    public async Task<GpuFitComparisonSet> QueryComparisonsAsync(
        ModelFitPrediction prediction,
        CancellationToken ct = default)
    {
        var rows = await _store.QueryAsync(new EmpiricalExperienceQuery
        {
            Domain = EmpiricalExperienceDomains.GpuFitObservation,
            Status = EmpiricalExperienceStatus.Current,
            Limit = 500
        }, ct);
        var compatible = new List<GpuFitComparisonEntry>();
        var incompatible = new List<GpuFitComparisonEntry>();
        foreach (var row in rows)
        {
            try
            {
                var action = _codec.DecodeAction(row.ActionJson);
                var payload = ExperienceJson.Decode<GpuFitEvidencePayload>(action.AnalyticalBreakdownJson);
                var comparison = GpuFitComparisonService.Compare(prediction, payload.Observation.ToSeries());
                var entry = new GpuFitComparisonEntry(row.Id, row.CreatedAtUtc, comparison);
                (comparison.Compatible ? compatible : incompatible).Add(entry);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            {
                incompatible.Add(new GpuFitComparisonEntry(
                    row.Id, row.CreatedAtUtc,
                    new GpuFitComparison(false, "Historical GPU Fit evidence does not contain a readable v2 observation.",
                        null, null, null, null, null, null, null, null)));
            }
        }
        return new GpuFitComparisonSet(compatible, incompatible);
    }
}
