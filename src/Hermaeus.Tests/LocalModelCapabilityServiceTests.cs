using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LocalModelCapabilityServiceTests
{
    [Fact]
    public void ParseHelp_discovers_only_speculative_types_printed_with_spec_type()
    {
        var facts = LocalModelCapabilityService.ParseHelp("""
            --spec-type TYPE     speculative mode: ngram-mod, draft-mtp, eagle3
            --threads-batch N    threads for prompt processing
            --perf               emit performance diagnostics
            """);

        Assert.True(facts.HelpProbeSucceeded);
        Assert.Equal(["draft-mtp", "eagle3", "ngram-mod"], facts.SpeculativeTypes);
        Assert.True(facts.SupportsDraftMtp);
        Assert.True(facts.SupportsPromptThreads);
        Assert.True(facts.SupportsPerformanceInstrumentation);
        Assert.False(facts.SupportsBackendSampling);
    }

    [Fact]
    public void ParseHelp_proves_new_load_mode_and_cors_options_from_help_text()
    {
        var facts = LocalModelCapabilityService.ParseHelp("--load-mode MODE --cors-origins ORIGINS");

        Assert.True(facts.SupportsLoadMode);
        Assert.True(facts.SupportsCorsOrigins);
    }

    [Fact]
    public void ParseHelp_projects_the_bounded_R32_launch_capability_matrix()
    {
        var facts = LocalModelCapabilityService.ParseHelp("""
            -ngl, --gpu-layers, --n-gpu-layers N  max number of layers to offload (exact number, 'auto', or 'all')
            --device <dev1,dev2,..>  devices to use
            --list-devices  list available devices
            --split-mode {none,layer,row,tensor}
            --tensor-split N0,N1,N2,...  fraction of the model to offload to each GPU
            --main-gpu INDEX  the GPU to use for the model
            --fit [on|off]  adjust unset arguments to fit available memory (default: on)
            --fit-target MiB0,...  target margin per device (default: 1024)
            --fit-ctx N  minimum context size set by fit (default: 4096)
            --kv-offload, --no-kv-offload
            --cache-ram MiB  prompt cache RAM limit
            --ctx-checkpoints N  context checkpoints
            --checkpoint-min-step N  minimum checkpoint spacing
            --kv-unified, --no-kv-unified
            --kv-unified-per-slot
            --cache-idle-slots N
            --slot-save-path PATH
            """);

        var capabilities = LocalModelCapabilityService.Combine("model.gguf", null, facts).Observations!
            .Where(observation => LocalModelCapabilityService.LaunchCapabilityIds.Contains(observation.CapabilityId))
            .ToDictionary(observation => observation.CapabilityId);

        foreach (var id in LocalModelCapabilityService.LaunchCapabilityIds.Where(id => id != "runtime.fit.report.effective"))
            Assert.True(capabilities[id].State == CapabilityState.Available,
                $"{id}: {capabilities[id].State}, {capabilities[id].Detail}");
        Assert.Equal(CapabilityState.Unknown, capabilities["runtime.fit.report.effective"].State);
    }

    [Fact]
    public void Combine_keeps_launch_capabilities_bounded_to_the_reviewed_matrix()
    {
        var facts = LocalModelCapabilityService.ParseHelp("--fit") with
        {
            LaunchCapabilities = new Dictionary<string, CapabilityEvidence>
            {
                ["future.runtime.control"] = new(CapabilityState.Available, "future", "future")
            }
        };

        var observations = LocalModelCapabilityService.Combine("model.gguf", null, facts).Observations!;

        Assert.DoesNotContain(observations, observation => observation.CapabilityId == "future.runtime.control");
        Assert.Equal(LocalModelCapabilityService.LaunchCapabilityIds.Count,
            observations.Count(observation => LocalModelCapabilityService.LaunchCapabilityIds.Contains(observation.CapabilityId)));
    }

    [Fact]
    public void ParseHelp_does_not_promote_near_match_options_or_unobserved_effective_state()
    {
        var facts = LocalModelCapabilityService.ParseHelp("""
            --n-gpu-layers-draft N
            --fit-target MiB
            --fit-ctx N
            --device-draft NAME
            """);

        var capabilities = LocalModelCapabilityService.Combine("model.gguf", null, facts).Observations!
            .Where(observation => LocalModelCapabilityService.LaunchCapabilityIds.Contains(observation.CapabilityId))
            .ToDictionary(observation => observation.CapabilityId);

        Assert.Equal(CapabilityState.Unavailable, capabilities["runtime.gpu-placement.exact"].State);
        Assert.Equal(CapabilityState.Unavailable, capabilities["runtime.gpu-placement.auto"].State);
        Assert.Equal(CapabilityState.Unavailable, capabilities["runtime.device.list"].State);
        Assert.Equal(CapabilityState.Available, capabilities["runtime.fit.target"].State);
        Assert.Equal(CapabilityState.Available, capabilities["runtime.fit.minimum-context"].State);
        Assert.Equal(CapabilityState.Unavailable, capabilities["runtime.fit"].State);
        Assert.Equal(CapabilityState.Unknown, capabilities["runtime.fit.report.effective"].State);
    }

    [Fact]
    public void ParseProps_marks_effective_launch_evidence_only_for_bounded_values()
    {
        var facts = LocalModelCapabilityService.ParseHelp("--fit");

        var observed = LocalModelCapabilityService.ParseProps(
            "{\"n_gpu_layers\":0,\"split_mode\":\"none\",\"fit\":false}", facts);
        var observedCapability = observed.LaunchCapabilities!["runtime.fit.report.effective"];
        Assert.Equal(CapabilityState.Available, observedCapability.State);
        Assert.Contains("n_gpu_layers", observedCapability.Detail, StringComparison.Ordinal);

        var unobserved = LocalModelCapabilityService.ParseProps("{\"n_gpu_layers\":null,\"fit\":{}}", facts);
        Assert.Equal(CapabilityState.Unknown, unobserved.LaunchCapabilities!["runtime.fit.report.effective"].State);
    }

    [Fact]
    public void ParseHelp_discovers_only_kv_types_printed_near_cache_type_option()
    {
        var facts = LocalModelCapabilityService.ParseHelp("""
            --cache-type-k TYPE    allowed: f16, q8_0, q4_0, iq4_nl
            --cache-type-v TYPE    allowed: f16, q8_0, q4_0, iq4_nl
            elsewhere future_q2
            """);

        Assert.Equal(["f16", "q8_0", "q4_0", "iq4_nl"], facts.SupportedKvCacheTypes);
        var capabilities = LocalModelCapabilityService.Combine("model.gguf", null, facts);
        Assert.Contains(capabilities.Observations!, item =>
            item.CapabilityId == "runtime.kv.type.q4_0" && item.State == CapabilityState.Available);
        Assert.DoesNotContain(capabilities.Observations!, item => item.CapabilityId.Contains("future", StringComparison.Ordinal));
    }

    [Fact]
    public void Combine_exposes_only_reviewed_speculative_mechanisms_as_configurable()
    {
        var facts = LocalModelCapabilityService.ParseHelp("--spec-type TYPE: ngram-mod, draft-mtp, eagle3");

        var capabilities = LocalModelCapabilityService.Combine("model.gguf", null, facts);
        var types = capabilities.RuntimeSurface!.Speculative;

        Assert.True(types.Single(t => t.Type == "ngram-mod").Configurable);
        Assert.Equal(SpeculativeDrafterKind.Self, types.Single(t => t.Type == "ngram-mod").DrafterKind);
        Assert.True(types.Single(t => t.Type == "draft-mtp").Configurable);
        Assert.Equal(SpeculativeDrafterKind.EmbeddedMtp, types.Single(t => t.Type == "draft-mtp").DrafterKind);
        Assert.True(types.Single(t => t.Type == "eagle3").Configurable);
        Assert.Equal(SpeculativeDrafterKind.External, types.Single(t => t.Type == "eagle3").DrafterKind);
    }

    [Fact]
    public void Prompt_threads_emit_only_after_runtime_proof()
    {
        var cfg = new ServerConfig { ModelPath = "model.gguf", PromptThreads = 6 };

        Assert.DoesNotContain("--threads-batch", ServerProcessManager.BuildLaunchArguments(cfg));

        cfg.RuntimeSupportsPromptThreads = true;
        var args = ServerProcessManager.BuildLaunchArguments(cfg).ToList();
        var index = args.IndexOf("--threads-batch");
        Assert.True(index >= 0);
        Assert.Equal("6", args[index + 1]);
    }

    [Fact]
    public void Managed_server_normalization_keeps_negative_prompt_threads_at_runtime_default()
    {
        var server = new ServerConfig { PromptThreads = -4 };

        SettingsService.NormalizeManagedServers([server]);

        Assert.Equal(0, server.PromptThreads);
    }

    [Fact]
    public void Capability_drift_ignores_unchanged_evidence_and_names_meaningful_changes()
    {
        var unavailable = new CapabilityEvidence(CapabilityState.Unavailable, "old", "old");
        var available = new CapabilityEvidence(CapabilityState.Available, "new", "new");
        var previous = new LocalModelCapabilities("model.gguf", unavailable, unavailable, unavailable, unavailable, DateTime.UtcNow,
            new RuntimeCapabilitySurface([], unavailable, unavailable, unavailable));
        var current = new LocalModelCapabilities("model.gguf", available, unavailable, unavailable, unavailable, DateTime.UtcNow,
            new RuntimeCapabilitySurface(
                [new RuntimeSpeculativeCapability("eagle3", SpeculativeDrafterKind.Unknown, false)],
                unavailable, unavailable, unavailable));

        var drift = LocalModelCapabilityService.Compare(previous, current);

        Assert.Contains(drift, change => change.Capability == "embedded MTP");
        Assert.Contains(drift, change => change.Detail.Contains("eagle3", StringComparison.Ordinal));
        Assert.DoesNotContain(drift, change => change.Capability == "vision");
    }

    [Fact]
    public void Capability_drift_reports_fixed_launch_capability_changes()
    {
        var previous = LocalModelCapabilityService.Combine("model.gguf", null,
            LocalModelCapabilityService.ParseHelp("--fit"));
        var current = LocalModelCapabilityService.Combine("model.gguf", null,
            LocalModelCapabilityService.ParseHelp("--fit --fit-target MiB"));

        var drift = LocalModelCapabilityService.Compare(previous, current);

        Assert.Contains(drift, change => change.Capability == "runtime.fit.target");
    }
}
