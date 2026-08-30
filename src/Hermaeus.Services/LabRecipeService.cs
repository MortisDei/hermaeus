using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public static class LabRecipeCatalog
{
    private static readonly int[] ContextLadder = [2048, 4096, 8192, 16384, 32768, 65536, 131072];
    private static readonly string[] ReviewedKvTypes = ["f16", "q8_0", "q4_0"];

    public static LabRecipePlan Build(LabRecipeKind kind, ServerConfig source,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities, GgufModelInfo? gguf = null,
        GgufModelInfo? draftGguf = null, ModelIdentityV2? targetIdentity = null,
        ModelIdentityV2? draftIdentity = null)
    {
        var baseline = LabConfigurationMapper.FromServer(source, "baseline", "Baseline");
        if (draftIdentity is not null && baseline.SpeculativeTypes.Count > 0)
            baseline = baseline with { SpeculativeCompanionIdentity = draftIdentity.StableId };
        var plan = kind switch
        {
            LabRecipeKind.EngineProfile => EngineProfile(baseline, gguf),
            LabRecipeKind.Context => Context(baseline),
            LabRecipeKind.KvCache => Kv(baseline, source, capabilities),
            LabRecipeKind.FlashAttention => Flash(baseline, capabilities),
            LabRecipeKind.CpuMoePlacement => CpuMoe(baseline, capabilities, gguf),
            LabRecipeKind.ExternalDraft => ExternalDraft(source, baseline, capabilities, gguf, draftGguf, targetIdentity, draftIdentity),
            LabRecipeKind.Eagle3 => Eagle3(source, baseline, capabilities, gguf, draftGguf, targetIdentity, draftIdentity),
            LabRecipeKind.SpeculativeDraftMaximum => SpeculativeTuning(source, baseline, capabilities,
                kind, "runtime.speculative.parameter.n-max", gguf, draftGguf, targetIdentity, draftIdentity),
            LabRecipeKind.SpeculativeDraftMinimum => SpeculativeTuning(source, baseline, capabilities,
                kind, "runtime.speculative.parameter.n-min", gguf, draftGguf, targetIdentity, draftIdentity),
            LabRecipeKind.SpeculativeProbabilityMinimum => SpeculativeTuning(source, baseline, capabilities,
                kind, "runtime.speculative.parameter.p-min", gguf, draftGguf, targetIdentity, draftIdentity),
            LabRecipeKind.SpeculativeDraftGpuLayers => SpeculativeTuning(source, baseline, capabilities,
                kind, "runtime.speculative.parameter.draft-gpu-layers", gguf, draftGguf, targetIdentity, draftIdentity),
            LabRecipeKind.PromptPrefixReuse => PromptPrefixReuse(baseline),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Validate(plan);
        return plan;
    }

    public static void Validate(LabRecipePlan plan)
    {
        if (plan.MaximumRunCount is < 2 or > 8 || plan.Candidates.Count is < 1 or > 7
            || plan.Candidates.Count + 1 > plan.MaximumRunCount)
            throw new InvalidOperationException("Lab recipes require a bounded baseline-plus-candidate run count between 2 and 8.");
        var ids = plan.Candidates.Select(item => item.Id).Append(plan.Baseline.Id).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException("Lab recipe configuration ids must be unique.");
        foreach (var candidate in plan.Candidates)
        {
            var differences = Differences(plan.Baseline, candidate);
            if (differences.Count == 0)
                throw new InvalidOperationException("A Lab recipe candidate must differ from its baseline.");
            if (!plan.TestsInteraction && differences.Except(AllowedFields(plan.Kind), StringComparer.Ordinal).Any())
                throw new InvalidOperationException("A one-at-a-time Lab recipe changed a field outside its declared dimension.");
        }
    }

    private static LabRecipePlan EngineProfile(LabConfiguration baseline, GgufModelInfo? gguf)
    {
        var layers = new List<int> { 0, -1 };
        if (gguf?.BlockCount is > 2) layers.Insert(1, gguf.BlockCount.Value / 2);
        var candidates = layers.Distinct().Where(value => value != baseline.GpuLayers)
            .Select((value, index) => baseline with { Id = $"gpu-{index + 1}", Label = value switch { 0 => "CPU", -1 => "All GPU layers", _ => $"{value} GPU layers" }, GpuLayers = value })
            .Take(3).ToArray();
        return Plan("engine-profile-v1", "GPU layer placement", LabRecipeKind.EngineProfile,
            CapabilityState.Available, "GPU layer placement is a first-class managed runtime setting.", baseline, candidates, []);
    }

    private static LabRecipePlan Context(LabConfiguration baseline)
    {
        var candidates = ContextLadder.OrderBy(value => Math.Abs((long)value - baseline.ContextSize))
            .Where(value => value != baseline.ContextSize).Take(3)
            .Order().Select((value, index) => baseline with
            {
                Id = $"context-{index + 1}", Label = $"Context {value:N0}", ContextSize = value
            }).ToArray();
        return Plan("context-v1", "Context size", LabRecipeKind.Context, CapabilityState.Available,
            "Context candidates come from the reviewed bounded ladder.", baseline, candidates, []);
    }

    private static LabRecipePlan Kv(LabConfiguration baseline, ServerConfig source,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities)
    {
        var available = ReviewedKvTypes.Where(type => HasAvailable(capabilities, $"runtime.kv.type.{type}"))
            .ToArray();
        var baselineAvailable = HasAvailable(capabilities, $"runtime.kv.type.{baseline.KvCacheTypeK.ToLowerInvariant()}")
            && HasAvailable(capabilities, $"runtime.kv.type.{baseline.KvCacheTypeV.ToLowerInvariant()}");
        var candidates = available.Where(type => type != baseline.KvCacheTypeK && type != baseline.KvCacheTypeV)
            .Select((type, index) => baseline with
            {
                Id = $"kv-{index + 1}", Label = $"KV {type}", KvCacheTypeK = type, KvCacheTypeV = type
            }).Take(3).ToArray();
        var hasQuantizedValue = KvCacheMath.RequiresRuntimeAdvertisement(baseline.KvCacheTypeV)
            || candidates.Any(candidate => KvCacheMath.RequiresRuntimeAdvertisement(candidate.KvCacheTypeV));
        var flashAttentionAvailable = HasAvailable(capabilities, "runtime.flash-attention");
        var flashAttentionExplicit = LabDefinitionValidator.IsExplicitFlashAttentionOn(baseline.FlashAttention, source.ExtraArgs);
        var state = baselineAvailable && candidates.Length > 0
            && (!hasQuantizedValue || (flashAttentionAvailable && flashAttentionExplicit))
            ? CapabilityState.Available : CapabilityState.Unknown;
        var detail = state == CapabilityState.Available
            ? "Every offered KV representation was advertised by this exact runtime."
            : hasQuantizedValue && !flashAttentionExplicit
                ? "Quantized V-cache configurations require Flash Attention to be explicitly on; auto is not treated as a runnable Lab baseline."
                : hasQuantizedValue && !flashAttentionAvailable
                    ? "The exact runtime has not advertised Flash Attention, which is required by the quantized V-cache configurations."
                    : "The exact runtime has not advertised both the baseline and an alternate reviewed KV representation.";
        var requiredCapabilities = available.Select(type => $"runtime.kv.type.{type}").ToList();
        if (hasQuantizedValue)
            requiredCapabilities.Add("runtime.flash-attention");
        return Plan("kv-cache-v1", "KV cache representation", LabRecipeKind.KvCache, state, detail,
            baseline, candidates.Length == 0 ? [baseline with
            {
                Id = "kv-unavailable", Label = "Unavailable placeholder",
                KvCacheTypeK = "unknown", KvCacheTypeV = "unknown"
            }] : candidates,
            requiredCapabilities, lowBitQuality: true);
    }

    private static LabRecipePlan Flash(LabConfiguration baseline, IReadOnlyCollection<RuntimeCapabilityObservation> capabilities)
    {
        var capability = State(capabilities, "runtime.flash-attention");
        var candidates = new[] { "off", "on" }.Where(value => value != baseline.FlashAttention)
            .Select((value, index) => baseline with { Id = $"flash-{index + 1}", Label = $"Flash Attention {value}", FlashAttention = value })
            .ToArray();
        return Plan("flash-attention-v1", "Flash Attention", LabRecipeKind.FlashAttention,
            capability, Detail(capabilities, "runtime.flash-attention"), baseline, candidates, ["runtime.flash-attention"]);
    }

    private static LabRecipePlan CpuMoe(LabConfiguration baseline,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities, GgufModelInfo? gguf)
    {
        var capability = State(capabilities, "runtime.moe.cpu-placement");
        var values = new List<int> { 0, -1 };
        if (gguf?.BlockCount is > 2) values.Insert(1, gguf.BlockCount.Value / 2);
        var candidates = values.Distinct().Where(value => value != baseline.CpuMoeLayers)
            .Select((value, index) => baseline with
            {
                Id = $"cpu-moe-{index + 1}",
                Label = value switch { 0 => "No CPU expert placement", -1 => "All experts on CPU", _ => $"{value} expert layers on CPU" },
                CpuMoeLayers = value
            }).ToArray();
        return Plan("cpu-moe-placement-v1", "CPU-MoE placement", LabRecipeKind.CpuMoePlacement,
            capability, Detail(capabilities, "runtime.moe.cpu-placement"), baseline, candidates,
            ["runtime.moe.cpu-placement"]);
    }

    private static LabRecipePlan ExternalDraft(ServerConfig source, LabConfiguration original,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities, GgufModelInfo? target, GgufModelInfo? draft,
        ModelIdentityV2? targetIdentity, ModelIdentityV2? draftIdentity)
        => DraftPairPlan("external-draft-v1", "External draft model", LabRecipeKind.ExternalDraft,
            "draft-simple", source, original, capabilities, target, draft, targetIdentity, draftIdentity);

    private static LabRecipePlan Eagle3(ServerConfig source, LabConfiguration original,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities, GgufModelInfo? target, GgufModelInfo? draft,
        ModelIdentityV2? targetIdentity, ModelIdentityV2? draftIdentity)
        => DraftPairPlan("eagle3-v1", "EAGLE-3", LabRecipeKind.Eagle3,
            "draft-eagle3", source, original, capabilities, target, draft, targetIdentity, draftIdentity);

    private static LabRecipePlan DraftPairPlan(string id, string label, LabRecipeKind kind,
        string mechanism, ServerConfig source, LabConfiguration original,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities, GgufModelInfo? target, GgufModelInfo? draft,
        ModelIdentityV2? targetIdentity, ModelIdentityV2? draftIdentity)
    {
        var inspection = SpeculativePairInspector.Inspect(mechanism, source, capabilities, target, draft,
            targetIdentity, draftIdentity);
        var baseline = original with
        {
            Id = "baseline", Label = "Speculation off", SpeculativeTypes = [],
            SpeculativeCompanionIdentity = string.Empty, SpeculativeDraftGpuLayers = null,
            SpeculativeNMax = null, SpeculativeNMin = null, SpeculativePMin = null
        };
        var candidate = original with
        {
            Id = "speculative", Label = label, SpeculativeTypes = [mechanism],
            SpeculativeCompanionIdentity = inspection.CompanionIdentity.StableId
        };
        return new LabRecipePlan(id, label, kind, inspection.State, inspection.Detail,
            baseline, [candidate], 2, false, [LocalModelCapabilityService.CapabilityIdForSpeculativeType(mechanism)],
            SpeculativeMetrics(), LabCorrectnessRequirement.ExactEquivalence);
    }

    private static LabRecipePlan SpeculativeTuning(ServerConfig source, LabConfiguration baseline,
        IReadOnlyCollection<RuntimeCapabilityObservation> capabilities, LabRecipeKind kind,
        string parameterCapability, GgufModelInfo? target, GgufModelInfo? draft,
        ModelIdentityV2? targetIdentity, ModelIdentityV2? draftIdentity)
    {
        var mechanism = baseline.SpeculativeTypes.Count == 1 ? baseline.SpeculativeTypes[0] : string.Empty;
        var mechanismId = mechanism.Length == 0 ? string.Empty : LocalModelCapabilityService.CapabilityIdForSpeculativeType(mechanism);
        var mechanismState = mechanismId.Length == 0 ? CapabilityState.Unknown : State(capabilities, mechanismId);
        var parameterState = State(capabilities, parameterCapability);
        var pairState = mechanism is "draft-simple" or "draft-eagle3"
            ? SpeculativePairInspector.Inspect(mechanism, source, capabilities, target, draft,
                targetIdentity, draftIdentity).State
            : mechanismState;
        var explicitBaseline = kind switch
        {
            LabRecipeKind.SpeculativeDraftMaximum => baseline.SpeculativeNMax.HasValue,
            LabRecipeKind.SpeculativeDraftMinimum => baseline.SpeculativeNMin.HasValue,
            LabRecipeKind.SpeculativeProbabilityMinimum => baseline.SpeculativePMin.HasValue,
            LabRecipeKind.SpeculativeDraftGpuLayers => baseline.SpeculativeDraftGpuLayers.HasValue,
            _ => false
        };
        var state = mechanismState == CapabilityState.Unavailable || parameterState == CapabilityState.Unavailable
            || pairState == CapabilityState.Unavailable
                ? CapabilityState.Unavailable
                : mechanismState == CapabilityState.Available && parameterState == CapabilityState.Available
                    && pairState == CapabilityState.Available && explicitBaseline
                    ? CapabilityState.Available : CapabilityState.Unknown;
        var candidates = TuningCandidates(kind, baseline, draft ?? GgufMetadataReader.TryRead(source.Speculative?.DraftModelPath ?? string.Empty));
        var label = kind switch
        {
            LabRecipeKind.SpeculativeDraftMaximum => "Speculative draft maximum",
            LabRecipeKind.SpeculativeDraftMinimum => "Speculative draft minimum",
            LabRecipeKind.SpeculativeProbabilityMinimum => "Speculative probability minimum",
            _ => "Speculative draft GPU layers"
        };
        var detail = state == CapabilityState.Available
            ? "The exact mechanism and parameter flag are advertised, pair validation passed where required, and the baseline value is explicit."
            : "Select one proven speculative mechanism and set an explicit baseline value; runtime defaults are not assumed stable.";
        return new LabRecipePlan($"{kind.ToString().ToLowerInvariant()}-v1", label, kind, state, detail,
            baseline, candidates, Math.Min(8, candidates.Count + 1), false,
            new[] { mechanismId, parameterCapability }.Where(value => value.Length > 0).ToArray(),
            SpeculativeMetrics(), LabCorrectnessRequirement.ExactEquivalence);
    }

    private static IReadOnlyList<LabConfiguration> TuningCandidates(LabRecipeKind kind,
        LabConfiguration baseline, GgufModelInfo? companion) => kind switch
    {
        LabRecipeKind.SpeculativeDraftMaximum => new[] { 1, 3, 5, 8 }
            .Where(value => value != baseline.SpeculativeNMax && value >= (baseline.SpeculativeNMin ?? 0))
            .Select((value, index) => baseline with { Id = $"spec-nmax-{index + 1}", Label = $"Draft maximum {value}", SpeculativeNMax = value }).ToArray(),
        LabRecipeKind.SpeculativeDraftMinimum => new[] { 0, 1, 2, 3 }
            .Where(value => value != baseline.SpeculativeNMin && value <= (baseline.SpeculativeNMax ?? 128))
            .Select((value, index) => baseline with { Id = $"spec-nmin-{index + 1}", Label = $"Draft minimum {value}", SpeculativeNMin = value }).ToArray(),
        LabRecipeKind.SpeculativeProbabilityMinimum => new[] { 0d, 0.1d, 0.25d, 0.5d }
            .Where(value => value != baseline.SpeculativePMin)
            .Select((value, index) => baseline with { Id = $"spec-pmin-{index + 1}", Label = $"Draft probability {value:0.##}", SpeculativePMin = value }).ToArray(),
        LabRecipeKind.SpeculativeDraftGpuLayers => new[] { 0, Math.Max(1, companion?.BlockCount ?? 1) }
            .Distinct().Where(value => value != baseline.SpeculativeDraftGpuLayers)
            .Select((value, index) => baseline with { Id = $"spec-ngld-{index + 1}", Label = $"Draft GPU layers {value}", SpeculativeDraftGpuLayers = value }).ToArray(),
        _ => []
    };

    private static IReadOnlyList<string> SpeculativeMetrics() =>
    [
        "prompt.tokens_per_second", "decode.tokens_per_second", "ttft.milliseconds",
        "speculative.draft.tokens", "speculative.accepted.tokens", "speculative.acceptance.rate",
        "memory.ram.observed", "memory.gpu.observed", "memory.ram.predicted", "memory.gpu.predicted"
    ];

    private static LabRecipePlan PromptPrefixReuse(LabConfiguration original)
    {
        var baseline = original with
        {
            Id = "prefix-cache-off", Label = "Prompt cache disabled", PromptCacheMode = "disabled"
        };
        var candidate = original with
        {
            Id = "prefix-cache-on", Label = "Prompt cache enabled", PromptCacheMode = "enabled"
        };
        return new LabRecipePlan("prompt-prefix-reuse-v1", "Prompt/shared-prefix timing effect",
            LabRecipeKind.PromptPrefixReuse, CapabilityState.Available,
            "The controlled effect compares identical reconstructed prompts with request-level prompt caching disabled and enabled. It does not infer reused-token counts.",
            baseline, [candidate], 2, false, [],
            ["prompt.milliseconds", "prompt.tokens_per_second", "prompt.reused.tokens", "decode.tokens_per_second"],
            LabCorrectnessRequirement.ExactEquivalence);
    }

    private static LabRecipePlan Plan(string id, string label, LabRecipeKind kind, CapabilityState state,
        string detail, LabConfiguration baseline, IReadOnlyList<LabConfiguration> candidates,
        IReadOnlyList<string> capabilities, bool lowBitQuality = false) => new(
            id, label, kind, state, detail, baseline, candidates, Math.Min(8, candidates.Count + 1), false,
            capabilities,
            ["prompt.tokens_per_second", "decode.tokens_per_second", "ttft.milliseconds", "memory.ram.observed", "memory.gpu.observed",
             "memory.ram.predicted", "memory.gpu.predicted", "context.accepted.tokens", .. lowBitQuality ? new[] { "quality.score" } : []],
            LabCorrectnessRequirement.ExactEquivalence);

    private static IReadOnlySet<string> AllowedFields(LabRecipeKind kind) => kind switch
    {
        LabRecipeKind.EngineProfile => new HashSet<string>([nameof(LabConfiguration.GpuLayers)], StringComparer.Ordinal),
        LabRecipeKind.Context => new HashSet<string>([nameof(LabConfiguration.ContextSize)], StringComparer.Ordinal),
        LabRecipeKind.KvCache => new HashSet<string>([nameof(LabConfiguration.KvCacheTypeK), nameof(LabConfiguration.KvCacheTypeV)], StringComparer.Ordinal),
        LabRecipeKind.FlashAttention => new HashSet<string>([nameof(LabConfiguration.FlashAttention)], StringComparer.Ordinal),
        LabRecipeKind.CpuMoePlacement => new HashSet<string>([nameof(LabConfiguration.CpuMoeLayers)], StringComparer.Ordinal),
        LabRecipeKind.ExternalDraft or LabRecipeKind.Eagle3 => new HashSet<string>(
            [nameof(LabConfiguration.SpeculativeTypes), nameof(LabConfiguration.SpeculativeCompanionIdentity),
             nameof(LabConfiguration.SpeculativeDraftGpuLayers), nameof(LabConfiguration.SpeculativeNMax),
             nameof(LabConfiguration.SpeculativeNMin), nameof(LabConfiguration.SpeculativePMin)], StringComparer.Ordinal),
        LabRecipeKind.SpeculativeDraftMaximum => new HashSet<string>([nameof(LabConfiguration.SpeculativeNMax)], StringComparer.Ordinal),
        LabRecipeKind.SpeculativeDraftMinimum => new HashSet<string>([nameof(LabConfiguration.SpeculativeNMin)], StringComparer.Ordinal),
        LabRecipeKind.SpeculativeProbabilityMinimum => new HashSet<string>([nameof(LabConfiguration.SpeculativePMin)], StringComparer.Ordinal),
        LabRecipeKind.SpeculativeDraftGpuLayers => new HashSet<string>([nameof(LabConfiguration.SpeculativeDraftGpuLayers)], StringComparer.Ordinal),
        LabRecipeKind.PromptPrefixReuse => new HashSet<string>([nameof(LabConfiguration.PromptCacheMode)], StringComparer.Ordinal),
        _ => new HashSet<string>(StringComparer.Ordinal)
    };

    private static IReadOnlyList<string> Differences(LabConfiguration left, LabConfiguration right)
    {
        var differences = new List<string>();
        Add(nameof(LabConfiguration.ContextSize), left.ContextSize, right.ContextSize);
        Add(nameof(LabConfiguration.GpuLayers), left.GpuLayers, right.GpuLayers);
        Add(nameof(LabConfiguration.Threads), left.Threads, right.Threads);
        Add(nameof(LabConfiguration.PromptThreads), left.PromptThreads, right.PromptThreads);
        Add(nameof(LabConfiguration.Slots), left.Slots, right.Slots);
        Add(nameof(LabConfiguration.KvCacheTypeK), left.KvCacheTypeK, right.KvCacheTypeK);
        Add(nameof(LabConfiguration.KvCacheTypeV), left.KvCacheTypeV, right.KvCacheTypeV);
        Add(nameof(LabConfiguration.FlashAttention), left.FlashAttention, right.FlashAttention);
        Add(nameof(LabConfiguration.CpuMoeLayers), left.CpuMoeLayers, right.CpuMoeLayers);
        Add(nameof(LabConfiguration.SpeculativeTypes), string.Join(',', left.SpeculativeTypes), string.Join(',', right.SpeculativeTypes));
        Add(nameof(LabConfiguration.SpeculativeCompanionIdentity), left.SpeculativeCompanionIdentity, right.SpeculativeCompanionIdentity);
        Add(nameof(LabConfiguration.SpeculativeDraftGpuLayers), left.SpeculativeDraftGpuLayers, right.SpeculativeDraftGpuLayers);
        Add(nameof(LabConfiguration.SpeculativeNMax), left.SpeculativeNMax, right.SpeculativeNMax);
        Add(nameof(LabConfiguration.SpeculativeNMin), left.SpeculativeNMin, right.SpeculativeNMin);
        Add(nameof(LabConfiguration.SpeculativePMin), left.SpeculativePMin, right.SpeculativePMin);
        Add(nameof(LabConfiguration.PromptCacheMode), left.PromptCacheMode, right.PromptCacheMode);
        Add(nameof(LabConfiguration.ExtraArgumentsSha256), left.ExtraArgumentsSha256, right.ExtraArgumentsSha256);
        return differences;
        void Add<T>(string field, T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) differences.Add(field); }
    }

    private static bool HasAvailable(IEnumerable<RuntimeCapabilityObservation> values, string id) =>
        values.Any(value => value.CapabilityId == id && value.State == CapabilityState.Available);
    private static CapabilityState State(IEnumerable<RuntimeCapabilityObservation> values, string id) =>
        values.FirstOrDefault(value => value.CapabilityId == id)?.State ?? CapabilityState.Unknown;
    private static string Detail(IEnumerable<RuntimeCapabilityObservation> values, string id) =>
        values.FirstOrDefault(value => value.CapabilityId == id)?.Detail ?? "The exact runtime capability is Unknown.";
}

