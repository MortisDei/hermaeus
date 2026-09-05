using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ResourceAdmissionTests
{
    [Fact]
    public async Task Plan_reports_known_device_headroom_and_preserves_the_policy()
    {
        using var coordinator = CreateCoordinator();
        coordinator.RegisterConsumer(Descriptor("chat"));

        var plan = await coordinator.PlanAsync(Request("chat", DeviceAllocation("chat", 300)));

        Assert.Equal(ResourcePlanFeasibility.Fits, plan.Feasibility);
        var headroom = Assert.Single(plan.DeviceHeadroom);
        Assert.Equal(1_000L, headroom.CapacityBytes);
        Assert.Equal(100L, headroom.UsedBytes);
        Assert.Equal(600L, headroom.RemainingBytes);
        Assert.True(headroom.IsKnown);
        Assert.Equal(0L, plan.HeadroomPolicy.DeviceStabilityBytes);
    }

    [Fact]
    public async Task Definite_device_or_system_conflicts_are_not_reported_as_unknown()
    {
        using var coordinator = CreateCoordinator(usedDeviceBytes: 900, usedSystemBytes: 900);
        coordinator.RegisterConsumer(Descriptor("chat"));

        var devicePlan = await coordinator.PlanAsync(Request("chat", DeviceAllocation("chat", 200)));
        var systemPlan = await coordinator.PlanAsync(Request("chat", SystemAllocation("chat", 200)));

        Assert.Equal(ResourcePlanFeasibility.DoesNotFit, devicePlan.Feasibility);
        Assert.Equal(-100L, Assert.Single(devicePlan.DeviceHeadroom).RemainingBytes);
        Assert.Equal(ResourcePlanFeasibility.DoesNotFit, systemPlan.Feasibility);
        Assert.Equal(-100L, systemPlan.SystemRemainingBytes);
    }

    [Fact]
    public async Task Unassigned_device_memory_is_unknown_and_requires_explicit_review()
    {
        using var coordinator = CreateCoordinator();
        coordinator.RegisterConsumer(Descriptor("chat"));
        var request = Request("chat", new ResourceAllocation(
            "chat-allocation", "chat", null, ResourceLifecycleState.Planned,
            null, [], null, null,
            [new ResourceAllocationComponent(
                "weights", ResourceComponentKind.ModelWeights, null, 100, 100, null,
                ResourceEvidenceState.Reserved, ResourceKind.DeviceMemory)],
            null, []));

        var plan = await coordinator.PlanAsync(request);
        Assert.Equal(ResourcePlanFeasibility.Unknown, plan.Feasibility);
        Assert.Contains(plan.UnknownComponents, unknown => unknown.Code == "resource-device-headroom-unknown");
        await Assert.ThrowsAsync<ResourceAdmissionException>(() => coordinator.AcquireAsync(request));
    }

    [Fact]
    public async Task Unregistered_consumer_is_refused_at_admission_even_when_unknown_is_allowed()
    {
        using var coordinator = CreateCoordinator();
        var request = new ResourceAdmissionRequest(
            "not-registered", DeviceAllocation("unregistered", 100, "not-registered"), Policy(),
            callerId: "test", allowUnknown: true);

        var refusal = await Assert.ThrowsAsync<ResourceAdmissionException>(() => coordinator.AcquireAsync(request));

        Assert.Contains(refusal.Plan.UnknownComponents,
            unknown => unknown.Code == "resource-consumer-unregistered");
    }

    [Fact]
    public async Task Concurrent_acquires_cannot_both_reserve_the_same_headroom()
    {
        using var coordinator = CreateCoordinator(usedDeviceBytes: 100);
        coordinator.RegisterConsumer(Descriptor("chat"));
        var requestA = Request("chat", DeviceAllocation("chat-a", 500));
        var requestB = Request("chat", DeviceAllocation("chat-b", 500));

        var first = await coordinator.AcquireAsync(requestA);
        var refusal = await Assert.ThrowsAsync<ResourceAdmissionException>(() => coordinator.AcquireAsync(requestB));

        Assert.Equal(ResourcePlanFeasibility.DoesNotFit, refusal.Plan.Feasibility);
        Assert.Contains(refusal.Plan.PreservedReservations, reservation => reservation.ConsumerId == "chat");
        Assert.False(first.IsCompleted);
        await first.ReleaseAsync("test cleanup");

        await using var retry = await coordinator.AcquireAsync(requestB);
        Assert.False(retry.IsReleased);
    }

    [Fact]
    public async Task Expired_and_cancelled_leases_do_not_leave_reservations_behind()
    {
        using var coordinator = CreateCoordinator();
        coordinator.RegisterConsumer(Descriptor("chat"));
        var policy = Policy(TimeSpan.FromMilliseconds(10));
        var first = await coordinator.AcquireAsync(Request("chat", DeviceAllocation("chat-a", 100), policy));
        await Task.Delay(40);

        await using var second = await coordinator.AcquireAsync(Request("chat", DeviceAllocation("chat-b", 100), policy));
        Assert.True(first.IsReleased);
        var expiredReceipt = Assert.Single(coordinator.RecentReleaseReceipts);
        Assert.Equal(first.LeaseId, expiredReceipt.ReservationId);
        Assert.Equal("reservation expired", expiredReceipt.Reason);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.AcquireAsync(Request("chat", DeviceAllocation("chat-c", 100), policy), cts.Token));
        Assert.Equal(2, coordinator.RecentPlans.Count);
    }

    [Fact]
    public async Task Completion_publishes_one_active_allocation_and_release_removes_it()
    {
        var registry = new ResourceConsumerRegistry();
        var source = new FixedSnapshotSource(Snapshot([Descriptor("chat")]));
        using var coordinator = new ResourceCoordinator(source, registry);
        coordinator.RegisterConsumer(Descriptor("chat"));
        var request = Request("chat", DeviceAllocation("chat", 100));
        await using var lease = await coordinator.AcquireAsync(request);
        var active = new ResourceAllocation(
            "chat", "chat", null, ResourceLifecycleState.Active,
            null, [], null, "pid-42", request.ProposedAllocation.Components, DateTime.UtcNow, []);

        await lease.CompleteAsync(active);
        var captured = await registry.CaptureSnapshotAsync(new ResourceSnapshotCapture(Hardware()));
        Assert.Equal(ResourceLifecycleState.Active, Assert.Single(captured.Allocations).LifecycleState);
        Assert.True(lease.IsCompleted);

        coordinator.ReleaseAllocation(active.AllocationId);
        captured = await registry.CaptureSnapshotAsync(new ResourceSnapshotCapture(Hardware()));
        Assert.Empty(captured.Allocations);
    }

    [Fact]
    public async Task Release_receipt_preserves_the_caller_reason()
    {
        using var coordinator = CreateCoordinator();
        coordinator.RegisterConsumer(Descriptor("chat"));
        await using var lease = await coordinator.AcquireAsync(Request("chat", DeviceAllocation("chat", 100)));

        await lease.ReleaseAsync("chat startup failed after process launch");

        var receipt = Assert.Single(coordinator.RecentReleaseReceipts);
        Assert.Equal(lease.LeaseId, receipt.ReservationId);
        Assert.Equal(lease.Plan.PlanId, receipt.PlanId);
        Assert.Equal("chat", receipt.ConsumerId);
        Assert.Equal("chat startup failed after process launch", receipt.Reason);
    }

    [Fact]
    public async Task A_conflicting_plan_does_not_stop_or_change_an_existing_consumer()
    {
        var descriptor = Descriptor("existing");
        var registry = new ResourceConsumerRegistry();
        registry.RegisterConsumer(descriptor);
        var activeExisting = DeviceAllocation("existing", 700, "existing", ResourceLifecycleState.Active);
        registry.RegisterAllocation(activeExisting);
        var source = new FixedSnapshotSource(Snapshot(
            [descriptor, Descriptor("chat")], [activeExisting], deviceUsedBytes: 900));
        using var coordinator = new ResourceCoordinator(source, registry);
        coordinator.RegisterConsumer(Descriptor("chat"));

        var refusal = await Assert.ThrowsAsync<ResourceAdmissionException>(() =>
            coordinator.AcquireAsync(Request("chat", DeviceAllocation("chat", 300))));

        Assert.Equal(ResourcePlanFeasibility.DoesNotFit, refusal.Plan.Feasibility);
        var after = await registry.CaptureSnapshotAsync(new ResourceSnapshotCapture(Hardware()));
        var allocation = Assert.Single(after.Allocations);
        Assert.Equal("existing", allocation.ConsumerId);
        Assert.Equal(ResourceLifecycleState.Active, allocation.LifecycleState);
    }

    [Fact]
    public void Lab_uses_the_injected_managed_runtime_factory_boundary()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var source = File.ReadAllText(Path.Combine(root, "src", "Hermaeus.Services", "IsolatedLabRuntimeHost.cs"));

        Assert.Contains("IManagedRuntimeProcessFactory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ServerProcessManager", source, StringComparison.Ordinal);
    }

    private static ResourceCoordinator CreateCoordinator(long usedDeviceBytes = 100, long usedSystemBytes = 100)
    {
        var descriptor = Descriptor("chat");
        return new ResourceCoordinator(
            new FixedSnapshotSource(Snapshot([descriptor], deviceUsedBytes: usedDeviceBytes, systemUsedBytes: usedSystemBytes)),
            new ResourceConsumerRegistry());
    }

    private static ResourceAdmissionRequest Request(
        string consumerId,
        ResourceAllocation allocation,
        ResourceHeadroomPolicy? policy = null) => new(
        consumerId, allocation, policy ?? Policy(), callerId: "test", allowUnknown: false);

    private static ResourceHeadroomPolicy Policy(TimeSpan? lifetime = null) => new(
        deviceStabilityBytes: 0,
        systemStabilityBytes: 0,
        interactiveReservationBytes: 0,
        foregroundReservationBytes: 0,
        inProcessReservationBytes: 0,
        unknownDeviceReservationBytes: 0,
        reservationLifetime: lifetime ?? TimeSpan.FromMinutes(1));

    private static ResourceAllocation DeviceAllocation(
        string allocationId,
        long bytes,
        string consumerId = "chat",
        ResourceLifecycleState state = ResourceLifecycleState.Planned) => new(
        allocationId,
        consumerId,
        null,
        state,
        null,
        [],
        null,
        null,
        [new ResourceAllocationComponent(
            "weights", ResourceComponentKind.ModelWeights, "gpu-0", bytes, bytes, null,
            ResourceEvidenceState.Reserved, ResourceKind.DeviceMemory)],
        null,
        []);

    private static ResourceAllocation SystemAllocation(string consumerId, long bytes) => new(
        "system-allocation",
        consumerId,
        null,
        ResourceLifecycleState.Planned,
        null,
        [],
        null,
        null,
        [new ResourceAllocationComponent(
            "onnx", ResourceComponentKind.OnnxSession, null, bytes, bytes, null,
            ResourceEvidenceState.Reserved, ResourceKind.SystemResidentMemory)],
        null,
        []);

    private static ResourceSnapshot Snapshot(
        IReadOnlyList<ResourceConsumerDescriptor> descriptors,
        IReadOnlyList<ResourceAllocation>? allocations = null,
        long deviceUsedBytes = 100,
        long systemUsedBytes = 100) => new(
        "snapshot",
        Hardware(),
        DateTime.UtcNow,
        descriptors,
        allocations ?? [],
        [new ResourceObservation(
            "system-memory", ResourceKind.SystemResidentMemory, systemUsedBytes, 1_000,
            ResourceObservationScope.System, null, null, "system-counter",
            ResourceObservationTrustState.TrustedRuntime, DateTime.UtcNow, "system-memory", "system memory")],
        [],
        [new ResourceDeviceTotal("gpu-0", deviceUsedBytes, 1_000, "gpu-memory")]);

    private static ResourceConsumerDescriptor Descriptor(string consumerId) => new(
        consumerId,
        ResourceConsumerKind.ChatRuntime,
        ResourceOwnerIdentity.OwnedProcess($"owner-{consumerId}"),
        "TestLifecycle",
        ResourcePriorityClass.Interactive,
        ResourceReclaimability.Cooperative,
        [ResourceKind.DeviceMemory, ResourceKind.SystemResidentMemory]);

    private static HardwareIdentityV2 Hardware() => new(
        "test-os", "x64", "test-backend", "test-gpu", 1_000, 1_000, "driver", "one-device", IdentityCompleteness.Complete);

    private sealed class FixedSnapshotSource(ResourceSnapshot snapshot) : IResourceSnapshotSource
    {
        public Task<ResourceSnapshot> CaptureAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }
}
