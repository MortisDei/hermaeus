using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class ResourceAdmissionException : InvalidOperationException
{
    public ResourceWorkloadPlan Plan { get; }

    public ResourceAdmissionException(ResourceWorkloadPlan plan, string message)
        : base(message) => Plan = plan;
}

/// <summary>
/// Serializes whole-workload planning and keeps short-lived in-memory
/// reservations. It coordinates lifecycle owners but never starts, stops,
/// unloads, kills, or changes another consumer.
/// </summary>
public sealed class ResourceCoordinator : IResourceCoordinator, IDisposable
{
    private const string DerivationVersion = "r32-resource-plan-v1";
    private readonly IResourceSnapshotSource _snapshots;
    private readonly IResourceConsumerRegistry _registry;
    private readonly SemaphoreSlim _decisionGate = new(1, 1);
    private readonly Dictionary<string, ActiveReservation> _reservations = new(StringComparer.Ordinal);
    private readonly LinkedList<ResourceWorkloadPlan> _recentPlans = [];
    private readonly LinkedList<ResourceReleaseReceipt> _recentReleaseReceipts = [];
    private bool _disposed;

    public ResourceCoordinator(IResourceSnapshotSource snapshots, IResourceConsumerRegistry registry)
    {
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<ResourceWorkloadPlan> RecentPlans
    {
        get
        {
            _decisionGate.Wait();
            try { return _recentPlans.ToArray(); }
            finally { _decisionGate.Release(); }
        }
    }

    public IReadOnlyList<ResourceReleaseReceipt> RecentReleaseReceipts
    {
        get
        {
            _decisionGate.Wait();
            try { return _recentReleaseReceipts.ToArray(); }
            finally { _decisionGate.Release(); }
        }
    }

    public void RegisterConsumer(ResourceConsumerDescriptor descriptor) => _registry.RegisterConsumer(descriptor);

    public Task<ResourceSnapshot> CaptureSnapshotAsync(CancellationToken ct = default) => _snapshots.CaptureAsync(ct);

    public async Task<ResourceWorkloadPlan> PlanAsync(ResourceAdmissionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _decisionGate.WaitAsync(ct);
        try
        {
            ThrowIfDisposed();
            CleanupExpiredReservations();
            var snapshot = await _snapshots.CaptureAsync(ct);
            var plan = BuildPlan(snapshot, request);
            RememberPlan(plan);
            return plan;
        }
        finally
        {
            _decisionGate.Release();
        }
    }

    public async Task<IResourceAdmissionLease> AcquireAsync(ResourceAdmissionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _decisionGate.WaitAsync(ct);
        try
        {
            ThrowIfDisposed();
            CleanupExpiredReservations();
            var snapshot = await _snapshots.CaptureAsync(ct);
            var plan = BuildPlan(snapshot, request);
            RememberPlan(plan);
            if (!snapshot.Consumers.Any(consumer => string.Equals(
                    consumer.ConsumerId, request.RequestedConsumerId, StringComparison.Ordinal)))
                throw new ResourceAdmissionException(plan,
                    "The requested workload has no registered consumer descriptor.");
            if (plan.Feasibility == ResourcePlanFeasibility.DoesNotFit)
                throw new ResourceAdmissionException(plan, "The requested workload exceeds the current whole-workload headroom.");
            if (plan.Feasibility == ResourcePlanFeasibility.Unknown && !request.AllowUnknown)
                throw new ResourceAdmissionException(plan, "Whole-workload admission is Unknown and requires review before starting.");

            var reservationId = $"reservation-{Guid.NewGuid():N}";
            var expiresAt = DateTime.UtcNow.Add(request.HeadroomPolicy.ReservationLifetime);
            var reservation = new ActiveReservation(
                reservationId,
                plan,
                request.ProposedAllocation,
                GetPriority(request.RequestedConsumerId, snapshot),
                expiresAt);
            _reservations.Add(reservationId, reservation);
            return new Lease(this, reservation);
        }
        finally
        {
            _decisionGate.Release();
        }
    }

    public void ReleaseAllocation(string allocationId) => _registry.TryReleaseAllocation(allocationId);

    internal async Task CompleteAsync(ActiveReservation reservation, ResourceAllocation allocation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        await _decisionGate.WaitAsync(ct);
        try
        {
            ThrowIfDisposed();
            EnsureActive(reservation);
            if (!string.Equals(allocation.ConsumerId, reservation.Plan.RequestedConsumerId, StringComparison.Ordinal))
                throw new InvalidOperationException("A lease can only complete its requested consumer allocation.");
            var proposed = reservation.ProposedAllocation;
            if (!string.Equals(allocation.AllocationId, proposed.AllocationId, StringComparison.Ordinal))
                throw new InvalidOperationException("A lease can only complete its proposed allocation.");
            if (allocation.LifecycleState is not (ResourceLifecycleState.Starting or ResourceLifecycleState.Active or ResourceLifecycleState.Idle))
                throw new InvalidOperationException("A completed admission lease requires a resident allocation state.");

            _registry.RegisterAllocation(allocation);
            _reservations.Remove(reservation.ReservationId);
            reservation.MarkCompleted();
        }
        finally
        {
            _decisionGate.Release();
        }
    }

    internal async Task ReleaseAsync(ActiveReservation reservation, string reason, CancellationToken ct)
    {
        await _decisionGate.WaitAsync(ct);
        try
        {
            if (_reservations.Remove(reservation.ReservationId))
            {
                reservation.MarkReleased();
                RememberRelease(reservation, reason);
            }
        }
        finally
        {
            _decisionGate.Release();
        }
    }

    private ResourceWorkloadPlan BuildPlan(ResourceSnapshot snapshot, ResourceAdmissionRequest request)
    {
        var descriptor = snapshot.Consumers.FirstOrDefault(c =>
            string.Equals(c.ConsumerId, request.RequestedConsumerId, StringComparison.Ordinal));
        var unknowns = new List<ResourceUnknown>(snapshot.Unknowns);
        if (descriptor is null)
        {
            unknowns.Add(new ResourceUnknown(
                "resource-consumer-unregistered",
                "The requested workload has no registered consumer descriptor.",
                request.RequestedConsumerId));
        }

        var activeAllocations = snapshot.Allocations
            .Where(a => a.LifecycleState is ResourceLifecycleState.Starting or ResourceLifecycleState.Active or ResourceLifecycleState.Idle)
            .ToArray();
        var existingConsumers = activeAllocations
            .Select(a => a.ConsumerId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var preserved = _reservations.Values
            .Where(r => !string.Equals(r.ProposedAllocation.AllocationId,
                request.ProposedAllocation.AllocationId, StringComparison.Ordinal))
            .Select(r => r.ToSummary())
            .OrderBy(r => r.ReservationId, StringComparer.Ordinal)
            .ToArray();

        var deviceTotals = snapshot.DeviceTotals.ToDictionary(t => t.DeviceId, StringComparer.Ordinal);
        var deviceHeadroom = new List<ResourceDeviceHeadroom>();
        var proposedByDevice = DeviceBytes(request.ProposedAllocation);
        var reservedByDevice = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var reservation in _reservations.Values)
        {
            foreach (var (deviceId, bytes) in DeviceBytes(reservation.ProposedAllocation))
                Add(reservedByDevice, deviceId, bytes);
        }

        var unknownDeviceBytes = request.ProposedAllocation.Components.Any(c =>
            c.ResourceKind == ResourceKind.DeviceMemory && c.DeviceId is null && Bytes(c) is not null);
        foreach (var deviceId in deviceTotals.Keys
                     .Concat(proposedByDevice.Keys)
                     .Concat(reservedByDevice.Keys)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            deviceTotals.TryGetValue(deviceId, out var total);
            proposedByDevice.TryGetValue(deviceId, out var proposedBytes);
            reservedByDevice.TryGetValue(deviceId, out var reservedBytes);
            var required = request.HeadroomPolicy.DeviceStabilityBytes
                + request.HeadroomPolicy.UnknownDeviceReservationBytes
                + request.HeadroomPolicy.ForPriority(descriptor?.PriorityClass ?? ResourcePriorityClass.Interactive);
            long? remaining = null;
            var known = total?.CapacityBytes is not null && total.UsedBytes is not null && !unknownDeviceBytes;
            if (known)
                remaining = total!.CapacityBytes!.Value - total.UsedBytes!.Value - reservedBytes - proposedBytes - required;
            else
                unknowns.Add(new ResourceUnknown(
                    "resource-device-headroom-unknown",
                    $"Device '{deviceId}' lacks a trustworthy capacity or usage pair, or the request has unassigned device memory.",
                    request.RequestedConsumerId,
                    deviceId));
            deviceHeadroom.Add(new ResourceDeviceHeadroom(
                deviceId,
                total?.CapacityBytes,
                total?.UsedBytes,
                reservedBytes,
                proposedBytes,
                required,
                remaining,
                known));
        }

        if (proposedByDevice.Count == 0 && deviceTotals.Count == 0
            && request.ProposedAllocation.Components.Any(c => c.ResourceKind == ResourceKind.DeviceMemory))
        {
            unknowns.Add(new ResourceUnknown(
                "resource-device-total-unknown",
                "No device total is available for a device-memory request.",
                request.RequestedConsumerId));
        }

        var systemObservation = snapshot.AuthoritativeObservations.FirstOrDefault(o =>
            o.ResourceKind == ResourceKind.SystemResidentMemory && o.Scope == ResourceObservationScope.System);
        var proposedSystem = SystemBytes(request.ProposedAllocation);
        var reservedSystem = _reservations.Values.Sum(r => SystemBytes(r.ProposedAllocation));
        var systemRequired = request.HeadroomPolicy.SystemStabilityBytes
            + request.HeadroomPolicy.InProcessReservationBytes
            + request.HeadroomPolicy.ForPriority(descriptor?.PriorityClass ?? ResourcePriorityClass.Interactive);
        long? systemRemaining = null;
        if (systemObservation?.CapacityBytes is long systemCapacity && systemObservation.ValueBytes is long systemUsed)
            systemRemaining = systemCapacity - systemUsed - reservedSystem - proposedSystem - systemRequired;
        else if (request.ProposedAllocation.Components.Any(c => c.ResourceKind == ResourceKind.SystemResidentMemory))
            unknowns.Add(new ResourceUnknown(
                "resource-system-headroom-unknown",
                "System resident-memory capacity or usage is not trustworthy for this request.",
                request.RequestedConsumerId));

        if (request.ProposedAllocation.Components.Any(c => Bytes(c) is null))
            unknowns.Add(new ResourceUnknown(
                "resource-proposal-bytes-unknown",
                "At least one proposed allocation component has no predicted, reserved, or observed byte count.",
                request.RequestedConsumerId));

        var definiteConflict = deviceHeadroom.Any(d => d.IsKnown && d.RemainingBytes < 0)
            || systemRemaining < 0;
        var feasibility = definiteConflict
            ? ResourcePlanFeasibility.DoesNotFit
            : unknowns.Count > 0
                ? ResourcePlanFeasibility.Unknown
                : ResourcePlanFeasibility.Fits;
        return new ResourceWorkloadPlan(
            $"plan-{Guid.NewGuid():N}",
            snapshot.SnapshotId,
            request.RequestedConsumerId,
            existingConsumers,
            [request.ProposedAllocation],
            preserved,
            unknowns,
            request.HeadroomPolicy,
            feasibility,
            deviceHeadroom,
            systemRemaining,
            DerivationVersion,
            snapshot.HardwareIdentity.StableId,
            snapshot.HardwareIdentity.Completeness == IdentityCompleteness.Complete);
    }

    private ResourcePriorityClass GetPriority(string consumerId, ResourceSnapshot snapshot) =>
        snapshot.Consumers.FirstOrDefault(c => string.Equals(c.ConsumerId, consumerId, StringComparison.Ordinal))?.PriorityClass
        ?? ResourcePriorityClass.Interactive;

    private void RememberPlan(ResourceWorkloadPlan plan)
    {
        _recentPlans.AddFirst(plan);
        while (_recentPlans.Count > 16)
            _recentPlans.RemoveLast();
    }

    private void CleanupExpiredReservations()
    {
        var now = DateTime.UtcNow;
        foreach (var reservation in _reservations.Values.Where(r => r.ExpiresAtUtc <= now).ToArray())
        {
            _reservations.Remove(reservation.ReservationId);
            reservation.MarkReleased();
            RememberRelease(reservation, "reservation expired");
        }
    }

    private void RememberRelease(ActiveReservation reservation, string reason)
    {
        var boundedReason = string.IsNullOrWhiteSpace(reason) ? "released" : reason.Trim();
        if (boundedReason.Length > 512)
            boundedReason = boundedReason[..512];
        _recentReleaseReceipts.AddFirst(new ResourceReleaseReceipt(
            reservation.ReservationId,
            reservation.Plan.PlanId,
            reservation.Plan.RequestedConsumerId,
            boundedReason,
            DateTime.UtcNow));
        while (_recentReleaseReceipts.Count > 32)
            _recentReleaseReceipts.RemoveLast();
    }

    private void EnsureActive(ActiveReservation reservation)
    {
        if (!_reservations.ContainsKey(reservation.ReservationId)
            || reservation.ExpiresAtUtc <= DateTime.UtcNow
            || reservation.IsReleased
            || reservation.IsCompleted)
            throw new InvalidOperationException("The resource admission lease is expired or no longer active.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ResourceCoordinator));
    }

    private static long? Bytes(ResourceAllocationComponent component) =>
        component.ObservedBytes ?? component.ReservedBytes ?? component.PredictedBytes;

    private static Dictionary<string, long> DeviceBytes(ResourceAllocation allocation)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var component in allocation.Components.Where(c => c.ResourceKind == ResourceKind.DeviceMemory && c.DeviceId is not null))
        {
            if (Bytes(component) is long bytes)
                Add(result, component.DeviceId!, bytes);
        }
        return result;
    }

