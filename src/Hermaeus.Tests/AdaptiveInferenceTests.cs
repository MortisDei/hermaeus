using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AdaptiveInferenceTests
{
    [Fact]
    public void Envelope_defaults_to_fixed_and_preserves_acceleration()
    {
        var envelope = new AdaptiveInferenceEnvelope();

        Assert.Equal(AdaptiveInferenceMode.Fixed, envelope.Mode);
        Assert.Equal(ResourceHeadroomPolicy.DefaultDeviceStabilityBytes, envelope.MinimumGpuHeadroomBytes);
        Assert.True(envelope.PreserveAcceleratedBackend);
        Assert.Equal(TimeSpan.FromDays(7), envelope.PreferredEvidenceAge);
        Assert.True(envelope.TryValidate(out _));
    }

    [Fact]
    public void Envelope_clone_is_independent_and_rejects_invalid_bounds()
    {
        var source = new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.AdaptAtLaunch,
            MinimumContext = 4096,
            MinimumGpuHeadroomBytes = 123,
            AllowGpuLayerReduction = true,
            PreferredEvidenceAge = TimeSpan.FromDays(3)
        };

        var clone = source.Clone();
        clone.MinimumContext = 8192;
        clone.AllowGpuLayerReduction = false;

        Assert.Equal(4096, source.MinimumContext);
        Assert.True(source.AllowGpuLayerReduction);
        Assert.False(new AdaptiveInferenceEnvelope { MinimumContext = -1 }.TryValidate(out _));
        Assert.False(new AdaptiveInferenceEnvelope { MinimumGpuHeadroomBytes = -1 }.TryValidate(out _));
        Assert.False(new AdaptiveInferenceEnvelope { PreferredEvidenceAge = TimeSpan.FromDays(31) }.TryValidate(out _));
    }

    [Fact]
    public void Fixed_mode_returns_only_the_configured_candidate()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope());
        var plan = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), Facts("runtime.fit.target"));

        var candidate = Assert.Single(plan.Candidates);
        Assert.Equal("configured", candidate.CandidateId);
        Assert.False(candidate.ChangesConfiguration);
        Assert.False(plan.HasAdaptation);
    }

    [Fact]
    public void Planner_is_deterministic_bounded_and_single_axis()
    {
        var envelope = new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise,
            MinimumContext = 4096,
            AllowGpuLayerReduction = true,
            AllowContextReduction = true,
            AllowKvPrecisionChange = true,
            AllowCpuMoePlacement = true,
            AllowMultiDevicePlacement = true
        };
        var config = Config(GpuPlacementIntent.Auto(), envelope, context: 65536);
        var facts = FactsWithOptions(true, ["q8_0", "q4_0"],
            "runtime.gpu-placement.exact", "runtime.fit.target", "runtime.fit.minimum-context");
        var model = Model(32, generalType: "moe");

        var first = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), facts, model, ["q8_0"]);
        var second = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), facts, model, ["q8_0"]);

        Assert.Equal(first.Candidates.Select(candidate => candidate.CandidateId), second.Candidates.Select(candidate => candidate.CandidateId));
        Assert.InRange(first.Candidates.Count, 1, AdaptiveInferencePlanner.MaximumCandidates);
        Assert.All(first.Candidates.Skip(1), candidate => Assert.Single(candidate.ChangedFields));
        Assert.Contains(first.UnavailableReasons, reason => reason.Contains("Multi-device", StringComparison.Ordinal));
    }

    [Fact]
    public void GPU_layer_reduction_uses_known_layers_and_never_zeroes_acceleration()
    {
        var config = Config(GpuPlacementIntent.Exact(32), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.AdaptAtLaunch,
            AllowGpuLayerReduction = true
        });
        var plan = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), Facts("runtime.gpu-placement.exact"));

        Assert.Equal(["configured", "gpu-layers-24", "gpu-layers-16", "gpu-layers-8"],
            plan.Candidates.Select(candidate => candidate.CandidateId));
        Assert.All(plan.Candidates.Skip(1), candidate => Assert.True(candidate.Configuration.GpuPlacement!.ExactLayerCount > 0));
    }

    [Fact]
    public void Auto_gpu_reduction_requires_model_layer_metadata()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise,
            AllowGpuLayerReduction = true
        });
        var plan = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), Facts("runtime.gpu-placement.exact"));

        Assert.Single(plan.Candidates);
        Assert.Contains(plan.UnavailableReasons, reason => reason.Contains("model layer count", StringComparison.Ordinal));
    }

    [Fact]
    public void Context_reduction_respects_the_configured_floor()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise,
            MinimumContext = 8192,
            AllowContextReduction = true
        }, context: 32768);
        var plan = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), Facts());

        Assert.Equal(["configured", "context-24576", "context-16384", "context-12288", "context-8192"],
            plan.Candidates.Select(candidate => candidate.CandidateId));
        Assert.All(plan.Candidates.Skip(1), candidate => Assert.True(candidate.Configuration.ContextSize >= 8192));
    }

    [Fact]
    public void KV_change_requires_runtime_advertisement_and_quality_evidence()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise,
            AllowKvPrecisionChange = true
        });
        var facts = FactsWithOptions(false, ["f16", "q8_0", "q4_0"]);

        var withoutEvidence = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), facts);
        var withEvidence = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), facts, qualityApprovedKvTypes: ["q8_0"]);

        Assert.Single(withoutEvidence.Candidates);
        Assert.Equal("kv-q8_0", Assert.Single(withEvidence.Candidates.Skip(1)).CandidateId);
        Assert.DoesNotContain(withEvidence.Candidates, candidate => candidate.Configuration.KvCacheType == "f16" && candidate.ChangesConfiguration);
    }

    [Fact]
    public void CPU_MoE_change_requires_known_MoE_metadata_and_runtime_support()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise,
            AllowCpuMoePlacement = true
        });
        var unsupported = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), Facts(), Model(32, "moe"));
        var supported = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), FactsWithOptions(true, null), Model(32, "moe"));

        Assert.Single(unsupported.Candidates);
        Assert.Equal("cpu-moe-all", Assert.Single(supported.Candidates.Skip(1)).CandidateId);
        Assert.Equal(-1, supported.Candidates[1].Configuration.CpuMoeLayers);
    }

    [Fact]
    public void Multi_device_adaptation_remains_unknown_without_a_proven_overlay()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise,
            AllowMultiDevicePlacement = true
        });

        var plan = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), Facts());

        Assert.Single(plan.Candidates);
        Assert.Contains(plan.UnavailableReasons, reason => reason.Contains("Unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void Fit_target_requires_exactly_one_complete_known_device_headroom()
    {
        var one = AdaptiveInferencePlanner.FitTarget(Workload(KnownHeadroom()));
        var two = AdaptiveInferencePlanner.FitTarget(Workload(KnownHeadroom(), KnownHeadroom("gpu-1")));
        var unknown = AdaptiveInferencePlanner.FitTarget(Workload(new ResourceDeviceHeadroom(
            "gpu-0", null, null, 0, 100, 0, null, false)));

        Assert.Equal(1000, one);
        Assert.Null(two);
        Assert.Null(unknown);
    }

    [Fact]
    public void Fit_controls_are_added_only_when_the_runtime_proves_each_control()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise,
            MinimumContext = 2048
        });
        var facts = Facts("runtime.fit", "runtime.fit.target", "runtime.fit.minimum-context");
        var plan = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), facts);
        var candidate = Assert.Single(plan.Candidates);
        var args = ServerProcessManager.BuildLaunchArguments(candidate.Configuration).ToList();

        Assert.Equal(1000, candidate.Configuration.RuntimeFitTargetBytes);
        Assert.Equal(2048, candidate.Configuration.RuntimeFitMinimumContext);
        Assert.Equal("1000", ArgValue(args, "--fit-target"));
        Assert.Equal("2048", ArgValue(args, "--fit-ctx"));
    }

    [Fact]
    public void Fit_controls_are_rejected_in_extra_arguments_without_runtime_ownership()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise
        });
        config.ExtraArgs = "--fit-target 1000";

        var error = Assert.Throws<InvalidOperationException>(() => ServerProcessManager.BuildLaunchArguments(config));
        Assert.Contains("fit-target", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Effective_parser_keeps_missing_props_unknown_and_not_auditable()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.AdaptAtLaunch
        });
        var observation = EffectiveLaunchObservationParser.Parse(config, Runtime(), null);

        Assert.False(observation.PropsProbeSucceeded);
        Assert.False(observation.IsAuditable);
        Assert.All(observation.Fields, field => Assert.Equal(AdaptiveEvidenceState.Unknown, field.EvidenceState));
        Assert.DoesNotContain("r32", observation.ParserVersion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Effective_parser_accepts_only_structured_scalar_props_as_proof()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.AdaptAtLaunch
        });
        var observation = EffectiveLaunchObservationParser.Parse(config, Runtime(),
            "{\"ctx_size\":4096,\"n_gpu_layers\":24,\"fit\":true,\"parallel\":1}");

        Assert.True(observation.PropsProbeSucceeded);
        Assert.True(observation.IsAuditable);
        Assert.Equal("4096", Assert.Single(observation.Fields, field => field.Field == "context").EffectiveValue);
        Assert.Equal("24", Assert.Single(observation.Fields, field => field.Field == "gpu_layers").EffectiveValue);
        Assert.Equal(AdaptiveEvidenceState.Proven, Assert.Single(observation.Fields, field => field.Field == "fit").EvidenceState);
        Assert.DoesNotContain(observation.Fields, field => field.EffectiveValue?.Contains("/", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Effective_parser_marks_malformed_or_non_object_props_unknown()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope { Mode = AdaptiveInferenceMode.AdaptAtLaunch });
        var malformed = EffectiveLaunchObservationParser.Parse(config, Runtime(), "not-json");
        var array = EffectiveLaunchObservationParser.Parse(config, Runtime(), "[]");

        Assert.False(malformed.PropsProbeSucceeded);
        Assert.False(malformed.IsAuditable);
        Assert.False(array.PropsProbeSucceeded);
        Assert.False(array.IsAuditable);
    }

    [Theory]
    [InlineData("out of memory", ServerLaunchFailureKind.ResourceExhaustion)]
    [InlineData("invalid value for --ctx-size", ServerLaunchFailureKind.Configuration)]
    public void Launch_failures_are_classified_without_treating_unknown_as_resource_exhaustion(
        string message, ServerLaunchFailureKind expected)
    {
        Assert.Equal(expected, ServerProcessManager.ClassifyFailure(new InvalidOperationException(message)));
    }

    [Fact]
    public void Missing_runtime_is_classified_as_unavailable()
    {
        Assert.Equal(ServerLaunchFailureKind.RuntimeUnavailable,
            ServerProcessManager.ClassifyFailure(new FileNotFoundException("runtime executable was not found")));
    }

    [Fact]
    public void Adaptive_envelope_is_persisted_but_runtime_fit_receipts_are_not()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise,
            MinimumContext = 2048
        });
        config.RuntimeFitTargetBytes = 1000;

        var json = JsonSerializer.Serialize(config);

        Assert.Contains("AdaptiveEnvelope", json, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFitTargetBytes", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recent_exact_auditable_success_is_preferred_without_reusing_a_snapshot()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var store = new SqliteEmpiricalExperienceStore(settings, new RedactionService());
        var service = new AdaptiveInferenceExperienceService(store);
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.AdaptAtLaunch
        });
        var workload = Workload(KnownHeadroom());
        var runtime = Runtime();
        var model = CompleteModel();
        var observation = EffectiveLaunchObservationParser.Parse(
            config,
            runtime,
            "{\"ctx_size\":4096,\"n_gpu_layers\":24}");
        var result = new ServerLaunchResult(ServerStatus.Running, ServerLaunchFailureKind.None, observation, string.Empty);

        await service.RecordAsync(
            workload,
            runtime,
            model,
            ConfigurationIdentityFactory.Create(config).StableId,
            "context-2048",
            ["context"],
            result);

        var envelope = config.AdaptiveEnvelope!.Clone();
        envelope.PreferredEvidenceAge = TimeSpan.FromDays(1);
        var preference = await service.FindPreferredCandidateAsync(
            workload,
            runtime,
            model,
            ConfigurationIdentityFactory.Create(config).StableId,
            envelope);

        Assert.NotNull(preference);
        Assert.Equal("context-2048", preference!.CandidateId);
        Assert.NotEqual("snapshot-reused", workload.SnapshotId);
    }

    [Fact]
    public void Planner_preference_reorders_only_a_currently_generated_candidate()
    {
        var config = Config(GpuPlacementIntent.Auto(), new AdaptiveInferenceEnvelope
        {
            Mode = AdaptiveInferenceMode.Advise,
            MinimumContext = 2048,
            AllowContextReduction = true
        }, context: 8192);
        var plan = AdaptiveInferencePlanner.Build(config, Workload(KnownHeadroom()), Facts());

        var preferred = AdaptiveInferencePlanner.PreferCandidate(plan, "context-4096");
        Assert.Equal("context-4096", preferred.Candidates[0].CandidateId);
        Assert.Equal(0, preferred.Candidates[0].Ordinal);
        Assert.Equal(plan.Candidates.Count, preferred.Candidates.Count);
        Assert.Same(plan, AdaptiveInferencePlanner.PreferCandidate(plan, "not-current"));
    }

    private static ServerConfig Config(GpuPlacementIntent placement, AdaptiveInferenceEnvelope envelope, int context = 4096) => new()
    {
        Id = "server",
        Name = "Test",
        ContextSize = context,
        GpuPlacement = placement,
        AdaptiveEnvelope = envelope
    };

    private static GgufModelInfo Model(int blocks, string generalType) => new(
        "llama", "Q4_K_M", blocks, 131072, 4096, 32, 8, 128, 128, GeneralType: generalType);

    private static LlamaRuntimeCapabilityFacts Facts(
        params string[] available) => FactsWithOptions(false, ["f16", "q8_0"], available);

    private static LlamaRuntimeCapabilityFacts FactsWithOptions(
        bool supportsCpuMoe, IReadOnlyList<string>? kvTypes, params string[] available) => new(
        HelpProbeSucceeded: true,
        SupportsDraftMtp: false,
        SupportsReasoningFormat: false,
        SupportsReasoningFlag: false,
        SupportsReasoningPreserve: false,
        PropsProbeSucceeded: false,
        SupportsPreserveReasoningTemplate: null,
        Modalities: [],
        SpeculativeTypes: [],
        SupportsPromptThreads: false,
        SupportsBackendSampling: false,
        SupportsPerformanceInstrumentation: false,
        SupportedKvCacheTypes: kvTypes ?? ["f16", "q8_0"],
        SupportsCpuMoePlacement: supportsCpuMoe,
        LaunchCapabilities: available
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, id => new CapabilityEvidence(CapabilityState.Available, "test", "test"), StringComparer.Ordinal));

    private static RuntimeIdentityV2 Runtime() => new(
        "llama.cpp", "hash", 1, DateTime.UnixEpoch, "version", "build", "compiler", "backend", "asset", IdentityCompleteness.Complete);

    private static ModelIdentityV2 CompleteModel() => new(
        "manifest", "hash", 1, DateTime.UnixEpoch, "llama", "Q4_K_M", string.Empty,
        ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete);

    private static ResourceDeviceHeadroom KnownHeadroom(string device = "gpu-0") =>
        new(device, 2000, 1000, 0, 800, 0, 200, true);

    private static ResourceWorkloadPlan Workload(params ResourceDeviceHeadroom[] headroom) => new(
        "plan", "snapshot", "server", [], [], [], [],
        new ResourceHeadroomPolicy(
            deviceStabilityBytes: 0,
            systemStabilityBytes: 0,
            interactiveReservationBytes: 0,
            foregroundReservationBytes: 0,
            inProcessReservationBytes: 0,
            unknownDeviceReservationBytes: 0),
        ResourcePlanFeasibility.Fits, headroom, 1000, "test-v1", "hardware-test", true);

    private static string ArgValue(IReadOnlyList<string> args, string flag)
    {
        var index = -1;
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        Assert.True(index >= 0, $"Expected {flag} in launch arguments.");
        return args[index + 1];
    }
}
