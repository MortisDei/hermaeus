using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

public sealed class SpeculativeLabTests
{
    private static RuntimeIdentityV2 Runtime() => RuntimeIdentityFactory.Unknown("llama.cpp");

    private static RuntimeCapabilityObservation Capability(string type, CapabilityState state = CapabilityState.Available) =>
        RuntimeCapabilityObservation.Create(LocalModelCapabilityService.CapabilityIdForSpeculativeType(type), state,
            "test", "exact runtime help", Runtime(), null,
            new Dictionary<string, string> { ["runtime_type"] = type }, DateTime.UtcNow);

    private static RuntimeCapabilityObservation Parameter(string id) =>
        RuntimeCapabilityObservation.Create(id, CapabilityState.Available, "test", "exact runtime flag",
            Runtime(), null, null, DateTime.UtcNow);

    private static GgufModelInfo Model(string architecture, string name, string repository,
        string baseName = "", string baseRepository = "", int vocabulary = 128,
        string tokenizerModel = "gpt2", string tokenizerPre = "qwen2") => new(
            architecture, "Q4_K_M", 32, 8192, 4096, 32, 8, 128, 128,
            VocabularySize: vocabulary, Name: name, RepositoryUrl: repository,
            BaseModelName: baseName, BaseModelRepositoryUrl: baseRepository,
            TokenizerModel: tokenizerModel, TokenizerPre: tokenizerPre);

    [Fact]
    public void Runtime_help_maps_exact_eagle3_and_speculative_parameter_flags()
    {
        var facts = LocalModelCapabilityService.ParseHelp("""
            --spec-type [none|draft-simple|draft-eagle3]
            --spec-draft-n-max N
            --spec-draft-n-min N
            --spec-draft-p-min P
            --spec-draft-ngl N
            """);
        var capabilities = LocalModelCapabilityService.Combine("model.gguf", null, facts, Runtime(),
            RuntimeIdentityFactory.CreateModelIdentity("model.gguf", null), DateTime.UtcNow);

        Assert.Contains(capabilities.Observations!, item =>
            item.CapabilityId == "speculative.draft.eagle3" && item.State == CapabilityState.Available);
        Assert.All(new[]
        {
            "runtime.speculative.parameter.n-max", "runtime.speculative.parameter.n-min",
            "runtime.speculative.parameter.p-min", "runtime.speculative.parameter.draft-gpu-layers"
        }, id => Assert.Contains(capabilities.Observations!, item => item.CapabilityId == id && item.State == CapabilityState.Available));
    }

    [Fact]
    public void Failed_help_keeps_speculative_parameter_capabilities_unknown()
    {
        var capabilities = LocalModelCapabilityService.Combine("model.gguf", null,
            LocalModelCapabilityService.ParseHelp(null), Runtime(),
            RuntimeIdentityFactory.CreateModelIdentity("model.gguf", null), DateTime.UtcNow);

        Assert.All(capabilities.Observations!.Where(item => item.CapabilityId.StartsWith("runtime.speculative.parameter", StringComparison.Ordinal)),
            item => Assert.Equal(CapabilityState.Unknown, item.State));
    }

    [Fact]
    public void External_pair_requires_matching_family_vocabulary_and_tokenizer()
    {
        using var temp = new TempDir();
        var source = Source(temp);
        var target = Model("qwen3", "Qwen3 4B", "Qwen/Qwen3-4B");
        var draft = Model("qwen3", "Qwen3 0.6B", "Qwen/Qwen3-0.6B");

        var result = SpeculativePairInspector.Inspect("draft-simple", source,
            [Capability("draft-simple")], target, draft,
            Proven(source.ModelPath, target, 'a'), Proven(source.Speculative.DraftModelPath, draft, 'b'));

        Assert.Equal(CapabilityState.Available, result.State);
        Assert.NotEqual(result.TargetIdentity.StableId, result.CompanionIdentity.StableId);
        Assert.Equal(target.TokenizerIdentity, result.TokenizerIdentity);
    }

