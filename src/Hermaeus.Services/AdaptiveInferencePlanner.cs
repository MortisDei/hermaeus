using System.Globalization;
using Hermaeus.Core.Models;

namespace Hermaeus.Services;

public sealed record AdaptiveInferenceCandidate(
    string CandidateId,
    int Ordinal,
    string Reason,
    ServerConfig Configuration,
    IReadOnlyList<string> ChangedFields,
    bool RequiresEffectiveObservation)
{
    public bool ChangesConfiguration => ChangedFields.Count > 0;
}

public sealed record AdaptiveInferencePlan(
    AdaptiveInferenceMode Mode,
    IReadOnlyList<AdaptiveInferenceCandidate> Candidates,
    IReadOnlyList<string> UnavailableReasons,
    long? FitTargetBytes,
    int? FitMinimumContext)
{
    public bool HasAdaptation => Candidates.Any(candidate => candidate.ChangesConfiguration);
}

/// <summary>
/// Builds a small, deterministic set of single-axis launch candidates. It
/// never mutates the saved configuration and never combines speculative
/// changes into an unbounded Cartesian search.
/// </summary>
public static class AdaptiveInferencePlanner
{
    public const int MaximumCandidates = 8;

    private static readonly int[] ContextLadder =
        [2048, 4096, 8192, 12288, 16384, 24576, 32768, 49152, 65536, 98304, 131072];

    private static readonly double[] LayerFractions = [0.75, 0.5, 0.25];

    public static ResourceHeadroomPolicy HeadroomPolicy(ServerConfig config)
    {
        var envelope = config.AdaptiveEnvelope ?? new AdaptiveInferenceEnvelope();
        return new ResourceHeadroomPolicy(
            deviceStabilityBytes: envelope.MinimumGpuHeadroomBytes,
            systemStabilityBytes: ResourceHeadroomPolicy.DefaultSystemStabilityBytes,
            interactiveReservationBytes: ResourceHeadroomPolicy.DefaultInteractiveBytes,
            foregroundReservationBytes: ResourceHeadroomPolicy.DefaultForegroundBytes,
            inProcessReservationBytes: ResourceHeadroomPolicy.DefaultInProcessBytes,
            unknownDeviceReservationBytes: ResourceHeadroomPolicy.DefaultUnknownDeviceBytes);
    }

    public static AdaptiveInferencePlan Build(
        ServerConfig configured,
        ResourceWorkloadPlan workloadPlan,
        LlamaRuntimeCapabilityFacts runtime,
        GgufModelInfo? model = null,
        IEnumerable<string>? qualityApprovedKvTypes = null)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(workloadPlan);
        ArgumentNullException.ThrowIfNull(runtime);

        var envelope = configured.AdaptiveEnvelope?.Clone() ?? new AdaptiveInferenceEnvelope();
        if (!envelope.TryValidate(out var envelopeError))
            return new(envelope.Mode, [], [envelopeError!], null, null);

        var unavailable = new List<string>();
        var candidates = new List<AdaptiveInferenceCandidate>();
        var configuredPlacement = configured.TryGetGpuPlacement(out var placement, out var placementError)
            ? placement
            : null;
        if (configuredPlacement is null)
        {
            unavailable.Add(placementError ?? "Configured GPU placement is invalid.");
            return new(envelope.Mode, [], unavailable, null, null);
        }

        var minimumContext = ResolveMinimumContext(configured.ContextSize, envelope, unavailable);
        var fitTarget = FitTarget(workloadPlan);
        var fitMinimumContext = envelope.Mode == AdaptiveInferenceMode.Fixed
            ? (int?)null
            : minimumContext;

        var baseline = Clone(configured);
        if (envelope.Mode != AdaptiveInferenceMode.Fixed && configuredPlacement.Kind == GpuPlacementKind.Auto)
            ApplyFitControls(baseline, runtime, fitTarget, fitMinimumContext);

        AddCandidate(
            "configured",
            "Configured launch semantics are preserved; whole-workload fit controls are added only when the selected runtime proves them.",
            baseline,
            []);

        if (envelope.Mode == AdaptiveInferenceMode.Fixed)
            return new(envelope.Mode, candidates, unavailable, fitTarget, fitMinimumContext);

        if (envelope.AllowGpuLayerReduction)
            AddGpuLayerCandidates(configured, configuredPlacement, runtime, model, envelope, candidates, unavailable);

        if (envelope.AllowContextReduction)
            AddContextCandidates(configured, minimumContext, candidates, unavailable);

        if (envelope.AllowKvPrecisionChange)
            AddKvCandidates(configured, runtime, qualityApprovedKvTypes, candidates, unavailable);

