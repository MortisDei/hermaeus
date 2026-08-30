using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

public static class LabDefinitionValidator
{
    public const int MaximumCandidates = 16;
    public const int MaximumRepetitions = 20;

    public static void Validate(LabExperimentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.SchemaVersion != 1 || definition.ProtocolVersion < 1 || definition.Revision < 1)
            throw new InvalidOperationException("Lab definition versions must be positive and schema version must be 1.");
        RequireId(definition.Id, "definition");
        RequireId(definition.ProtocolId, "protocol");
        RequireId(definition.TargetServerId, "target server");
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 128)
            throw new InvalidOperationException("Lab experiment name must contain 1 to 128 characters.");
        if (definition.ProfileFingerprint is null)
            throw new InvalidOperationException("An exact v2 profile fingerprint is required.");
        if (definition.Candidates.Count is < 1 or > MaximumCandidates)
            throw new InvalidOperationException($"Lab definitions require 1 to {MaximumCandidates} candidates.");
        if (definition.Repetitions is < 1 or > MaximumRepetitions)
            throw new InvalidOperationException($"Lab repetitions must be between 1 and {MaximumRepetitions}.");
        if (definition.WarmupRepetitions is < 0 or > 5)
            throw new InvalidOperationException("Lab warm-up repetitions must be between 0 and 5.");
        if (definition.TimeoutSeconds is < 1 or > 3600)
            throw new InvalidOperationException("Lab timeout must be between 1 and 3600 seconds.");
        ValidateConfiguration(definition.Baseline);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!ids.Add(definition.Baseline.Id))
            throw new InvalidOperationException("Lab configuration ids must be unique.");
        foreach (var candidate in definition.Candidates)
        {
            ValidateConfiguration(candidate);
            if (!ids.Add(candidate.Id))
                throw new InvalidOperationException("Lab configuration ids must be unique.");
        }
        if (ids.Any(id => !definition.ConfigurationFingerprints.TryGetValue(id, out var fingerprint)
            || string.IsNullOrWhiteSpace(fingerprint)))
            throw new InvalidOperationException("Every Lab configuration requires a frozen v2 configuration fingerprint.");
        if (ids.Any(id => !definition.ConfigurationIdentities.TryGetValue(id, out var identity)
            || definition.ConfigurationFingerprints[id] != identity.StableId))
            throw new InvalidOperationException("Every Lab configuration requires its matching frozen v2 configuration identity.");
        if (definition.PromptHashes.Count > 64 || definition.RequiredMetrics.Count > 32
            || definition.StopConditions.Count > 16 || definition.RequestedCapabilityIds.Count > 32)
            throw new InvalidOperationException("Lab definition collections exceed their bounded limits.");
    }

    public static void ValidateConfiguration(LabConfiguration configuration, string? extraArguments = null)
    {
        RequireId(configuration.Id, "configuration");
        if (string.IsNullOrWhiteSpace(configuration.Label) || configuration.Label.Length > 96)
            throw new InvalidOperationException("Lab configuration labels must contain 1 to 96 characters.");
        if (configuration.ContextSize is < 128 or > 2_097_152)
            throw new InvalidOperationException("Lab context must be between 128 and 2,097,152 tokens.");
        if (configuration.GpuLayers < -1 || configuration.Threads < 0 || configuration.PromptThreads < 0
            || configuration.Slots is < 1 or > 64 || configuration.CpuMoeLayers < -1)
            throw new InvalidOperationException("Lab configuration contains an out-of-range runtime value.");
        if (configuration.SpeculativeTypes.Count > 4
            || configuration.SpeculativeNMax is < 0 or > 128
            || configuration.SpeculativeNMin is < 0 or > 128
            || configuration.SpeculativeNMin > configuration.SpeculativeNMax
            || configuration.SpeculativePMin is < 0 or > 1
            || configuration.SpeculativeDraftGpuLayers is < 0 or > 4096)
            throw new InvalidOperationException("Lab speculative configuration is outside the reviewed bounds.");
        if (configuration.PromptCacheMode is not ("default" or "enabled" or "disabled"))
            throw new InvalidOperationException("Lab prompt-cache mode must be default, enabled, or disabled.");
        var effectiveFlashAttention = EffectiveFlashAttention(configuration.FlashAttention, extraArguments);
        if (KvCacheMath.RequiresRuntimeAdvertisement(configuration.KvCacheTypeV)
            && !string.Equals(effectiveFlashAttention, "on", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Lab configurations with a quantized V cache require Flash Attention to be explicitly on.");
        if (!string.IsNullOrWhiteSpace(configuration.ExtraArgumentsSha256)
            && (configuration.ExtraArgumentsSha256.Length != 64
                || configuration.ExtraArgumentsSha256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidOperationException("Lab extra-argument identity must be an opaque SHA256 value.");
    }

    public static bool IsExplicitFlashAttentionOn(string configuredFlashAttention, string extraArguments)
    {
        var overrideValue = FlashAttentionOverride(extraArguments);
        return string.Equals(configuredFlashAttention, "on", StringComparison.OrdinalIgnoreCase)
            && (overrideValue is null || string.Equals(overrideValue, "on", StringComparison.OrdinalIgnoreCase));
    }

    private static string EffectiveFlashAttention(string configuredFlashAttention, string? extraArguments) =>
        FlashAttentionOverride(extraArguments ?? string.Empty) ?? configuredFlashAttention;

    private static string? FlashAttentionOverride(string extraArguments)
    {
        var tokens = ExtraArgsParser.Split(extraArguments).ToArray();
        string? value = null;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.StartsWith("--flash-attn=", StringComparison.OrdinalIgnoreCase))
                value = token["--flash-attn=".Length..];
            else if (token.StartsWith("-fa=", StringComparison.OrdinalIgnoreCase))
                value = token["-fa=".Length..];
            else if (token.Equals("--flash-attn", StringComparison.OrdinalIgnoreCase)
                || token.Equals("-fa", StringComparison.OrdinalIgnoreCase))
                value = index + 1 < tokens.Length && !tokens[index + 1].StartsWith("-", StringComparison.Ordinal)
                    ? tokens[++index] : string.Empty;
        }

        return value;
    }

    public static void ValidateIsolationArguments(string extraArguments)
    {
        if (extraArguments.Length > 4096)
            throw new InvalidOperationException("Lab extra arguments exceed 4,096 characters.");
        var tokens = ExtraArgsParser.Split(extraArguments);
        if (tokens.Any(IsNetworkOverride))
            throw new InvalidOperationException("Lab extra arguments cannot override the isolated loopback host or temporary port.");
    }

    private static bool IsNetworkOverride(string value) =>
        value.Equals("--host", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("--host=", StringComparison.OrdinalIgnoreCase)
        || value.Equals("--port", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("--port=", StringComparison.OrdinalIgnoreCase)
        || value.Equals("--listen", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("--listen=", StringComparison.OrdinalIgnoreCase);

    private static void RequireId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new InvalidOperationException($"The Lab {label} id must be a safe opaque identifier.");
    }
}

public static class LabObservationValidator
{
    public static void Validate(LabObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.Id) || string.IsNullOrWhiteSpace(observation.RunId)
            || string.IsNullOrWhiteSpace(observation.ConfigurationId) || string.IsNullOrWhiteSpace(observation.CaseId)
            || string.IsNullOrWhiteSpace(observation.MetricId) || string.IsNullOrWhiteSpace(observation.Unit)
            || string.IsNullOrWhiteSpace(observation.Source))
            throw new InvalidOperationException("Lab observations require ids, metric, unit, and source.");
        if (observation.Repetition < 0)
            throw new InvalidOperationException("Lab observation repetition cannot be negative.");
        if (observation.Value is double value && (double.IsNaN(value) || double.IsInfinity(value)))
            throw new InvalidOperationException("Lab observation values must be finite.");
        if (observation.Value.HasValue && !string.IsNullOrWhiteSpace(observation.MissingReason))
            throw new InvalidOperationException("A present Lab observation cannot also have a missing reason.");
        if (!observation.Value.HasValue && string.IsNullOrWhiteSpace(observation.MissingReason))
            throw new InvalidOperationException("A missing Lab observation requires a reason.");
        if (string.IsNullOrWhiteSpace(observation.RuntimeFingerprint)
            || string.IsNullOrWhiteSpace(observation.ModelFingerprint)
            || string.IsNullOrWhiteSpace(observation.HardwareFingerprint)
            || string.IsNullOrWhiteSpace(observation.ConfigurationFingerprint))
            throw new InvalidOperationException("Lab observations require all v2 fingerprint components.");
    }
}

public static class LabConfigurationMapper
{
    public static LabConfiguration FromServer(ServerConfig source, string id, string label) => new()
    {
        Id = id,
        Label = label,
        ContextSize = source.ContextSize,
        GpuLayers = source.GpuLayers,
        Threads = source.Threads,
        PromptThreads = source.PromptThreads,
        Slots = source.Slots,
        KvCacheTypeK = EffectiveKv(source.KvCacheTypeK, source.KvCacheType),
        KvCacheTypeV = EffectiveKv(source.KvCacheTypeV, source.KvCacheType),
        FlashAttention = source.FlashAttention,
        CpuMoeLayers = source.CpuMoeLayers,
        SpeculativeTypes = source.Speculative?.Types.ToArray() ?? [],
        SpeculativeCompanionIdentity = CompanionIdentity(source.Speculative?.DraftModelPath),
        SpeculativeDraftGpuLayers = source.Speculative?.DraftGpuLayers,
        SpeculativeNMax = source.Speculative?.NMax,
        SpeculativeNMin = source.Speculative?.NMin,
        SpeculativePMin = source.Speculative?.PMin,
        PromptCacheMode = "enabled",
        ExtraArgumentsSha256 = string.IsNullOrWhiteSpace(source.ExtraArgs)
            ? string.Empty : LabCanonicalJson.Hash(source.ExtraArgs)
    };

    public static ServerConfig Apply(ServerConfig source, LabConfiguration configuration, int port) => new()
    {
        Id = source.Id,
        Name = $"Lab {source.Name}",
        ExecutablePath = source.ExecutablePath,
        ModelPath = source.ModelPath,
        Port = port,
        ContextSize = configuration.ContextSize,
        GpuLayers = configuration.GpuLayers,
        Threads = configuration.Threads,
        PromptThreads = configuration.PromptThreads,
        Slots = configuration.Slots,
        EmbeddingsMode = source.EmbeddingsMode,
        AutoStart = false,
        ExtraArgs = source.ExtraArgs,
        ReasoningPreserveSupported = source.ReasoningPreserveSupported,
        KvCacheType = configuration.KvCacheTypeK == configuration.KvCacheTypeV ? configuration.KvCacheTypeK : source.KvCacheType,
        KvCacheTypeK = configuration.KvCacheTypeK,
        KvCacheTypeV = configuration.KvCacheTypeV,
        PreserveReasoning = source.PreserveReasoning,
        FlashAttention = configuration.FlashAttention,
        ContextShift = source.ContextShift,
        MemoryLock = source.MemoryLock,
        NoMemoryMap = source.NoMemoryMap,
        CpuMoeLayers = configuration.CpuMoeLayers,
        Speculative = new SpeculativeDecodingConfig
        {
            Types = configuration.SpeculativeTypes.ToList(),
            DraftModelPath = configuration.SpeculativeTypes.Any(type => type.StartsWith("draft-", StringComparison.OrdinalIgnoreCase))
                ? source.Speculative?.DraftModelPath ?? string.Empty : string.Empty,
            DraftGpuLayers = configuration.SpeculativeDraftGpuLayers,
            NMax = configuration.SpeculativeNMax,
            NMin = configuration.SpeculativeNMin,
            PMin = configuration.SpeculativePMin
        },
        MmprojPath = source.MmprojPath,
        UseProjector = source.UseProjector
    };

    public static string Hash(ServerConfig source) =>
        LabCanonicalJson.Hash(LabCanonicalJson.Serialize(FromServer(source, "current", "Current")));

    public static IReadOnlyList<LabApplyChange> Differences(ServerConfig source, LabConfiguration proposed)
    {
        var current = FromServer(source, "current", "Current");
        var changes = new List<LabApplyChange>();
        Add(nameof(ServerConfig.ContextSize), current.ContextSize, proposed.ContextSize);
        Add(nameof(ServerConfig.GpuLayers), current.GpuLayers, proposed.GpuLayers);
        Add(nameof(ServerConfig.Threads), current.Threads, proposed.Threads);
        Add(nameof(ServerConfig.PromptThreads), current.PromptThreads, proposed.PromptThreads);
        Add(nameof(ServerConfig.Slots), current.Slots, proposed.Slots);
        Add(nameof(ServerConfig.KvCacheTypeK), current.KvCacheTypeK, proposed.KvCacheTypeK);
        Add(nameof(ServerConfig.KvCacheTypeV), current.KvCacheTypeV, proposed.KvCacheTypeV);
        Add(nameof(ServerConfig.FlashAttention), current.FlashAttention, proposed.FlashAttention);
        Add(nameof(ServerConfig.CpuMoeLayers), current.CpuMoeLayers, proposed.CpuMoeLayers);
        Add(nameof(SpeculativeDecodingConfig.Types), string.Join(',', current.SpeculativeTypes), string.Join(',', proposed.SpeculativeTypes));
        Add(nameof(SpeculativeDecodingConfig.DraftGpuLayers), current.SpeculativeDraftGpuLayers, proposed.SpeculativeDraftGpuLayers);
        Add(nameof(SpeculativeDecodingConfig.NMax), current.SpeculativeNMax, proposed.SpeculativeNMax);
        Add(nameof(SpeculativeDecodingConfig.NMin), current.SpeculativeNMin, proposed.SpeculativeNMin);
        Add(nameof(SpeculativeDecodingConfig.PMin), current.SpeculativePMin, proposed.SpeculativePMin);
        return changes;

        void Add<T>(string field, T before, T after)
        {
            if (EqualityComparer<T>.Default.Equals(before, after)) return;
            changes.Add(new LabApplyChange(field, Convert.ToString(before, CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(after, CultureInfo.InvariantCulture) ?? string.Empty));
        }
    }

    public static void ApplyTo(ServerConfig target, LabConfiguration proposed)
    {
        target.ContextSize = proposed.ContextSize;
        target.GpuLayers = proposed.GpuLayers;
        target.Threads = proposed.Threads;
        target.PromptThreads = proposed.PromptThreads;
        target.Slots = proposed.Slots;
        target.KvCacheTypeK = proposed.KvCacheTypeK;
        target.KvCacheTypeV = proposed.KvCacheTypeV;
        if (proposed.KvCacheTypeK == proposed.KvCacheTypeV)
            target.KvCacheType = proposed.KvCacheTypeK;
        target.FlashAttention = proposed.FlashAttention;
        target.CpuMoeLayers = proposed.CpuMoeLayers;
        target.Speculative ??= new SpeculativeDecodingConfig();
        target.Speculative.Types = proposed.SpeculativeTypes.ToList();
        target.Speculative.DraftGpuLayers = proposed.SpeculativeDraftGpuLayers;
        target.Speculative.NMax = proposed.SpeculativeNMax;
        target.Speculative.NMin = proposed.SpeculativeNMin;
        target.Speculative.PMin = proposed.SpeculativePMin;
    }

    private static string EffectiveKv(string specific, string shared) =>
        string.IsNullOrWhiteSpace(specific) ? shared : specific;

    private static string CompanionIdentity(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return RuntimeIdentityFactory.CreateModelIdentity(path, GgufMetadataReader.TryRead(path)).StableId;
    }

}

public static class LabCorrectnessEvaluator
{
    public static LabOutputEvidence Capture(
        string configurationId,
        string caseId,
        int repetition,
        string text,
        IReadOnlyList<int>? tokenIds = null) =>
        new(configurationId, caseId, repetition, tokenIds?.Take(4096).ToArray(),
            LabCanonicalJson.Hash(text), text[..Math.Min(text.Length, 512)]);

    public static LabEquivalenceResult Compare(LabOutputEvidence? baseline, LabOutputEvidence? candidate)
    {
        if (baseline is null || candidate is null)
            return Unknown("Baseline or candidate output evidence is missing.");
        if (baseline.TokenIds is not null && candidate.TokenIds is not null)
        {
            var equal = baseline.TokenIds.SequenceEqual(candidate.TokenIds);
            return Result(equal, LabEquivalenceLevel.TokenIds, baseline, candidate,
                equal ? string.Empty : FirstTokenDifference(baseline.TokenIds, candidate.TokenIds));
        }
        if (string.IsNullOrWhiteSpace(baseline.Utf8Sha256) || string.IsNullOrWhiteSpace(candidate.Utf8Sha256))
            return Unknown("The runtime did not expose token ids and an output hash is missing.");
        var same = string.Equals(baseline.Utf8Sha256, candidate.Utf8Sha256, StringComparison.Ordinal);
        return Result(same, LabEquivalenceLevel.ExactUtf8, baseline, candidate,
            same ? string.Empty : "Exact UTF-8 output hashes differ; private output text is omitted.");
    }

    private static LabEquivalenceResult Result(bool equal, LabEquivalenceLevel level, LabOutputEvidence baseline, LabOutputEvidence candidate, string diff) =>
        new(equal ? LabEquivalenceState.Equivalent : LabEquivalenceState.Different, level,
            baseline.Utf8Sha256, candidate.Utf8Sha256,
            equal ? "Outputs are equivalent at the declared comparison level." : "Outputs differ at the declared comparison level.", diff);

    private static LabEquivalenceResult Unknown(string reason) =>
        new(LabEquivalenceState.Unknown, LabEquivalenceLevel.Unknown, string.Empty, string.Empty, reason, string.Empty);

    private static string FirstTokenDifference(IReadOnlyList<int> baseline, IReadOnlyList<int> candidate)
    {
        var length = Math.Min(baseline.Count, candidate.Count);
        for (var index = 0; index < length; index++)
            if (baseline[index] != candidate[index])
                return $"First token difference at index {index}; token values are omitted from export.";
        return $"Token sequence lengths differ ({baseline.Count} versus {candidate.Count}).";
    }
}

public static class LabComparisonEngine
{
    public static LabComparison Compare(
        LabExperimentDefinition definition,
        LabConfiguration candidate,
        IReadOnlyList<LabObservation> observations,
        IReadOnlyList<LabOutputEvidence> outputs)
    {
        var fingerprints = ValidateFingerprints(definition, observations);
        var equivalence = CombineEquivalence(definition.Baseline.Id, candidate.Id, outputs);
        var correctnessPassed = definition.CorrectnessRequirement switch
        {
            LabCorrectnessRequirement.ExactEquivalence => equivalence.State == LabEquivalenceState.Equivalent,
            LabCorrectnessRequirement.Behavioral => equivalence.State == LabEquivalenceState.Equivalent,
            _ => true
        };
        var missingCorrectnessMetric = definition.RequiredMetrics
            .Where(metric => metric == "quality.score")
            .FirstOrDefault(metric => !HasMetric(observations, definition.Baseline.Id, metric)
                || !HasMetric(observations, candidate.Id, metric));
        if (missingCorrectnessMetric is not null)
            correctnessPassed = false;
        var controlled = fingerprints.Count == 0;
        var canHeadline = controlled && correctnessPassed && definition.CorrectnessRequirement != LabCorrectnessRequirement.SpeedOnly;
        return new LabComparison
        {
            BaselineConfigurationId = definition.Baseline.Id,
            CandidateConfigurationId = candidate.Id,
            IsControlled = controlled,
            FingerprintDifferences = fingerprints,
            BaselineMetrics = Summarize(observations.Where(item => item.ConfigurationId == definition.Baseline.Id)),
            CandidateMetrics = Summarize(observations.Where(item => item.ConfigurationId == candidate.Id)),
            Equivalence = equivalence,
            CorrectnessPassed = correctnessPassed,
            CanShowHeadlineDelta = canHeadline,
            RefusalReason = controlled
                ? correctnessPassed ? definition.CorrectnessRequirement == LabCorrectnessRequirement.SpeedOnly ? "Speed-only experiments cannot produce an Apply recommendation." : string.Empty
                    : missingCorrectnessMetric is not null ? $"Required correctness evidence {missingCorrectnessMetric} is missing." : "The declared correctness requirement failed."
                : "Uncontrolled fingerprint differences prevent a headline delta."
        };
    }

    private static bool HasMetric(IEnumerable<LabObservation> observations, string configurationId, string metricId) =>
        observations.Any(item => item.ConfigurationId == configurationId && item.MetricId == metricId && item.Value.HasValue);

    private static IReadOnlyList<string> ValidateFingerprints(LabExperimentDefinition definition, IReadOnlyList<LabObservation> observations)
    {
        var expected = definition.ProfileFingerprint;
        var differences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            if (observation.RuntimeFingerprint != expected.Runtime.StableId) differences.Add("runtime");
            if (observation.ModelFingerprint != expected.Model.StableId) differences.Add("model");
            if (observation.HardwareFingerprint != expected.Hardware.StableId) differences.Add("hardware");
            if (!definition.ConfigurationFingerprints.TryGetValue(observation.ConfigurationId, out var expectedConfiguration)
                || observation.ConfigurationFingerprint != expectedConfiguration)
                differences.Add($"configuration:{observation.ConfigurationId}");
        }
        return differences.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<LabMetricSummary> Summarize(IEnumerable<LabObservation> source) =>
        source.GroupBy(item => (item.MetricId, item.Unit, item.Source))
            .Select(group =>
            {
                var values = group.Where(item => item.Value.HasValue).Select(item => item.Value!.Value).Order().ToArray();
                return new LabMetricSummary(group.Key.MetricId, group.Key.Unit,
                    values.Length == 0 ? null : values.Length % 2 == 1 ? values[values.Length / 2] : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2,
                    values.Length == 0 ? null : values[0], values.Length == 0 ? null : values[^1], values.Length, group.Key.Source);
            }).ToArray();

    private static LabEquivalenceResult CombineEquivalence(string baselineId, string candidateId, IReadOnlyList<LabOutputEvidence> outputs)
    {
        var results = outputs.Where(item => item.ConfigurationId == baselineId)
            .Select(item => LabCorrectnessEvaluator.Compare(item,
                outputs.FirstOrDefault(candidate => candidate.ConfigurationId == candidateId
                    && candidate.CaseId == item.CaseId && candidate.Repetition == item.Repetition)))
            .ToArray();
        if (results.Length == 0 || results.Any(item => item.State == LabEquivalenceState.Unknown))
            return results.FirstOrDefault(item => item.State == LabEquivalenceState.Unknown)
                ?? new LabEquivalenceResult(LabEquivalenceState.Unknown, LabEquivalenceLevel.Unknown, string.Empty, string.Empty, "No paired outputs were observed.", string.Empty);
        return results.FirstOrDefault(item => item.State == LabEquivalenceState.Different)
            ?? results[0];
    }
}

public sealed class LabExperimentService : ILabExperimentService, IAsyncDisposable
{
    private readonly ISettingsService _settings;
    private readonly ISystemInfoService _systemInfo;
    private readonly IEmpiricalExperienceStore _experience;
    private readonly ILabRuntimeHost _runtimeHost;
    private readonly ModelManifestStore? _manifest;
    private readonly ConcurrentDictionary<string, RunState> _runs = new(StringComparer.Ordinal);
    private string? _activeRunId;

    public LabExperimentService(ISettingsService settings, ISystemInfoService systemInfo,
        IEmpiricalExperienceStore experience, ILabRuntimeHost runtimeHost, ModelManifestStore? manifest = null)
    {
        _settings = settings;
        _systemInfo = systemInfo;
        _experience = experience;
        _runtimeHost = runtimeHost;
        _manifest = manifest;
    }

    public async Task<LabExperimentDefinition> CreateDefinitionAsync(string name, string protocolId,
        ServerConfig source, LabConfiguration baseline, IReadOnlyList<LabConfiguration> candidates,
        int repetitions, LabCorrectnessRequirement correctness, CancellationToken ct = default)
    {
        var profile = await CreateFingerprintAsync(source, baseline, ct);
        LabDefinitionValidator.ValidateIsolationArguments(source.ExtraArgs);
        LabDefinitionValidator.ValidateConfiguration(baseline, source.ExtraArgs);
        foreach (var candidate in candidates)
            LabDefinitionValidator.ValidateConfiguration(candidate, source.ExtraArgs);
        var configurationIdentities = candidates.Append(baseline)
            .ToDictionary(item => item.Id, item => CreateConfigurationIdentity(source, item), StringComparer.Ordinal);
        var configurationFingerprints = configurationIdentities
            .ToDictionary(item => item.Key, item => item.Value.StableId, StringComparer.Ordinal);
        var definition = new LabExperimentDefinition
        {
            Name = name.Trim(), ProtocolId = protocolId.Trim(), TargetServerId = source.Id,
            ProfileFingerprint = profile, Baseline = baseline,
            ConfigurationFingerprints = configurationFingerprints,
            ConfigurationIdentities = configurationIdentities,
            Candidates = candidates.ToArray(), WorkloadId = "lab-shell-baseline",
            Repetitions = repetitions, RequiredMetrics = ["runtime.ready", "process.ram.current"],
            CorrectnessRequirement = correctness
        };
        LabDefinitionValidator.Validate(definition);
        return definition;
    }

    public async Task<LabRunSnapshot> StartAsync(LabExperimentDefinition definition, ServerConfig source, CancellationToken ct = default)
    {
        LabDefinitionValidator.Validate(definition);
        definition = JsonSerializer.Deserialize<LabExperimentDefinition>(definition.CanonicalJson(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The Lab definition could not be frozen.");
        LabDefinitionValidator.Validate(definition);
        if (!string.Equals(definition.TargetServerId, source.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected server no longer matches the frozen Lab definition.");
        if (LabConfigurationMapper.Differences(source, definition.Baseline).Count != 0)
            throw new InvalidOperationException("The saved Services configuration changed after the Lab definition was frozen.");
        LabDefinitionValidator.ValidateIsolationArguments(source.ExtraArgs);
        LabDefinitionValidator.ValidateConfiguration(definition.Baseline, source.ExtraArgs);
        foreach (var candidate in definition.Candidates)
            LabDefinitionValidator.ValidateConfiguration(candidate, source.ExtraArgs);
        var current = await CreateFingerprintAsync(source, definition.Baseline, ct);
        if (!definition.ProfileFingerprint.IsExactlyCompatibleWith(current))
            throw new InvalidOperationException("The runtime, model, hardware, or baseline configuration changed after the Lab definition was frozen.");

        var snapshot = new LabRunSnapshot
        {
            DefinitionHash = definition.DefinitionHash, Definition = definition,
            Status = LabRunStatus.Starting, StartedAtUtc = DateTime.UtcNow
        };
        ClaimActiveRun(snapshot.Id);
        RunState? state = null;
        try
        {
            var startEvidence = await PersistAsync(snapshot, "lab-run-started", NormalizedOutcome.Unknown,
                "The immutable Lab definition was frozen before temporary runtime launch.", [], ct);
            snapshot = snapshot with { StartEvidenceId = startEvidence.Id };
            state = new RunState(snapshot);
            if (!_runs.TryAdd(snapshot.Id, state))
                throw new InvalidOperationException("The Lab run id is already active.");
        }
        catch
        {
            ReleaseActiveRun(snapshot.Id);
            throw;
        }

        try
        {
            state!.Session = await _runtimeHost.StartAsync(snapshot.Id, source, definition.Baseline, ct);
            state.Snapshot = snapshot with
            {
                Status = LabRunStatus.Running,
                TemporaryPort = state.Session.Port,
                RuntimeOwnershipId = state.Session.OwnershipId,
                RuntimeProcessId = state.Session.Process?.ProcessId,
                RuntimeProcessStartedAtUtc = state.Session.Process?.StartedAtUtc
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return await CancelAsync(snapshot.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            state!.Snapshot = snapshot with { Status = LabRunStatus.Failed, Failures = [ex.Message], CompletedAtUtc = DateTime.UtcNow };
            try
            {
                await DisposeSessionAsync(state, CancellationToken.None);
                var evidence = await PersistAsync(state.Snapshot, "lab-runtime-launch-failed", NormalizedOutcome.Failed,
                    "The isolated Lab runtime did not start.", [], CancellationToken.None);
                state.Snapshot = state.Snapshot with { CompletionEvidenceId = evidence.Id };
            }
            finally
            {
                ReleaseActiveRun(snapshot.Id);
            }
        }
        return state!.Snapshot;
    }

    public async Task<LabRunSnapshot> SwitchConfigurationAsync(string runId, ServerConfig source,
        string configurationId, CancellationToken ct = default)
    {
        var state = GetActive(runId);
        if (state.Snapshot.Status != LabRunStatus.Running)
            throw new InvalidOperationException("Only a running Lab experiment can switch configurations.");
        if (state.Snapshot.Definition.TargetServerId != source.Id
            || LabConfigurationMapper.Differences(source, state.Snapshot.Definition.Baseline).Count != 0)
            throw new InvalidOperationException("The Services source changed after the Lab run started.");
        var configuration = state.Snapshot.Definition.Candidates.Append(state.Snapshot.Definition.Baseline)
            .FirstOrDefault(item => item.Id == configurationId)
            ?? throw new KeyNotFoundException("The requested Lab configuration does not exist.");
        LabDefinitionValidator.ValidateIsolationArguments(source.ExtraArgs);
        await DisposeSessionAsync(state, ct);
        try
        {
            state.Session = await _runtimeHost.StartAsync(runId, source, configuration, ct);
        }
        catch (Exception ex)
        {
            var failure = $"{configuration.Id}: isolated runtime launch failed: {ex.Message}";
            state.Snapshot = state.Snapshot with
            {
                Status = LabRunStatus.Failed,
                CompletedAtUtc = DateTime.UtcNow,
                Failures = state.Snapshot.Failures.Append(failure).Take(32).ToArray(),
                TemporaryPort = null,
                RuntimeOwnershipId = string.Empty,
                RuntimeProcessId = null,
                RuntimeProcessStartedAtUtc = null
            };
            ReleaseActiveRun(runId);
            throw;
        }
        state.Snapshot = state.Snapshot with
        {
            TemporaryPort = state.Session.Port,
            RuntimeOwnershipId = state.Session.OwnershipId,
            RuntimeProcessId = state.Session.Process?.ProcessId,
            RuntimeProcessStartedAtUtc = state.Session.Process?.StartedAtUtc
        };
        return state.Snapshot;
    }

    public async Task<LabRunSnapshot> CompleteAsync(string runId, IReadOnlyList<LabObservation> observations,
        IReadOnlyList<LabOutputEvidence> outputs, IReadOnlyList<string>? failures = null, CancellationToken ct = default)
    {
        var state = GetActive(runId);
        var definition = state.Snapshot.Definition;
        var validIds = definition.Candidates.Select(item => item.Id).Append(definition.Baseline.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            LabObservationValidator.Validate(observation);
            if (observation.RunId != runId || !validIds.Contains(observation.ConfigurationId))
                throw new InvalidOperationException("A Lab observation does not belong to this frozen run.");
        }
        var safeOutputs = outputs.Select(item => item with { TokenIds = null, BoundedText = string.Empty }).ToArray();
        var comparisons = definition.Candidates.Select(candidate => LabComparisonEngine.Compare(definition, candidate, observations, outputs)).ToArray();
        var boundedFailures = (failures ?? []).Take(32).Select(value => value[..Math.Min(value.Length, 512)]).ToArray();
        var status = boundedFailures.Length == 0 ? LabRunStatus.Succeeded
            : observations.Count > 0 ? LabRunStatus.PartiallySucceeded : LabRunStatus.Failed;
        state.Snapshot = state.Snapshot with
        {
            Status = status, CompletedAtUtc = DateTime.UtcNow,
            Observations = observations.ToArray(), Outputs = safeOutputs,
            Comparisons = comparisons, Failures = boundedFailures
        };
        await DisposeSessionAsync(state, ct);
        var outcome = status == LabRunStatus.Succeeded ? NormalizedOutcome.Succeeded
            : status == LabRunStatus.PartiallySucceeded ? NormalizedOutcome.PartiallySucceeded : NormalizedOutcome.Failed;
        try
        {
            var evidence = await PersistCompletionAsync(state.Snapshot, outcome, ct);
            state.Snapshot = state.Snapshot with { CompletionEvidenceId = evidence.Id };
            return state.Snapshot;
        }
        finally
        {
            ReleaseActiveRun(runId);
        }
    }

    public async Task<LabRunSnapshot> CancelAsync(string runId, CancellationToken ct = default)
    {
        var state = GetActive(runId);
        if (state.Snapshot.Status is LabRunStatus.Succeeded or LabRunStatus.PartiallySucceeded or LabRunStatus.Cancelled or LabRunStatus.Failed)
            return state.Snapshot;
        await DisposeSessionAsync(state, ct);
        var status = state.Snapshot.Observations.Count == 0 ? LabRunStatus.Cancelled : LabRunStatus.PartiallySucceeded;
        state.Snapshot = state.Snapshot with { Status = status, CompletedAtUtc = DateTime.UtcNow };
        try
        {
            var evidence = await PersistAsync(state.Snapshot, "lab-run-cancelled",
                status == LabRunStatus.Cancelled ? NormalizedOutcome.Cancelled : NormalizedOutcome.PartiallySucceeded,
                "The Lab run stopped at an owned runtime boundary and retained completed evidence.", [state.Snapshot.StartEvidenceId], ct);
            state.Snapshot = state.Snapshot with { CompletionEvidenceId = evidence.Id };
            return state.Snapshot;
        }
        finally
        {
            ReleaseActiveRun(runId);
        }
    }

    public LabRunSnapshot? GetRun(string runId) => _runs.TryGetValue(runId, out var state) ? state.Snapshot : null;

    public LabApplyReview CreateApplyReview(string runId, string candidateId)
    {
        var run = GetRun(runId) ?? throw new KeyNotFoundException("The Lab run is not available in this session.");
        var candidate = run.Definition.Candidates.FirstOrDefault(item => item.Id == candidateId)
            ?? throw new KeyNotFoundException("The Lab candidate does not exist.");
        var server = _settings.Settings.ManagedServers.FirstOrDefault(item => item.Id == run.Definition.TargetServerId)
            ?? throw new InvalidOperationException("The target Services configuration no longer exists.");
        var comparison = run.Comparisons.FirstOrDefault(item => item.CandidateConfigurationId == candidateId);
        var changes = LabConfigurationMapper.Differences(server, candidate);
        var refusal = run.Status is not (LabRunStatus.Succeeded or LabRunStatus.PartiallySucceeded)
            ? "Only a completed Lab run can produce an Apply review."
            : comparison is null || !comparison.CanShowHeadlineDelta
                ? comparison?.RefusalReason ?? "The candidate has no controlled comparison."
                : changes.Count == 0 ? "The candidate does not change any persisted Services field." : string.Empty;
        return new LabApplyReview
        {
            RunId = run.Id, DefinitionHash = run.DefinitionHash, TargetServerId = server.Id,
            CandidateConfigurationId = candidateId, ExpectedCurrentConfigurationHash = LabConfigurationMapper.Hash(server),
            ExpectedRuntimeFingerprint = run.Definition.ProfileFingerprint.Runtime.StableId,
            ExpectedModelFingerprint = run.Definition.ProfileFingerprint.Model.StableId,
            Changes = changes, CanApply = string.IsNullOrEmpty(refusal), RefusalReason = refusal
        };
    }

    public async Task ApplyAsync(LabApplyReview review, CancellationToken ct = default)
    {
        if (!review.CanApply) throw new InvalidOperationException(review.RefusalReason);
        var run = GetRun(review.RunId) ?? throw new InvalidOperationException("The reviewed Lab run is no longer available.");
        if (review.DefinitionHash != run.DefinitionHash)
            throw new InvalidOperationException("The Lab definition changed after review.");
        var current = _settings.Settings.ManagedServers.FirstOrDefault(item => item.Id == review.TargetServerId)
            ?? throw new InvalidOperationException("The reviewed Services configuration no longer exists.");
        if (LabConfigurationMapper.Hash(current) != review.ExpectedCurrentConfigurationHash)
            throw new InvalidOperationException("The Services configuration changed after Apply review. Review the changes again.");
        var identity = await CreateFingerprintAsync(current, LabConfigurationMapper.FromServer(current, "current", "Current"), ct);
        if (identity.Runtime.StableId != review.ExpectedRuntimeFingerprint || identity.Model.StableId != review.ExpectedModelFingerprint)
            throw new InvalidOperationException("The runtime or model identity changed after the experiment.");

        var clone = _settings.Settings.Clone();
        var target = clone.ManagedServers.First(item => item.Id == review.TargetServerId);
        var candidate = run.Definition.Candidates.First(item => item.Id == review.CandidateConfigurationId);
        LabConfigurationMapper.ApplyTo(target, candidate);
        await _settings.SaveAsync(clone);
        await PersistApplyAsync(run, review, ct);
    }

    private async Task<EmpiricalProfileFingerprintV2> CreateFingerprintAsync(ServerConfig source, LabConfiguration configuration, CancellationToken ct)
    {
        var runtime = await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(source.ExecutablePath, null, ct);
        var gguf = GgufMetadataReader.TryRead(source.ModelPath);
        var manifest = _manifest is null ? null : await _manifest.FindAsync(source.ModelPath, ct);
        var verifiedHash = string.IsNullOrWhiteSpace(manifest?.Sha256) ? null : manifest.Sha256;
        var model = RuntimeIdentityFactory.CreateModelIdentity(source.ModelPath, gguf,
            verifiedHash, verifiedHash is null ? null : string.Join(':', manifest!.RepoId, manifest.RevisionSha, manifest.RepoFile));
        var hardwareProfile = await _systemInfo.GetHardwareProfileAsync(ct);
        var hardware = new HardwareIdentityV2(
            RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(), string.Empty,
            hardwareProfile.GpuName ?? string.Empty,
            hardwareProfile.MaxGpuVramBytes > 0 ? hardwareProfile.MaxGpuVramBytes : null,
            hardwareProfile.TotalRamBytes > 0 ? hardwareProfile.TotalRamBytes : null,
            string.Empty, "unknown", IdentityCompleteness.Incomplete);
        var config = CreateConfigurationIdentity(source, configuration);
        return new EmpiricalProfileFingerprintV2(runtime, model, hardware, config);
    }

    private static ConfigurationIdentityV2 CreateConfigurationIdentity(ServerConfig source, LabConfiguration configuration)
    {
        var parsed = string.IsNullOrWhiteSpace(configuration.ExtraArgumentsSha256)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["extraArgumentsSha256"] = configuration.ExtraArgumentsSha256
            };
        if (configuration.PromptCacheMode != "default")
            parsed["promptCacheMode"] = configuration.PromptCacheMode;
        return new ConfigurationIdentityV2(configuration.ContextSize, configuration.GpuLayers,
            configuration.GpuLayers switch { 0 => "cpu", -1 => "gpu-all", _ => "gpu-partial" },
            configuration.Threads, configuration.PromptThreads, configuration.Slots, null, null,
            configuration.KvCacheTypeK, configuration.KvCacheTypeV, configuration.FlashAttention,
            string.Join(',', configuration.SpeculativeTypes), configuration.SpeculativeCompanionIdentity,
            $"nmax={configuration.SpeculativeNMax?.ToString(CultureInfo.InvariantCulture) ?? string.Empty};" +
            $"nmin={configuration.SpeculativeNMin?.ToString(CultureInfo.InvariantCulture) ?? string.Empty};" +
            $"pmin={configuration.SpeculativePMin?.ToString(CultureInfo.InvariantCulture) ?? string.Empty};" +
            $"ngld={configuration.SpeculativeDraftGpuLayers?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}",
            configuration.CpuMoeLayers, parsed,
            string.IsNullOrWhiteSpace(configuration.ExtraArgumentsSha256) ? IdentityCompleteness.Complete : IdentityCompleteness.Incomplete);
    }

    private async Task<EmpiricalExperience> PersistAsync(LabRunSnapshot run, string code, NormalizedOutcome outcome,
        string detail, IReadOnlyList<string> priorEvidence, CancellationToken ct,
        EvidenceOrigin origin = EvidenceOrigin.DirectObservation)
    {
        var provenance = priorEvidence.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => new EmpiricalExperienceProvenance(value,
                new SourceReference(ProvenanceKind.Lab, "Prior immutable Lab evidence", value, EvidenceOrigin: EvidenceOrigin.Extracted)))
            .ToList();
        provenance.Add(new EmpiricalExperienceProvenance($"lab:{run.Id}:{code}",
            new SourceReference(ProvenanceKind.Lab, "Lab experiment protocol", run.Definition.ProtocolId,
                EvidenceOrigin: origin)));
        return await _experience.AddAsync(new EmpiricalExperienceDraft
        {
            Domain = EmpiricalExperienceDomains.LabRun,
            ContextJson = run.Definition.CanonicalJson(),
            ActionJson = LabCanonicalJson.Serialize(run),
            RuntimeFingerprint = run.Definition.ProfileFingerprint.Runtime.StableId,
            ModelFingerprint = run.Definition.ProfileFingerprint.Model.StableId,
            Provenance = provenance,
            Outcome = NormalizedToolOutcome.Create(outcome, code, detail)
        }, ct);
    }

    private async Task<EmpiricalExperience> PersistCompletionAsync(
        LabRunSnapshot run, NormalizedOutcome outcome, CancellationToken ct)
    {
        var drafts = new List<EmpiricalExperienceDraft>();
        var sliceIds = new List<string>();
        var configurationIds = run.Definition.Candidates.Select(item => item.Id)
            .Prepend(run.Definition.Baseline.Id).ToArray();
        foreach (var configurationId in configurationIds)
        {
            var observations = run.Observations.Where(item => item.ConfigurationId == configurationId).ToArray();
            var outputs = run.Outputs.Where(item => item.ConfigurationId == configurationId).ToArray();
            var chunkIndex = 0;
            foreach (var slice in SplitEvidenceSlices(run.Id, run.DefinitionHash, configurationId, observations, outputs))
            {
                var chunk = slice with { ChunkIndex = chunkIndex++ };
                var sliceId = Guid.NewGuid().ToString("N");
                drafts.Add(new EmpiricalExperienceDraft
                {
                    Id = sliceId,
                    Domain = EmpiricalExperienceDomains.LabRun,
                    ContextJson = LabCanonicalJson.Serialize(new { runId = run.Id, run.DefinitionHash, configurationId, chunk.ChunkIndex }),
                    ActionJson = ExperienceJson.Canonicalize(chunk),
                    RuntimeFingerprint = run.Definition.ProfileFingerprint.Runtime.StableId,
                    ModelFingerprint = run.Definition.ProfileFingerprint.Model.StableId,
                    Provenance =
                    [
                        new EmpiricalExperienceProvenance(run.StartEvidenceId,
                            new SourceReference(ProvenanceKind.Lab, "Frozen Lab definition", run.StartEvidenceId,
                                EvidenceOrigin: EvidenceOrigin.Extracted))
                    ],
                    Outcome = NormalizedToolOutcome.Create(
                        chunk.Observations.Count > 0 ? NormalizedOutcome.Succeeded : NormalizedOutcome.Unknown,
                        "lab-run-evidence-slice",
                        $"Immutable {configurationId} evidence chunk {chunk.ChunkIndex} contains {chunk.Observations.Count} observations and {chunk.Outputs.Count} output hashes.")
                });
                sliceIds.Add(sliceId);
            }
        }

        var decisions = run.Comparisons.Select(comparison => new LabComparisonDecision(
            comparison.BaselineConfigurationId, comparison.CandidateConfigurationId,
            comparison.IsControlled, comparison.FingerprintDifferences, comparison.Equivalence,
            comparison.CorrectnessPassed, comparison.CanShowHeadlineDelta, comparison.RefusalReason)).ToArray();
        var summary = new LabRunCompletionSummary(run.Id, run.DefinitionHash, run.Status,
            run.StartedAtUtc, run.CompletedAtUtc, run.Failures, decisions, sliceIds,
            run.Definition.Candidates.Prepend(run.Definition.Baseline).ToArray(), run.Comparisons,
            run.Definition.Name, DescribeModelIdentity(run.Definition.ProfileFingerprint.Model));
        drafts.Add(new EmpiricalExperienceDraft
        {
            Id = Guid.NewGuid().ToString("N"),
            Domain = EmpiricalExperienceDomains.LabRun,
            ContextJson = LabCanonicalJson.Serialize(new { runId = run.Id, run.DefinitionHash }),
            ActionJson = LabCanonicalJson.Serialize(summary),
            RuntimeFingerprint = run.Definition.ProfileFingerprint.Runtime.StableId,
            ModelFingerprint = run.Definition.ProfileFingerprint.Model.StableId,
            Provenance = sliceIds.Select(id => new EmpiricalExperienceProvenance(id,
                new SourceReference(ProvenanceKind.Lab, "Immutable configuration evidence", id,
                    EvidenceOrigin: EvidenceOrigin.Extracted))).ToArray(),
            Outcome = NormalizedToolOutcome.Create(outcome, "lab-run-completed",
                $"Lab run completed as {run.Status} with {run.Observations.Count} observations across {sliceIds.Count} configuration slices.")
        });
        var saved = await _experience.AddBatchAsync(drafts, ct);
        return saved[^1];
    }

    private static string? DescribeModelIdentity(ModelIdentityV2 model)
    {
        if (!string.IsNullOrWhiteSpace(model.ManifestIdentity))
            return model.ManifestIdentity;

        var descriptor = string.Join(" · ", new[] { model.Architecture, model.Quantization }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(descriptor) ? null : descriptor;
    }

    private static IEnumerable<LabRunEvidenceSlice> SplitEvidenceSlices(
        string runId,
        string definitionHash,
        string configurationId,
        IReadOnlyList<LabObservation> observations,
        IReadOnlyList<LabOutputEvidence> outputs)
    {
        if (observations.Count == 0)
        {
            yield return new LabRunEvidenceSlice(runId, definitionHash, configurationId, [], outputs);
            yield break;
        }

        var current = new List<LabObservation>();
        var pendingOutputs = outputs;
        foreach (var observation in observations)
        {
            var candidate = new LabRunEvidenceSlice(runId, definitionHash, configurationId,
                [.. current, observation], pendingOutputs);
            if (current.Count > 0 && !FitsExperienceDocument(candidate))
            {
                yield return new LabRunEvidenceSlice(runId, definitionHash, configurationId, current.ToArray(), pendingOutputs);
                current.Clear();
                pendingOutputs = [];
                candidate = new LabRunEvidenceSlice(runId, definitionHash, configurationId, [observation], pendingOutputs);
            }

            if (!FitsExperienceDocument(candidate))
                throw new InvalidOperationException("A single Lab observation exceeds the evidence document limit.");
            current.Add(observation);
        }

        if (current.Count > 0)
            yield return new LabRunEvidenceSlice(runId, definitionHash, configurationId, current.ToArray(), pendingOutputs);
    }

    private static bool FitsExperienceDocument(LabRunEvidenceSlice slice)
    {
        try
        {
            _ = ExperienceJson.Canonicalize(slice);
            return true;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("exceeds", StringComparison.Ordinal))
        {
            return false;
        }
    }

    private Task<EmpiricalExperience> PersistApplyAsync(LabRunSnapshot run, LabApplyReview review, CancellationToken ct) =>
        _experience.AddAsync(new EmpiricalExperienceDraft
        {
            Domain = EmpiricalExperienceDomains.LabRun,
            ContextJson = LabCanonicalJson.Serialize(new { runId = run.Id, run.DefinitionHash, review.ReviewId }),
            ActionJson = LabCanonicalJson.Serialize(new LabApplyEvidence(
                run.Id, run.DefinitionHash, review.ReviewId, review.TargetServerId,
                review.CandidateConfigurationId, review.Changes)),
            RuntimeFingerprint = run.Definition.ProfileFingerprint.Runtime.StableId,
            ModelFingerprint = run.Definition.ProfileFingerprint.Model.StableId,
            Provenance =
            [
                new EmpiricalExperienceProvenance(run.CompletionEvidenceId,
                    new SourceReference(ProvenanceKind.Lab, "Completed Lab comparison", run.CompletionEvidenceId,
                        EvidenceOrigin: EvidenceOrigin.Extracted))
            ],
            Outcome = NormalizedToolOutcome.Create(NormalizedOutcome.Succeeded, "lab-apply-confirmed",
                $"User applied {review.Changes.Count} reviewed Services field changes.")
        }, ct);

    private RunState GetActive(string runId) => _runs.TryGetValue(runId, out var state)
        ? state : throw new KeyNotFoundException("The Lab run is not available in this session.");

    private void ClaimActiveRun(string runId)
    {
        if (Interlocked.CompareExchange(ref _activeRunId, runId, null) is not null)
            throw new InvalidOperationException("Another Lab run is already active. Complete or cancel it before starting another.");
    }

    private void ReleaseActiveRun(string runId) =>
        Interlocked.CompareExchange(ref _activeRunId, null, runId);

    private static async Task DisposeSessionAsync(RunState state, CancellationToken ct)
    {
        if (state.Session is null) return;
        await state.Session.StopAsync(ct);
        await state.Session.DisposeAsync();
        state.Session = null;
    }

    private sealed class RunState(LabRunSnapshot snapshot)
    {
        public LabRunSnapshot Snapshot { get; set; } = snapshot;
        public ILabRuntimeSession? Session { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var state in _runs.Values)
        {
            try { await DisposeSessionAsync(state, CancellationToken.None); }
            catch { }
            finally { ReleaseActiveRun(state.Snapshot.Id); }
        }
        _runs.Clear();
    }
}
