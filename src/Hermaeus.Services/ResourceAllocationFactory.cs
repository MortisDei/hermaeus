using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

/// <summary>
/// Builds bounded resource identities for lifecycle owners. File sizes are
/// analytical inputs only; an unobserved runtime or device placement remains
/// Unknown in the resulting allocation.
/// </summary>
public static class ResourceAllocationFactory
{
    public static ResourceConsumerDescriptor ManagedServerConsumer(ServerConfig config) => new(
        config.Id,
        config.EmbeddingsMode ? ResourceConsumerKind.EmbeddingRuntime : ResourceConsumerKind.ChatRuntime,
        ResourceOwnerIdentity.OwnedProcess($"managed-server-{config.Id}"),
        nameof(ProcessManagement.ServerProcessManager),
        config.EmbeddingsMode ? ResourcePriorityClass.Background : ResourcePriorityClass.Interactive,
        ResourceReclaimability.Cooperative,
        [ResourceKind.DeviceMemory, ResourceKind.SystemResidentMemory, ResourceKind.ModelWeights,
            ResourceKind.KvAllocation, ResourceKind.RuntimeComputeOverhead, ResourceKind.CompanionAllocation]);

    public static ResourceAllocation ManagedServerProposal(ServerConfig config) =>
        CreateManagedServerAllocation(config, ResourceLifecycleState.Planned, null, null);

    public static ResourceAllocation ActiveFromProcess(
        ResourceAllocation proposal,
        ManagedRuntimeProcessIdentity process) =>
        ActiveFromProcess(proposal, $"pid-{process.ProcessId}-started-{process.StartedAtUtc.Ticks}", process.StartedAtUtc);

    public static ResourceAllocation ActiveFromProcess(
        ResourceAllocation proposal,
        string processIdentity,
        DateTime startedAtUtc) =>
        new(
            proposal.AllocationId,
            proposal.ConsumerId,
            proposal.AttemptId,
            ResourceLifecycleState.Active,
            proposal.RuntimeIdentity,
            proposal.ModelIdentities,
            proposal.ConfigurationIdentity,
            processIdentity,
            proposal.Components,
            startedAtUtc,
            proposal.Evidence);

    public static ResourceConsumerDescriptor LocalVoiceProcessConsumer(string consumerId, string lifecycleService) => new(
        consumerId,
        ResourceConsumerKind.TextToSpeech,
        ResourceOwnerIdentity.OwnedProcess($"voice-process-{consumerId}"),
        lifecycleService,
        ResourcePriorityClass.Interactive,
        ResourceReclaimability.Cooperative,
        [ResourceKind.SystemResidentMemory, ResourceKind.DeviceMemory]);

    public static ResourceAllocation LocalVoiceProcessProposal(string consumerId) => new(
        $"allocation-{consumerId}",
        consumerId,
        null,
        ResourceLifecycleState.Planned,
        null,
        null,
        null,
        null,
        [new ResourceAllocationComponent(
            "voice-process",
            ResourceComponentKind.Other,
            null,
            null,
            null,
            null,
            ResourceEvidenceState.Unknown,
            ResourceKind.SystemResidentMemory)],
        null,
        null);

    public static ResourceConsumerDescriptor EmbeddingBackfillConsumer(string consumerId, string lifecycleService) => new(
        consumerId,
        ResourceConsumerKind.EmbeddingRuntime,
        ResourceOwnerIdentity.InProcess(consumerId),
        lifecycleService,
        ResourcePriorityClass.Background,
        ResourceReclaimability.Unloadable,
        [ResourceKind.SystemResidentMemory]);

    public static ResourceAllocation EmbeddingBackfillProposal(string consumerId) => new(
        $"allocation-{consumerId}",
        consumerId,
        null,
        ResourceLifecycleState.Planned,
        null,
        null,
        null,
        null,
        [new ResourceAllocationComponent(
            "embedding-backfill",
            ResourceComponentKind.Other,
            null,
            null,
            null,
            null,
            ResourceEvidenceState.Unknown,
            ResourceKind.SystemResidentMemory)],
        null,
        null);

    public static ResourceConsumerDescriptor LabConsumer(string runId) => new(
        LabConsumerId(runId),
        ResourceConsumerKind.LabRuntime,
        ResourceOwnerIdentity.OwnedProcess($"lab-runtime-{runId}"),
        nameof(IsolatedLabRuntimeHost),
        ResourcePriorityClass.Foreground,
        ResourceReclaimability.Cooperative,
        [ResourceKind.DeviceMemory, ResourceKind.SystemResidentMemory, ResourceKind.ModelWeights,
            ResourceKind.KvAllocation, ResourceKind.RuntimeComputeOverhead, ResourceKind.CompanionAllocation]);