        if (envelope.AllowCpuMoePlacement)
            AddCpuMoeCandidate(configured, configuredPlacement, runtime, model, candidates, unavailable);

        if (envelope.AllowMultiDevicePlacement)
        {
            unavailable.Add("Multi-device placement remains Unknown because no proven production overlay owns its device and split fields.");
        }

        foreach (var candidate in candidates.Skip(1))
        {
            if (candidate.Configuration.GpuPlacement?.Kind == GpuPlacementKind.Auto)
                ApplyFitControls(candidate.Configuration, runtime, fitTarget, fitMinimumContext);
        }

        return new(envelope.Mode, candidates, unavailable, fitTarget, fitMinimumContext);

        void AddCandidate(
            string id,
            string reason,
            ServerConfig candidate,
            IReadOnlyList<string> changedFields)
        {
            if (candidates.Count >= MaximumCandidates)
                return;

            var changes = changedFields.Count > 0
                ? changedFields
                : ChangedFields(configured, candidate);
            if (candidates.Any(existing => string.Equals(existing.CandidateId, id, StringComparison.Ordinal)))
                return;

            candidates.Add(new(
                id,
                candidates.Count,
                reason,
                candidate,
                changes,
                envelope.Mode == AdaptiveInferenceMode.AdaptAtLaunch));
        }

        void AddGpuLayerCandidate(int layers, string reason)
        {
            if (candidates.Count >= MaximumCandidates || layers <= 0)
                return;
            var candidate = Clone(configured);
            candidate.GpuPlacement = GpuPlacementIntent.Exact(layers);
            candidate.GpuLayers = layers;
            AddCandidate(
                $"gpu-layers-{layers.ToString(CultureInfo.InvariantCulture)}",
                reason,
                candidate,
                ["GPU placement"]);
        }

        void AddContextCandidate(int context, string reason)
        {
            if (candidates.Count >= MaximumCandidates)
                return;
            var candidate = Clone(configured);
            candidate.ContextSize = context;
            AddCandidate(
                $"context-{context.ToString(CultureInfo.InvariantCulture)}",
                reason,
                candidate,
                ["context"]);
        }

        void AddKvCandidate(string type)
        {
            if (candidates.Count >= MaximumCandidates)
                return;
            var candidate = Clone(configured);
            candidate.KvCacheType = type;
            candidate.KvCacheTypeK = type;
            candidate.KvCacheTypeV = type;
            AddCandidate($"kv-{type.ToLowerInvariant()}",
                $"Use runtime-advertised {type} KV precision only because compatible quality evidence was supplied.",
                candidate,
                ["KV precision"]);
        }

        void AddCpuMoeConfigurationCandidate()
        {
            if (candidates.Count >= MaximumCandidates)
                return;
            var candidate = Clone(configured);
            candidate.CpuMoeLayers = -1;
            AddCandidate(
                "cpu-moe-all",
                "Keep known MoE experts on the CPU while preserving the configured attention placement.",
                candidate,
                ["CPU-MoE placement"]);
        }

        void AddGpuLayerCandidates(
            ServerConfig source,
            GpuPlacementIntent sourcePlacement,
            LlamaRuntimeCapabilityFacts facts,
            GgufModelInfo? gguf,
            AdaptiveInferenceEnvelope bounds,
            List<AdaptiveInferenceCandidate> target,
            List<string> reasons)
        {
            if (!IsAvailable(facts, "runtime.gpu-placement.exact"))
            {
                reasons.Add("GPU-layer reduction is unavailable because the selected runtime has no proven exact placement capability.");
                return;
            }

            var maximum = sourcePlacement.Kind == GpuPlacementKind.Exact
                ? sourcePlacement.ExactLayerCount
                : gguf?.BlockCount;
            if (maximum is not > 1)
            {
                reasons.Add("GPU-layer reduction is Unknown because a positive model layer count is not available.");
                return;
            }

            foreach (var fraction in LayerFractions)
            {
                var layers = Math.Max(1, (int)Math.Floor(maximum.Value * fraction));
                if (layers >= maximum.Value || (bounds.PreserveAcceleratedBackend && layers <= 0))
                    continue;
                AddGpuLayerCandidate(layers,
                    $"Reduce GPU layers to {layers.ToString(CultureInfo.InvariantCulture)} while preserving context and an accelerated backend.");
            }
        }

        void AddContextCandidates(
            ServerConfig source,
            int minimum,
            List<AdaptiveInferenceCandidate> target,
            List<string> reasons)
        {
            if (minimum >= source.ContextSize)
            {
                reasons.Add("Context reduction produced no candidate because the configured context is already at the envelope minimum.");
                return;
            }

            foreach (var context in ContextLadder.Reverse().Where(value => value < source.ContextSize && value >= minimum))
            {
                AddContextCandidate(context,
                    $"Reduce context to {context.ToString(CultureInfo.InvariantCulture)} on the configured context ladder, never below the envelope minimum.");
                if (candidates.Count >= MaximumCandidates)
                    break;
            }
        }

