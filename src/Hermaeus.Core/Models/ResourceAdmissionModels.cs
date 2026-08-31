namespace Hermaeus.Core.Models;

public enum ResourcePlanFeasibility
{
    Fits,
    FitsWithBoundedAdaptation,
    DoesNotFit,
    Unknown
}

/// <summary>
/// Explicit safety reservations used by whole-workload admission. These are
/// planning margins, not physical device reservations and never authorize
/// stopping or changing another consumer.
/// </summary>
public sealed record ResourceHeadroomPolicy
{
    public const long DefaultDeviceStabilityBytes = 512L * 1024 * 1024;
    public const long DefaultSystemStabilityBytes = 256L * 1024 * 1024;
    public const long DefaultInteractiveBytes = 256L * 1024 * 1024;
    public const long DefaultForegroundBytes = 128L * 1024 * 1024;
    public const long DefaultInProcessBytes = 128L * 1024 * 1024;
    public const long DefaultUnknownDeviceBytes = 256L * 1024 * 1024;

    public long DeviceStabilityBytes { get; }
    public long SystemStabilityBytes { get; }
    public long InteractiveReservationBytes { get; }
    public long ForegroundReservationBytes { get; }
    public long InProcessReservationBytes { get; }
    public long UnknownDeviceReservationBytes { get; }
    public TimeSpan ReservationLifetime { get; }

    public ResourceHeadroomPolicy(
        long deviceStabilityBytes = DefaultDeviceStabilityBytes,
        long systemStabilityBytes = DefaultSystemStabilityBytes,
        long interactiveReservationBytes = DefaultInteractiveBytes,
        long foregroundReservationBytes = DefaultForegroundBytes,
        long inProcessReservationBytes = DefaultInProcessBytes,
        long unknownDeviceReservationBytes = DefaultUnknownDeviceBytes,
        TimeSpan? reservationLifetime = null)
    {
        DeviceStabilityBytes = ResourceModelValidation.NonNegative(deviceStabilityBytes, nameof(deviceStabilityBytes)) ?? 0;
        SystemStabilityBytes = ResourceModelValidation.NonNegative(systemStabilityBytes, nameof(systemStabilityBytes)) ?? 0;
        InteractiveReservationBytes = ResourceModelValidation.NonNegative(interactiveReservationBytes, nameof(interactiveReservationBytes)) ?? 0;
        ForegroundReservationBytes = ResourceModelValidation.NonNegative(foregroundReservationBytes, nameof(foregroundReservationBytes)) ?? 0;
        InProcessReservationBytes = ResourceModelValidation.NonNegative(inProcessReservationBytes, nameof(inProcessReservationBytes)) ?? 0;
        UnknownDeviceReservationBytes = ResourceModelValidation.NonNegative(unknownDeviceReservationBytes, nameof(unknownDeviceReservationBytes)) ?? 0;
        var lifetime = reservationLifetime ?? TimeSpan.FromMinutes(2);
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(reservationLifetime), "Reservation lifetime must be between one tick and ten minutes.");
        ReservationLifetime = lifetime;
    }

    public static ResourceHeadroomPolicy Conservative { get; } = new();

    public long ForPriority(ResourcePriorityClass priority) => priority switch
    {
        ResourcePriorityClass.Interactive => InteractiveReservationBytes,
        ResourcePriorityClass.Foreground => ForegroundReservationBytes,
        _ => 0
    };
}

public sealed record ResourceAdmissionRequest
{
    public string RequestedConsumerId { get; }
    public ResourceAllocation ProposedAllocation { get; }
    public ResourceHeadroomPolicy HeadroomPolicy { get; }
    public string CallerId { get; }
    public bool AllowUnknown { get; }
    public DateTime RequestedAtUtc { get; }

    public ResourceAdmissionRequest(
        string requestedConsumerId,
        ResourceAllocation proposedAllocation,
        ResourceHeadroomPolicy? headroomPolicy = null,
        string callerId = "unspecified",
        bool allowUnknown = false,
        DateTime? requestedAtUtc = null)
    {
        RequestedConsumerId = ResourceModelValidation.Opaque(requestedConsumerId, nameof(requestedConsumerId));
        ProposedAllocation = proposedAllocation ?? throw new ArgumentNullException(nameof(proposedAllocation));
        if (!string.Equals(RequestedConsumerId, ProposedAllocation.ConsumerId, StringComparison.Ordinal))
            throw new ArgumentException("The requested consumer must own the proposed allocation.", nameof(proposedAllocation));
        HeadroomPolicy = headroomPolicy ?? ResourceHeadroomPolicy.Conservative;
        CallerId = ResourceModelValidation.PathFreeText(callerId, ResourceModelValidation.MaximumEvidenceCodeLength, nameof(callerId));
        AllowUnknown = allowUnknown;
        RequestedAtUtc = requestedAtUtc ?? DateTime.UtcNow;
        ResourceModelValidation.RequireUtc(RequestedAtUtc, nameof(requestedAtUtc));
    }
}

