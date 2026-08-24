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
    public void Combine_marks_unknown_speculative_mechanisms_as_not_configurable()
    {
        var facts = LocalModelCapabilityService.ParseHelp("--spec-type TYPE: ngram-mod, draft-mtp, eagle3");

        var capabilities = LocalModelCapabilityService.Combine("model.gguf", null, facts);
        var types = capabilities.RuntimeSurface!.Speculative;

        Assert.True(types.Single(t => t.Type == "ngram-mod").Configurable);
        Assert.Equal(SpeculativeDrafterKind.Self, types.Single(t => t.Type == "ngram-mod").DrafterKind);
        Assert.True(types.Single(t => t.Type == "draft-mtp").Configurable);
        Assert.Equal(SpeculativeDrafterKind.EmbeddedMtp, types.Single(t => t.Type == "draft-mtp").DrafterKind);
        Assert.False(types.Single(t => t.Type == "eagle3").Configurable);
        Assert.Equal(SpeculativeDrafterKind.Unknown, types.Single(t => t.Type == "eagle3").DrafterKind);
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
}
