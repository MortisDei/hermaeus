using System.Text.Json;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Desktop;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class RuntimeIdentityAndCapabilityTests
{
    [Fact]
    public void Registry_retains_unknown_capability_ids()
    {
        var registry = new RuntimeCapabilityRegistry();
        registry.Observe(Observation("vendor.future.feature", CapabilityState.Unknown));

        Assert.Equal("vendor.future.feature", registry.Find("vendor.future.feature")?.CapabilityId);
    }

    [Fact]
    public void Registry_replaces_only_the_same_capability_id()
    {
        var registry = new RuntimeCapabilityRegistry();
        registry.Observe(Observation("runtime.prompt-threads", CapabilityState.Unknown));
        registry.Observe(Observation("runtime.prompt-threads", CapabilityState.Available));

        Assert.Single(registry.Observations);
        Assert.Equal(CapabilityState.Available, registry.Find("runtime.prompt-threads")?.State);
    }

    [Fact]
    public void Observation_bounds_parameter_count_and_lengths()
    {
        var parameters = Enumerable.Range(0, 30)
            .Select(index => new KeyValuePair<string, string>($"{index}{new string('k', 80)}", new string('v', 400)));

        var observation = RuntimeCapabilityObservation.Create(
            "future.capability", CapabilityState.Unknown, "probe-failed", "unknown",
            Runtime(), null, parameters, DateTime.UtcNow);

        Assert.Equal(RuntimeCapabilityObservation.MaximumParameters, observation.Parameters.Count);
        Assert.All(observation.Parameters, pair =>
        {
            Assert.True(pair.Key.Length <= RuntimeCapabilityObservation.MaximumParameterKeyLength);
            Assert.True(pair.Value.Length <= RuntimeCapabilityObservation.MaximumParameterValueLength);
        });
    }

    [Fact]
    public void Unknown_capability_round_trips_as_data()
    {
        var original = Observation("speculative.observed.future-drafter", CapabilityState.Unknown);

        var restored = JsonSerializer.Deserialize<RuntimeCapabilityObservation>(JsonSerializer.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(original.CapabilityId, restored.CapabilityId);
        Assert.Equal(original.State, restored.State);
        Assert.Equal(original.RuntimeIdentity.StableId, restored.RuntimeIdentity.StableId);
        Assert.Equal(original.Parameters, restored.Parameters);
    }

    [Fact]
    public void Runtime_stable_id_changes_with_executable_hash()
    {
        var first = Runtime();
        var second = first with { ExecutableSha256 = "different" };

        Assert.NotEqual(first.StableId, second.StableId);
    }

    [Fact]
    public void Runtime_stable_id_is_repeatable()
    {
        var first = Runtime();
        var second = Runtime();

        Assert.Equal(first.StableId, second.StableId);
    }

    [Fact]
    public void Same_executable_hash_identifies_runtime_when_optional_version_facts_differ()
    {
        var probed = Runtime();
        var cacheLookup = probed with { Version = string.Empty, Build = string.Empty, Compiler = string.Empty, Backend = string.Empty };

        Assert.True(probed.IdentifiesSameRuntime(cacheLookup));
    }

    [Fact]
    public void Model_file_metadata_fallback_is_explicitly_incomplete()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("model.gguf");
        File.WriteAllText(path, "bounded fixture");

        var identity = RuntimeIdentityFactory.CreateModelIdentity(path, null);

        Assert.Equal(ModelIdentityStrength.FileMetadataFallback, identity.Strength);
        Assert.Equal(IdentityCompleteness.Incomplete, identity.Completeness);
        Assert.DoesNotContain(Path.GetDirectoryName(path)!, JsonSerializer.Serialize(identity), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verified_model_hash_is_complete()
    {
        var identity = RuntimeIdentityFactory.CreateModelIdentity("missing.gguf", null, "ABC123");

        Assert.Equal(ModelIdentityStrength.VerifiedHash, identity.Strength);
        Assert.Equal(IdentityCompleteness.Complete, identity.Completeness);
        Assert.Equal("abc123", identity.Sha256);
    }

    [Fact]
    public void V2_fingerprint_is_incomplete_when_any_component_is_incomplete()
    {
        var fingerprint = Fingerprint(Runtime() with { Completeness = IdentityCompleteness.Incomplete });

        Assert.Equal(IdentityCompleteness.Incomplete, fingerprint.Completeness);
    }

    [Fact]
    public void V2_exact_compatibility_requires_every_subidentity()
    {
        var first = Fingerprint(Runtime());
        var same = Fingerprint(Runtime());
        var differentHardware = same with
        {
            Hardware = same.Hardware with { VramBytes = same.Hardware.VramBytes + 1 }
        };

        Assert.True(first.IsExactlyCompatibleWith(same));
        Assert.False(first.IsExactlyCompatibleWith(differentHardware));
    }

    [Fact]
    public void Configuration_map_order_does_not_change_stable_id()
    {
        var first = Configuration(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        var second = Configuration(new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });

        Assert.Equal(first.StableId, second.StableId);
    }

    [Fact]
    public void Historical_v1_fingerprint_stays_version_one_and_incomplete()
    {
        var fingerprint = new EmpiricalProfileFingerprint { ModelIdentity = "model", ContextSize = 4096 };
        var stableId = fingerprint.StableId;
        var restored = JsonSerializer.Deserialize<EmpiricalProfileFingerprint>(JsonSerializer.Serialize(fingerprint));

        Assert.Equal(1, restored!.Version);
        Assert.Equal(IdentityCompleteness.Incomplete, restored.Completeness);
        Assert.Equal(stableId, restored.StableId);
    }

    [Fact]
    public void Runtime_version_parser_keeps_only_known_fields()
    {
        var parsed = RuntimeIdentityFactory.ParseVersion("version b10590 build 10590\ncompiler: MSVC 19.44\nbackend: CUDA");

        Assert.Equal("b10590", parsed.Version);
        Assert.Equal("10590", parsed.Build);
        Assert.Equal("MSVC 19.44", parsed.Compiler);
        Assert.Equal("CUDA", parsed.Backend);
    }

    [Theory]
    [InlineData("draft-simple", "speculative.draft.simple")]
    [InlineData("draft-mtp", "speculative.draft.mtp")]
    [InlineData("ngram-mod", "speculative.ngram.mod")]
    [InlineData("eagle3", "speculative.draft.eagle3")]
    [InlineData("future-x", "speculative.observed.future.x")]
    public void Speculative_runtime_types_map_to_stable_dotted_ids(string type, string expected)
    {
        Assert.Equal(expected, LocalModelCapabilityService.CapabilityIdForSpeculativeType(type));
    }

    [Fact]
    public void Generic_mtp_help_plus_nextn_metadata_remains_unknown()
    {
        var gguf = Gguf(nextnPredictLayers: 1);
        var facts = LocalModelCapabilityService.ParseHelp("--spec-type draft-mtp");

        var capabilities = LocalModelCapabilityService.Combine("model.gguf", gguf, facts);

        Assert.Equal(CapabilityState.Unknown, capabilities.EmbeddedMtp.State);
        Assert.Equal("model-mtp-engagement-unknown", capabilities.EmbeddedMtp.EvidenceCode);
    }

    [Fact]
    public void Direct_model_drafting_evidence_makes_mtp_available()
    {
        var gguf = Gguf(nextnPredictLayers: 1);
        var facts = LocalModelCapabilityService.ParseProps(
            """{"speculative":{"draft_n":3}}""",
            LocalModelCapabilityService.ParseHelp("--spec-type draft-mtp"));

        var capabilities = LocalModelCapabilityService.Combine("model.gguf", gguf, facts);

        Assert.Equal(CapabilityState.Available, capabilities.EmbeddedMtp.State);
        Assert.Equal("runtime-model-mtp-confirmed", capabilities.EmbeddedMtp.EvidenceCode);
    }

    [Fact]
    public void Failed_help_probe_keeps_capability_unknown()
    {
        var capabilities = LocalModelCapabilityService.Combine("model.gguf", null, LocalModelCapabilityService.ParseHelp(null));

        Assert.Equal(CapabilityState.Unknown, capabilities.ReasoningOutput.State);
        Assert.All(capabilities.Observations!, observation => Assert.NotEqual(CapabilityState.Unavailable, observation.State));
    }

    [Fact]
    public void Successful_authoritative_probe_can_report_unavailable()
    {
        var capabilities = LocalModelCapabilityService.Combine("model.gguf", Gguf(1), LocalModelCapabilityService.ParseHelp("llama-server help"));

        Assert.Equal(CapabilityState.Unavailable, capabilities.EmbeddedMtp.State);
        Assert.Equal(CapabilityState.Unavailable, capabilities.ReasoningOutput.State);
    }

    [Fact]
    public void Capability_observations_carry_exact_runtime_and_model_identity()
    {
        var runtime = Runtime();
        var model = new ModelIdentityV2("manifest", "hash", null, null, "gemma", "Q4", string.Empty,
            ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete);
        var capabilities = LocalModelCapabilityService.Combine(
            "model.gguf", Gguf(1), LocalModelCapabilityService.ParseHelp("--reasoning-format"),
            runtime, model, DateTime.UnixEpoch);

        Assert.All(capabilities.Observations!, observation => Assert.Equal(runtime.StableId, observation.RuntimeIdentity.StableId));
        Assert.Equal(model.StableId, capabilities.Observations!.Single(item => item.CapabilityId == "reasoning.separate-output").ModelIdentity?.StableId);
    }

    [Fact]
    public async Task V1_capability_cache_entries_still_load()
    {
        using var temp = new TempDir();
        var dataRoot = temp.PathFor("data");
        Directory.CreateDirectory(dataRoot);
        var modelPath = temp.PathFor("model.gguf");
        var executablePath = temp.PathFor("llama-server.exe");
        File.WriteAllText(modelPath, "model fixture");
        File.WriteAllText(executablePath, "runtime fixture");
        var model = new FileInfo(modelPath);
        var executable = new FileInfo(executablePath);
        var expected = LocalModelCapabilityService.Combine(modelPath, null, LocalModelCapabilityService.ParseHelp(null));
        var legacyEntry = new
        {
            ModelPath = Path.GetFullPath(modelPath),
            ModelSize = model.Length,
            ModelMtime = model.LastWriteTimeUtc,
            ExecutablePath = Path.GetFullPath(executablePath),
            ExecutableSize = executable.Length,
            ExecutableMtime = executable.LastWriteTimeUtc,
            Capabilities = expected
        };
        await File.WriteAllTextAsync(
            Path.Combine(dataRoot, "capability-cache.json"),
            JsonSerializer.Serialize(new[] { legacyEntry }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = dataRoot;
        var service = new LocalModelCapabilityService(settings, new RuntimeLogService(settings));

        Assert.NotNull(await service.TryGetCachedAsync(modelPath, executablePath));

        var cached = await service.TryGetCachedAsync(modelPath, executablePath);

        Assert.NotNull(cached);
        Assert.Equal(expected.ProbedAtUtc, cached.ProbedAtUtc);
    }

    [Fact]
    public async Task V2_capability_cache_rejects_changed_runtime_content_even_when_size_and_mtime_match()
    {
        using var temp = new TempDir();
        var dataRoot = temp.PathFor("data");
        Directory.CreateDirectory(dataRoot);
        var modelPath = temp.PathFor("model.gguf");
        var executablePath = temp.PathFor("llama-server.exe");
        File.WriteAllText(modelPath, "model fixture");
        File.WriteAllText(executablePath, "runtime-one");
        var originalMtime = File.GetLastWriteTimeUtc(executablePath);
        var model = new FileInfo(modelPath);
        var executable = new FileInfo(executablePath);
        var runtimeIdentity = await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(executablePath, null);
        var modelIdentity = RuntimeIdentityFactory.CreateModelIdentity(modelPath, null);
        var capabilities = LocalModelCapabilityService.Combine(
            modelPath, null, LocalModelCapabilityService.ParseHelp(null),
            runtimeIdentity, modelIdentity, DateTime.UtcNow);
        var v2Entry = new
        {
            ModelPath = Path.GetFullPath(modelPath),
            ModelSize = model.Length,
            ModelMtime = model.LastWriteTimeUtc,
            ExecutablePath = Path.GetFullPath(executablePath),
            ExecutableSize = executable.Length,
            ExecutableMtime = executable.LastWriteTimeUtc,
            Capabilities = capabilities,
            RuntimeIdentity = runtimeIdentity,
            ModelIdentity = modelIdentity,
            SchemaVersion = 2
        };
        await File.WriteAllTextAsync(
            Path.Combine(dataRoot, "capability-cache.json"),
            JsonSerializer.Serialize(new[] { v2Entry }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        File.WriteAllText(executablePath, "runtime-two");
        File.SetLastWriteTimeUtc(executablePath, originalMtime);
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = dataRoot;
        var service = new LocalModelCapabilityService(settings, new RuntimeLogService(settings));

        var cached = await service.TryGetCachedAsync(modelPath, executablePath);

        Assert.Null(cached);
    }

    [Fact]
    public async Task Missing_executable_probe_is_unknown_instead_of_unavailable()
    {
        using var temp = new TempDir();
        var dataRoot = temp.PathFor("data");
        Directory.CreateDirectory(dataRoot);
        var modelPath = temp.PathFor("model.gguf");
        var executablePath = temp.PathFor("llama-server.exe");
        File.WriteAllText(modelPath, "not gguf");
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = dataRoot;
        var service = new LocalModelCapabilityService(settings, new RuntimeLogService(settings));

        var result = await service.ProbeAsync(modelPath, executablePath);

        Assert.Equal(CapabilityState.Unknown, result.ReasoningOutput.State);
        Assert.Equal(CapabilityState.Unknown, result.EmbeddedMtp.State);
    }

    [Fact]
    public async Task Capability_cache_uses_the_effective_root_across_restart_and_recomposed_siblings()
    {
        using var temp = new TempDir();
        var settingsPath = temp.PathFor("settings/settings.json");
        var oldRoot = temp.PathFor("old-root");
        var newRoot = temp.PathFor("new-root");
        var modelPath = temp.PathFor("model.gguf");
        var executablePath = temp.PathFor("llama-server.exe");
        File.WriteAllText(modelPath, "model fixture");
        File.WriteAllText(executablePath, "runtime fixture");

        var writer = new SettingsService(settingsPath);
        var candidate = writer.Settings.Clone();
        candidate.DataManagement.DataRootDirectory = oldRoot;
        await writer.SaveAsync(candidate);

        var loaded = new SettingsService(settingsPath);
        App.LoadSettingsBeforeComposition(loaded);
        Assert.Equal(Path.GetFullPath(oldRoot), SettingsService.ResolveDataRoot(loaded.Settings));

        var logs = new RuntimeLogService(loaded);
        var capability = new LocalModelCapabilityService(loaded, logs);
        var first = await capability.ProbeWithDriftAsync(modelPath, executablePath, "{}");
        var oldCachePath = Path.Combine(oldRoot, "capability-cache.json");
        Assert.Equal(CapabilityCacheWriteState.Succeeded, first.CacheWrite?.State);
        Assert.True(File.Exists(oldCachePath));
        Assert.NotNull(await capability.TryGetCachedAsync(modelPath, executablePath));

        var changed = loaded.Settings.Clone();
        changed.DataManagement.DataRootDirectory = newRoot;
        await loaded.SaveAsync(changed, oldRoot);

        var newCachePath = Path.Combine(newRoot, "capability-cache.json");
        Assert.Equal(Path.GetFullPath(newRoot), SettingsService.ResolveDataRoot(loaded.Settings));
        Assert.Equal(newCachePath, capability.CapabilityCachePath);
        Assert.True(File.Exists(newCachePath));
        Assert.False(File.Exists(oldCachePath));

        Directory.Delete(newRoot, recursive: true);
        Assert.False(Directory.Exists(newRoot));

        var restarted = new SettingsService(settingsPath);
        App.LoadSettingsBeforeComposition(restarted);
        Assert.Equal(Path.GetFullPath(newRoot), SettingsService.ResolveDataRoot(restarted.Settings));

        var restartedLogs = new RuntimeLogService(restarted);
        var restartedCapability = new LocalModelCapabilityService(restarted, restartedLogs);
        var second = await restartedCapability.ProbeWithDriftAsync(modelPath, executablePath, "{}");
        Assert.Equal(CapabilityCacheWriteState.Succeeded, second.CacheWrite?.State);
        Assert.Equal(newCachePath, restartedCapability.CapabilityCachePath);
        Assert.True(File.Exists(newCachePath));
        Assert.NotNull(await restartedCapability.TryGetCachedAsync(modelPath, executablePath));
        Assert.False(File.Exists(oldCachePath));

        var conversations = new ConversationStore(restarted);
        var memories = new MemoryStore(restarted);
        var rag = new SqliteRagStore(restarted);
        var agent = new FileAgentTaskStateStore(restarted);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();
        await rag.InitializeAsync();
        await agent.InitializeAsync();

        var persistedLogs = new RuntimeLogService(restarted);
        persistedLogs.Add(new RuntimeLogEntry(
            DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Service, "path contract fixture"));
        var manifest = new ModelManifestStore(restarted);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath,
            RepoId = "fixture/repository",
            Source = "manual"
        });
        await new SqliteTraceStore(restarted).AppendAsync(new TraceRecord
        {
            Kind = TraceKind.System,
            Operation = "path-contract-fixture"
        });
        new AppLifecycleJournalService(restarted).RecordStartup();

        Assert.True(File.Exists(Path.Combine(newRoot, "conversations.db")));
        Assert.True(File.Exists(Path.Combine(newRoot, "memories.db")));
        Assert.True(File.Exists(Path.Combine(newRoot, "agent", "task_index.db")));
        Assert.True(File.Exists(Path.Combine(newRoot, "model-manifest.json")));
        Assert.True(File.Exists(Path.Combine(newRoot, "traces.db")));
        Assert.True(File.Exists(Path.Combine(newRoot, "lifecycle.json")));
        Assert.StartsWith(Path.GetFullPath(newRoot), persistedLogs.GetLogFilePath(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capability_cache_reports_an_observed_write_failure_and_settings_rejects_the_root()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var blockedRoot = temp.PathFor("blocked-root");
        File.WriteAllText(blockedRoot, "a file, not a directory");
        var candidate = settings.Settings.Clone();
        candidate.DataManagement.DataRootDirectory = blockedRoot;

        await Assert.ThrowsAnyAsync<IOException>(() => settings.SaveAsync(candidate));

        settings.Settings.DataManagement.DataRootDirectory = blockedRoot;
        var modelPath = temp.PathFor("model.gguf");
        var executablePath = temp.PathFor("llama-server.exe");
        File.WriteAllText(modelPath, "model fixture");
        File.WriteAllText(executablePath, "runtime fixture");
        var logs = new RuntimeLogService(settings);
        var capability = new LocalModelCapabilityService(settings, logs);

        var result = await capability.ProbeWithDriftAsync(modelPath, executablePath, "{}");

        Assert.Equal(CapabilityCacheWriteState.Failed, result.CacheWrite?.State);
        Assert.Equal(Path.Combine(Path.GetFullPath(blockedRoot), "capability-cache.json"), result.CacheWrite?.Path);
        Assert.NotEqual(CapabilityCacheWriteState.Succeeded, result.CacheWrite?.State);
        Assert.Contains(logs.GetEntries(), entry =>
            entry.Message.Contains("Capability cache write failed", StringComparison.Ordinal));
    }

    private static RuntimeCapabilityObservation Observation(string id, CapabilityState state) =>
        RuntimeCapabilityObservation.Create(id, state, "test", "bounded", Runtime(), null, null, DateTime.UnixEpoch);

    private static RuntimeIdentityV2 Runtime() => new(
        "llama.cpp", "runtime-hash", 123, DateTime.UnixEpoch, "b10590", "10590",
        "MSVC", "CUDA", "win-cuda-x64", IdentityCompleteness.Complete);

    private static EmpiricalProfileFingerprintV2 Fingerprint(RuntimeIdentityV2 runtime) => new(
        runtime,
        new ModelIdentityV2("manifest", "hash", null, null, "gemma", "Q4", string.Empty,
            ModelIdentityStrength.VerifiedHash, IdentityCompleteness.Complete),
        new HardwareIdentityV2("Windows", "x64", "CUDA", "GPU", 8, 32, "driver", "single", IdentityCompleteness.Complete),
        Configuration(new Dictionary<string, string>()));

    private static ConfigurationIdentityV2 Configuration(IReadOnlyDictionary<string, string> extras) => new(
        4096, -1, "gpu-all", 8, 8, 1, 512, 128, "f16", "f16", "on",
        string.Empty, string.Empty, string.Empty, 0, extras, IdentityCompleteness.Complete);

    private static GgufModelInfo Gguf(int? nextnPredictLayers) => new(
        "gemma", "Q4_K_M", 32, 8192, 4096, 32, 8, 128, 128,
        NextnPredictLayers: nextnPredictLayers);
}