        void AddKvCandidates(
            ServerConfig source,
            LlamaRuntimeCapabilityFacts facts,
            IEnumerable<string>? approved,
            List<AdaptiveInferenceCandidate> target,
            List<string> reasons)
        {
            var approvedSet = approved?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            if (approvedSet.Count == 0)
            {
                reasons.Add("KV precision reduction is not offered without compatible quality evidence.");
                return;
            }

            var current = EffectiveKvType(source);
            foreach (var type in facts.SupportedKvCacheTypes ?? [])
            {
                if (string.Equals(type, current, StringComparison.OrdinalIgnoreCase)
                    || !approvedSet.Contains(type)
                    || string.Equals(type, "f16", StringComparison.OrdinalIgnoreCase))
                    continue;
                AddKvCandidate(type);
                if (candidates.Count >= MaximumCandidates)
                    break;
            }

            if (!facts.HelpProbeSucceeded || (facts.SupportedKvCacheTypes?.Count ?? 0) == 0)
                reasons.Add("KV precision is Unknown because the selected runtime did not advertise a bounded type list.");
        }

        void AddCpuMoeCandidate(
            ServerConfig source,
            GpuPlacementIntent sourcePlacement,
            LlamaRuntimeCapabilityFacts facts,
            GgufModelInfo? gguf,
            List<AdaptiveInferenceCandidate> target,
            List<string> reasons)
        {
            var knownMoe = gguf is not null
                && (gguf.GeneralType.Contains("moe", StringComparison.OrdinalIgnoreCase)
                    || gguf.Architecture.Contains("moe", StringComparison.OrdinalIgnoreCase));
            if (!knownMoe)
            {
                reasons.Add("CPU-MoE placement is Unknown because the model metadata does not identify a MoE model.");
                return;
            }
            if (!facts.HelpProbeSucceeded || !facts.SupportsCpuMoePlacement)
            {
                reasons.Add("CPU-MoE placement is unavailable because the selected runtime does not advertise both CPU-MoE controls.");
                return;
            }
            if (source.CpuMoeLayers != 0)
                return;
            if (sourcePlacement.Kind == GpuPlacementKind.Cpu)
            {
                reasons.Add("CPU-MoE placement is not offered for a CPU-only configured backend.");
                return;
            }

            AddCpuMoeConfigurationCandidate();
        }
    }

    public static long? FitTarget(ResourceWorkloadPlan plan)
    {
        var known = plan.DeviceHeadroom
            .Where(device => device.IsKnown
                && device.CapacityBytes.HasValue
                && device.UsedBytes.HasValue
                && device.RemainingBytes.HasValue)
            .ToArray();
        if (known.Length != 1)
            return null;

        var target = checked(known[0].RemainingBytes!.Value + known[0].ProposedBytes);
        return target > 0 ? target : null;
    }

    /// <summary>
    /// Moves one currently generated, compatible prior-success candidate to
    /// the front without bypassing the configured baseline or changing the
    /// bounded candidate set. A fresh admission plan is still required.
    /// </summary>
    public static AdaptiveInferencePlan PreferCandidate(
        AdaptiveInferencePlan plan,
        string? candidateId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(candidateId))
            return plan;

        var preferred = plan.Candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.CandidateId, candidateId, StringComparison.Ordinal));
        if (preferred is null || preferred.Ordinal == 0)
            return plan;

        var ordered = plan.Candidates
            .Where(candidate => !ReferenceEquals(candidate, preferred))
            .Prepend(preferred)
            .Select((candidate, ordinal) => candidate with { Ordinal = ordinal })
            .ToArray();
        return plan with { Candidates = ordered };
    }

    private static int ResolveMinimumContext(int configured, AdaptiveInferenceEnvelope envelope, List<string> reasons)
    {
        var minimum = envelope.MinimumContext == 0 ? configured : envelope.MinimumContext;
        if (minimum > configured)
            reasons.Add("The adaptive minimum context exceeds the configured context; no context reduction is offered.");
        return Math.Min(configured, minimum);
    }

    private static void ApplyFitControls(ServerConfig config, LlamaRuntimeCapabilityFacts runtime, long? target, int? minimumContext)
    {
        config.RuntimeSupportsFit = IsAvailable(runtime, "runtime.fit");
        config.RuntimeSupportsFitTarget = config.RuntimeSupportsFit && IsAvailable(runtime, "runtime.fit.target");
        config.RuntimeSupportsFitMinimumContext = config.RuntimeSupportsFit && IsAvailable(runtime, "runtime.fit.minimum-context");
        config.RuntimeFitTargetBytes = config.RuntimeSupportsFitTarget ? target : null;
        config.RuntimeFitMinimumContext = config.RuntimeSupportsFitMinimumContext ? minimumContext : null;
    }

    private static bool IsAvailable(LlamaRuntimeCapabilityFacts runtime, string id) =>
        runtime.LaunchCapabilities is not null
        && runtime.LaunchCapabilities.TryGetValue(id, out var evidence)
        && evidence.State == CapabilityState.Available;

    private static string EffectiveKvType(ServerConfig config) =>
        !string.IsNullOrWhiteSpace(config.KvCacheType) ? config.KvCacheType : config.KvCacheTypeK;

    private static IReadOnlyList<string> ChangedFields(ServerConfig source, ServerConfig candidate)
    {
        var changes = new List<string>();
        if (source.ContextSize != candidate.ContextSize) changes.Add("context");
        if (source.GpuPlacement?.CanonicalValue != candidate.GpuPlacement?.CanonicalValue) changes.Add("GPU placement");
        if (!string.Equals(EffectiveKvType(source), EffectiveKvType(candidate), StringComparison.OrdinalIgnoreCase)) changes.Add("KV precision");
        if (source.CpuMoeLayers != candidate.CpuMoeLayers) changes.Add("CPU-MoE placement");
        return changes;
    }

    private static ServerConfig Clone(ServerConfig source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        ExecutablePath = source.ExecutablePath,
        ModelPath = source.ModelPath,
        Port = source.Port,
        ContextSize = source.ContextSize,
        GpuLayers = source.GpuLayers,
        GpuPlacement = source.GpuPlacement is null ? null : new GpuPlacementIntent
        {
            SchemaVersion = source.GpuPlacement.SchemaVersion,
            Kind = source.GpuPlacement.Kind,
            ExactLayerCount = source.GpuPlacement.ExactLayerCount
        },
        Threads = source.Threads,
        PromptThreads = source.PromptThreads,
        Slots = source.Slots,
        EmbeddingsMode = source.EmbeddingsMode,
        AutoStart = source.AutoStart,
        ExtraArgs = source.ExtraArgs,
        AdaptiveEnvelope = source.AdaptiveEnvelope?.Clone() ?? new AdaptiveInferenceEnvelope(),
        MmprojPath = source.MmprojPath,
        UseProjector = source.UseProjector,
        KvCacheType = source.KvCacheType,
        KvCacheTypeK = source.KvCacheTypeK,
        KvCacheTypeV = source.KvCacheTypeV,
        PreserveReasoning = source.PreserveReasoning,
        ReasoningPreserveSupported = source.ReasoningPreserveSupported,
        FlashAttention = source.FlashAttention,
        ContextShift = source.ContextShift,
        MemoryLock = source.MemoryLock,
        NoMemoryMap = source.NoMemoryMap,
        CpuMoeLayers = source.CpuMoeLayers,
        NgramSpeculative = source.NgramSpeculative,
        Speculative = new SpeculativeDecodingConfig
        {
            Types = source.Speculative?.Types.ToList() ?? [],
            DraftModelPath = source.Speculative?.DraftModelPath ?? string.Empty,
            DraftGpuLayers = source.Speculative?.DraftGpuLayers,
            NMax = source.Speculative?.NMax,
            NMin = source.Speculative?.NMin,
            PMin = source.Speculative?.PMin
        },
        RuntimeHelpProbed = source.RuntimeHelpProbed,
        RuntimeSpeculativeTypes = source.RuntimeSpeculativeTypes,
        RuntimeSupportsPromptThreads = source.RuntimeSupportsPromptThreads,
        RuntimeSupportsLoadMode = source.RuntimeSupportsLoadMode,
        RuntimeSupportsCorsOrigins = source.RuntimeSupportsCorsOrigins,
        RuntimeSupportsGpuPlacementCpu = source.RuntimeSupportsGpuPlacementCpu,
        RuntimeSupportsGpuPlacementAuto = source.RuntimeSupportsGpuPlacementAuto,
        RuntimeSupportsGpuPlacementAll = source.RuntimeSupportsGpuPlacementAll,
        RuntimeSupportsGpuPlacementExact = source.RuntimeSupportsGpuPlacementExact,
        RuntimeSupportsFit = source.RuntimeSupportsFit,
        RuntimeSupportsFitTarget = source.RuntimeSupportsFitTarget,
        RuntimeSupportsFitMinimumContext = source.RuntimeSupportsFitMinimumContext,
        RuntimeFitTargetBytes = source.RuntimeFitTargetBytes,
        RuntimeFitMinimumContext = source.RuntimeFitMinimumContext
    };
}
