using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ResourceIntelligenceTests
{
    [Fact]
    public void Owner_identity_refuses_an_endpoint_or_path_instead_of_persisting_it()
    {
        Assert.Throws<ArgumentException>(() => ResourceOwnerIdentity.ExternalEndpoint("https://127.0.0.1:8080"));
        Assert.Throws<ArgumentException>(() => ResourceOwnerIdentity.OwnedProcess("/home/user/llama-server"));
    }

    [Fact]
    public void Registry_refuses_reusing_a_consumer_id_for_a_different_owner()
    {
        var registry = new ResourceConsumerRegistry();
        registry.RegisterConsumer(Descriptor(ResourceOwnerIdentity.InProcess("component-a")));

        Assert.Throws<InvalidOperationException>(() => registry.RegisterConsumer(
            Descriptor(ResourceOwnerIdentity.ExternalEndpoint("endpoint-fingerprint"))));
    }

    [Fact]
    public void Registry_allows_idempotent_registration_of_the_same_descriptor()
    {
        var registry = new ResourceConsumerRegistry();
        var descriptor = Descriptor(ResourceOwnerIdentity.InProcess("component-a"));

        registry.RegisterConsumer(descriptor);
        registry.RegisterConsumer(descriptor);

        Assert.Single(registry.Consumers);
    }

    [Fact]
    public void Allocation_requires_a_registered_consumer_and_unique_components()
    {
        var registry = new ResourceConsumerRegistry();
        var allocation = Allocation("allocation-a", "consumer-a", [Component("component-a"), Component("component-a")]);

        Assert.Throws<InvalidOperationException>(() => registry.RegisterAllocation(allocation));

        registry.RegisterConsumer(Descriptor(ResourceOwnerIdentity.InProcess("component-a")));
        Assert.Throws<InvalidOperationException>(() => registry.RegisterAllocation(allocation));
    }

    [Fact]
    public void A_component_cannot_also_be_a_top_level_allocation()
    {
        var registry = new ResourceConsumerRegistry();
        registry.RegisterConsumer(Descriptor(ResourceOwnerIdentity.InProcess("component-a")));
        registry.RegisterConsumer(Descriptor("consumer-b", ResourceOwnerIdentity.InProcess("component-b")));
        registry.RegisterAllocation(Allocation("component-a", "consumer-a", [Component("child-a")]));

        var duplicate = Allocation("allocation-b", "consumer-b", [Component("component-a")]);
        Assert.Throws<InvalidOperationException>(() => registry.RegisterAllocation(duplicate));
    }

    [Fact]
    public void Snapshot_is_immutable_and_copies_all_input_collections()
    {
        var descriptors = new List<ResourceConsumerDescriptor> { Descriptor(ResourceOwnerIdentity.InProcess("component-a")) };
        var allocations = new List<ResourceAllocation> { Allocation("allocation-a", "consumer-a", [Component("component-a")]) };
        var observations = new List<ResourceObservation> { Observation("observation-a", ResourceObservationTrustState.Unknown) };
        var snapshot = new ResourceSnapshot(
            "snapshot-a", Hardware(), DateTime.UnixEpoch, descriptors, allocations, observations, [], []);

        descriptors.Clear();
        allocations.Clear();
        observations.Clear();

        Assert.Single(snapshot.Consumers);
        Assert.Single(snapshot.Allocations);
        Assert.Single(snapshot.Observations);
        Assert.Single(snapshot.AuthoritativeObservations);
    }

    [Fact]
    public void Snapshot_refuses_orphaned_allocations_and_duplicate_observation_ids()
    {
        Assert.Throws<ArgumentException>(() => new ResourceSnapshot(
            "orphan", Hardware(), DateTime.UnixEpoch, [Descriptor(ResourceOwnerIdentity.InProcess("component-a"))],
            [Allocation("allocation-a", "missing-consumer", [Component("component-a")])], [], [], []));

        var observation = Observation("same", ResourceObservationTrustState.Unknown);
        Assert.Throws<ArgumentException>(() => new ResourceSnapshot(
            "duplicate", Hardware(), DateTime.UnixEpoch, [Descriptor(ResourceOwnerIdentity.InProcess("component-a"))],
            [], [observation, observation], [], []));
    }

    [Fact]
    public void Snapshot_selects_the_highest_trust_observation_without_summing_evidence()
    {
        var snapshot = new ResourceSnapshot(
            "snapshot-a",
            Hardware(),
            DateTime.UnixEpoch,
            [Descriptor(ResourceOwnerIdentity.InProcess("component-a"))],
            [],
            [
                Observation("device", ResourceObservationTrustState.DeviceTotal, ResourceObservationScope.Device),
                Observation("process", ResourceObservationTrustState.ProcessScoped),
                Observation("runtime", ResourceObservationTrustState.TrustedRuntime)
            ],
            [],
            []);

        Assert.Equal(2, snapshot.AuthoritativeObservations.Count);
        Assert.Contains(snapshot.AuthoritativeObservations, observation => observation.ObservationId == "runtime");
        Assert.Contains(snapshot.AuthoritativeObservations, observation => observation.ObservationId == "device");
        Assert.DoesNotContain(snapshot.AuthoritativeObservations, observation => observation.ObservationId == "process");
    }

    [Fact]
    public void Device_total_observations_cannot_be_attributed_to_a_consumer()
    {
        Assert.Throws<ArgumentException>(() => new ResourceObservation(
            "device", ResourceKind.DeviceMemory, 10, 100, ResourceObservationScope.Device,
            "consumer-a", "gpu-0", "device-counter", ResourceObservationTrustState.DeviceTotal,
            DateTime.UnixEpoch, "device-total", "whole-device total"));
        Assert.Throws<ArgumentException>(() => new ResourceObservation(
            "consumer-device", ResourceKind.DeviceMemory, 10, 100, ResourceObservationScope.Consumer,
            "consumer-a", "gpu-0", "device-counter", ResourceObservationTrustState.DeviceTotal,
            DateTime.UnixEpoch, "device-total", "whole-device total"));
    }

    [Fact]
    public void Persisted_observation_metadata_cannot_contain_a_path()
    {
        Assert.Throws<ArgumentException>(() => new ResourceObservation(
            "observation", ResourceKind.DeviceMemory, 1, 10, ResourceObservationScope.Device,
            null, "gpu-0", "/tmp/nvidia-smi", ResourceObservationTrustState.DeviceTotal,
            DateTime.UnixEpoch, "device-total", "whole-device total"));
        Assert.Throws<ArgumentException>(() => new ResourceUnknown("unknown", "/tmp/private-detail"));
    }

    [Fact]
    public async Task In_process_adapter_reports_no_allocation_until_a_session_is_loaded()
    {
        var loaded = false;
        var adapter = new InProcessResourceConsumerAdapter(
            Descriptor(ResourceOwnerIdentity.InProcess("component-a")), () => loaded);

        Assert.Null(await adapter.CaptureAsync());

        loaded = true;
        var allocation = await adapter.CaptureAsync();
        Assert.NotNull(allocation);
        Assert.Equal(ResourceLifecycleState.Active, allocation!.LifecycleState);
        Assert.Equal(ResourceEvidenceState.Unknown, Assert.Single(allocation.Components).EvidenceState);
    }

    [Fact]
    public async Task Registry_composes_registered_allocations_and_in_process_adapters_with_unknowns()
    {
        var loaded = true;
        var adapter = new InProcessResourceConsumerAdapter(
            Descriptor(ResourceOwnerIdentity.InProcess("component-a")), () => loaded);
        var registry = new ResourceConsumerRegistry([adapter]);
        registry.RegisterAllocation(Allocation("allocation-a", "consumer-a", [
            new ResourceAllocationComponent("weights", ResourceComponentKind.ModelWeights, "gpu-0", 100, null, 80, ResourceEvidenceState.Observed)]));

        var snapshot = await registry.CaptureSnapshotAsync(new ResourceSnapshotCapture(Hardware()));

        Assert.Equal(2, snapshot.Allocations.Count);
        Assert.Contains(snapshot.Unknowns, unknown => unknown.Code == "resource-component-unobserved");
        Assert.Equal("consumer-a", snapshot.Allocations[0].ConsumerId);
    }

    [Fact]
    public async Task Adapter_failure_is_unknown_and_does_not_abort_the_snapshot()
    {
        var adapter = new InProcessResourceConsumerAdapter(
            Descriptor(ResourceOwnerIdentity.InProcess("component-a")), () => throw new InvalidOperationException());
        var registry = new ResourceConsumerRegistry([adapter]);

        var snapshot = await registry.CaptureSnapshotAsync(new ResourceSnapshotCapture(Hardware()));

        Assert.Contains(snapshot.Unknowns, unknown => unknown.Code == "resource-adapter-failed");
        Assert.Empty(snapshot.Allocations);
    }

    [Fact]
    public async Task Registry_rejects_an_adapter_allocation_that_collides_with_a_registered_allocation()
    {
        var adapter = new InProcessResourceConsumerAdapter(
            Descriptor(ResourceOwnerIdentity.InProcess("component-a")), () => true);
        var registry = new ResourceConsumerRegistry([adapter]);
        registry.RegisterAllocation(Allocation("consumer-a:allocation", "consumer-a", [Component("registered")])) ;

        var snapshot = await registry.CaptureSnapshotAsync(new ResourceSnapshotCapture(Hardware()));

        Assert.Contains(snapshot.Unknowns, unknown => unknown.Code == "resource-allocation-duplicate");
        Assert.Single(snapshot.Allocations);
    }

    [Fact]
    public async Task Registry_rejects_an_adapter_that_returns_an_allocation_for_another_consumer()
    {
        var descriptor = Descriptor(ResourceOwnerIdentity.InProcess("component-a"));
        var adapter = new MismatchedAdapter(descriptor, Allocation("allocation-b", "consumer-b", [Component("component-b")]));
        var registry = new ResourceConsumerRegistry([adapter]);

        var snapshot = await registry.CaptureSnapshotAsync(new ResourceSnapshotCapture(Hardware()));

        Assert.Contains(snapshot.Unknowns, unknown => unknown.Code == "resource-adapter-invalid");
        Assert.Empty(snapshot.Allocations);
    }

    [Fact]
    public void Removing_an_allocation_is_idempotent()
    {
        var registry = new ResourceConsumerRegistry();
        registry.RegisterConsumer(Descriptor(ResourceOwnerIdentity.InProcess("component-a")));
        registry.RegisterAllocation(Allocation("allocation-a", "consumer-a", [Component("component-a")])) ;

        Assert.False(registry.RemoveAllocation("allocation-a"));
        registry.UpdateAllocation(Allocation("allocation-a", "consumer-a", [Component("component-a")], ResourceLifecycleState.Released));
        Assert.True(registry.RemoveAllocation("allocation-a"));
        Assert.False(registry.RemoveAllocation("allocation-a"));
    }

    [Fact]
    public void Allocation_lifecycle_updates_are_owner_preserving_and_terminal_states_are_final()
    {
        var registry = new ResourceConsumerRegistry();
        registry.RegisterConsumer(Descriptor(ResourceOwnerIdentity.InProcess("component-a")));
        registry.RegisterAllocation(Allocation("allocation-a", "consumer-a", [Component("component-a")], ResourceLifecycleState.Planned));

        registry.UpdateAllocation(Allocation("allocation-a", "consumer-a", [Component("component-a")], ResourceLifecycleState.Starting));
        registry.UpdateAllocation(Allocation("allocation-a", "consumer-a", [Component("component-a")], ResourceLifecycleState.Active));
        registry.UpdateAllocation(Allocation("allocation-a", "consumer-a", [Component("component-a")], ResourceLifecycleState.Stopping));
        registry.UpdateAllocation(Allocation("allocation-a", "consumer-a", [Component("component-a")], ResourceLifecycleState.Released));

        Assert.Throws<InvalidOperationException>(() => registry.UpdateAllocation(
            Allocation("allocation-a", "consumer-a", [Component("component-a")], ResourceLifecycleState.Active)));
    }

    [Fact]
    public void Adaptive_launch_is_an_explicit_supported_experience_domain()
    {
        Assert.Contains(EmpiricalExperienceDomains.AdaptiveLaunch, EmpiricalExperienceDomains.Initial);
        Assert.Equal(EmpiricalExperienceDomains.AdaptiveLaunch, new AdaptiveLaunchExperienceCodec().Domain);
    }

    [Fact]
    public void Persistence_projection_keeps_only_stable_identity_and_bounded_evidence()
    {
        var fingerprint = ModelFitPredictionTests.Fingerprint();
        var allocation = new ResourceAllocation(
            "allocation-a", "consumer-a", "attempt-a", ResourceLifecycleState.Active,
            fingerprint.Runtime, [fingerprint.Model], fingerprint.Configuration, "process-a",
            [Component("weights")], DateTime.UnixEpoch, []);
        var snapshot = new ResourceSnapshot(
            "snapshot-a", Hardware(), DateTime.UnixEpoch, [Descriptor(ResourceOwnerIdentity.OwnedProcess(fingerprint.Runtime.StableId))],
            [allocation], [Observation("observation-a", ResourceObservationTrustState.ProcessScoped)], [], []);

        var persisted = ResourceSnapshotPersistenceProjection.Project(snapshot);

        var persistedAllocation = Assert.Single(persisted.Allocations);
        Assert.Equal(fingerprint.Runtime.StableId, persistedAllocation.RuntimeStableId);
        Assert.Equal(fingerprint.Model.StableId, Assert.Single(persistedAllocation.ModelStableIds));
        Assert.Equal(fingerprint.Configuration.StableId, persistedAllocation.ConfigurationStableId);
        Assert.DoesNotContain("process-a", JsonSerializer.Serialize(persisted), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_snapshot_store_retains_only_the_bounded_recent_window()
    {
        using var temp = new TempDir();
        var store = new SqliteResourceSnapshotStore(NewSettings(temp));
        for (var i = 0; i < 35; i++)
        {
            var capturedAt = DateTime.UnixEpoch.AddSeconds(i);
            await store.SaveAsync(new ResourceSnapshot(
                $"snapshot-{i}", Hardware(), capturedAt, [], [], [], [], []));
        }

        var rows = await store.LoadRecentAsync(100);

        Assert.Equal(32, rows.Count);
        Assert.DoesNotContain(rows, row => row.SnapshotId == "snapshot-0");
        Assert.Equal("snapshot-34", rows[0].SnapshotId);
    }

    [Fact]
    public async Task Sqlite_snapshot_store_load_limit_is_bounded_and_path_free()
    {
        using var temp = new TempDir();
        var store = new SqliteResourceSnapshotStore(NewSettings(temp));
        await store.SaveAsync(new ResourceSnapshot(
            "snapshot-a", Hardware(), DateTime.UnixEpoch, [Descriptor(ResourceOwnerIdentity.ExternalEndpoint("endpoint-fingerprint"))], [], [], [], []));

        var rows = await store.LoadRecentAsync(1);

        var row = Assert.Single(rows);
        Assert.Equal("endpoint-fingerprint", Assert.Single(row.Consumers).OwnerStableId);
        Assert.DoesNotContain(temp.PathFor("secret"), JsonSerializer.Serialize(row), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_snapshot_store_can_be_reopened_and_reads_the_same_projection()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        await new SqliteResourceSnapshotStore(settings).SaveAsync(
            new ResourceSnapshot("snapshot-a", Hardware(), DateTime.UnixEpoch, [], [], [], [], []));

        var rows = await new SqliteResourceSnapshotStore(settings).LoadRecentAsync();

        Assert.Equal("snapshot-a", Assert.Single(rows).SnapshotId);
    }

    [Fact]
    public async Task Registry_honours_cancellation_before_asking_adapters()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var adapter = new InProcessResourceConsumerAdapter(
            Descriptor(ResourceOwnerIdentity.InProcess("component-a")), () => throw new InvalidOperationException());
        var registry = new ResourceConsumerRegistry([adapter]);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            registry.CaptureSnapshotAsync(new ResourceSnapshotCapture(Hardware()), cts.Token));
    }

    private static ResourceConsumerDescriptor Descriptor(ResourceOwnerIdentity owner) =>
        Descriptor("consumer-a", owner);

    private static ResourceConsumerDescriptor Descriptor(string consumerId, ResourceOwnerIdentity owner) => new(
        consumerId,
        ResourceConsumerKind.Reranker,
        owner,
        "ResourceLifecycle",
        ResourcePriorityClass.Background,
        ResourceReclaimability.Cooperative,
        [ResourceKind.DeviceMemory, ResourceKind.SystemResidentMemory]);

    private static ResourceAllocation Allocation(
        string allocationId,
        string consumerId,
        IReadOnlyList<ResourceAllocationComponent> components,
        ResourceLifecycleState state = ResourceLifecycleState.Active) => new(
        allocationId, consumerId, null, state, null, [], null, null, components, DateTime.UnixEpoch, []);

    private static ResourceAllocationComponent Component(string id) => new(
        id, ResourceComponentKind.ModelWeights, "gpu-0", 100, null, null, ResourceEvidenceState.Unknown);

    private static ResourceObservation Observation(
        string id,
        ResourceObservationTrustState trust,
        ResourceObservationScope scope = ResourceObservationScope.Consumer) => new(
        id, ResourceKind.DeviceMemory, 10, 100, scope,
        scope == ResourceObservationScope.Device ? null : "consumer-a",
        "gpu-0", "test", trust, DateTime.UnixEpoch, "test-observation", "test observation");

    private static HardwareIdentityV2 Hardware() => new(
        "test-os", "x64", "test-backend", "test-gpu", 1024, 4096, "driver", "one-device", IdentityCompleteness.Complete);

    private sealed class MismatchedAdapter(ResourceConsumerDescriptor descriptor, ResourceAllocation allocation)
        : IResourceConsumerAdapter
    {
        public ResourceConsumerDescriptor Descriptor { get; } = descriptor;
        public Task<ResourceAllocation?> CaptureAsync(CancellationToken ct = default) => Task.FromResult<ResourceAllocation?>(allocation);
    }
}