public sealed record LabWorkloadRequest(
    string RunId,
    int Port,
    LabConfiguration Configuration,
    EmpiricalProfileFingerprintV2 Fingerprint,
    string Prompt,
    int Seed,
    int MaximumTokens,
    string CaseId,
    int Repetition,
    TimeSpan Timeout,
    bool DisablePromptCache = false,
    string DirectReusedTokenCounterField = "");

public sealed record LabWorkloadResult(
    IReadOnlyList<LabObservation> Observations,
    LabOutputEvidence? Output,
    string? Failure);

public interface ILabWorkloadExecutor
{
    Task<LabWorkloadResult> ExecuteAsync(LabWorkloadRequest request, CancellationToken ct = default);
}

public sealed class LlamaServerLabWorkloadExecutor : ILabWorkloadExecutor
{
    public async Task<LabWorkloadResult> ExecuteAsync(LabWorkloadRequest request, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = request.Timeout };
        var timer = Stopwatch.StartNew();
        try
        {
            using var response = await http.PostAsJsonAsync($"http://127.0.0.1:{request.Port}/v1/chat/completions", new
            {
                model = "local",
                messages = new[] { new { role = "user", content = request.Prompt } },
                temperature = 0,
                seed = request.Seed,
                max_tokens = request.MaximumTokens,
                stream = false,
                cache_prompt = !request.DisablePromptCache
            }, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            timer.Stop();
            if (!response.IsSuccessStatusCode)
                return new([], null, $"HTTP {(int)response.StatusCode} from isolated workload endpoint.");
            return ParseSuccessfulResponse(request, body, timer.Elapsed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new([], null, ex is TaskCanceledException && ct.IsCancellationRequested
                ? "Workload cancelled." : $"Isolated workload failed: {ex.Message}");
        }
    }

    public static LabWorkloadResult ParseSuccessfulResponse(LabWorkloadRequest request, string body, TimeSpan elapsed)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var text = ReadContent(root);
        var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;
        var timings = root.TryGetProperty("timings", out var timingsElement) ? timingsElement : default;
        var drafted = Number(timings, "draft_n") ?? Number(root, "draft_n");
        var accepted = Number(timings, "draft_n_accepted") ?? Number(root, "draft_n_accepted");
        double? acceptance = drafted is > 0 && accepted.HasValue ? accepted.Value / drafted.Value : null;
        var acceptanceObservation = Metric(request, "speculative.acceptance.rate", acceptance, "ratio",
            "runtime-timings", EvidenceOrigin.DeterministicCalculation);
        if (!acceptance.HasValue && drafted == 0)
            acceptanceObservation = acceptanceObservation with
            {
                MissingReason = "The runtime reported zero drafted tokens, so an acceptance ratio is undefined."
            };
        var reused = PromptReuseEvidenceAdapter.ReadDirectCounter(root, request.DirectReusedTokenCounterField);
        var observations = new List<LabObservation>
        {
            Metric(request, "request.total.milliseconds", elapsed.TotalMilliseconds, "ms", "wall-clock", EvidenceOrigin.DirectObservation),
            Metric(request, "prompt.tokens", Number(usage, "prompt_tokens"), "tokens", "runtime-response", EvidenceOrigin.DirectObservation),
            Metric(request, "completion.tokens", Number(usage, "completion_tokens"), "tokens", "runtime-response", EvidenceOrigin.DirectObservation),
            Metric(request, "tokens.served", Number(usage, "total_tokens"), "tokens", "runtime-response", EvidenceOrigin.DirectObservation),
            Metric(request, "context.accepted.tokens", Number(usage, "prompt_tokens"), "tokens", "runtime-response", EvidenceOrigin.DirectObservation),
            Metric(request, "prompt.tokens_per_second", Number(timings, "prompt_per_second"), "tokens/s", "runtime-timings", EvidenceOrigin.DirectObservation),
            Metric(request, "prompt.milliseconds", Number(timings, "prompt_ms"), "ms", "runtime-timings", EvidenceOrigin.DirectObservation),
            Metric(request, "prompt.reused.tokens", reused, "tokens",
                request.DirectReusedTokenCounterField.Length == 0 ? "unavailable" : "runtime-counter",
                EvidenceOrigin.DirectObservation) with
            {
                MissingReason = reused.HasValue ? string.Empty
                    : request.DirectReusedTokenCounterField.Length == 0
                        ? "No proven machine-readable reused-token counter schema is available for this runtime."
                        : "The proven reused-token counter field was absent from this response."
            },
            Metric(request, "decode.tokens_per_second", Number(timings, "predicted_per_second"), "tokens/s", "runtime-timings", EvidenceOrigin.DirectObservation),
            Metric(request, "speculative.draft.tokens", drafted, "tokens", "runtime-timings", EvidenceOrigin.DirectObservation),
            Metric(request, "speculative.accepted.tokens", accepted, "tokens", "runtime-timings", EvidenceOrigin.DirectObservation),
            acceptanceObservation,
            Missing(request, "ttft.milliseconds", "ms", "The buffered llama-server response does not expose trustworthy TTFT.")
        };
        return new(observations, LabCorrectnessEvaluator.Capture(
            request.Configuration.Id, request.CaseId, request.Repetition, text), null);
    }

