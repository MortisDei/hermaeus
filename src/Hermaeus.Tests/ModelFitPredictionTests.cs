using System.Diagnostics;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ModelFitPredictionTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void Separate_k_and_v_formats_produce_separate_components()
    {
        var prediction = Predict(Request(kType: "f16", vType: "f32"), Shape());

        Assert.Equal(2, prediction.KvAllocation.Count);
        Assert.True(prediction.KvAllocation[1].Bytes > prediction.KvAllocation[0].Bytes);
    }

    [Fact]
    public void Gqa_uses_fewer_kv_bytes_than_mha()
    {
        var gqa = Predict(Request(), Shape(headsKv: 8));
        var mha = Predict(Request(), Shape(headsKv: 32));

        Assert.True(gqa.KvAllocation.Sum(x => x.Bytes) < mha.KvAllocation.Sum(x => x.Bytes));
    }

    [Fact]
    public void Mqa_uses_fewer_kv_bytes_than_gqa()
    {
        var mqa = Predict(Request(), Shape(headsKv: 1));
        var gqa = Predict(Request(), Shape(headsKv: 8));

        Assert.True(mqa.KvAllocation.Sum(x => x.Bytes) < gqa.KvAllocation.Sum(x => x.Bytes));
    }

    [Fact]
    public void Sliding_attention_reduces_interleaved_kv_allocation()
    {
        var sliding = Shape(slidingWindow: 1024, pattern: [true, false]);
        var reduced = Predict(Request(context: 8192), sliding);
        var full = Predict(Request(context: 8192, swaFull: true), sliding);

        Assert.True(reduced.KvAllocation.Sum(x => x.Bytes) < full.KvAllocation.Sum(x => x.Bytes));
    }

    [Fact]
    public void Full_gpu_offload_places_weights_and_kv_on_gpu()
    {
        var prediction = Predict(Request(gpuLayers: -1), Shape());

        Assert.Equal(FitPlacement.Gpu, prediction.WeightPlacement.Placement);
        Assert.All(prediction.KvAllocation, item => Assert.Equal(FitPlacement.Gpu, item.Placement));
    }

    [Fact]
    public void Cpu_inference_places_weights_and_kv_in_system_ram()
    {
        var prediction = Predict(Request(gpuLayers: 0), Shape());

        Assert.Equal(FitPlacement.SystemRam, prediction.WeightPlacement.Placement);
        Assert.All(prediction.KvAllocation, item => Assert.Equal(FitPlacement.SystemRam, item.Placement));
    }

    [Fact]
    public void Partial_gpu_layers_split_weights_and_kv()
    {
        var prediction = Predict(Request(gpuLayers: 16), Shape(blocks: 32));

        Assert.Equal(FitPlacement.Split, prediction.WeightPlacement.Placement);
        Assert.All(prediction.KvAllocation, item => Assert.Equal(FitPlacement.Split, item.Placement));
    }

    [Fact]
    public void Partial_offload_without_block_count_withholds_totals()
    {
        var prediction = Predict(Request(gpuLayers: 12), Shape(blocks: null));

        Assert.Null(prediction.GpuRequiredBytes);
        Assert.Contains("Partial GPU layer placement", prediction.UnknownComponents);
        var text = ModelFitPredictor.FormatBreakdown(prediction);
        Assert.Contains("GPU Unknown (known subtotal", text, StringComparison.Ordinal);
        Assert.Contains("Model weights:", text, StringComparison.Ordinal);
        Assert.Contains("KV key cache: Unknown", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_kv_format_never_falls_back_to_f16()
    {
        var prediction = Predict(Request(kType: "future", vType: "f16"), Shape());

        Assert.Null(prediction.GpuRequiredBytes);
        Assert.Contains(prediction.UnknownComponents, value => value.Contains("future", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_model_weight_size_withholds_totals()
    {
        var prediction = Predict(Request(modelBytes: 0), Shape());

        Assert.Null(prediction.GpuRequiredBytes);
        Assert.Contains("Model weight size", prediction.UnknownComponents);
        Assert.Null(prediction.WeightPlacement.Bytes);
        Assert.Contains("Model weights: Unknown", ModelFitPredictor.FormatBreakdown(prediction), StringComparison.Ordinal);
    }

    [Fact]
    public void Low_bit_kv_requires_runtime_advertisement()
    {
        var prediction = Predict(Request(kType: "q4_0", vType: "q4_0", kvState: CapabilityState.Unknown), Shape());

        Assert.Null(prediction.GpuRequiredBytes);
        Assert.Contains(prediction.UnknownComponents, value => value.Contains("runtime support", StringComparison.Ordinal));
    }

    [Fact]
    public void Advertised_low_bit_kv_uses_known_representation()
    {
        var lowBit = Predict(Request(kType: "q4_0", vType: "q4_0", kvState: CapabilityState.Available), Shape());
        var f16 = Predict(Request(), Shape());

        Assert.NotNull(lowBit.GpuRequiredBytes);
        Assert.True(lowBit.KvAllocation.Sum(x => x.Bytes) < f16.KvAllocation.Sum(x => x.Bytes));
    }

    [Fact]
    public void Cpu_moe_without_tensor_placement_metadata_is_unknown()
    {
        var prediction = Predict(Request(cpuMoeLayers: -1), Shape());

        Assert.Null(prediction.GpuRequiredBytes);
        Assert.Contains("MoE expert tensor placement", prediction.UnknownComponents);
    }

    [Fact]
    public void Companion_file_is_a_separate_allocation()
    {
        var companion = new FitCompanionInput("Draft", 256 * 1024 * 1024, FitPlacement.Gpu,
            EvidenceOrigin.DeterministicCalculation, "fixture");
        var prediction = Predict(Request(companions: [companion]), Shape());

        Assert.Single(prediction.CompanionAllocations);
        Assert.Equal(companion.FileBytes, prediction.CompanionAllocations[0].GpuBytes);
    }

    [Fact]
    public void Observed_runtime_overhead_is_not_labelled_as_calculation()
    {
        var prediction = Predict(Request(runtimeOverhead: 1234), Shape());

        Assert.Equal(EvidenceOrigin.DirectObservation, prediction.RuntimeAndComputeOverhead.Origin);
        Assert.Equal(1234, prediction.RuntimeAndComputeOverhead.Bytes);
    }

    [Fact]
    public void Headroom_is_separate_from_runtime_overhead()
    {
        var prediction = Predict(Request(gpuHeadroom: 100, ramHeadroom: 200), Shape());

        Assert.Equal(2, prediction.Headroom.Count);
        Assert.DoesNotContain(prediction.Headroom, item => item.Name.Contains("overhead", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(KvCacheMath.GpuHeadroomBytes, prediction.RuntimeAndComputeOverhead.Bytes);
    }

    [Fact]
    public void Slots_are_recorded_without_multiplying_total_context_allocation()
    {
        var one = Predict(Request(slots: 1), Shape());
        var four = Predict(Request(slots: 4), Shape());

        Assert.Equal(one.KvAllocation.Sum(x => x.Bytes), four.KvAllocation.Sum(x => x.Bytes));
        Assert.Equal(4, four.Inputs.Slots);
    }

    [Fact]
    public void Breakdown_names_every_component_and_unknown_total()
    {
        var prediction = Predict(Request(kType: "future"), Shape());

        var text = ModelFitPredictor.FormatBreakdown(prediction);

        Assert.Contains("Model weights", text, StringComparison.Ordinal);
        Assert.Contains("KV key cache", text, StringComparison.Ordinal);
        Assert.Contains("Runtime and compute overhead", text, StringComparison.Ordinal);
        Assert.Contains("GPU Unknown", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Prediction_tier_reflects_available_budgets()
    {
        var fits = Predict(Request(hardware: new HardwareProfile(32 * GiB, 16 * GiB, "GPU")), Shape());
        var tooLarge = Predict(Request(modelBytes: 20 * GiB, hardware: new HardwareProfile(4 * GiB, 2 * GiB, "GPU")), Shape());

        Assert.Equal(ModelFitTier.FitsGpu, fits.Tier);
        Assert.Equal(ModelFitTier.TooLarge, tooLarge.Tier);
    }

    [Fact]
    public void Exact_fingerprint_comparison_reports_signed_discrepancy()
    {
        var fingerprint = Fingerprint();
        var prediction = Predict(Request(fingerprint: fingerprint), Shape());
        var series = Series(fingerprint,
            Sample(fingerprint, RuntimeTelemetryMetric.ProcessGpuMemoryBytes, prediction.GpuRequiredBytes + 1024, RuntimeTelemetryTrustState.ProcessScoped),
            Sample(fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, prediction.SystemRamRequiredBytes + 2048, RuntimeTelemetryTrustState.ProcessScoped));

        var comparison = GpuFitComparisonService.Compare(prediction, series);

        Assert.True(comparison.Compatible);
        Assert.Equal(1024, comparison.GpuDiscrepancyBytes);
        Assert.Equal(2048, comparison.SystemRamDiscrepancyBytes);
    }

    [Fact]
    public void Fingerprint_mismatch_refuses_comparison()
    {
        var prediction = Predict(Request(fingerprint: Fingerprint()), Shape());
        var other = Fingerprint() with { Configuration = Fingerprint().Configuration with { ContextSize = 8192 } };

        var comparison = GpuFitComparisonService.Compare(prediction, Series(other));

        Assert.False(comparison.Compatible);
        Assert.Null(comparison.GpuDiscrepancyBytes);
    }

    [Fact]
    public void Device_total_gpu_sample_is_not_attributed_to_model()
    {
        var fingerprint = Fingerprint();
        var prediction = Predict(Request(fingerprint: fingerprint), Shape());
        var series = Series(fingerprint,
            Sample(fingerprint, RuntimeTelemetryMetric.DeviceGpuMemoryBytes, 10 * GiB, RuntimeTelemetryTrustState.DeviceTotal));

        var comparison = GpuFitComparisonService.Compare(prediction, series);

        Assert.True(comparison.Compatible);
        Assert.Null(comparison.ObservedPeakGpuBytes);
        Assert.Null(comparison.GpuDiscrepancyBytes);
    }

    private static ModelFitPrediction Predict(ModelFitPredictionRequest request, GgufModelInfo info) =>
        ModelFitPredictor.Predict(request, info);

    private static ModelFitPredictionRequest Request(
        EmpiricalProfileFingerprintV2? fingerprint = null,
        long modelBytes = 4 * GiB,
        int context = 4096,
        int gpuLayers = -1,
        int slots = 1,
        string kType = "f16",
        string vType = "f16",
        CapabilityState kvState = CapabilityState.Unknown,
        bool swaFull = false,
        int cpuMoeLayers = 0,
        HardwareProfile? hardware = null,
        IReadOnlyList<FitCompanionInput>? companions = null,
        long? runtimeOverhead = null,
        long gpuHeadroom = 0,
        long ramHeadroom = 2 * GiB) => new(
            fingerprint, modelBytes, context, gpuLayers, slots, kType, vType,
            kvState, kvState, swaFull, cpuMoeLayers,
            hardware ?? new HardwareProfile(32 * GiB, 16 * GiB, "GPU"),
            companions ?? [], runtimeOverhead, gpuHeadroom, ramHeadroom);

    private static GgufModelInfo Shape(
        int? blocks = 32,
        int headsKv = 8,
        int? slidingWindow = null,
        IReadOnlyList<bool>? pattern = null) => new(
            "test", "Q4", blocks, 8192, 4096, 32, headsKv, 128, 128,
            slidingWindow, pattern);

    internal static EmpiricalProfileFingerprintV2 Fingerprint() => new(
        new RuntimeIdentityV2("llama.cpp", "runtime", 1, DateTime.UnixEpoch, "b1", "1", "c", "gpu", string.Empty, IdentityCompleteness.Complete),
        new ModelIdentityV2("model", "hash", null, null, "test", "Q4", string.Empty, ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete),
        new HardwareIdentityV2("os", "x64", "gpu", "device", 16 * GiB, 32 * GiB, "driver", "single", IdentityCompleteness.Complete),
        new ConfigurationIdentityV2(4096, -1, "gpu-all", 8, 8, 1, null, null, "f16", "f16", "on", string.Empty, string.Empty, string.Empty, 0, new Dictionary<string, string>(), IdentityCompleteness.Complete));

    internal static RuntimeTelemetrySeries Series(EmpiricalProfileFingerprintV2 fingerprint, params RuntimeTelemetrySample[] samples)
    {
        var request = new RuntimeTelemetryRequest("series", 42, DateTime.UnixEpoch, fingerprint.Runtime, fingerprint);
        return RuntimeTelemetrySeries.Start(request).Append(samples);
    }

    internal static RuntimeTelemetrySample Sample(
        EmpiricalProfileFingerprintV2 fingerprint,
        RuntimeTelemetryMetric metric,
        long? value,
        RuntimeTelemetryTrustState trust,
        DateTime? observedAt = null) => new(
            "series", RuntimeTelemetrySeries.ProcessInstance(42, DateTime.UnixEpoch), metric, value,
            trust == RuntimeTelemetryTrustState.DeviceTotal ? RuntimeTelemetrySourceKind.DeviceCounter : RuntimeTelemetrySourceKind.ProcessCounter,
            trust, observedAt ?? DateTime.UnixEpoch, fingerprint.Runtime.StableId, "test", "test");
}