    private static long SystemBytes(ResourceAllocation allocation) => allocation.Components
        .Where(c => c.ResourceKind == ResourceKind.SystemResidentMemory)
        .Select(Bytes)
        .Where(bytes => bytes is not null)
        .Sum(bytes => bytes!.Value);

    private static void Add(Dictionary<string, long> values, string key, long amount)
    {
        values[key] = values.GetValueOrDefault(key) + amount;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _reservations.Clear();
        _decisionGate.Dispose();
    }

    internal sealed class ActiveReservation(
        string reservationId,
        ResourceWorkloadPlan plan,
        ResourceAllocation proposedAllocation,
        ResourcePriorityClass priorityClass,
        DateTime expiresAtUtc)
    {
        public string ReservationId { get; } = reservationId;
        public ResourceWorkloadPlan Plan { get; } = plan;
        public ResourceAllocation ProposedAllocation { get; } = proposedAllocation;
        public ResourcePriorityClass PriorityClass { get; } = priorityClass;
        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
        public bool IsCompleted { get; private set; }
        public bool IsReleased { get; private set; }

        public void MarkCompleted() => IsCompleted = true;
        public void MarkReleased() => IsReleased = true;

        public ResourceReservationSummary ToSummary() => new(
            ReservationId,
            Plan.PlanId,
            Plan.RequestedConsumerId,
            PriorityClass,
            ExpiresAtUtc,
            DeviceBytes(ProposedAllocation),
            SystemBytes(ProposedAllocation));
    }

    private sealed class Lease(ResourceCoordinator owner, ActiveReservation reservation) : IResourceAdmissionLease
    {
        public string LeaseId => reservation.ReservationId;
        public string PlanId => reservation.Plan.PlanId;
        public string ConsumerId => reservation.Plan.RequestedConsumerId;
        public DateTime ExpiresAtUtc => reservation.ExpiresAtUtc;
        public ResourceWorkloadPlan Plan => reservation.Plan;
        public bool IsCompleted => reservation.IsCompleted;
        public bool IsReleased => reservation.IsReleased;

        public Task CompleteAsync(ResourceAllocation allocation, CancellationToken ct = default) =>
            owner.CompleteAsync(reservation, allocation, ct);

        public Task ReleaseAsync(string reason = "released", CancellationToken ct = default) =>
            owner.ReleaseAsync(reservation, reason, ct);

        public async ValueTask DisposeAsync()
        {
            if (!IsCompleted && !IsReleased)
                await ReleaseAsync("disposed");
        }
    }
}
