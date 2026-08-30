using System.Diagnostics;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class RuntimeTelemetryTests
{
    [Fact]
    public void Chat_telemetry_button_invokes_the_existing_managed_runtime_request_path()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var chatView = File.ReadAllText(Path.Combine(repoRoot, "src", "Hermaeus.Desktop", "Views", "ChatView.axaml"));
        var buttonStart = chatView.IndexOf("<Button Grid.Column=\"10\"", StringComparison.Ordinal);
        var buttonEnd = chatView.IndexOf('>', buttonStart);
        Assert.True(buttonStart >= 0 && buttonEnd > buttonStart, "the telemetry button was not found");
        var telemetryButton = chatView[buttonStart..buttonEnd];

        Assert.Contains("Command=\"{Binding OpenTelemetryCommand}\"", telemetryButton, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("123, 512\n456, 1024", 123, 512L * 1024 * 1024)]
    [InlineData("123, [N/A]", 123, null)]
    public void Nvidia_process_memory_parser_is_pid_scoped(string output, int pid, long? expected)
    {
        var parsed = ProcessGpuMemoryParser.TryGetBytes(output, pid, out var bytes);

        Assert.Equal(expected.HasValue, parsed);
        if (expected.HasValue) Assert.Equal(expected.Value, bytes);
    }

    [Fact]
    public void Series_rejects_samples_from_restarted_process()
    {
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var series = ModelFitPredictionTests.Series(fingerprint);
        var wrong = ModelFitPredictionTests.Sample(fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 1, RuntimeTelemetryTrustState.ProcessScoped)
            with { ProcessInstanceId = RuntimeTelemetrySeries.ProcessInstance(42, DateTime.UnixEpoch.AddSeconds(1)) };

        Assert.Throws<InvalidOperationException>(() => series.Append([wrong]));
    }

    [Fact]
    public void Series_rejects_samples_from_other_runtime_identity()
    {
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var series = ModelFitPredictionTests.Series(fingerprint);
        var wrong = ModelFitPredictionTests.Sample(fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 1, RuntimeTelemetryTrustState.ProcessScoped)
            with { RuntimeStableId = "other" };

        Assert.Throws<InvalidOperationException>(() => series.Append([wrong]));
    }

    [Fact]
    public void Series_start_rejects_runtime_that_differs_from_fingerprint()
    {
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var otherRuntime = fingerprint.Runtime with { ExecutableSha256 = "other" };
        var request = new RuntimeTelemetryRequest("series", 1, DateTime.UnixEpoch, otherRuntime, fingerprint);

        Assert.Throws<InvalidOperationException>(() => RuntimeTelemetrySeries.Start(request));
    }

    [Fact]
    public void Series_deduplicates_identical_samples()
    {
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var sample = ModelFitPredictionTests.Sample(fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 1, RuntimeTelemetryTrustState.ProcessScoped);

        var series = ModelFitPredictionTests.Series(fingerprint, sample, sample);

        Assert.Single(series.Samples);
    }

    [Fact]
    public void Series_is_bounded()
    {
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var samples = Enumerable.Range(0, RuntimeTelemetrySeries.MaximumSamples + 20)
            .Select(index => ModelFitPredictionTests.Sample(
                fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, index,
                RuntimeTelemetryTrustState.ProcessScoped, DateTime.UnixEpoch.AddSeconds(index)))
            .ToArray();

        var series = ModelFitPredictionTests.Series(fingerprint, samples);

        Assert.Equal(RuntimeTelemetrySeries.MaximumSamples, series.Samples.Count);
        Assert.Equal(20, series.Samples[0].ValueBytes);
    }

    [Fact]
    public void Series_reports_current_and_peak_separately()
    {
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var series = ModelFitPredictionTests.Series(fingerprint,
            ModelFitPredictionTests.Sample(fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 20, RuntimeTelemetryTrustState.ProcessScoped, DateTime.UnixEpoch),
            ModelFitPredictionTests.Sample(fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 10, RuntimeTelemetryTrustState.ProcessScoped, DateTime.UnixEpoch.AddSeconds(1)));

        Assert.Equal(10, series.Current(RuntimeTelemetryMetric.ProcessWorkingSetBytes)?.ValueBytes);
        Assert.Equal(20, series.Peak(RuntimeTelemetryMetric.ProcessWorkingSetBytes)?.ValueBytes);
    }

    [Fact]
    public async Task Process_source_samples_matching_process_working_set()
    {
        using var process = Process.GetCurrentProcess();
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var request = new RuntimeTelemetryRequest(
            "live", process.Id, process.StartTime.ToUniversalTime(), fingerprint.Runtime, fingerprint);
        var source = new ProcessRuntimeTelemetrySource();

        var samples = await source.CaptureAsync(request);

        var workingSet = Assert.Single(samples, sample => sample.Metric == RuntimeTelemetryMetric.ProcessWorkingSetBytes);
        Assert.True(workingSet.ValueBytes > 0);
        Assert.Equal(RuntimeTelemetryTrustState.ProcessScoped, workingSet.Trust);
        var gpu = Assert.Single(samples, sample => sample.Metric == RuntimeTelemetryMetric.ProcessGpuMemoryBytes);
        Assert.Null(gpu.ValueBytes);
        Assert.Equal(RuntimeTelemetryTrustState.Unknown, gpu.Trust);
    }

    [Fact]
    public async Task Missing_process_returns_unknown_samples()
    {
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var request = new RuntimeTelemetryRequest(
            "missing", int.MaxValue, DateTime.UnixEpoch, fingerprint.Runtime, fingerprint);

        var samples = await new ProcessRuntimeTelemetrySource().CaptureAsync(request);

        Assert.All(samples, sample =>
        {
            Assert.Null(sample.ValueBytes);
            Assert.Equal(RuntimeTelemetryTrustState.Unknown, sample.Trust);
        });
    }

    [Fact]
    public async Task Gpu_fit_experience_retains_prediction_observation_and_comparison()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var prediction = ModelFitPredictor.Predict(new ModelFitPredictionRequest(
            fingerprint, 1024, 4096, -1, 1, "f16", "f16",
            CapabilityState.Unknown, CapabilityState.Unknown, false, 0,
            new HardwareProfile(32L * 1024 * 1024 * 1024, 16L * 1024 * 1024 * 1024, "GPU"), []),
            new GgufModelInfo("test", "Q4", 2, 4096, 128, 2, 1, 64, 64));
        var series = ModelFitPredictionTests.Series(fingerprint,
            ModelFitPredictionTests.Sample(fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 1234, RuntimeTelemetryTrustState.ProcessScoped));

        var saved = await new GpuFitExperienceService(store).RecordAsync(prediction, series);

        Assert.Equal(EmpiricalExperienceDomains.GpuFitObservation, saved.Domain);
        Assert.Contains("observation", saved.ActionJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("comparison", saved.ActionJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fingerprint.Runtime.StableId, saved.RuntimeFingerprint);
    }

    [Fact]
    public async Task Gpu_fit_experience_refuses_mismatched_fingerprints()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var prediction = ModelFitPredictor.Predict(new ModelFitPredictionRequest(
            fingerprint, 1024, 4096, -1, 1, "f16", "f16",
            CapabilityState.Unknown, CapabilityState.Unknown, false, 0,
            new HardwareProfile(32, 16, "GPU"), []),
            new GgufModelInfo("test", "Q4", 2, 4096, 128, 2, 1, 64, 64));
        var mismatch = fingerprint with { Hardware = fingerprint.Hardware with { DriverVersion = "changed" } };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GpuFitExperienceService(store).RecordAsync(prediction, ModelFitPredictionTests.Series(mismatch)));
    }

    [Fact]
    public void Persisted_observation_summarizes_high_frequency_samples_and_retains_extrema()
    {
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var samples = Enumerable.Range(0, 200).Select(index =>
            ModelFitPredictionTests.Sample(
                fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, index,
                RuntimeTelemetryTrustState.ProcessScoped, DateTime.UnixEpoch.AddMilliseconds(index))).ToArray();
        var evidence = GpuFitObservationEvidence.From(ModelFitPredictionTests.Series(fingerprint, samples));

        Assert.Equal(200, evidence.TotalSampleCount);
        Assert.True(evidence.RetainedSamples.Count < evidence.TotalSampleCount);
        var summary = Assert.Single(evidence.Summaries);
        Assert.Equal(0, summary.MinimumBytes);
        Assert.Equal(199, summary.MaximumBytes);
        Assert.Equal(199, summary.CurrentBytes);
    }

    [Fact]
    public async Task Query_separates_compatible_and_incompatible_observations()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        var service = new GpuFitExperienceService(store);
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var prediction = ModelFitPredictor.Predict(new ModelFitPredictionRequest(
            fingerprint, 1024, 4096, -1, 1, "f16", "f16",
            CapabilityState.Unknown, CapabilityState.Unknown, false, 0,
            new HardwareProfile(32L * 1024 * 1024 * 1024, 16L * 1024 * 1024 * 1024, "GPU"), []),
            new GgufModelInfo("test", "Q4", 2, 4096, 128, 2, 1, 64, 64));
        await service.RecordAsync(prediction, ModelFitPredictionTests.Series(fingerprint,
            ModelFitPredictionTests.Sample(fingerprint, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 1234, RuntimeTelemetryTrustState.ProcessScoped)));
        var other = fingerprint with { Configuration = fingerprint.Configuration with { ContextSize = 8192 } };
        var otherPrediction = prediction with { Fingerprint = other };
        await service.RecordAsync(otherPrediction, ModelFitPredictionTests.Series(other,
            ModelFitPredictionTests.Sample(other, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 5678, RuntimeTelemetryTrustState.ProcessScoped)));

        var result = await service.QueryComparisonsAsync(prediction);

        Assert.Single(result.Compatible);
        Assert.Single(result.Incompatible);
        Assert.Contains("fingerprint differs", result.Incompatible[0].Comparison.CompatibilityDetail, StringComparison.OrdinalIgnoreCase);
    }
}