    public static string LabConsumerId(string runId) => $"lab.{OpaquePart(runId)}";

    public static ResourceAllocation LabProposal(string runId, ServerConfig config)
    {
        var allocation = CreateManagedServerAllocation(config, ResourceLifecycleState.Planned, null, null);
        return new ResourceAllocation(
            $"lab-allocation-{OpaquePart(runId)}",
            LabConsumerId(runId),
            OpaquePart(runId),
            ResourceLifecycleState.Planned,
            allocation.RuntimeIdentity,
            allocation.ModelIdentities,
            allocation.ConfigurationIdentity,
            allocation.ProcessIdentity,
            allocation.Components,
            allocation.StartedAtUtc,
            allocation.Evidence);
    }

    public static ResourceAllocation InProcessProposal(
        string consumerId,
        ResourceComponentKind componentKind = ResourceComponentKind.OnnxSession) => new(
        $"inprocess-{consumerId}",
        consumerId,
        null,
        ResourceLifecycleState.Planned,
        null,
        null,
        null,
        null,
        [new ResourceAllocationComponent(
            "onnx-session",
            componentKind,
            null,
            null,
            null,
            null,
            ResourceEvidenceState.Unknown,
            ResourceKind.SystemResidentMemory)],
        null,
        null);

    public static ResourceConsumerDescriptor InProcessConsumer(
        string consumerId,
        ResourceConsumerKind kind,
        string lifecycleService,
        ResourcePriorityClass priority,
        ResourceReclaimability reclaimability) => new(
        consumerId,
        kind,
        ResourceOwnerIdentity.InProcess(consumerId),
        lifecycleService,
        priority,
        reclaimability,
        [ResourceKind.SystemResidentMemory, ResourceKind.DeviceMemory]);

    private static ResourceAllocation CreateManagedServerAllocation(
        ServerConfig config,
        ResourceLifecycleState state,
        ManagedRuntimeProcessIdentity? process,
        string? attemptId)
    {
        var components = new List<ResourceAllocationComponent>();
        var placement = config.TryGetGpuPlacement(out var intent, out _) ? intent : null;
        var memoryKind = placement?.Kind == GpuPlacementKind.Cpu
            ? ResourceKind.SystemResidentMemory
            : ResourceKind.DeviceMemory;
        string? deviceId = null;
        var modelBytes = TryGetSize(config.ModelPath);
        components.Add(new ResourceAllocationComponent(
            "model-weights",
            ResourceComponentKind.ModelWeights,
            deviceId,
            modelBytes,
            modelBytes,
            null,
            modelBytes.HasValue ? ResourceEvidenceState.Predicted : ResourceEvidenceState.Unknown,
            memoryKind));
        components.Add(new ResourceAllocationComponent(
            "kv-cache",
            ResourceComponentKind.KvCache,
            deviceId,
            null,
            null,
            null,
            ResourceEvidenceState.Unknown,
            memoryKind));
        components.Add(new ResourceAllocationComponent(
            "runtime-compute",
            ResourceComponentKind.RuntimeCompute,
            deviceId,
            null,
            null,
            null,
            ResourceEvidenceState.Unknown,
            memoryKind));
        AddCompanion("projector", ResourceComponentKind.Projector, config.UseProjector ? config.MmprojPath : string.Empty);
        var speculative = config.Speculative;
        AddCompanion("draft-companion", ResourceComponentKind.Companion,
            speculative?.Types.Contains("draft-mtp", StringComparer.OrdinalIgnoreCase) == true
                ? speculative.DraftModelPath : string.Empty);

        return new ResourceAllocation(
            $"managed-allocation-{config.Id}",
            config.Id,
            attemptId,
            state,
            null,
            null,
            null,
            process is null ? null : $"pid-{process.ProcessId}-started-{process.StartedAtUtc.Ticks}",
            components,
            process?.StartedAtUtc,
            null);

        void AddCompanion(string id, ResourceComponentKind kind, string path)
        {
            var bytes = TryGetSize(path);
            if (bytes is null && string.IsNullOrWhiteSpace(path))
                return;
            components.Add(new ResourceAllocationComponent(
                id,
                kind,
                null,
                bytes,
                bytes,
                null,
                bytes.HasValue ? ResourceEvidenceState.Predicted : ResourceEvidenceState.Unknown,
                ResourceKind.DeviceMemory));
        }
    }

    private static long? TryGetSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length >= 0 ? file.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string OpaquePart(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        var chars = trimmed.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        return new string(chars)[..Math.Min(chars.Length, 80)];
    }
}
