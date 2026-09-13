using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class RuntimeEvidenceTests
{
    [Fact]
    public void Evidence_requires_the_same_process_for_effective_and_telemetry_receipts()
    {
        var runtime = new RuntimeIdentityV2(
            "llama.cpp", "runtime-hash", 1, DateTime.UnixEpoch, "v", "b", "c", "vulkan", "",
            IdentityCompleteness.Complete);
        var model = new ModelIdentityV2(
            "model", "model-hash", 1, DateTime.UnixEpoch, "test", "Q4", "",
            ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete);
        var configuration = Configuration(4096);
        var process = new RuntimeLaunchProcessEvidence(
            42, DateTime.UnixEpoch, "/runtime/llama-server", ["--ctx-size", "4096"])
        {
            ExecutableSha256 = "runtime-hash"
        };
        var effective = new EffectiveLaunchObservation(
            runtime, EffectiveLaunchObservationParser.ParserVersion, true, false, null, null,
            [
                Field("context", "4096"), Field("slots", "1"), Field("gpu_layers", "0")
            ], ["test.props"], true)
        {
            Process = process
        };

        var envelope = RuntimeEvidenceEvaluator.Evaluate(
            "benchmark", "run", "candidate", runtime, model,
            configuration, configuration, configuration, process, effective,
            ["context", "slots", "gpu_layers"],
            [RuntimeTelemetrySeries.ProcessInstance(99, DateTime.UnixEpoch)],
            RuntimeEvidenceStatus.Unverified);

        Assert.Equal(RuntimeEvidenceStatus.Mismatch, envelope.Status);
        Assert.Contains("telemetry-process-mismatch", envelope.Reasons);
        Assert.False(envelope.ComparisonEligible);
    }

    [Fact]
    public void Evidence_is_inconclusive_when_a_recipe_field_is_not_proven()
    {
        var runtime = new RuntimeIdentityV2(
            "llama.cpp", "runtime-hash", 1, DateTime.UnixEpoch, "v", "b", "c", "vulkan", "",
            IdentityCompleteness.Complete);
        var model = new ModelIdentityV2(
            "model", "model-hash", 1, DateTime.UnixEpoch, "test", "Q4", "",
            ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete);
        var configuration = Configuration(4096);
        var process = new RuntimeLaunchProcessEvidence(
            42, DateTime.UnixEpoch, "/runtime/llama-server", ["--ctx-size", "4096"])
        {
            ExecutableSha256 = "runtime-hash"
        };
        var effective = new EffectiveLaunchObservation(
            runtime, EffectiveLaunchObservationParser.ParserVersion, true, false, null, null,
            [Field("context", "4096"), Field("slots", "1"), Field("gpu_layers", "0")],
            ["test.props"], true)
        {
            Process = process
        };

        var envelope = RuntimeEvidenceEvaluator.Evaluate(
            "lab", "run", "kv-1", runtime, model,
            configuration, configuration, configuration, process, effective,
            ["context", "slots", "gpu_layers", "kv_cache_type_k"],
            [RuntimeTelemetrySeries.ProcessInstance(42, DateTime.UnixEpoch)],
            RuntimeEvidenceStatus.Inconclusive);

        Assert.Equal(RuntimeEvidenceStatus.Inconclusive, envelope.Status);
        Assert.Contains("effective-field:kv_cache_type_k", envelope.Reasons);
        Assert.False(envelope.ApplyEligible);
    }

    [Fact]
    public void Evidence_rejects_a_proven_but_mismatched_effective_field()
    {
        var runtime = new RuntimeIdentityV2(
            "llama.cpp", "runtime-hash", 1, DateTime.UnixEpoch, "v", "b", "c", "vulkan", "",
            IdentityCompleteness.Complete);
        var model = new ModelIdentityV2(
            "model", "model-hash", 1, DateTime.UnixEpoch, "test", "Q4", "",
            ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete);
        var configuration = Configuration(4096);
        var process = new RuntimeLaunchProcessEvidence(
            42, DateTime.UnixEpoch, "/runtime/llama-server", ["--ctx-size", "4096"])
        {
            ExecutableSha256 = "runtime-hash"
        };
        var effective = new EffectiveLaunchObservation(
            runtime, EffectiveLaunchObservationParser.ParserVersion, true, false, null, null,
            [Field("context", "8192"), Field("slots", "1"), Field("gpu_layers", "0")],
            ["test.props"], true)
        {
            Process = process
        };

        var envelope = RuntimeEvidenceEvaluator.Evaluate(
            "benchmark", "run", "candidate", runtime, model,
            configuration, configuration, configuration, process, effective,
            ["context", "slots", "gpu_layers"],
            [RuntimeTelemetrySeries.ProcessInstance(42, DateTime.UnixEpoch)],
            RuntimeEvidenceStatus.Unverified,
            RuntimeEvidenceEvaluator.ExpectedEffectiveValues(configuration,
                ["context", "slots", "gpu_layers"]));

        Assert.Equal(RuntimeEvidenceStatus.Mismatch, envelope.Status);
        Assert.Contains("effective-field-mismatch:context", envelope.Reasons);
        Assert.Equal(string.Empty, envelope.EffectiveConfigurationStableId);
        Assert.False(envelope.ComparisonEligible);
    }

    private static ConfigurationIdentityV2 Configuration(int context) => new(
        context, 0, "v2:cpu", 4, 0, 1, null, null, "f16", "f16", "off", "", "", "", 0,
        new Dictionary<string, string>(), IdentityCompleteness.Complete);

    private static AdaptiveFieldObservation Field(string name, string value) => new(
        name, value, value, value, value, AdaptiveEvidenceState.Proven, $"test.{name}");
}