    private static string ReadContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0) return string.Empty;
        var first = choices[0];
        return first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
            ? content.GetString() ?? string.Empty : string.Empty;
    }

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number : null;

    private static LabObservation Metric(LabWorkloadRequest request, string metric, double? value,
        string unit, string source, EvidenceOrigin origin) => new()
    {
        RunId = request.RunId, ConfigurationId = request.Configuration.Id, CaseId = request.CaseId,
        Repetition = request.Repetition, MetricId = metric, Value = value, Unit = unit, Source = source,
        Origin = origin, Trust = "TrustedRuntime", MissingReason = value.HasValue ? string.Empty : "The runtime response omitted this counter.",
        RuntimeFingerprint = request.Fingerprint.Runtime.StableId, ModelFingerprint = request.Fingerprint.Model.StableId,
        HardwareFingerprint = request.Fingerprint.Hardware.StableId,
        ConfigurationFingerprint = request.Fingerprint.Configuration.StableId
    };

    private static LabObservation Missing(LabWorkloadRequest request, string metric, string unit, string reason) =>
        Metric(request, metric, null, unit, "unavailable", EvidenceOrigin.DirectObservation) with { MissingReason = reason, Trust = "Unknown" };
}

public static class PromptReuseEvidenceAdapter
{
    private static readonly IReadOnlySet<string> ReviewedFields =
        new HashSet<string>(["reused_tokens", "prompt_tokens_reused"], StringComparer.Ordinal);

