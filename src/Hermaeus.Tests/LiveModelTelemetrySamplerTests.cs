using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LiveModelTelemetrySamplerTests
{
    [Fact]
    public async Task Start_captures_once_and_stop_releases_sampling()
    {
        var source = new CapturingSource();
        await using var sampler = new LiveModelTelemetrySampler(source, TimeSpan.FromHours(1));

        await sampler.StartAsync(Request("one"));

        Assert.True(sampler.IsSampling);
        Assert.Single(sampler.CurrentSeries!.Samples);
        await sampler.StopAsync();
        Assert.False(sampler.IsSampling);
    }

    [Fact]
    public async Task Starting_a_new_identity_resets_the_bounded_series()
    {
        var source = new CapturingSource();
        await using var sampler = new LiveModelTelemetrySampler(source, TimeSpan.FromHours(1));

        await sampler.StartAsync(Request("one"));
        await sampler.StartAsync(Request("two"));

        Assert.Equal("two", sampler.CurrentSeries!.Fingerprint.Model.ManifestIdentity);
        Assert.Single(sampler.CurrentSeries.Samples);
        Assert.All(sampler.CurrentSeries.Samples, sample => Assert.Equal(sampler.CurrentSeries.ProcessInstanceId, sample.ProcessInstanceId));
    }

    private static RuntimeTelemetryRequest Request(string modelId)
    {
        var runtime = new RuntimeIdentityV2("test", "runtime", null, null, "1", "build", "compiler", "cpu", "", IdentityCompleteness.Complete);
        var model = new ModelIdentityV2(modelId, "model", null, null, "arch", "q", "", ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete);
        var hardware = new HardwareIdentityV2("test", "x64", "cpu", "device", null, null, "", "", IdentityCompleteness.Complete);
        var config = new ConfigurationIdentityV2(4096, 0, "cpu", 4, null, 1, null, null, "f16", "f16", "off", "none", "", "", null, new Dictionary<string, string>(), IdentityCompleteness.Complete);
        return new RuntimeTelemetryRequest("series", 1, DateTime.UnixEpoch, runtime, new EmpiricalProfileFingerprintV2(runtime, model, hardware, config));
    }

    private sealed class CapturingSource : IRuntimeTelemetrySource
    {
        public Task<IReadOnlyList<RuntimeTelemetrySample>> CaptureAsync(RuntimeTelemetryRequest request, CancellationToken ct = default)
        {
            var instance = RuntimeTelemetrySeries.ProcessInstance(request.ProcessId, request.ProcessStartedAtUtc);
            return Task.FromResult<IReadOnlyList<RuntimeTelemetrySample>>([
                new(request.SeriesId, instance, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 123,
                    RuntimeTelemetrySourceKind.ProcessCounter, RuntimeTelemetryTrustState.ProcessScoped,
                    DateTime.UtcNow, request.RuntimeIdentity.StableId, "test", "test")
            ]);
        }
    }
}
