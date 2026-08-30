using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LabRecipeTests
{
    private static ServerConfig Server(string flashAttention = "auto") => new()
    {
        Id = "server-1", Name = "Chat", ExecutablePath = "missing-server",
        ModelPath = "missing-model.gguf", ContextSize = 8192, GpuLayers = 0,
        Threads = 4, Slots = 1, KvCacheTypeK = "f16", KvCacheTypeV = "f16",
        FlashAttention = flashAttention
    };

    private static RuntimeIdentityV2 Runtime() => RuntimeIdentityFactory.Unknown("llama.cpp");
    private static EmpiricalProfileFingerprintV2 Fingerprint() => new(
        Runtime(), RuntimeIdentityFactory.CreateModelIdentity("missing.gguf", null),
        new HardwareIdentityV2("test", "x64", "cpu", "cpu", null, 1024, "", "single", IdentityCompleteness.Incomplete),
        new ConfigurationIdentityV2(8192, 0, "cpu", 4, 0, 1, null, null, "f16", "f16", "auto", "", "", "", 0,
            new Dictionary<string, string>(), IdentityCompleteness.Complete));
    private static RuntimeCapabilityObservation Capability(string id, CapabilityState state = CapabilityState.Available) =>
        RuntimeCapabilityObservation.Create(id, state, "test", "test evidence", Runtime(), null, null, DateTime.UtcNow);

    private static string? ArgValue(IReadOnlyList<string> args, string name)
    {
        var index = args.ToList().IndexOf(name);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    [Fact]
    public void Runtime_help_observes_flash_and_cpu_moe_capabilities()
    {
        var facts = LocalModelCapabilityService.ParseHelp("--flash-attn on --cpu-moe --n-cpu-moe N");
        var capabilities = LocalModelCapabilityService.Combine("missing.gguf", null, facts, Runtime(),
            RuntimeIdentityFactory.CreateModelIdentity("missing.gguf", null), DateTime.UtcNow);
        Assert.Equal(CapabilityState.Available, capabilities.Observations!.Single(item => item.CapabilityId == "runtime.flash-attention").State);
        Assert.Equal(CapabilityState.Available, capabilities.Observations!.Single(item => item.CapabilityId == "runtime.moe.cpu-placement").State);
    }

    [Fact]
    public void Failed_help_keeps_flash_and_cpu_moe_unknown()
    {
        var capabilities = LocalModelCapabilityService.Combine("missing.gguf", null,
            LocalModelCapabilityService.ParseHelp(null), Runtime(),
            RuntimeIdentityFactory.CreateModelIdentity("missing.gguf", null), DateTime.UtcNow);
        Assert.All(capabilities.Observations!.Where(item => item.CapabilityId is "runtime.flash-attention" or "runtime.moe.cpu-placement"),
            item => Assert.Equal(CapabilityState.Unknown, item.State));
    }

    [Fact]
    public void Context_recipe_is_bounded_and_keeps_baseline()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.Context, Server(), []);
        Assert.Equal(CapabilityState.Available, plan.Availability);
        Assert.InRange(plan.Candidates.Count, 1, 3);
        Assert.Equal("baseline", plan.Baseline.Id);
        Assert.True(plan.Candidates.Count + 1 <= plan.MaximumRunCount);
        Assert.All(plan.Candidates, candidate => Assert.NotEqual(plan.Baseline.ContextSize, candidate.ContextSize));
    }

    [Fact]
    public void Engine_recipe_changes_only_gpu_layers()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.EngineProfile, Server(), []);
        Assert.Contains(plan.Candidates, candidate => candidate.GpuLayers == -1);
        Assert.All(plan.Candidates, candidate => Assert.Equal(plan.Baseline.ContextSize, candidate.ContextSize));
    }

    [Fact]
    public void Isolated_lab_mapping_preserves_launch_capability_facts()
    {
        var source = Server();
        source.PreserveReasoning = true;
        source.ReasoningPreserveSupported = true;
        source.NoMemoryMap = true;
        source.MemoryLock = false;
        source.ContextShift = true;
        source.ExtraArgs = "--some-runtime-option value";
        var configuration = LabConfigurationMapper.FromServer(source, "baseline", "Baseline");

        var isolated = LabConfigurationMapper.Apply(source, configuration, 39202);

        Assert.True(isolated.PreserveReasoning);
        Assert.True(isolated.ReasoningPreserveSupported);
        Assert.True(isolated.NoMemoryMap);
        Assert.True(isolated.ContextShift);
        Assert.Equal(source.ExtraArgs, isolated.ExtraArgs);
    }

    [Fact]
    public void Kv_recipe_is_unknown_without_exact_advertisement()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, Server(), []);
        Assert.Equal(CapabilityState.Unknown, plan.Availability);
    }

    [Fact]
    public void Kv_recipe_offers_only_reviewed_advertised_types()
    {
        var capabilities = new[]
        {
            Capability("runtime.kv.type.f16"), Capability("runtime.kv.type.q8_0"),
            Capability("runtime.kv.type.future_2bit"), Capability("runtime.flash-attention")
        };
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, Server("on"), capabilities);
        Assert.Equal(CapabilityState.Available, plan.Availability);
        var candidate = Assert.Single(plan.Candidates);
        Assert.Equal("q8_0", candidate.KvCacheTypeK);
        Assert.DoesNotContain(plan.Candidates, value => value.KvCacheTypeK == "future_2bit");
    }

    [Fact]
    public void Low_bit_kv_recipe_requires_quality_metric()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, Server("on"),
            [Capability("runtime.kv.type.f16"), Capability("runtime.kv.type.q4_0"),
                Capability("runtime.flash-attention")]);
        Assert.Contains("quality.score", plan.RequiredMetrics);
    }

    [Fact]
    public void Kv_recipe_excludes_the_baseline_representation()
    {
        var source = Server("on");
        source.KvCacheType = "q8_0";
        source.KvCacheTypeK = "q8_0";
        source.KvCacheTypeV = "q8_0";
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, source,
            [Capability("runtime.kv.type.f16"), Capability("runtime.kv.type.q8_0"),
                Capability("runtime.kv.type.q4_0"), Capability("runtime.flash-attention")]);

        Assert.DoesNotContain(plan.Candidates, candidate => candidate.KvCacheTypeK == "q8_0");
        Assert.All(plan.Candidates, candidate => Assert.NotEqual(plan.Baseline.KvCacheTypeK, candidate.KvCacheTypeK));
    }

    [Fact]
    public void Kv_recipe_requires_explicit_flash_attention_for_quantized_values()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, Server(),
            [Capability("runtime.kv.type.f16"), Capability("runtime.kv.type.q8_0"),
                Capability("runtime.flash-attention")]);

        Assert.Equal(CapabilityState.Unknown, plan.Availability);
        Assert.Contains("explicitly on", plan.AvailabilityDetail, StringComparison.Ordinal);
        Assert.Contains("runtime.flash-attention", plan.RequiredCapabilityIds);
    }

    [Fact]
    public void Kv_recipe_requires_explicit_flash_attention_for_a_quantized_baseline()
    {
        var source = Server();
        source.KvCacheType = "q8_0";
        source.KvCacheTypeK = "q8_0";
        source.KvCacheTypeV = "q8_0";
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, source,
            [Capability("runtime.kv.type.q8_0"), Capability("runtime.flash-attention")]);

        Assert.Equal(CapabilityState.Unknown, plan.Availability);
        Assert.Contains("explicitly on", plan.AvailabilityDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Kv_recipe_rejects_a_conflicting_flash_attention_extra_argument()
    {
        var source = Server("on");
        source.ExtraArgs = "--flash-attn off";
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, source,
            [Capability("runtime.kv.type.f16"), Capability("runtime.kv.type.q8_0"),
                Capability("runtime.flash-attention")]);

        Assert.Equal(CapabilityState.Unknown, plan.Availability);
        Assert.Contains("explicitly on", plan.AvailabilityDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Kv_recipe_mapping_emits_the_explicit_flash_attention_override()
    {
        var source = Server("on");
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, source,
            [Capability("runtime.kv.type.f16"), Capability("runtime.kv.type.q8_0"),
                Capability("runtime.flash-attention")]);
        var candidate = Assert.Single(plan.Candidates);
        var isolated = LabConfigurationMapper.Apply(source, candidate, 39202);
        var args = ServerProcessManager.BuildLaunchArguments(isolated);

        Assert.Equal("q8_0", ArgValue(args, "--cache-type-v"));
        Assert.Equal("on", ArgValue(args, "--flash-attn"));
    }

    [Theory]
    [InlineData(CapabilityState.Available)]
    [InlineData(CapabilityState.Unavailable)]
    [InlineData(CapabilityState.Unknown)]
    public void Flash_recipe_follows_exact_capability_state(CapabilityState state)
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.FlashAttention, Server(), [Capability("runtime.flash-attention", state)]);
        Assert.Equal(state, plan.Availability);
    }

    [Fact]
    public void Cpu_moe_is_not_inferred_from_model_family()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.CpuMoePlacement, Server(), []);
        Assert.Equal(CapabilityState.Unknown, plan.Availability);
        Assert.Contains("Unknown", plan.AvailabilityDetail);
    }

    [Fact]
    public void Cpu_moe_candidates_are_bounded_when_capability_is_available()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.CpuMoePlacement, Server(),
            [Capability("runtime.moe.cpu-placement")]);
        Assert.Equal(CapabilityState.Available, plan.Availability);
        Assert.InRange(plan.Candidates.Count, 1, 3);
        Assert.Contains(plan.Candidates, candidate => candidate.CpuMoeLayers == -1);
    }

    [Fact]
    public void One_at_a_time_validator_refuses_cross_dimension_change()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.Context, Server(), []);
        var invalid = plan with
        {
            Candidates = [plan.Candidates[0] with { GpuLayers = -1 }]
        };
        Assert.Throws<InvalidOperationException>(() => LabRecipeCatalog.Validate(invalid));
    }

    [Fact]
    public void Recipe_validator_refuses_unbounded_run_count()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.Context, Server(), []);
        Assert.Throws<InvalidOperationException>(() => LabRecipeCatalog.Validate(plan with { MaximumRunCount = 99 }));
    }

    [Fact]
    public void Runtime_response_parser_preserves_reported_zero_and_missing_ttft()
    {
        var request = WorkloadRequest();
        var result = LlamaServerLabWorkloadExecutor.ParseSuccessfulResponse(request,
            """{"choices":[{"message":{"content":"same"}}],"usage":{"prompt_tokens":0,"completion_tokens":2,"total_tokens":2},"timings":{"prompt_per_second":0,"predicted_per_second":5}}""",
            TimeSpan.FromMilliseconds(20));
        Assert.Equal(0, result.Observations.Single(item => item.MetricId == "prompt.tokens").Value);
        var ttft = result.Observations.Single(item => item.MetricId == "ttft.milliseconds");
        Assert.Null(ttft.Value);
        Assert.NotEmpty(ttft.MissingReason);
        Assert.NotNull(result.Output);
    }

    [Fact]
    public void Runtime_response_parser_keeps_omitted_timing_missing()
    {
        var result = LlamaServerLabWorkloadExecutor.ParseSuccessfulResponse(WorkloadRequest(),
            """{"choices":[{"message":{"content":"same"}}],"usage":{}}""", TimeSpan.Zero);
        var decode = result.Observations.Single(item => item.MetricId == "decode.tokens_per_second");
        Assert.Null(decode.Value);
        Assert.Contains("omitted", decode.MissingReason);
    }

    [Fact]
    public async Task Runner_refuses_unknown_recipe_before_launch()
    {
        using var fixture = new RecipeFixture();
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, fixture.Source, []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runner.RunAsync(plan, fixture.Source, fixture.Capabilities([]), "prompt"));
        Assert.Empty(fixture.Host.StartedConfigurations);
    }

    [Fact]
    public async Task Runner_executes_baseline_and_each_candidate_in_isolated_sessions()
    {
        using var fixture = new RecipeFixture();
        var plan = LabRecipeCatalog.Build(LabRecipeKind.Context, fixture.Source, []);
        var run = await fixture.Runner.RunAsync(plan, fixture.Source, fixture.Capabilities([]), "controlled prompt");
        Assert.Equal(LabRunStatus.Succeeded, run.Status);
        Assert.Equal(plan.Candidates.Count + 1, fixture.Host.StartedConfigurations.Count);
        Assert.Equal(plan.Candidates.Count + 1, fixture.Host.Sessions.Count(session => session.StopCount == 1));
        Assert.Equal((plan.Candidates.Count + 1) * 3, fixture.Workload.CallCount);
    }

    [Fact]
    public async Task Runner_cleans_owned_runtime_after_ordinary_workload_exception()
    {
        using var fixture = new RecipeFixture();
        fixture.Workload.ThrowOnCall = 1;
        var plan = LabRecipeCatalog.Build(LabRecipeKind.Context, fixture.Source, []);

        var run = await fixture.Runner.RunAsync(plan, fixture.Source, fixture.Capabilities([]), "controlled prompt");

        Assert.Equal(LabRunStatus.PartiallySucceeded, run.Status);
        Assert.Contains(run.Failures, failure => failure.Contains("injected workload failure", StringComparison.Ordinal));
        Assert.NotEmpty(fixture.Host.Sessions);
        Assert.All(fixture.Host.Sessions, session => Assert.Equal(1, session.StopCount));
    }

    [Fact]
    public async Task Successful_recipe_materializes_the_candidate_for_review()
    {
        using var fixture = new RecipeFixture();
        var plan = LabRecipeCatalog.Build(LabRecipeKind.Context, fixture.Source, []);

        var run = await fixture.Runner.RunAsync(plan, fixture.Source, fixture.Capabilities([]), "controlled prompt");

        var candidate = plan.Candidates[0];
        var review = fixture.Experiments.CreateApplyReview(run.Id, candidate.Id);

        Assert.True(review.CanApply);
        Assert.Equal(candidate.Id, review.CandidateConfigurationId);
        Assert.NotEmpty(review.Changes);
    }

    [Fact]
    public async Task Runner_preserves_prediction_and_observation_as_separate_metrics()
    {
        using var fixture = new RecipeFixture();
        var plan = LabRecipeCatalog.Build(LabRecipeKind.Context, fixture.Source, []);
        var run = await fixture.Runner.RunAsync(plan, fixture.Source, fixture.Capabilities([]), "controlled prompt");
        Assert.Contains(run.Observations, value => value.MetricId == "memory.ram.predicted" && value.Origin == EvidenceOrigin.DeterministicCalculation);
        Assert.Contains(run.Observations, value => value.MetricId == "memory.ram.observed" && value.Origin == EvidenceOrigin.DirectObservation);
        Assert.Contains(run.Observations, value => value.MetricId == "memory.gpu.observed" && value.Value is null);
    }

    [Fact]
    public async Task Correctness_difference_stops_before_remaining_candidates()
    {
        using var fixture = new RecipeFixture();
        fixture.Workload.DifferentCandidateOutput = true;
        var plan = LabRecipeCatalog.Build(LabRecipeKind.Context, fixture.Source, []);
        var run = await fixture.Runner.RunAsync(plan, fixture.Source, fixture.Capabilities([]), "controlled prompt");
        Assert.Equal(2, fixture.Host.StartedConfigurations.Count);
        Assert.Equal(LabEquivalenceState.Different, run.Comparisons[0].Equivalence.State);
        Assert.False(run.Comparisons[0].CanShowHeadlineDelta);
    }

    [Fact]
    public async Task Low_bit_apply_is_blocked_when_quality_evidence_is_missing()
    {
        using var fixture = new RecipeFixture();
        var observations = new[]
        {
            Capability("runtime.kv.type.f16"), Capability("runtime.kv.type.q4_0"),
            Capability("runtime.flash-attention")
        };
        var plan = LabRecipeCatalog.Build(LabRecipeKind.KvCache, fixture.Source, observations);
        var run = await fixture.Runner.RunAsync(plan, fixture.Source, fixture.Capabilities(observations), "controlled prompt");
        var comparison = Assert.Single(run.Comparisons);
        Assert.False(comparison.CanShowHeadlineDelta);
        Assert.Contains("quality.score", comparison.RefusalReason);
    }

    [Fact]
    public async Task Switch_configuration_replaces_owned_runtime_without_saving_settings()
    {
        using var fixture = new RecipeFixture();
        var plan = LabRecipeCatalog.Build(LabRecipeKind.Context, fixture.Source, []);
        var definition = await fixture.Experiments.CreateDefinitionAsync("test", "switch-test", fixture.Source,
            plan.Baseline, plan.Candidates, 1, LabCorrectnessRequirement.ExactEquivalence);
        var run = await fixture.Experiments.StartAsync(definition, fixture.Source);
        var switched = await fixture.Experiments.SwitchConfigurationAsync(run.Id, fixture.Source, plan.Candidates[0].Id);
        Assert.Equal(2, fixture.Host.StartedConfigurations.Count);
        Assert.Equal(1, fixture.Host.Sessions[0].StopCount);
        Assert.Equal(0, fixture.Settings.SaveCount);
        await fixture.Experiments.CancelAsync(switched.Id);
    }

    [Fact]
    public void Prompt_prefix_recipe_changes_only_request_cache_mode()
    {
        var plan = LabRecipeCatalog.Build(LabRecipeKind.PromptPrefixReuse, Server(), []);

        Assert.Equal(CapabilityState.Available, plan.Availability);
        Assert.Equal("disabled", plan.Baseline.PromptCacheMode);
        Assert.Equal("enabled", Assert.Single(plan.Candidates).PromptCacheMode);
        Assert.Contains("prompt.reused.tokens", plan.RequiredMetrics);
        LabRecipeCatalog.Validate(plan);
    }

    [Fact]
    public void Shared_prefix_fixture_changes_only_a_bounded_suffix()
    {
        const string prefix = "A controlled project-state prefix with no private workspace content.";
        var first = SharedPrefixPromptFixture.Build(prefix, 0);
        var second = SharedPrefixPromptFixture.Build(prefix, 1);

        Assert.StartsWith(prefix, first, StringComparison.Ordinal);
        Assert.StartsWith(prefix, second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.NotEqual(LabCanonicalJson.Hash(first), LabCanonicalJson.Hash(second));
    }

    [Fact]
    public void Direct_reuse_counter_requires_available_reviewed_schema()
    {
        var unknown = RuntimeCapabilityObservation.Create("runtime.prompt-cache.reused-token-counter",
            CapabilityState.Unknown, "test", "unknown", Runtime(), null,
            new Dictionary<string, string> { ["response_field"] = "reused_tokens" }, DateTime.UtcNow);
        var available = unknown with { State = CapabilityState.Available };
        var unreviewed = available with
        {
            Parameters = new Dictionary<string, string> { ["response_field"] = "some_future_guess" }
        };

        Assert.Empty(PromptReuseEvidenceAdapter.ProvenCounterField([unknown]));
        Assert.Empty(PromptReuseEvidenceAdapter.ProvenCounterField([unreviewed]));
        Assert.Equal("reused_tokens", PromptReuseEvidenceAdapter.ProvenCounterField([available]));
    }

    [Fact]
    public void Runtime_response_never_infers_reused_tokens_from_prompt_timing()
    {
        var withoutCapability = LlamaServerLabWorkloadExecutor.ParseSuccessfulResponse(WorkloadRequest(),
            """{"choices":[{"message":{"content":"same"}}],"timings":{"prompt_ms":12,"prompt_per_second":900,"reused_tokens":40}}""",
            TimeSpan.Zero);
        var directRequest = WorkloadRequest() with { DirectReusedTokenCounterField = "reused_tokens" };
        var withCapability = LlamaServerLabWorkloadExecutor.ParseSuccessfulResponse(directRequest,
            """{"choices":[{"message":{"content":"same"}}],"timings":{"prompt_ms":12,"reused_tokens":40}}""",
            TimeSpan.Zero);

        Assert.Null(withoutCapability.Observations.Single(item => item.MetricId == "prompt.reused.tokens").Value);
        Assert.Equal(12, withoutCapability.Observations.Single(item => item.MetricId == "prompt.milliseconds").Value);
        Assert.Equal(40, withCapability.Observations.Single(item => item.MetricId == "prompt.reused.tokens").Value);
    }

    [Fact]
    public async Task Prefix_runner_pairs_identical_prompts_with_cache_disabled_and_enabled()
    {
        using var fixture = new RecipeFixture();
        var plan = LabRecipeCatalog.Build(LabRecipeKind.PromptPrefixReuse, fixture.Source, []);

        var run = await fixture.Runner.RunAsync(plan, fixture.Source, fixture.Capabilities([]), "shared prefix");

        Assert.Equal(LabRunStatus.Succeeded, run.Status);
        Assert.Equal(6, fixture.Workload.Requests.Count);
        var disabled = fixture.Workload.Requests.Where(request => request.DisablePromptCache).ToArray();
        var enabled = fixture.Workload.Requests.Where(request => !request.DisablePromptCache).ToArray();
        Assert.Equal(3, disabled.Length);
        Assert.Equal(3, enabled.Length);
        Assert.Equal(disabled.Select(request => request.Prompt), enabled.Select(request => request.Prompt));
        Assert.All(fixture.Workload.Requests, request => Assert.Empty(request.DirectReusedTokenCounterField));
        Assert.Equal(3, run.Definition.PromptHashes.Count);
        Assert.DoesNotContain("shared prefix", run.Definition.CanonicalJson(), StringComparison.Ordinal);
    }

    private sealed class RecipeFixture : IDisposable
    {
        private readonly TempDir _temp = new();
        public TrackingSettings Settings { get; }
        public SqliteEmpiricalExperienceStore Store { get; }
        public FakeHost Host { get; } = new();
        public FakeWorkload Workload { get; } = new();
        public LabExperimentService Experiments { get; }
        public LabRecipeRunner Runner { get; }
        public ServerConfig Source => Settings.Settings.ManagedServers.Single();

        public RecipeFixture()
        {
            Settings = new TrackingSettings(Helpers.NewSettings(_temp));
            Settings.Settings.DataManagement.DataRootDirectory = _temp.PathFor("data");
            Settings.Settings.ManagedServers = [Server("on")];
            Store = new SqliteEmpiricalExperienceStore(Settings, new RedactionService());
            Experiments = new LabExperimentService(Settings, new FakeSystemInfo(), Store, Host);
            Runner = new LabRecipeRunner(Experiments, Workload, new FakeTelemetry(), new FakeSystemInfo());
        }

        public LocalModelCapabilities Capabilities(IReadOnlyList<RuntimeCapabilityObservation> observations) => new(
            Source.ModelPath, Unknown(), Unknown(), Unknown(), Unknown(), DateTime.UtcNow,
            new RuntimeCapabilitySurface([], Unknown(), Unknown(), Unknown()), observations);

        public void Dispose() => _temp.Dispose();
        private static CapabilityEvidence Unknown() => new(CapabilityState.Unknown, "test", "unknown");
    }

    private static LabWorkloadRequest WorkloadRequest() => new(
        "run", 1234, LabConfigurationMapper.FromServer(Server(), "baseline", "Baseline"),
        Fingerprint(), "prompt", 1, 16, "case", 0, TimeSpan.FromSeconds(1));

    private sealed class FakeHost : ILabRuntimeHost
    {
        public List<string> StartedConfigurations { get; } = [];
        public List<FakeSession> Sessions { get; } = [];
        public Task<ILabRuntimeSession> StartAsync(string runId, ServerConfig source, LabConfiguration configuration, CancellationToken ct = default)
        {
            StartedConfigurations.Add(configuration.Id);
            var session = new FakeSession(50000 + Sessions.Count, 100 + Sessions.Count);
            Sessions.Add(session);
            return Task.FromResult<ILabRuntimeSession>(session);
        }
        public Task<IReadOnlyList<string>> RecoverOwnedProcessesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FakeSession(int port, int processId) : ILabRuntimeSession
    {
        public string OwnershipId { get; } = Guid.NewGuid().ToString("N");
        public int Port { get; } = port;
        public bool IsRunning => StopCount == 0;
        public ManagedProcessReference? Process { get; } = new(processId, DateTime.UnixEpoch.AddSeconds(processId));
        public int StopCount { get; private set; }
        public Task StopAsync(CancellationToken ct = default) { if (StopCount == 0) StopCount++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeWorkload : ILabWorkloadExecutor
    {
        public int CallCount { get; private set; }
        public int? ThrowOnCall { get; set; }
        public List<LabWorkloadRequest> Requests { get; } = [];
        public bool DifferentCandidateOutput { get; set; }
        public Task<LabWorkloadResult> ExecuteAsync(LabWorkloadRequest request, CancellationToken ct = default)
        {
            CallCount++;
            if (CallCount == ThrowOnCall)
                throw new InvalidOperationException("injected workload failure");
            Requests.Add(request);
            var value = request.Configuration.Id == "baseline" ? 10d : 12d;
            var observation = new LabObservation
            {
                RunId = request.RunId, ConfigurationId = request.Configuration.Id,
                CaseId = request.CaseId, Repetition = request.Repetition,
                MetricId = "decode.tokens_per_second", Value = value, Unit = "tokens/s",
                Source = "fake-runtime", Trust = "TrustedRuntime",
                RuntimeFingerprint = request.Fingerprint.Runtime.StableId,
                ModelFingerprint = request.Fingerprint.Model.StableId,
                HardwareFingerprint = request.Fingerprint.Hardware.StableId,
                ConfigurationFingerprint = request.Fingerprint.Configuration.StableId
            };
            var text = DifferentCandidateOutput && request.Configuration.Id != "baseline" ? "different" : "same";
            return Task.FromResult(new LabWorkloadResult([observation],
                LabCorrectnessEvaluator.Capture(request.Configuration.Id, request.CaseId, request.Repetition, text), null));
        }
    }

    private sealed class FakeTelemetry : IRuntimeTelemetrySource
    {
        public Task<IReadOnlyList<RuntimeTelemetrySample>> CaptureAsync(RuntimeTelemetryRequest request, CancellationToken ct = default)
        {
            var instance = RuntimeTelemetrySeries.ProcessInstance(request.ProcessId, request.ProcessStartedAtUtc);
            return Task.FromResult<IReadOnlyList<RuntimeTelemetrySample>>
            ([
                new(request.SeriesId, instance, RuntimeTelemetryMetric.ProcessWorkingSetBytes, 1024,
                    RuntimeTelemetrySourceKind.ProcessCounter, RuntimeTelemetryTrustState.ProcessScoped,
                    DateTime.UtcNow, request.RuntimeIdentity.StableId, "test-ram", "process RAM"),
                new(request.SeriesId, instance, RuntimeTelemetryMetric.ProcessGpuMemoryBytes, null,
                    RuntimeTelemetrySourceKind.Unknown, RuntimeTelemetryTrustState.Unknown,
                    DateTime.UtcNow, request.RuntimeIdentity.StableId, "gpu-unknown", "per-process GPU is unavailable")
            ]);
        }
    }

    private sealed class TrackingSettings(ISettingsService inner) : ISettingsService
    {
        public AppSettings Settings => inner.Settings;
        public int SaveCount { get; private set; }
        public event EventHandler? SettingsChanged { add => inner.SettingsChanged += value; remove => inner.SettingsChanged -= value; }
        public Task LoadAsync() => inner.LoadAsync();
        public Task<SettingsSaveResult> SaveAsync(string? previousDataRootDirectory = null)
        { SaveCount++; return inner.SaveAsync(previousDataRootDirectory); }
        public Task<SettingsSaveResult> SaveAsync(AppSettings settings, string? previousDataRootDirectory = null)
        { SaveCount++; return inner.SaveAsync(settings, previousDataRootDirectory); }
        public DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory) =>
            inner.PreviewDataRootMigration(previousDataRootDirectory, nextDataRootDirectory);
    }
}