    public static string ProvenCounterField(IEnumerable<RuntimeCapabilityObservation> observations)
    {
        var capability = observations.FirstOrDefault(item =>
            item.CapabilityId == "runtime.prompt-cache.reused-token-counter"
            && item.State == CapabilityState.Available);
        if (capability is null || !capability.Parameters.TryGetValue("response_field", out var field)
            || !ReviewedFields.Contains(field)) return string.Empty;
        return field;
    }

    public static double? ReadDirectCounter(JsonElement root, string provenField)
    {
        if (!ReviewedFields.Contains(provenField)) return null;
        if (root.TryGetProperty(provenField, out var direct) && direct.TryGetDouble(out var value)) return value;
        return root.TryGetProperty("timings", out var timings) && timings.ValueKind == JsonValueKind.Object
            && timings.TryGetProperty(provenField, out var nested) && nested.TryGetDouble(out value)
                ? value : null;
    }
}

public static class SharedPrefixPromptFixture
{
    public static string Build(string sharedPrefix, int repetition)
    {
        if (repetition is < 0 or > 19) throw new ArgumentOutOfRangeException(nameof(repetition));
        return $"{sharedPrefix.TrimEnd()}\n\nControlled suffix {repetition + 1}: answer with one concise sentence.";
    }
}

public sealed class LabRecipeRunner
{
    private readonly ILabExperimentService _experiments;
    private readonly ILabWorkloadExecutor _workload;
    private readonly IRuntimeTelemetrySource _telemetry;
    private readonly ISystemInfoService _systemInfo;