    [Fact]
    public void External_pair_does_not_treat_vocabulary_equality_as_sufficient()
    {
        using var temp = new TempDir();
        var source = Source(temp);
        var target = Model("qwen3", "Qwen3 4B", "Qwen/Qwen3-4B");
        var wrongTokenizer = Model("qwen3", "Other", "other", tokenizerPre: "different");

        var result = SpeculativePairInspector.Inspect("draft-simple", source,
            [Capability("draft-simple")], target, wrongTokenizer,
            Proven(source.ModelPath, target, 'a'), Proven(source.Speculative.DraftModelPath, wrongTokenizer, 'b'));

        Assert.Equal(CapabilityState.Unavailable, result.State);
        Assert.Contains("tokenizer", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Eagle3_requires_exact_target_binding_metadata()
    {
        using var temp = new TempDir();
        var source = Source(temp);
        var target = Model("qwen3", "Qwen3 4B", "Qwen/Qwen3-4B");
        var unbound = Model("eagle3", "EAGLE-3", "draft/repo");
        var bound = Model("eagle3", "EAGLE-3", "draft/repo",
            baseName: "Qwen3 4B", baseRepository: "Qwen/Qwen3-4B");

        Assert.Equal(CapabilityState.Unknown, SpeculativePairInspector.Inspect(
            "draft-eagle3", source, [Capability("draft-eagle3")], target, unbound,
            Proven(source.ModelPath, target, 'a'), Proven(source.Speculative.DraftModelPath, unbound, 'b')).State);
        Assert.Equal(CapabilityState.Available, SpeculativePairInspector.Inspect(
            "draft-eagle3", source, [Capability("draft-eagle3")], target, bound,
            Proven(source.ModelPath, target, 'a'), Proven(source.Speculative.DraftModelPath, bound, 'b')).State);
    }

    [Fact]
    public void Probe_failure_is_unknown_even_for_a_compatible_pair()
    {
        using var temp = new TempDir();
        var source = Source(temp);
        var model = Model("qwen3", "Qwen3", "Qwen/Qwen3");

        var result = SpeculativePairInspector.Inspect("draft-simple", source, [], model, model);

        Assert.Equal(CapabilityState.Unknown, result.State);
    }

    [Fact]
    public void File_metadata_fallback_is_not_treated_as_an_exact_pair_identity()
    {
        using var temp = new TempDir();
        var source = Source(temp);
        var model = Model("qwen3", "Qwen3", "Qwen/Qwen3");

        var result = SpeculativePairInspector.Inspect("draft-simple", source,
            [Capability("draft-simple")], model, model);

        Assert.Equal(CapabilityState.Unknown, result.State);
        Assert.Contains("verified hash", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void External_recipe_is_a_bounded_baseline_candidate_pair_with_required_evidence()
    {
        using var temp = new TempDir();
        var source = Source(temp);
        var model = Model("qwen3", "Qwen3", "Qwen/Qwen3");

        var plan = LabRecipeCatalog.Build(LabRecipeKind.ExternalDraft, source,
            [Capability("draft-simple")], model, model,
            Proven(source.ModelPath, model, 'a'), Proven(source.Speculative.DraftModelPath, model, 'b'));

        Assert.Equal(CapabilityState.Available, plan.Availability);
        Assert.Empty(plan.Baseline.SpeculativeTypes);
        Assert.Equal(["draft-simple"], Assert.Single(plan.Candidates).SpeculativeTypes);
        Assert.Equal(2, plan.MaximumRunCount);
        Assert.Contains("speculative.acceptance.rate", plan.RequiredMetrics);
        Assert.Equal(LabCorrectnessRequirement.ExactEquivalence, plan.CorrectnessRequirement);
    }

    [Fact]
    public void Tuning_recipe_requires_an_explicit_baseline_instead_of_assuming_runtime_default()
    {
        using var temp = new TempDir();
        var source = Source(temp);
        source.Speculative.Types = ["draft-simple"];
        var model = Model("qwen3", "Qwen3", "Qwen/Qwen3");
        var capabilities = new[] { Capability("draft-simple"), Parameter("runtime.speculative.parameter.n-max") };

        var plan = LabRecipeCatalog.Build(LabRecipeKind.SpeculativeDraftMaximum, source, capabilities, model, model,
            Proven(source.ModelPath, model, 'a'), Proven(source.Speculative.DraftModelPath, model, 'b'));

        Assert.Equal(CapabilityState.Unknown, plan.Availability);
        Assert.Contains("explicit baseline", plan.AvailabilityDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tuning_recipe_changes_only_one_reviewed_parameter()
    {
        using var temp = new TempDir();
        var source = Source(temp);
        source.Speculative.Types = ["draft-simple"];
        source.Speculative.NMax = 3;
        var model = Model("qwen3", "Qwen3", "Qwen/Qwen3");
        var capabilities = new[] { Capability("draft-simple"), Parameter("runtime.speculative.parameter.n-max") };

        var plan = LabRecipeCatalog.Build(LabRecipeKind.SpeculativeDraftMaximum, source, capabilities, model, model,
            Proven(source.ModelPath, model, 'a'), Proven(source.Speculative.DraftModelPath, model, 'b'));

        Assert.Equal(CapabilityState.Available, plan.Availability);
        Assert.InRange(plan.Candidates.Count, 1, 4);
        Assert.All(plan.Candidates, candidate =>
        {
            Assert.Equal(plan.Baseline.SpeculativeTypes, candidate.SpeculativeTypes);
            Assert.Equal(plan.Baseline.SpeculativePMin, candidate.SpeculativePMin);
            Assert.NotEqual(plan.Baseline.SpeculativeNMax, candidate.SpeculativeNMax);
        });
    }

    [Fact]
    public void Lab_configuration_persists_companion_identity_not_private_path()
    {
        using var temp = new TempDir();
        var source = Source(temp);

        var configuration = LabConfigurationMapper.FromServer(source, "candidate", "Candidate");
        var json = JsonSerializer.Serialize(configuration);

        Assert.NotEmpty(configuration.SpeculativeCompanionIdentity);
        Assert.DoesNotContain(source.Speculative.DraftModelPath, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_response_preserves_draft_counters_and_derived_acceptance()
    {
        var result = LlamaServerLabWorkloadExecutor.ParseSuccessfulResponse(Request(),
            """{"choices":[{"message":{"content":"same"}}],"timings":{"draft_n":10,"draft_n_accepted":4}}""",
            TimeSpan.FromMilliseconds(10));

        Assert.Equal(10, result.Observations.Single(item => item.MetricId == "speculative.draft.tokens").Value);
        Assert.Equal(4, result.Observations.Single(item => item.MetricId == "speculative.accepted.tokens").Value);
        Assert.Equal(0.4, result.Observations.Single(item => item.MetricId == "speculative.acceptance.rate").Value);
    }

    [Fact]
    public void Zero_drafted_tokens_is_observed_but_acceptance_is_missing()
    {
        var result = LlamaServerLabWorkloadExecutor.ParseSuccessfulResponse(Request(),
            """{"choices":[{"message":{"content":"same"}}],"timings":{"draft_n":0,"draft_n_accepted":0}}""",
            TimeSpan.Zero);

        Assert.Equal(0, result.Observations.Single(item => item.MetricId == "speculative.draft.tokens").Value);
        var acceptance = result.Observations.Single(item => item.MetricId == "speculative.acceptance.rate");
        Assert.Null(acceptance.Value);
        Assert.Contains("undefined", acceptance.MissingReason);
    }

    [Fact]
    public void Eagle3_launch_uses_the_reviewed_argument_contract()
    {
        var config = new ServerConfig
        {
            ModelPath = "target.gguf", RuntimeSpeculativeTypes = ["draft-eagle3"],
            Speculative = new SpeculativeDecodingConfig
            {
                Types = ["draft-eagle3"], DraftModelPath = "companion.gguf", NMax = 5
            }
        };

        var args = ServerProcessManager.BuildLaunchArguments(config).ToArray();

        Assert.Equal("draft-eagle3", Value(args, "--spec-type"));
        Assert.Equal("companion.gguf", Value(args, "--spec-draft-model"));
        Assert.Equal("5", Value(args, "--spec-draft-n-max"));
    }

    private static ServerConfig Source(TempDir temp)
    {
        var target = temp.PathFor("target.gguf");
        var companion = temp.PathFor("companion.gguf");
        File.WriteAllBytes(target, [1, 2, 3]);
        File.WriteAllBytes(companion, [4, 5, 6, 7]);
        return new ServerConfig
        {
            Id = "server", Name = "Test", ExecutablePath = "llama-server", ModelPath = target,
            ContextSize = 8192, GpuLayers = 0, Threads = 4, Slots = 1,
            KvCacheTypeK = "f16", KvCacheTypeV = "f16",
            Speculative = new SpeculativeDecodingConfig { DraftModelPath = companion, DraftGpuLayers = 0 }
        };
    }

    private static LabWorkloadRequest Request()
    {
        var runtime = Runtime();
        var model = RuntimeIdentityFactory.CreateModelIdentity("model.gguf", null);
        var hardware = new HardwareIdentityV2("test", "x64", "cpu", "cpu", null, 1024, "", "single", IdentityCompleteness.Incomplete);
        var config = new ConfigurationIdentityV2(8192, 0, "cpu", 4, 0, 1, null, null,
            "f16", "f16", "auto", "", "", "", 0, new Dictionary<string, string>(), IdentityCompleteness.Complete);
        var fingerprint = new EmpiricalProfileFingerprintV2(runtime, model, hardware, config);
        return new LabWorkloadRequest("run", 1234, new LabConfiguration
        {
            Id = "baseline", Label = "Baseline", ContextSize = 8192, Threads = 4, Slots = 1
        }, fingerprint, "prompt", 1, 16, "case", 0, TimeSpan.FromSeconds(1));
    }

    private static ModelIdentityV2 Proven(string path, GgufModelInfo info, char digest) =>
        RuntimeIdentityFactory.CreateModelIdentity(path, info, new string(digest, 64), "test-manifest");

    private static string? Value(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
            if (args[index] == name) return args[index + 1];
        return null;
    }
}