public sealed record ResourceDeviceHeadroom(
    string DeviceId,
    long? CapacityBytes,
    long? UsedBytes,
    long ReservedBytes,
    long ProposedBytes,
    long RequiredHeadroomBytes,
    long? RemainingBytes,
    bool IsKnown);

public sealed record ResourceReservationSummary(
    string ReservationId,
    string PlanId,
    string ConsumerId,
    ResourcePriorityClass PriorityClass,
    DateTime ExpiresAtUtc,
    IReadOnlyDictionary<string, long> DeviceBytes,
    long SystemBytes);

public sealed record ResourceWorkloadPlan
{
    public string PlanId { get; }
    public string SnapshotId { get; }
    public string RequestedConsumerId { get; }
    public IReadOnlyList<string> ExistingConsumers { get; }
    public IReadOnlyList<ResourceAllocation> ProposedAllocations { get; }
    public IReadOnlyList<ResourceReservationSummary> PreservedReservations { get; }
    public IReadOnlyList<ResourceUnknown> UnknownComponents { get; }
    public ResourceHeadroomPolicy HeadroomPolicy { get; }
    public ResourcePlanFeasibility Feasibility { get; }
    public IReadOnlyList<ResourceDeviceHeadroom> DeviceHeadroom { get; }
    public long? SystemRemainingBytes { get; }
    public string DerivationVersion { get; }

    public ResourceWorkloadPlan(
        string planId,
        string snapshotId,
        string requestedConsumerId,
        IEnumerable<string>? existingConsumers,
        IEnumerable<ResourceAllocation>? proposedAllocations,
        IEnumerable<ResourceReservationSummary>? preservedReservations,
        IEnumerable<ResourceUnknown>? unknownComponents,
        ResourceHeadroomPolicy headroomPolicy,
        ResourcePlanFeasibility feasibility,
        IEnumerable<ResourceDeviceHeadroom>? deviceHeadroom,
        long? systemRemainingBytes,
        string derivationVersion)
    {
        PlanId = ResourceModelValidation.Opaque(planId, nameof(planId));
        SnapshotId = ResourceModelValidation.Opaque(snapshotId, nameof(snapshotId));
        RequestedConsumerId = ResourceModelValidation.Opaque(requestedConsumerId, nameof(requestedConsumerId));
        ExistingConsumers = ResourceModelValidation.List(existingConsumers ?? [], 128, nameof(existingConsumers));
        ProposedAllocations = ResourceModelValidation.List(proposedAllocations ?? [], 128, nameof(proposedAllocations));
        PreservedReservations = ResourceModelValidation.List(preservedReservations ?? [], 128, nameof(preservedReservations));
        UnknownComponents = ResourceModelValidation.List(unknownComponents ?? [], 256, nameof(unknownComponents));
        HeadroomPolicy = headroomPolicy ?? throw new ArgumentNullException(nameof(headroomPolicy));
        DeviceHeadroom = ResourceModelValidation.List(deviceHeadroom ?? [], 128, nameof(deviceHeadroom));
        // Negative remaining headroom is the evidence for DoesNotFit. It is
        // deliberately retained in the receipt rather than rejected as an
        // invalid byte count.
        SystemRemainingBytes = systemRemainingBytes;
        DerivationVersion = ResourceModelValidation.Opaque(derivationVersion, nameof(derivationVersion));
        Feasibility = feasibility;
    }
}

public interface IResourceAdmissionLease : IAsyncDisposable
{
    string LeaseId { get; }
    string PlanId { get; }
    string ConsumerId { get; }
    DateTime ExpiresAtUtc { get; }
    ResourceWorkloadPlan Plan { get; }
    bool IsCompleted { get; }
    bool IsReleased { get; }

    Task CompleteAsync(ResourceAllocation allocation, CancellationToken ct = default);
    Task ReleaseAsync(string reason = "released", CancellationToken ct = default);
}