    public LabRecipeRunner(ILabExperimentService experiments, ILabWorkloadExecutor workload,
        IRuntimeTelemetrySource telemetry, ISystemInfoService systemInfo)
    {
        _experiments = experiments;
        _workload = workload;
        _telemetry = telemetry;
        _systemInfo = systemInfo;
    }

    public async Task<LabRunSnapshot> RunAsync(LabRecipePlan plan, ServerConfig source,
        LocalModelCapabilities capabilities, string prompt, CancellationToken ct = default)
    {
        if (plan.Availability != CapabilityState.Available)
            throw new InvalidOperationException($"Recipe {plan.Label} is {plan.Availability}: {plan.AvailabilityDetail}");
        LabRecipeCatalog.Validate(plan);
        var definition = await _experiments.CreateDefinitionAsync(plan.Label, plan.Id, source,
            plan.Baseline, plan.Candidates, 3, plan.CorrectnessRequirement, ct);
        definition = definition with
        {
            WorkloadId = "greedy-chat-completion-v1",
            PromptHashes = plan.Kind == LabRecipeKind.PromptPrefixReuse
                ? Enumerable.Range(0, 3).Select(repetition =>
                    LabCanonicalJson.Hash(SharedPrefixPromptFixture.Build(prompt, repetition))).ToArray()
                : [LabCanonicalJson.Hash(prompt)],
            SamplingPolicy = "greedy-temperature-zero",
            Seed = 1,
            Repetitions = 3,
            TimeoutSeconds = 180,
            RequiredMetrics = plan.RequiredMetrics,
            RequestedCapabilityIds = plan.RequiredCapabilityIds
        };
        var plannedPredictions = new Dictionary<string, ModelFitPrediction>(StringComparer.Ordinal);
        foreach (var configuration in plan.Candidates.Prepend(plan.Baseline))
        {
            var fingerprint = definition.ProfileFingerprint with
            {
                Configuration = definition.ConfigurationIdentities[configuration.Id]
            };
            plannedPredictions[configuration.Id] = await PredictModelAsync(source, configuration, fingerprint, capabilities, ct);
        }
        var run = await _experiments.StartAsync(definition, source, ct);
        if (run.Status != LabRunStatus.Running) return run;

        var observations = new List<LabObservation>();
        var outputs = new List<LabOutputEvidence>();
        var failures = new List<string>();
        try
        {
            var consecutiveFailures = 0;
            var reusedCounterField = PromptReuseEvidenceAdapter.ProvenCounterField(capabilities.Observations ?? []);
            var configurations = plan.Candidates.Prepend(plan.Baseline).ToArray();
            foreach (var configuration in configurations)
            {
                if (configuration.Id != plan.Baseline.Id)
                {
                    try { run = await _experiments.SwitchConfigurationAsync(run.Id, source, configuration.Id, ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add($"{configuration.Id}: launch failed: {ex.Message}");
                        run = _experiments.GetRun(run.Id) ?? run;
                        if (run.Status != LabRunStatus.Running)
                            break;
                        if (++consecutiveFailures >= 2) break;
                        continue;
                    }
                }

                var fingerprint = definition.ProfileFingerprint with
                {
                    Configuration = definition.ConfigurationIdentities[configuration.Id]
                };
                observations.AddRange(PredictionObservations(run.Id, configuration.Id, fingerprint, plannedPredictions[configuration.Id]));
                if (plan.Kind == LabRecipeKind.PromptPrefixReuse)
                    observations.Add(PromptReuseLevelObservation(run.Id, configuration.Id, fingerprint,
                        reusedCounterField.Length == 0 ? PromptReuseEvidenceLevel.ControlledTimingEffect : PromptReuseEvidenceLevel.DirectCounter));
                if (plan.RequiredMetrics.Contains("quality.score", StringComparer.Ordinal))
                    observations.Add(MissingQualityObservation(run.Id, configuration.Id, fingerprint));
                for (var repetition = 0; repetition < definition.Repetitions; repetition++)
                {
                    var workloadPrompt = plan.Kind == LabRecipeKind.PromptPrefixReuse
                        ? SharedPrefixPromptFixture.Build(prompt, repetition) : prompt;
                    var caseId = plan.Kind == LabRecipeKind.PromptPrefixReuse
                        ? $"shared-prefix-{repetition + 1}" : "greedy-reference";
                    var result = await _workload.ExecuteAsync(new LabWorkloadRequest(
                        run.Id, run.TemporaryPort!.Value, configuration, fingerprint, workloadPrompt,
                        definition.Seed, 128, caseId, repetition,
                        TimeSpan.FromSeconds(definition.TimeoutSeconds),
                        configuration.PromptCacheMode == "disabled", reusedCounterField), ct);
                    observations.AddRange(result.Observations);
                    if (result.Output is not null) outputs.Add(result.Output);
                    observations.AddRange(await CaptureTelemetryAsync(run, configuration, fingerprint, ct));
                    if (result.Failure is not null)
                    {
                        failures.Add($"{configuration.Id} repetition {repetition}: {result.Failure}");
                        consecutiveFailures++;
                        break;
                    }
                    consecutiveFailures = 0;
                }
                if (consecutiveFailures >= 2) break;
                if (configuration.Id != plan.Baseline.Id)
                {
                    var comparison = LabCorrectnessEvaluator.Compare(
                        outputs.FirstOrDefault(item => item.ConfigurationId == plan.Baseline.Id),
                        outputs.FirstOrDefault(item => item.ConfigurationId == configuration.Id));
                    if (comparison.State == LabEquivalenceState.Different) break;
                }
            }
            return await _experiments.CompleteAsync(run.Id, observations, outputs, failures, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return await _experiments.CancelAsync(run.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            failures.Add($"run failed: {ex.Message}");
            try
            {
                return await _experiments.CompleteAsync(run.Id, observations, outputs, failures, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("The Lab run failed and cleanup also failed.", ex, cleanupException);
            }
        }
    }

    private async Task<ModelFitPrediction> PredictModelAsync(ServerConfig source,
        LabConfiguration configuration, EmpiricalProfileFingerprintV2 fingerprint,
        LocalModelCapabilities capabilities, CancellationToken ct)
    {
        var hardware = await _systemInfo.GetHardwareProfileAsync(ct);
        var info = GgufMetadataReader.TryRead(source.ModelPath);
        var modelBytes = File.Exists(source.ModelPath) ? new FileInfo(source.ModelPath).Length : 0;
        var companions = new List<FitCompanionInput>();
        if (configuration.SpeculativeTypes.Any(type => type.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)))
        {
            var path = source.Speculative?.DraftModelPath ?? string.Empty;
            var bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
            var companionInfo = GgufMetadataReader.TryRead(path);
            var placement = configuration.SpeculativeDraftGpuLayers switch
            {
                0 => FitPlacement.SystemRam,
                > 0 when companionInfo?.BlockCount is int blocks
                    && configuration.SpeculativeDraftGpuLayers >= blocks => FitPlacement.Gpu,
                _ => FitPlacement.Unknown
            };
            companions.Add(new FitCompanionInput("Speculative companion", bytes, placement,
                EvidenceOrigin.DeterministicCalculation,
                "Companion file bytes placed from the explicit draft GPU-layer setting; partial placement is not inferred."));
        }
        return ModelFitPredictor.Predict(new ModelFitPredictionRequest(
            fingerprint, modelBytes, configuration.ContextSize, configuration.GpuLayers, configuration.Slots,
            configuration.KvCacheTypeK, configuration.KvCacheTypeV,
            KvState(capabilities, configuration.KvCacheTypeK), KvState(capabilities, configuration.KvCacheTypeV),
            KvCacheMath.HasSwaFull(source.ExtraArgs), configuration.CpuMoeLayers, hardware, companions), info);
    }

    private static IReadOnlyList<LabObservation> PredictionObservations(string runId,
        string configurationId, EmpiricalProfileFingerprintV2 fingerprint, ModelFitPrediction prediction)
    {
        return
        [
            PredictionObservation(runId, configurationId, fingerprint, "memory.gpu.predicted", prediction.GpuRequiredBytes),
            PredictionObservation(runId, configurationId, fingerprint, "memory.ram.predicted", prediction.SystemRamRequiredBytes),
            new LabObservation
            {
                RunId = runId, ConfigurationId = configurationId, CaseId = "fit", Repetition = 0,
                MetricId = "fit.tier", Value = (double)prediction.Tier, Unit = "enum", Source = "gpu-fit-v1",
                Origin = EvidenceOrigin.DeterministicCalculation, Trust = "DeterministicCalculation",
                RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
                HardwareFingerprint = fingerprint.Hardware.StableId, ConfigurationFingerprint = fingerprint.Configuration.StableId
            }
        ];
    }

    private async Task<IReadOnlyList<LabObservation>> CaptureTelemetryAsync(LabRunSnapshot run,
        LabConfiguration configuration, EmpiricalProfileFingerprintV2 fingerprint, CancellationToken ct)
    {
        if (run.RuntimeProcessId is not int processId || run.RuntimeProcessStartedAtUtc is not DateTime started)
            return [MissingObservation(run.Id, configuration.Id, fingerprint, "memory.ram.observed", "Runtime process identity is unavailable."),
                MissingObservation(run.Id, configuration.Id, fingerprint, "memory.gpu.observed", "Runtime process identity is unavailable.")];
        var samples = await _telemetry.CaptureAsync(new RuntimeTelemetryRequest(
            $"lab-{run.Id}-{configuration.Id}", processId, started, fingerprint.Runtime, fingerprint), ct);
        var ram = samples.FirstOrDefault(sample => sample.Metric == RuntimeTelemetryMetric.ProcessWorkingSetBytes);
        var gpu = samples.FirstOrDefault(sample => sample.Metric is RuntimeTelemetryMetric.ProcessGpuMemoryBytes or RuntimeTelemetryMetric.RuntimeReportedGpuMemoryBytes);
        return [TelemetryObservation(run.Id, configuration.Id, fingerprint, "memory.ram.observed", ram),
            TelemetryObservation(run.Id, configuration.Id, fingerprint, "memory.gpu.observed", gpu)];
    }

    private static CapabilityState KvState(LocalModelCapabilities capabilities, string type) =>
        capabilities.Observations?.FirstOrDefault(value => value.CapabilityId == $"runtime.kv.type.{type.ToLowerInvariant()}")?.State
        ?? CapabilityState.Unknown;

    private static LabObservation PredictionObservation(string runId, string configurationId,
        EmpiricalProfileFingerprintV2 fingerprint, string metric, long? value) => new()
    {
        RunId = runId, ConfigurationId = configurationId, CaseId = "fit", Repetition = 0,
        MetricId = metric, Value = value, Unit = "bytes", Source = "gpu-fit-v1",
        Origin = EvidenceOrigin.DeterministicCalculation, Trust = "DeterministicCalculation",
        MissingReason = value.HasValue ? string.Empty : "GPU Fit withheld this total because a material input is Unknown.",
        RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
        HardwareFingerprint = fingerprint.Hardware.StableId, ConfigurationFingerprint = fingerprint.Configuration.StableId
    };

    private static LabObservation TelemetryObservation(string runId, string configurationId,
        EmpiricalProfileFingerprintV2 fingerprint, string metric, RuntimeTelemetrySample? sample) => new()
    {
        RunId = runId, ConfigurationId = configurationId, CaseId = "runtime-memory", Repetition = 0,
        MetricId = metric, Value = sample?.ValueBytes, Unit = "bytes",
        Source = sample?.Source.ToString() ?? "unavailable", Origin = EvidenceOrigin.DirectObservation,
        Trust = sample?.Trust.ToString() ?? "Unknown",
        MissingReason = sample?.ValueBytes.HasValue == true ? string.Empty : sample?.Detail ?? "No trustworthy process-scoped measurement is available.",
        RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
        HardwareFingerprint = fingerprint.Hardware.StableId, ConfigurationFingerprint = fingerprint.Configuration.StableId
    };

    private static LabObservation MissingObservation(string runId, string configurationId,
        EmpiricalProfileFingerprintV2 fingerprint, string metric, string reason) =>
        TelemetryObservation(runId, configurationId, fingerprint, metric, null) with { MissingReason = reason };

    private static LabObservation MissingQualityObservation(string runId, string configurationId,
        EmpiricalProfileFingerprintV2 fingerprint) => new()
    {
        RunId = runId, ConfigurationId = configurationId, CaseId = "quality", Repetition = 0,
        MetricId = "quality.score", Value = null, Unit = "suite-score", Source = "unavailable",
        Origin = EvidenceOrigin.DirectObservation, Trust = "Unknown",
        MissingReason = "No referenced benchmark/quality run was supplied; loading success is not quality evidence.",
        RuntimeFingerprint = fingerprint.Runtime.StableId, ModelFingerprint = fingerprint.Model.StableId,
        HardwareFingerprint = fingerprint.Hardware.StableId, ConfigurationFingerprint = fingerprint.Configuration.StableId
    };

    private static LabObservation PromptReuseLevelObservation(string runId, string configurationId,
        EmpiricalProfileFingerprintV2 fingerprint, PromptReuseEvidenceLevel level) => new()
    {
        RunId = runId, ConfigurationId = configurationId, CaseId = "shared-prefix", Repetition = 0,
        MetricId = "prompt.reuse.evidence-level", Value = (double)level, Unit = "enum",
        Source = level == PromptReuseEvidenceLevel.DirectCounter ? "runtime-capability" : "controlled-protocol",
        Origin = level == PromptReuseEvidenceLevel.DirectCounter ? EvidenceOrigin.Extracted : EvidenceOrigin.DeterministicCalculation,
        Trust = level.ToString(), RuntimeFingerprint = fingerprint.Runtime.StableId,
        ModelFingerprint = fingerprint.Model.StableId, HardwareFingerprint = fingerprint.Hardware.StableId,
        ConfigurationFingerprint = fingerprint.Configuration.StableId
    };
}

public sealed class LabRecipeService : ILabRecipeService
{
    private readonly LocalModelCapabilityService _capabilities;
    private readonly LabRecipeRunner _runner;
    private readonly ModelManifestStore _manifest;

    public LabRecipeService(LocalModelCapabilityService capabilities, LabRecipeRunner runner,
        ModelManifestStore manifest)
    {
        _capabilities = capabilities;
        _runner = runner;
        _manifest = manifest;
    }

    public async Task<IReadOnlyList<LabRecipePlan>> InspectAsync(ServerConfig source, CancellationToken ct = default)
    {
        var capabilities = await _capabilities.ProbeAsync(source.ModelPath, source.ExecutablePath, ct: ct);
        var observations = capabilities.Observations ?? [];
        var gguf = GgufMetadataReader.TryRead(source.ModelPath);
        var draftPath = source.Speculative?.DraftModelPath ?? string.Empty;
        var draftGguf = GgufMetadataReader.TryRead(draftPath);
        var targetIdentity = await ProvenIdentityAsync(source.ModelPath, gguf, ct);
        var draftIdentity = await ProvenIdentityAsync(draftPath, draftGguf, ct);
        return Enum.GetValues<LabRecipeKind>()
            .Select(kind => LabRecipeCatalog.Build(kind, source, observations, gguf, draftGguf,
                targetIdentity, draftIdentity))
            .ToArray();
    }

    public async Task<LabRunSnapshot> RunAsync(LabRecipePlan plan, ServerConfig source,
        string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4096)
            throw new InvalidOperationException("A Lab recipe prompt must contain 1 to 4,096 characters.");
        var currentPlan = (await InspectAsync(source, ct)).SingleOrDefault(item => item.Id == plan.Id)
            ?? throw new InvalidOperationException("The selected Lab recipe is no longer available.");
        if (currentPlan.Availability != CapabilityState.Available)
            throw new InvalidOperationException($"Recipe {currentPlan.Label} is {currentPlan.Availability}: {currentPlan.AvailabilityDetail}");
        if (LabCanonicalJson.Hash(LabCanonicalJson.Serialize(currentPlan))
            != LabCanonicalJson.Hash(LabCanonicalJson.Serialize(plan)))
            throw new InvalidOperationException("The recipe capability, asset identity, or baseline changed after inspection. Inspect it again.");
        var capabilities = await _capabilities.ProbeAsync(source.ModelPath, source.ExecutablePath, ct: ct);
        return await _runner.RunAsync(currentPlan, source, capabilities, prompt, ct);
    }

    private async Task<ModelIdentityV2?> ProvenIdentityAsync(string path, GgufModelInfo? gguf, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var manifest = await _manifest.FindAsync(path, ct);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Sha256)) return null;
        var manifestIdentity = string.Join(':', manifest.RepoId, manifest.RevisionSha, manifest.RepoFile);
        return RuntimeIdentityFactory.CreateModelIdentity(path, gguf, manifest.Sha256, manifestIdentity);
    }
}
