using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LabExperimentTests
{
    [Fact]
    public void Canonical_baseline_matches_an_unchanged_services_server()
    {
        var source = new ServerConfig
        {
            Id = "chat",
            ContextSize = 8192,
            GpuLayers = -1,
            Threads = 8,
            PromptThreads = 2,
            Slots = 1,
            KvCacheType = "q8_0",
            KvCacheTypeK = "q8_0",
            KvCacheTypeV = "q8_0",
            FlashAttention = "on",
            CpuMoeLayers = 0,
            Speculative = new SpeculativeDecodingConfig
            {
                Types = ["draft-simple"], DraftModelPath = "draft.gguf", DraftGpuLayers = 4,
                NMax = 8, NMin = 1, PMin = 0.2
            },
            ExtraArgs = "--parallel 1"
        };

        var baseline = LabConfigurationMapper.FromServer(source, "baseline", "Baseline");

        Assert.Empty(LabConfigurationMapper.Differences(source, baseline));
    }

    private static LabConfiguration Config(string id = "baseline", int context = 4096, string extraHash = "") => new()
    {
        Id = id, Label = id, ContextSize = context, GpuLayers = 0, Threads = 4,
        PromptThreads = 0, Slots = 1, KvCacheTypeK = "f16", KvCacheTypeV = "f16",
        FlashAttention = "auto", ExtraArgumentsSha256 = extraHash
    };

    private static ConfigurationIdentityV2 ConfigurationIdentity(int context = 4096) =>
        new(context, 0, "cpu", 4, 0, 1, null, null, "f16", "f16", "auto", "", "", "", 0,
            new Dictionary<string, string>(), IdentityCompleteness.Complete);

    private static EmpiricalProfileFingerprintV2 Fingerprint() => new(
        new RuntimeIdentityV2("test", "runtime-hash", 1, DateTime.UnixEpoch, "v", "b", "c", "cpu", "", IdentityCompleteness.Complete),
        new ModelIdentityV2("model", "model-hash", 1, DateTime.UnixEpoch, "test", "q4", "", ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete),
        new HardwareIdentityV2("test", "x64", "cpu", "cpu", null, 1024, "", "single", IdentityCompleteness.Complete),
        ConfigurationIdentity());

    private static LabExperimentDefinition Definition(LabConfiguration? baseline = null,
        IReadOnlyList<LabConfiguration>? candidates = null, LabCorrectnessRequirement correctness = LabCorrectnessRequirement.ExactEquivalence) => new()
    {
        Name = "test", ProtocolId = "test-protocol", TargetServerId = "server-1",
        ProfileFingerprint = Fingerprint(), Baseline = baseline ?? Config(),
        Candidates = candidates ?? [Config("candidate", 8192)], Repetitions = 3,
        ConfigurationFingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["baseline"] = ConfigurationIdentity().StableId,
            ["candidate"] = ConfigurationIdentity(8192).StableId
        },
        ConfigurationIdentities = new Dictionary<string, ConfigurationIdentityV2>(StringComparer.Ordinal)
        {
            ["baseline"] = ConfigurationIdentity(),
            ["candidate"] = ConfigurationIdentity(8192)
        },
        CorrectnessRequirement = correctness
    };

    [Fact]
    public void Definition_requires_schema_one() =>
        Assert.Throws<InvalidOperationException>(() => LabDefinitionValidator.Validate(Definition() with { SchemaVersion = 2 }));

    [Theory]
    [InlineData("auto")]
    [InlineData("off")]
    public void Definition_rejects_quantized_value_cache_without_explicit_flash_attention(string flashAttention)
    {
        var configuration = Config("candidate") with
        {
            KvCacheTypeK = "q8_0", KvCacheTypeV = "q8_0", FlashAttention = flashAttention
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => LabDefinitionValidator.ValidateConfiguration(configuration));

        Assert.Contains("Flash Attention", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Definition_rejects_a_conflicting_flash_attention_extra_argument()
    {
        var configuration = Config("candidate") with
        {
            KvCacheTypeK = "q8_0", KvCacheTypeV = "q8_0", FlashAttention = "on"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LabDefinitionValidator.ValidateConfiguration(configuration, "--flash-attn off"));

        Assert.Contains("Flash Attention", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Definition_bounds_repetitions(int repetitions) =>
        Assert.Throws<InvalidOperationException>(() => LabDefinitionValidator.Validate(Definition() with { Repetitions = repetitions }));

    [Fact]
    public void Definition_requires_candidate() =>
        Assert.Throws<InvalidOperationException>(() => LabDefinitionValidator.Validate(Definition(candidates: [])));

    [Fact]
    public void Definition_bounds_candidate_count() =>
        Assert.Throws<InvalidOperationException>(() => LabDefinitionValidator.Validate(Definition(candidates:
            Enumerable.Range(0, 17).Select(index => Config($"candidate-{index}")).ToArray())));

    [Fact]
    public void Definition_rejects_duplicate_configuration_ids() =>
        Assert.Throws<InvalidOperationException>(() => LabDefinitionValidator.Validate(Definition(candidates: [Config()])));

    [Fact]
    public void Definition_rejects_unsafe_protocol_id() =>
        Assert.Throws<InvalidOperationException>(() => LabDefinitionValidator.Validate(Definition() with { ProtocolId = "../../private" }));

    [Theory]
    [InlineData("--host 0.0.0.0")]
    [InlineData("--port=9000")]
    [InlineData("--listen ::")]
    public void Configuration_rejects_network_overrides(string arguments) =>
        Assert.Throws<InvalidOperationException>(() => LabDefinitionValidator.ValidateIsolationArguments(arguments));

    [Fact]
    public void Canonical_definition_is_stable_and_revision_sensitive()
    {
        var first = Definition();
        var copy = first with { };
        Assert.Equal(first.DefinitionHash, copy.DefinitionHash);
        Assert.NotEqual(first.DefinitionHash, (first with { Revision = 2 }).DefinitionHash);
    }

    [Fact]
    public void Observation_keeps_missing_distinct_from_zero()
    {
        var definition = Definition(correctness: LabCorrectnessRequirement.Behavioral);
        var observations = new[]
        {
            Observation(definition, "baseline", null, "not reported"),
            Observation(definition, "candidate", 0, "")
        };
        var comparison = LabComparisonEngine.Compare(definition, definition.Candidates[0], observations, []);
        Assert.Null(Assert.Single(comparison.BaselineMetrics).Median);
        Assert.Equal(0, Assert.Single(comparison.CandidateMetrics).Median);
    }

    [Fact]
    public void Comparison_reports_median_range_and_count()
    {
        var definition = Definition(correctness: LabCorrectnessRequirement.Behavioral);
        var observations = new[] { 1d, 9d, 5d }.Select((value, index) =>
            Observation(definition, "candidate", value, "", index)).ToArray();
        var summary = Assert.Single(LabComparisonEngine.Compare(definition, definition.Candidates[0], observations, []).CandidateMetrics);
        Assert.Equal(5, summary.Median);
        Assert.Equal(1, summary.Minimum);
        Assert.Equal(9, summary.Maximum);
        Assert.Equal(3, summary.Repetitions);
    }

    [Fact]
    public void Comparison_refuses_uncontrolled_runtime_fingerprint()
    {
        var definition = Definition(correctness: LabCorrectnessRequirement.Behavioral);
        var observation = Observation(definition, "candidate", 1, "") with { RuntimeFingerprint = "other" };
        var comparison = LabComparisonEngine.Compare(definition, definition.Candidates[0], [observation], []);
        Assert.False(comparison.IsControlled);
        Assert.False(comparison.CanShowHeadlineDelta);
        Assert.Contains("runtime", comparison.FingerprintDifferences);
    }

    [Fact]
    public void Exact_token_equivalence_is_preferred()
    {
        var baseline = LabCorrectnessEvaluator.Capture("baseline", "case", 0, "hello", [1, 2]);
        var candidate = LabCorrectnessEvaluator.Capture("candidate", "case", 0, "different decoding", [1, 2]);
        var result = LabCorrectnessEvaluator.Compare(baseline, candidate);
        Assert.Equal(LabEquivalenceState.Equivalent, result.State);
        Assert.Equal(LabEquivalenceLevel.TokenIds, result.Level);
    }

    [Fact]
    public void Token_difference_is_a_correctness_failure_without_exporting_tokens()
    {
        var baseline = LabCorrectnessEvaluator.Capture("baseline", "case", 0, "a", [1, 2]);
        var candidate = LabCorrectnessEvaluator.Capture("candidate", "case", 0, "b", [1, 3]);
        var result = LabCorrectnessEvaluator.Compare(baseline, candidate);
        Assert.Equal(LabEquivalenceState.Different, result.State);
        Assert.Contains("index 1", result.BoundedDiff);
        Assert.DoesNotContain("token 2", result.BoundedDiff, StringComparison.Ordinal);
    }

    [Fact]
    public void Utf8_hash_is_weaker_fallback()
    {
        var baseline = LabCorrectnessEvaluator.Capture("baseline", "case", 0, "same");
        var candidate = LabCorrectnessEvaluator.Capture("candidate", "case", 0, "same");
        var result = LabCorrectnessEvaluator.Compare(baseline, candidate);
        Assert.Equal(LabEquivalenceState.Equivalent, result.State);
        Assert.Equal(LabEquivalenceLevel.ExactUtf8, result.Level);
    }

    [Fact]
    public void Missing_output_is_unknown() =>
        Assert.Equal(LabEquivalenceState.Unknown, LabCorrectnessEvaluator.Compare(null, null).State);

    [Fact]
    public async Task Start_uses_frozen_clone_without_saving_settings()
    {
        using var fixture = new Fixture();
        var source = fixture.Source;
        var before = source.ContextSize;
        var definition = await fixture.Service.CreateDefinitionAsync("test", "protocol", source,
            Config(context: before), [Config("candidate", before * 2)], 1, LabCorrectnessRequirement.ExactEquivalence);

        var run = await fixture.Service.StartAsync(definition, source);

        Assert.Equal(LabRunStatus.Running, run.Status);
        Assert.Equal(before, source.ContextSize);
        Assert.Equal(0, fixture.Settings.SaveCount);
        Assert.NotEqual(source.Port, run.TemporaryPort);
    }

    [Fact]
    public async Task Start_refuses_a_second_active_run_until_the_first_is_finished()
    {
        using var fixture = new Fixture();
        var definition = await fixture.DefinitionAsync();
        var first = await fixture.Service.StartAsync(definition, fixture.Source);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.StartAsync(definition, fixture.Source));

        Assert.Contains("Another Lab run is already active", exception.Message, StringComparison.Ordinal);
        await fixture.Service.CancelAsync(first.Id);
    }

    [Fact]
    public async Task Start_deep_freezes_definition_collections()
    {
        using var fixture = new Fixture();
        var definition = await fixture.DefinitionAsync();
        var run = await fixture.Service.StartAsync(definition, fixture.Source);
        var original = run.Definition.ConfigurationFingerprints["baseline"];
        ((Dictionary<string, string>)definition.ConfigurationFingerprints)["baseline"] = "mutated";
        Assert.Equal(original, run.Definition.ConfigurationFingerprints["baseline"]);
    }

    [Fact]
    public async Task Definition_persists_extra_arguments_as_hash_only()
    {
        using var fixture = new Fixture();
        fixture.Source.ExtraArgs = "--alias private-local-value";
        var baseline = LabConfigurationMapper.FromServer(fixture.Source, "baseline", "Baseline");
        var definition = await fixture.Service.CreateDefinitionAsync("test", "protocol", fixture.Source,
            baseline, [baseline with { Id = "candidate", Label = "Candidate" }], 1,
            LabCorrectnessRequirement.ExactEquivalence);
        Assert.DoesNotContain("private-local-value", definition.CanonicalJson(), StringComparison.Ordinal);
        Assert.Equal(64, definition.Baseline.ExtraArgumentsSha256.Length);
    }

    [Fact]
    public async Task Start_refuses_changed_source_identity()
    {
        using var fixture = new Fixture();
        var definition = await fixture.DefinitionAsync();
        fixture.Source.ContextSize++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.StartAsync(definition, fixture.Source));
        Assert.Equal(0, fixture.Host.StartCount);
    }

    [Fact]
    public async Task Start_refuses_different_server()
    {
        using var fixture = new Fixture();
        var definition = await fixture.DefinitionAsync();
        fixture.Source.Id = "other";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.StartAsync(definition, fixture.Source));
    }

    [Fact]
    public async Task Launch_failure_is_persisted_as_failed()
    {
        using var fixture = new Fixture();
        fixture.Host.Failure = new InvalidOperationException("launch refused");
        var run = await fixture.Service.StartAsync(await fixture.DefinitionAsync(), fixture.Source);
        Assert.Equal(LabRunStatus.Failed, run.Status);
        Assert.Contains("launch refused", Assert.Single(run.Failures));
        Assert.NotEmpty(run.CompletionEvidenceId);
    }

    [Fact]
    public async Task Candidate_launch_failure_does_not_leave_a_running_snapshot_without_a_session()
    {
        using var fixture = new Fixture();
        var run = await fixture.Service.StartAsync(await fixture.DefinitionAsync(), fixture.Source);
        fixture.Host.FailOnStart = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SwitchConfigurationAsync(
            run.Id, fixture.Source, "candidate"));

        var failed = fixture.Service.GetRun(run.Id)!;
        Assert.Equal(LabRunStatus.Failed, failed.Status);
        Assert.Null(failed.TemporaryPort);
        Assert.Null(failed.RuntimeProcessId);
        Assert.Contains(failed.Failures, failure => failure.Contains("candidate", StringComparison.Ordinal));
        Assert.Equal(1, fixture.Host.Session.StopCount);
    }

    [Fact]
    public async Task Cancel_stops_only_owned_session_and_preserves_evidence()
    {
        using var fixture = new Fixture();
        var run = await fixture.Service.StartAsync(await fixture.DefinitionAsync(), fixture.Source);
        var cancelled = await fixture.Service.CancelAsync(run.Id);
        Assert.Equal(LabRunStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, fixture.Host.Session.StopCount);
        Assert.NotEmpty(cancelled.StartEvidenceId);
        Assert.NotEmpty(cancelled.CompletionEvidenceId);
    }

    [Fact]
    public async Task Complete_rejects_foreign_observation()
    {
        using var fixture = new Fixture();
        var run = await fixture.Service.StartAsync(await fixture.DefinitionAsync(), fixture.Source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CompleteAsync(run.Id,
            [Observation(run.Definition, "baseline", 1, "") with { RunId = "foreign" }], []));
    }

    [Fact]
    public async Task Complete_with_failures_is_partial_and_stops_runtime()
    {
        using var fixture = new Fixture();
        var run = await fixture.Service.StartAsync(await fixture.DefinitionAsync(), fixture.Source);
        var completed = await fixture.Service.CompleteAsync(run.Id,
            [Observation(run.Definition, "baseline", 1, "") with { RunId = run.Id }], [], ["candidate failed"]);
        Assert.Equal(LabRunStatus.PartiallySucceeded, completed.Status);
        Assert.Equal(1, fixture.Host.Session.StopCount);
    }

    [Fact]
    public async Task Completion_export_omits_private_output_text()
    {
        using var fixture = new Fixture();
        var run = await fixture.Service.StartAsync(await fixture.DefinitionAsync(), fixture.Source);
        var outputs = new[]
        {
            LabCorrectnessEvaluator.Capture("baseline", "case", 0, "private baseline body"),
            LabCorrectnessEvaluator.Capture("candidate", "case", 0, "private candidate body")
        };
        var completed = await fixture.Service.CompleteAsync(run.Id, Observations(run), outputs);
        var export = await fixture.Store.ExportAsync([completed.CompletionEvidenceId]);
        Assert.DoesNotContain("private baseline body", export, StringComparison.Ordinal);
        Assert.DoesNotContain("private candidate body", export, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completion_splits_large_evidence_into_bounded_recoverable_slices()
    {
        using var fixture = new Fixture();
        var run = await fixture.Service.StartAsync(await fixture.DefinitionAsync(), fixture.Source);
        var observations = Enumerable.Range(0, 400)
            .Select(index => Observation(run.Definition, index % 2 == 0 ? "baseline" : "candidate", index, "", index)
                with { RunId = run.Id })
            .ToArray();
        var outputs = new[]
        {
            LabCorrectnessEvaluator.Capture("baseline", "case", 0, "private baseline body"),
            LabCorrectnessEvaluator.Capture("candidate", "case", 0, "private candidate body")
        };

        var completed = await fixture.Service.CompleteAsync(run.Id, observations, outputs);
        var records = await fixture.Store.QueryAsync(new EmpiricalExperienceQuery
        {
            Domain = EmpiricalExperienceDomains.LabRun,
            Limit = 500
        });
        var slices = records
            .Where(record => record.ActionJson.Contains("\"observations\"", StringComparison.Ordinal))
            .Select(record => ExperienceJson.Decode<LabRunEvidenceSlice>(record.ActionJson))
            .ToArray();

        Assert.True(slices.Length > 2, "the oversized run should be stored as multiple evidence slices");
        Assert.Equal(observations.Length, slices.Sum(slice => slice.Observations.Count));
        Assert.Equal(outputs.Length, slices.Sum(slice => slice.Outputs.Count));
        Assert.Contains(records, record => record.Id == completed.CompletionEvidenceId);
        Assert.All(slices, slice => Assert.True(ExperienceJson.Canonicalize(slice).Length <= ExperienceJson.MaxDocumentBytes));
    }

    [Fact]
    public async Task Speed_only_run_cannot_produce_apply_recommendation()
    {
        using var fixture = new Fixture();
        var definition = await fixture.Service.CreateDefinitionAsync("test", "protocol", fixture.Source,
            Config(), [Config("candidate", 8192)], 1, LabCorrectnessRequirement.SpeedOnly);
        var run = await fixture.Service.StartAsync(definition, fixture.Source);
        await fixture.Service.CompleteAsync(run.Id, Observations(run), []);
        var review = fixture.Service.CreateApplyReview(run.Id, "candidate");
        Assert.False(review.CanApply);
        Assert.Contains("Speed-only", review.RefusalReason);
    }

    [Fact]
    public async Task Apply_review_lists_exact_fields_and_stale_change_is_refused()
    {
        using var fixture = new Fixture();
        var run = await CompleteEquivalentAsync(fixture);
        var review = fixture.Service.CreateApplyReview(run.Id, "candidate");
        Assert.True(review.CanApply);
        Assert.Equal(nameof(ServerConfig.ContextSize), Assert.Single(review.Changes).Field);
        fixture.Source.Threads++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyAsync(review));
        Assert.Equal(0, fixture.Settings.SaveCount);
    }

    [Fact]
    public async Task Explicit_apply_routes_through_settings_save_and_keeps_evidence()
    {
        using var fixture = new Fixture();
        var run = await CompleteEquivalentAsync(fixture);
        var review = fixture.Service.CreateApplyReview(run.Id, "candidate");
        await fixture.Service.ApplyAsync(review);
        Assert.Equal(1, fixture.Settings.SaveCount);
        Assert.Equal(8192, fixture.Settings.Settings.ManagedServers.Single(server => server.Id == "server-1").ContextSize);
        Assert.NotNull(await fixture.Store.GetAsync(run.CompletionEvidenceId));
    }

    [Fact]
    public async Task Empty_recovery_manifest_has_no_process_side_effects()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());
        Assert.Empty(await host.RecoverOwnedProcessesAsync());
    }

    [Fact]
    public async Task Service_disposal_stops_active_owned_runtime()
    {
        using var fixture = new Fixture();
        await fixture.Service.StartAsync(await fixture.DefinitionAsync(), fixture.Source);
        await fixture.Service.DisposeAsync();
        Assert.Equal(1, fixture.Host.Session.StopCount);
    }

    private static async Task<LabRunSnapshot> CompleteEquivalentAsync(Fixture fixture)
    {
        var run = await fixture.Service.StartAsync(await fixture.DefinitionAsync(), fixture.Source);
        var outputs = new[]
        {
            LabCorrectnessEvaluator.Capture("baseline", "case", 0, "same"),
            LabCorrectnessEvaluator.Capture("candidate", "case", 0, "same")
        };
        return await fixture.Service.CompleteAsync(run.Id, Observations(run), outputs);
    }

    private static IReadOnlyList<LabObservation> Observations(LabRunSnapshot run) =>
        [Observation(run.Definition, "baseline", 10, "") with { RunId = run.Id },
         Observation(run.Definition, "candidate", 12, "") with { RunId = run.Id }];

    private static LabObservation Observation(LabExperimentDefinition definition, string configId,
        double? value, string missing, int repetition = 0) => new()
    {
        RunId = "run", ConfigurationId = configId, CaseId = "case", Repetition = repetition,
        MetricId = "decode.tokens_per_second", Value = value, Unit = "tokens/s",
        Source = "runtime-metrics", Trust = "TrustedRuntime", MissingReason = missing,
        RuntimeFingerprint = definition.ProfileFingerprint.Runtime.StableId,
        ModelFingerprint = definition.ProfileFingerprint.Model.StableId,
        HardwareFingerprint = definition.ProfileFingerprint.Hardware.StableId,
        ConfigurationFingerprint = definition.ConfigurationFingerprints[configId]
    };

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _temp = new();
        public TrackingSettings Settings { get; }
        public SqliteEmpiricalExperienceStore Store { get; }
        public ServerConfig Source => Settings.Settings.ManagedServers.Single();
        public FakeLabRuntimeHost Host { get; } = new();
        public LabExperimentService Service { get; }

        public Fixture()
        {
            Settings = new TrackingSettings(Helpers.NewSettings(_temp));
            Settings.Settings.DataManagement.DataRootDirectory = _temp.PathFor("data");
            Settings.Settings.ManagedServers = [new ServerConfig
            {
                Id = "server-1", Name = "Chat", ExecutablePath = "missing-llama-server",
                ModelPath = "missing-model.gguf", Port = 39201, ContextSize = 4096,
                Threads = 4, Slots = 1
            }];
            Store = new SqliteEmpiricalExperienceStore(Settings, new RedactionService());
            Service = new LabExperimentService(Settings, new FakeSystemInfo(), Store, Host);
        }

        public Task<LabExperimentDefinition> DefinitionAsync() => Service.CreateDefinitionAsync(
            "test", "protocol", Source, Config(), [Config("candidate", 8192)], 1,
            LabCorrectnessRequirement.ExactEquivalence);

        public void Dispose() => _temp.Dispose();
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

    private sealed class FakeLabRuntimeHost : ILabRuntimeHost
    {
        public int StartCount { get; private set; }
        public int? FailOnStart { get; set; }
        public Exception? Failure { get; set; }
        public FakeSession Session { get; } = new();
        public Task<ILabRuntimeSession> StartAsync(string runId, ServerConfig source, LabConfiguration configuration, CancellationToken ct = default)
        {
            StartCount++;
            var failure = Failure ?? (FailOnStart == StartCount
                ? new InvalidOperationException("candidate launch refused") : null);
            return failure is null ? Task.FromResult<ILabRuntimeSession>(Session) : Task.FromException<ILabRuntimeSession>(failure);
        }
        public Task<IReadOnlyList<string>> RecoverOwnedProcessesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FakeSession : ILabRuntimeSession
    {
        public string OwnershipId => "owned-session";
        public int Port => 49152;
        public bool IsRunning => StopCount == 0;
        public ManagedProcessReference? Process => new(42, DateTime.UnixEpoch);
        public int StopCount { get; private set; }
        public Task StopAsync(CancellationToken ct = default) { StopCount++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
