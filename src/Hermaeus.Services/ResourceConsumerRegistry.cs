using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>
/// Services-owned registry for logical local-AI consumers and their resident
/// allocations. It composes current allocations with read-only in-process
/// adapters when a snapshot is requested. Admission and reservations are kept
/// in the separate resource coordinator so this registry remains lifecycle
/// state, not a planner.
/// </summary>
public sealed class ResourceConsumerRegistry : IResourceConsumerRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ResourceConsumerDescriptor> _consumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceAllocation> _allocations = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<IResourceConsumerAdapter> _adapters;

    public ResourceConsumerRegistry(IEnumerable<IResourceConsumerAdapter>? adapters = null)
    {
        _adapters = (adapters ?? []).ToArray();
        foreach (var adapter in _adapters)
            RegisterConsumer(adapter.Descriptor);
    }

    public IReadOnlyList<ResourceConsumerDescriptor> Consumers
    {
        get
        {
            lock (_gate)
                return _consumers.Values.OrderBy(consumer => consumer.ConsumerId, StringComparer.Ordinal).ToArray();
        }
    }

    public void RegisterConsumer(ResourceConsumerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_gate)
        {
            if (_consumers.TryGetValue(descriptor.ConsumerId, out var existing))
            {
                if (!AreEquivalent(existing, descriptor))
                    throw new InvalidOperationException($"Consumer id '{descriptor.ConsumerId}' is already registered for a different owner.");
                return;
            }

            _consumers.Add(descriptor.ConsumerId, descriptor);
        }
    }

    public void RegisterAllocation(ResourceAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        lock (_gate)
        {
            EnsureRegistered(allocation.ConsumerId);
            EnsureAllocationShape(allocation);
            if (_allocations.ContainsKey(allocation.AllocationId))
                throw new InvalidOperationException($"Allocation id '{allocation.AllocationId}' is already registered.");
            if (_allocations.Values.Any(existing => existing.Components.Any(component =>
                    string.Equals(component.ComponentId, allocation.AllocationId, StringComparison.Ordinal)))
                || allocation.Components.Any(component => _allocations.ContainsKey(component.ComponentId)))
                throw new InvalidOperationException("A component cannot also be a top-level allocation.");

            _allocations.Add(allocation.AllocationId, allocation);
        }
    }

    public void UpdateAllocation(ResourceAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        lock (_gate)
        {
            EnsureRegistered(allocation.ConsumerId);
            EnsureAllocationShape(allocation);
            if (!_allocations.TryGetValue(allocation.AllocationId, out var existing))
                throw new KeyNotFoundException($"Allocation '{allocation.AllocationId}' is not registered.");
            if (!string.Equals(existing.ConsumerId, allocation.ConsumerId, StringComparison.Ordinal))
                throw new InvalidOperationException("An allocation cannot change owners.");
            if (!IsValidTransition(existing.LifecycleState, allocation.LifecycleState))
                throw new InvalidOperationException($"Allocation state cannot move from {existing.LifecycleState} to {allocation.LifecycleState}.");
            _allocations[allocation.AllocationId] = allocation;
        }
    }

    public bool RemoveAllocation(string allocationId)
    {
        if (string.IsNullOrWhiteSpace(allocationId))
            return false;
        lock (_gate)
        {
            if (!_allocations.TryGetValue(allocationId, out var allocation)
                || allocation.LifecycleState is not (ResourceLifecycleState.Released or ResourceLifecycleState.Failed or ResourceLifecycleState.Unavailable))
                return false;
            return _allocations.Remove(allocationId);
        }
    }

    public bool TryReleaseAllocation(string allocationId)
    {
        if (string.IsNullOrWhiteSpace(allocationId))
            return false;
        lock (_gate)
        {
            if (!_allocations.TryGetValue(allocationId, out var allocation))
                return false;
            if (allocation.LifecycleState is not (ResourceLifecycleState.Released or ResourceLifecycleState.Failed or ResourceLifecycleState.Unavailable))
            {
                var released = new ResourceAllocation(
                    allocation.AllocationId,
                    allocation.ConsumerId,
                    allocation.AttemptId,
                    ResourceLifecycleState.Released,
                    allocation.RuntimeIdentity,
                    allocation.ModelIdentities,
                    allocation.ConfigurationIdentity,
                    allocation.ProcessIdentity,
                    allocation.Components,
                    allocation.StartedAtUtc,
                    allocation.Evidence);
                _allocations[allocationId] = released;
            }
            return _allocations.Remove(allocationId);
        }
    }

    public async Task<ResourceSnapshot> CaptureSnapshotAsync(ResourceSnapshotCapture capture, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ResourceConsumerDescriptor[] consumers;
        ResourceAllocation[] registeredAllocations;
        lock (_gate)
        {
            consumers = _consumers.Values.OrderBy(consumer => consumer.ConsumerId, StringComparer.Ordinal).ToArray();
            registeredAllocations = _allocations.Values.OrderBy(allocation => allocation.AllocationId, StringComparer.Ordinal).ToArray();
        }

        var allocations = registeredAllocations.ToList();
        var unknowns = new List<ResourceUnknown>(capture.Unknowns);
        foreach (var adapter in _adapters)
        {
            ct.ThrowIfCancellationRequested();
            ResourceAllocation? allocation;
            try
            {
                allocation = await adapter.CaptureAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                unknowns.Add(new ResourceUnknown(
                    "resource-adapter-failed",
                    "The in-process resource adapter did not return a trustworthy state.",
                    adapter.Descriptor.ConsumerId));
                continue;
            }

            if (allocation is null)
                continue;
            try
            {
                if (!string.Equals(allocation.ConsumerId, adapter.Descriptor.ConsumerId, StringComparison.Ordinal))
                    throw new InvalidOperationException("The adapter returned an allocation for a different consumer.");
                EnsureRegistered(allocation.ConsumerId, consumers);
                EnsureAllocationShape(allocation);
            }
            catch
            {
                unknowns.Add(new ResourceUnknown(
                    "resource-adapter-invalid",
                    "The in-process resource adapter returned an invalid allocation.",
                    adapter.Descriptor.ConsumerId));
                continue;
            }

            if (allocations.Any(existing => string.Equals(existing.AllocationId, allocation.AllocationId, StringComparison.Ordinal)))
            {
                // A lifecycle owner publishes the authoritative allocation
                // after admission. The adapter is a discovery fallback for
                // sessions loaded outside that owner, so the same identity is
                // not false-positive duplicate evidence.
                continue;
            }
            allocations.Add(allocation);
        }

        var observations = capture.Observations.Concat(allocations.SelectMany(allocation => allocation.Evidence)).ToArray();
        unknowns.AddRange(allocations
            .Where(allocation => allocation.LifecycleState is ResourceLifecycleState.Active or ResourceLifecycleState.Idle or ResourceLifecycleState.Starting)
            .SelectMany(allocation => allocation.Components
                .Where(component => component.ObservedBytes is null)
                .Select(component => new ResourceUnknown(
                    "resource-component-unobserved",
                    $"Component '{component.ComponentId}' has no authoritative observed byte count.",
                    allocation.ConsumerId,
                    component.DeviceId))));

        return new ResourceSnapshot(
            Guid.NewGuid().ToString("N"),
            capture.HardwareIdentity,
            DateTime.UtcNow,
            consumers,
            allocations,
            observations,
            unknowns,
            capture.DeviceTotals);
    }

    private void EnsureRegistered(string consumerId) =>
        EnsureRegistered(consumerId, _consumers.Values.ToArray());

    private static void EnsureRegistered(string consumerId, IEnumerable<ResourceConsumerDescriptor> consumers)
    {
        if (!consumers.Any(consumer => string.Equals(consumer.ConsumerId, consumerId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Consumer '{consumerId}' must be registered before an allocation can be published.");
    }

    private static void EnsureAllocationShape(ResourceAllocation allocation)
    {
        var duplicateComponent = allocation.Components
            .GroupBy(component => component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateComponent is not null)
            throw new InvalidOperationException($"Allocation '{allocation.AllocationId}' repeats component '{duplicateComponent.Key}'.");
    }

    private static bool IsValidTransition(ResourceLifecycleState from, ResourceLifecycleState to)
    {
        if (from == to)
            return true;
        return from switch
        {
        ResourceLifecycleState.Planned => to is ResourceLifecycleState.Starting or ResourceLifecycleState.Failed or ResourceLifecycleState.Unavailable or ResourceLifecycleState.Released,
        ResourceLifecycleState.Starting => to is ResourceLifecycleState.Active or ResourceLifecycleState.Idle or ResourceLifecycleState.Stopping or ResourceLifecycleState.Failed or ResourceLifecycleState.Unavailable or ResourceLifecycleState.Released,
        ResourceLifecycleState.Active => to is ResourceLifecycleState.Idle or ResourceLifecycleState.Stopping or ResourceLifecycleState.Failed or ResourceLifecycleState.Released,
        ResourceLifecycleState.Idle => to is ResourceLifecycleState.Active or ResourceLifecycleState.Stopping or ResourceLifecycleState.Failed or ResourceLifecycleState.Released,
        ResourceLifecycleState.Stopping => to is ResourceLifecycleState.Released or ResourceLifecycleState.Failed or ResourceLifecycleState.Unavailable,
        _ => false
        };
    }

    private static bool AreEquivalent(ResourceConsumerDescriptor left, ResourceConsumerDescriptor right) =>
        left.Kind == right.Kind
        && left.Owner == right.Owner
        && string.Equals(left.OwningLifecycleService, right.OwningLifecycleService, StringComparison.Ordinal)
        && left.PriorityClass == right.PriorityClass
        && left.Reclaimability == right.Reclaimability
        && left.SupportedResourceKinds.SequenceEqual(right.SupportedResourceKinds);
}
