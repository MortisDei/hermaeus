using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Hermaeus.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceConsumerKind
{
    ChatRuntime,
    EmbeddingRuntime,
    ManagedRuntime,
    LabRuntime,
    Reranker,
    SpeechToText,
    TextToSpeech,
    ExternalDeviceUse
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceOwnerKind
{
    HermaeusOwnedProcess,
    HermaeusInProcess,
    ConfiguredExternalEndpoint,
    UnrelatedUnknownDeviceUse
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourcePriorityClass
{
    Interactive,
    Foreground,
    Background,
    Experiment
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceReclaimability
{
    Unloadable,
    Cooperative,
    NotReclaimable,
    Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceLifecycleState
{
    Planned,
    Starting,
    Active,
    Idle,
    Stopping,
    Released,
    Failed,
    Unavailable
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceComponentKind
{
    ModelWeights,
    KvCache,
    RuntimeCompute,
    Projector,
    Companion,
    OnnxSession,
    HostCache,
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceKind
{
    DeviceMemory,
    SystemResidentMemory,
    SystemCommit,
    SwapPressure,
    ModelWeights,
    KvAllocation,
    RuntimeComputeOverhead,
    HostPromptCache,
    CompanionAllocation
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceObservationScope
{
    Allocation,
    Consumer,
    Device,
    System
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceObservationTrustState
{
    TrustedRuntime,
    BuildScoped,
    ProcessScoped,
    DeviceTotal,
    Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceEvidenceState
{
    Predicted,
    Reserved,
    Observed,
    Unknown
}

/// <summary>
/// Stable, non-secret identity for who owns a resource. Endpoint URLs,
/// executable paths, and process ids do not belong in this identity.
/// </summary>
public sealed record ResourceOwnerIdentity
{
    public ResourceOwnerKind Kind { get; }
    public string StableId { get; }

    public ResourceOwnerIdentity(ResourceOwnerKind kind, string stableId)
    {
        Kind = kind;
        StableId = ResourceModelValidation.Opaque(stableId, nameof(stableId));
    }

    public static ResourceOwnerIdentity OwnedProcess(string runtimeStableId) =>
        new(ResourceOwnerKind.HermaeusOwnedProcess, runtimeStableId);

    public static ResourceOwnerIdentity InProcess(string componentId) =>
        new(ResourceOwnerKind.HermaeusInProcess, componentId);

    public static ResourceOwnerIdentity ExternalEndpoint(string endpointFingerprint) =>
        new(ResourceOwnerKind.ConfiguredExternalEndpoint, endpointFingerprint);

    public static ResourceOwnerIdentity UnknownDevice(string deviceId) =>
        new(ResourceOwnerKind.UnrelatedUnknownDeviceUse, deviceId);
}

/// <summary>Immutable registration metadata for one logical workload role.</summary>
public sealed record ResourceConsumerDescriptor
{
    public string ConsumerId { get; }
    public ResourceConsumerKind Kind { get; }
    public ResourceOwnerIdentity Owner { get; }
    public string OwningLifecycleService { get; }
    public ResourcePriorityClass PriorityClass { get; }
    public ResourceReclaimability Reclaimability { get; }
    public IReadOnlyList<ResourceKind> SupportedResourceKinds { get; }

    public ResourceConsumerDescriptor(
        string consumerId,
        ResourceConsumerKind kind,
        ResourceOwnerIdentity owner,
        string owningLifecycleService,
        ResourcePriorityClass priorityClass,
        ResourceReclaimability reclaimability,
        IEnumerable<ResourceKind>? supportedResourceKinds = null)
    {
        ConsumerId = ResourceModelValidation.Opaque(consumerId, nameof(consumerId));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        OwningLifecycleService = ResourceModelValidation.Opaque(owningLifecycleService, nameof(owningLifecycleService));
        Kind = kind;
        PriorityClass = priorityClass;
        Reclaimability = reclaimability;
        SupportedResourceKinds = ResourceModelValidation.List(
            supportedResourceKinds ?? [], ResourceModelValidation.MaximumSupportedKinds, nameof(supportedResourceKinds));
    }
}

public sealed record ResourceAllocationComponent
{
    /// <summary>
    /// Stable within its owning allocation. Cross-allocation uniqueness is
    /// expressed by the full identity tuple (AllocationId, ComponentId,
    /// ResourceKind, DeviceId), so independent workloads may both contain a
    /// component named "weights".
    /// </summary>
    public string ComponentId { get; }
    public ResourceComponentKind Kind { get; }
    public ResourceKind ResourceKind { get; }
    public string? DeviceId { get; }
    public long? PredictedBytes { get; }
    public long? ReservedBytes { get; }
    public long? ObservedBytes { get; }
    public ResourceEvidenceState EvidenceState { get; }

    public ResourceAllocationComponent(
        string componentId,
        ResourceComponentKind kind,
        string? deviceId,
        long? predictedBytes,
        long? reservedBytes,
        long? observedBytes,
        ResourceEvidenceState evidenceState)
        : this(componentId, kind, deviceId, predictedBytes, reservedBytes, observedBytes, evidenceState,
            ResourceKind.DeviceMemory)
    {
    }

    public ResourceAllocationComponent(
        string componentId,
        ResourceComponentKind kind,
        string? deviceId,
        long? predictedBytes,
        long? reservedBytes,
        long? observedBytes,
        ResourceEvidenceState evidenceState,
        ResourceKind resourceKind)
    {
        ComponentId = ResourceModelValidation.Opaque(componentId, nameof(componentId));
        DeviceId = ResourceModelValidation.NullableOpaque(deviceId, nameof(deviceId));
        PredictedBytes = ResourceModelValidation.NonNegative(predictedBytes, nameof(predictedBytes));
        ReservedBytes = ResourceModelValidation.NonNegative(reservedBytes, nameof(reservedBytes));
        ObservedBytes = ResourceModelValidation.NonNegative(observedBytes, nameof(observedBytes));
        Kind = kind;
        ResourceKind = resourceKind;
        EvidenceState = evidenceState;
    }
}

public sealed record ResourceAllocation
{
    public string AllocationId { get; }
    public string ConsumerId { get; }
    public string? AttemptId { get; }
    public ResourceLifecycleState LifecycleState { get; }
    public RuntimeIdentityV2? RuntimeIdentity { get; }
    public IReadOnlyList<ModelIdentityV2> ModelIdentities { get; }
    public ConfigurationIdentityV2? ConfigurationIdentity { get; }
    public string? ProcessIdentity { get; }
    public IReadOnlyList<ResourceAllocationComponent> Components { get; }
    public DateTime? StartedAtUtc { get; }
    public IReadOnlyList<ResourceObservation> Evidence { get; }

    public ResourceAllocation(
        string allocationId,
        string consumerId,
        string? attemptId,
        ResourceLifecycleState lifecycleState,
        RuntimeIdentityV2? runtimeIdentity,
        IEnumerable<ModelIdentityV2>? modelIdentities,
        ConfigurationIdentityV2? configurationIdentity,
        string? processIdentity,
        IEnumerable<ResourceAllocationComponent>? components,
        DateTime? startedAtUtc,
        IEnumerable<ResourceObservation>? evidence)
    {
        AllocationId = ResourceModelValidation.Opaque(allocationId, nameof(allocationId));
        ConsumerId = ResourceModelValidation.Opaque(consumerId, nameof(consumerId));
        AttemptId = ResourceModelValidation.NullableOpaque(attemptId, nameof(attemptId));
        ProcessIdentity = ResourceModelValidation.NullableOpaque(processIdentity, nameof(processIdentity));
        LifecycleState = lifecycleState;
        RuntimeIdentity = runtimeIdentity;
        ConfigurationIdentity = configurationIdentity;
        ModelIdentities = ResourceModelValidation.List(modelIdentities ?? [], ResourceModelValidation.MaximumModels, nameof(modelIdentities));
        Components = ResourceModelValidation.List(components ?? [], ResourceModelValidation.MaximumComponents, nameof(components));
        Evidence = ResourceModelValidation.List(evidence ?? [], ResourceModelValidation.MaximumEvidence, nameof(evidence));
        StartedAtUtc = startedAtUtc?.ToUniversalTime();
    }
}

public sealed record ResourceObservation
{
    public string ObservationId { get; }
    public ResourceKind ResourceKind { get; }
    public long? ValueBytes { get; }
    public long? CapacityBytes { get; }
    public ResourceObservationScope Scope { get; }
    public string? ConsumerId { get; }
    public string? DeviceId { get; }
    public string Source { get; }
    public ResourceObservationTrustState TrustState { get; }
    public DateTime ObservedAtUtc { get; }
    public string EvidenceCode { get; }
    public string Detail { get; }

    public ResourceObservation(
        string observationId,
        ResourceKind resourceKind,
        long? valueBytes,
        long? capacityBytes,
        ResourceObservationScope scope,
        string? consumerId,
        string? deviceId,
        string source,
        ResourceObservationTrustState trustState,
        DateTime observedAtUtc,
        string evidenceCode,
        string detail)
    {
        ObservationId = ResourceModelValidation.Opaque(observationId, nameof(observationId));
        ConsumerId = ResourceModelValidation.NullableOpaque(consumerId, nameof(consumerId));
        DeviceId = ResourceModelValidation.NullableOpaque(deviceId, nameof(deviceId));
        Source = ResourceModelValidation.PathFreeText(source, ResourceModelValidation.MaximumSourceLength, nameof(source));
        EvidenceCode = ResourceModelValidation.PathFreeText(evidenceCode, ResourceModelValidation.MaximumEvidenceCodeLength, nameof(evidenceCode));
        Detail = ResourceModelValidation.BoundedText(detail, ResourceModelValidation.MaximumDetailLength, nameof(detail));
        ValueBytes = ResourceModelValidation.NonNegative(valueBytes, nameof(valueBytes));
        CapacityBytes = ResourceModelValidation.NonNegative(capacityBytes, nameof(capacityBytes));
        if (ValueBytes.HasValue && CapacityBytes.HasValue && ValueBytes > CapacityBytes)
            throw new ArgumentException("Observed bytes cannot exceed capacity bytes.", nameof(valueBytes));
        if (scope == ResourceObservationScope.Device && ConsumerId is not null)
            throw new ArgumentException("A device-total observation cannot be attributed to a consumer.", nameof(consumerId));
        if (trustState == ResourceObservationTrustState.DeviceTotal && scope != ResourceObservationScope.Device)
            throw new ArgumentException("Device-total evidence must remain device-scoped.", nameof(scope));
        if (scope is ResourceObservationScope.Allocation or ResourceObservationScope.Consumer && ConsumerId is null)
            throw new ArgumentException("An allocation or consumer observation requires a consumer id.", nameof(consumerId));
        ResourceModelValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
        ResourceKind = resourceKind;
        Scope = scope;
        TrustState = trustState;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }
}

public sealed record ResourceUnknown
{
    public string Code { get; }
    public string Detail { get; }
    public string? ConsumerId { get; }
    public string? DeviceId { get; }

    public ResourceUnknown(string code, string detail, string? consumerId = null, string? deviceId = null)
    {
        Code = ResourceModelValidation.PathFreeText(code, ResourceModelValidation.MaximumEvidenceCodeLength, nameof(code));
        Detail = ResourceModelValidation.PathFreeText(detail, ResourceModelValidation.MaximumDetailLength, nameof(detail));
        ConsumerId = ResourceModelValidation.NullableOpaque(consumerId, nameof(consumerId));
        DeviceId = ResourceModelValidation.NullableOpaque(deviceId, nameof(deviceId));
    }
}

public sealed record ResourceDeviceTotal
{
    public string DeviceId { get; }
    public long? UsedBytes { get; }
    public long? CapacityBytes { get; }
    public string ObservationId { get; }

    public ResourceDeviceTotal(string deviceId, long? usedBytes, long? capacityBytes, string observationId)
    {
        DeviceId = ResourceModelValidation.Opaque(deviceId, nameof(deviceId));
        UsedBytes = ResourceModelValidation.NonNegative(usedBytes, nameof(usedBytes));
        CapacityBytes = ResourceModelValidation.NonNegative(capacityBytes, nameof(capacityBytes));
        if (UsedBytes.HasValue && CapacityBytes.HasValue && UsedBytes > CapacityBytes)
            throw new ArgumentException("Device used bytes cannot exceed capacity bytes.", nameof(usedBytes));
        ObservationId = ResourceModelValidation.Opaque(observationId, nameof(observationId));
    }
}

/// <summary>Immutable, bounded view of one workload/resource capture.</summary>
public sealed class ResourceSnapshot
{
    public const int MaximumConsumers = 128;
    public const int MaximumAllocations = 128;
    public const int MaximumObservations = 512;
    public const int MaximumUnknowns = 256;
    public const int MaximumDeviceTotals = 128;

    public string SnapshotId { get; }
    public HardwareIdentityV2 HardwareIdentity { get; }
    public DateTime CapturedAtUtc { get; }
    public IReadOnlyList<ResourceConsumerDescriptor> Consumers { get; }
    public IReadOnlyList<ResourceAllocation> Allocations { get; }
    public IReadOnlyList<ResourceObservation> Observations { get; }
    public IReadOnlyList<ResourceUnknown> Unknowns { get; }
    public IReadOnlyList<ResourceDeviceTotal> DeviceTotals { get; }
    public IReadOnlyList<ResourceObservation> AuthoritativeObservations { get; }

    public ResourceSnapshot(
        string snapshotId,
        HardwareIdentityV2 hardwareIdentity,
        DateTime capturedAtUtc,
        IEnumerable<ResourceConsumerDescriptor>? consumers,
        IEnumerable<ResourceAllocation>? allocations,
        IEnumerable<ResourceObservation>? observations,
        IEnumerable<ResourceUnknown>? unknowns,
        IEnumerable<ResourceDeviceTotal>? deviceTotals)
    {
        SnapshotId = ResourceModelValidation.Opaque(snapshotId, nameof(snapshotId));
        HardwareIdentity = hardwareIdentity ?? throw new ArgumentNullException(nameof(hardwareIdentity));
        ResourceModelValidation.RequireUtc(capturedAtUtc, nameof(capturedAtUtc));
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        Consumers = ResourceModelValidation.List(consumers ?? [], MaximumConsumers, nameof(consumers));
        Allocations = ResourceModelValidation.List(allocations ?? [], MaximumAllocations, nameof(allocations));
        Observations = ResourceModelValidation.List(observations ?? [], MaximumObservations, nameof(observations));
        Unknowns = ResourceModelValidation.List(unknowns ?? [], MaximumUnknowns, nameof(unknowns));
        DeviceTotals = ResourceModelValidation.List(deviceTotals ?? [], MaximumDeviceTotals, nameof(deviceTotals));
        ResourceModelValidation.EnsureUnique(Consumers.Select(consumer => consumer.ConsumerId), "consumer id");
        ResourceModelValidation.EnsureUnique(Allocations.Select(allocation => allocation.AllocationId), "allocation id");
        ResourceModelValidation.EnsureUnique(Observations.Select(observation => observation.ObservationId), "observation id");
        ResourceModelValidation.EnsureUnique(DeviceTotals.Select(total => total.DeviceId), "device id");
        var consumerIds = Consumers.Select(consumer => consumer.ConsumerId).ToHashSet(StringComparer.Ordinal);
        if (Allocations.Any(allocation => !consumerIds.Contains(allocation.ConsumerId)))
            throw new ArgumentException("Every allocation must belong to a registered consumer.", nameof(allocations));
        if (Observations.Any(observation => observation.ConsumerId is not null && !consumerIds.Contains(observation.ConsumerId)))
            throw new ArgumentException("Every attributed observation must belong to a registered consumer.", nameof(observations));
        AuthoritativeObservations = new ReadOnlyCollection<ResourceObservation>(
            Observations
                .GroupBy(ObservationKey.Create)
                .Select(group => group
                    .OrderBy(observation => ResourceObservationAuthority.Rank(observation.TrustState))
                    .ThenByDescending(observation => observation.ObservedAtUtc)
                    .ThenBy(observation => observation.ObservationId, StringComparer.Ordinal)
                    .First())
                .OrderBy(observation => observation.ObservationId, StringComparer.Ordinal)
                .ToArray());
    }

    private sealed record ObservationKey(
        ResourceKind ResourceKind,
        ResourceObservationScope Scope,
        string? ConsumerId,
        string? DeviceId)
    {
        public static ObservationKey Create(ResourceObservation observation) => new(
            observation.ResourceKind, observation.Scope, observation.ConsumerId, observation.DeviceId);
    }
}

public static class ResourceObservationAuthority
{
    public static int Rank(ResourceObservationTrustState trustState) => trustState switch
    {
        ResourceObservationTrustState.TrustedRuntime => 0,
        ResourceObservationTrustState.BuildScoped => 1,
        ResourceObservationTrustState.ProcessScoped => 2,
        ResourceObservationTrustState.DeviceTotal => 3,
        _ => 4
    };
}

/// <summary>Bounded, path-free persistence projection for a resource snapshot.</summary>
public sealed record PersistedResourceSnapshot(
    string SnapshotId,
    DateTime CapturedAtUtc,
    string HardwareIdentityId,
    IReadOnlyList<PersistedResourceConsumer> Consumers,
    IReadOnlyList<PersistedResourceAllocation> Allocations,
    IReadOnlyList<PersistedResourceObservation> Observations,
    IReadOnlyList<ResourceUnknown> Unknowns,
    IReadOnlyList<ResourceDeviceTotal> DeviceTotals);

public sealed record PersistedResourceConsumer(
    string ConsumerId,
    ResourceConsumerKind Kind,
    ResourceOwnerKind OwnerKind,
    string OwnerStableId,
    ResourcePriorityClass PriorityClass);

public sealed record PersistedResourceAllocation(
    string AllocationId,
    string ConsumerId,
    ResourceLifecycleState LifecycleState,
    string? RuntimeStableId,
    IReadOnlyList<string> ModelStableIds,
    string? ConfigurationStableId,
    IReadOnlyList<PersistedResourceComponent> Components);

public sealed record PersistedResourceComponent(
    string ComponentId,
    ResourceComponentKind Kind,
    string? DeviceId,
    long? PredictedBytes,
    long? ReservedBytes,
    long? ObservedBytes,
    ResourceEvidenceState EvidenceState,
    ResourceKind ResourceKind = ResourceKind.DeviceMemory);

public sealed record PersistedResourceObservation(
    string ObservationId,
    ResourceKind ResourceKind,
    long? ValueBytes,
    long? CapacityBytes,
    ResourceObservationScope Scope,
    string? ConsumerId,
    string? DeviceId,
    string Source,
    ResourceObservationTrustState TrustState,
    DateTime ObservedAtUtc,
    string EvidenceCode);

public static class ResourceSnapshotPersistenceProjection
{
    public static PersistedResourceSnapshot Project(ResourceSnapshot snapshot) => new(
        snapshot.SnapshotId,
        snapshot.CapturedAtUtc,
        snapshot.HardwareIdentity.StableId,
        snapshot.Consumers.Select(consumer => new PersistedResourceConsumer(
            consumer.ConsumerId, consumer.Kind, consumer.Owner.Kind, consumer.Owner.StableId, consumer.PriorityClass)).ToArray(),
        snapshot.Allocations.Select(allocation => new PersistedResourceAllocation(
            allocation.AllocationId,
            allocation.ConsumerId,
            allocation.LifecycleState,
            allocation.RuntimeIdentity?.StableId,
            allocation.ModelIdentities.Select(model => model.StableId).ToArray(),
            allocation.ConfigurationIdentity?.StableId,
            allocation.Components.Select(component => new PersistedResourceComponent(
                component.ComponentId, component.Kind, component.DeviceId, component.PredictedBytes,
                component.ReservedBytes, component.ObservedBytes, component.EvidenceState, component.ResourceKind)).ToArray())).ToArray(),
        snapshot.Observations.Select(observation => new PersistedResourceObservation(
            observation.ObservationId, observation.ResourceKind, observation.ValueBytes, observation.CapacityBytes,
            observation.Scope, observation.ConsumerId, observation.DeviceId, observation.Source,
            observation.TrustState, observation.ObservedAtUtc, observation.EvidenceCode)).ToArray(),
        snapshot.Unknowns.ToArray(),
        snapshot.DeviceTotals.ToArray());
}

internal static class ResourceModelValidation
{
    public const int MaximumSupportedKinds = 32;
    public const int MaximumModels = 16;
    public const int MaximumComponents = 64;
    public const int MaximumEvidence = 128;
    public const int MaximumSourceLength = 128;
    public const int MaximumEvidenceCodeLength = 128;
    public const int MaximumDetailLength = 512;

    public static string Opaque(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("An opaque identity is required.", parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > 128 || trimmed.Contains('/') || trimmed.Contains('\\') || Path.IsPathRooted(trimmed))
            throw new ArgumentException("The identity must be a bounded opaque value, not a path.", parameterName);
        return trimmed;
    }

    public static string? NullableOpaque(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : Opaque(value, parameterName);

    public static string BoundedText(string value, int maximum, string parameterName)
    {
        if (value is null)
            throw new ArgumentNullException(parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > maximum)
            throw new ArgumentException($"The value cannot exceed {maximum} characters.", parameterName);
        return trimmed;
    }

    public static string PathFreeText(string value, int maximum, string parameterName)
    {
        var bounded = BoundedText(value, maximum, parameterName);
        if (bounded.Contains('/') || bounded.Contains('\\') || Path.IsPathRooted(bounded))
            throw new ArgumentException("The value must not contain a path.", parameterName);
        return bounded;
    }

    public static long? NonNegative(long? value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName, "A resource byte count cannot be negative.");
        return value;
    }

    public static IReadOnlyList<T> List<T>(IEnumerable<T> values, int maximum, string parameterName)
    {
        var array = values.ToArray();
        if (array.Length > maximum)
            throw new ArgumentException($"The collection cannot exceed {maximum} entries.", parameterName);
        return new ReadOnlyCollection<T>(array);
    }

    public static void EnsureUnique(IEnumerable<string> values, string description)
    {
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count())
            throw new ArgumentException($"A snapshot cannot repeat a {description}.", nameof(values));
    }

    public static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Resource timestamps must be UTC.", parameterName);
    }
}
