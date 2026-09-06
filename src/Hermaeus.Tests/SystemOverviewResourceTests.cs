using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class SystemOverviewResourceTests
{
    [Fact]
    public async Task Resource_receipts_keep_parent_observation_component_gaps_plans_and_residency_distinct()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var snapshot = new ResourceSnapshot(
            "snapshot-a",
            Hardware(),
            DateTime.UnixEpoch,
            [
                Descriptor("chat", ResourceConsumerKind.ChatRuntime),
                Descriptor("embedding", ResourceConsumerKind.EmbeddingRuntime),
                Descriptor("lab", ResourceConsumerKind.LabRuntime),
                Descriptor("reranker", ResourceConsumerKind.Reranker)
            ],
            [
                new ResourceAllocation(
                    "chat-allocation", "chat", null, ResourceLifecycleState.Active,
                    null, [], null, null,
                    [
                        new ResourceAllocationComponent("weights", ResourceComponentKind.ModelWeights, "gpu-0", null, null, 100, ResourceEvidenceState.Observed),
                        new ResourceAllocationComponent("kv", ResourceComponentKind.KvCache, "gpu-0", null, null, null, ResourceEvidenceState.Unknown),
                        new ResourceAllocationComponent("compute", ResourceComponentKind.RuntimeCompute, null, null, null, null, ResourceEvidenceState.Unknown, ResourceKind.SystemResidentMemory)
                    ],
                    DateTime.UnixEpoch,
                    []),
                new ResourceAllocation(
                    "embedding-allocation", "embedding", null, ResourceLifecycleState.Planned,
                    null, [], null, null,
                    [new ResourceAllocationComponent("weights", ResourceComponentKind.ModelWeights, "gpu-0", 50, 60, null, ResourceEvidenceState.Reserved)],
                    DateTime.UnixEpoch,
                    [])
            ],
            [new ResourceObservation(
                "chat-gpu", ResourceKind.DeviceMemory, 500, null, ResourceObservationScope.Consumer,
                "chat", "gpu-0", "runtime-counter", ResourceObservationTrustState.TrustedRuntime,
                DateTime.UnixEpoch, "runtime-observed", "runtime total")],
            [new ResourceUnknown("component-gap", "A component has no authoritative observed byte count.", "chat")],
            [new ResourceDeviceTotal("gpu-0", 800, 1000, "device-total")]);

        using var coordinator = new ResourceCoordinator(
            new FixedSnapshotSource(snapshot),
            new ResourceConsumerRegistry());
        var privacy = new PrivacyAuditService(
            settings,
            new FakeSecretStore(),
            new RuntimeLogService(settings),
            new FakeVoiceProviderRegistry(settings),
            new SqliteTraceStore(settings));
        var vm = new SystemOverviewViewModel(new FakeSystemInfo(), new FakeToasts(), privacy, resourceCoordinator: coordinator);

        await vm.RefreshAsync();

        var chat = Assert.Single(vm.ResourceConsumers, item => item.ConsumerId == "chat");
        Assert.Equal("500 B observed", chat.DeviceMemory);
        Assert.Equal("Not observed", chat.SystemMemory);
        Assert.Equal("2 component(s) attribution incomplete", chat.Unknown);

        var embedding = Assert.Single(vm.ResourceConsumers, item => item.ConsumerId == "embedding");
        Assert.Equal("60 B planned", embedding.DeviceMemory);
        Assert.Equal("Not observed", embedding.SystemMemory);
        Assert.Equal(string.Empty, embedding.Unknown);

        var lab = Assert.Single(vm.ResourceConsumers, item => item.ConsumerId == "lab");
        Assert.Equal("Registered, not resident", lab.State);
        Assert.Equal("Not resident", lab.DeviceMemory);
        Assert.Equal("Not resident", lab.SystemMemory);

        var reranker = Assert.Single(vm.ResourceConsumers, item => item.ConsumerId == "reranker");
        Assert.Equal("Registered, lazy until a RAG query needs reranking", reranker.State);
        Assert.Equal("Not resident", reranker.DeviceMemory);

        var device = Assert.Single(vm.ResourceDevices);
        Assert.Equal("800 B", device.Used);
        Assert.Equal("1000 B", device.Capacity);
        Assert.Contains("1 evidence gap(s)", vm.ResourceStatus, StringComparison.Ordinal);
    }

    private sealed class FixedSnapshotSource(ResourceSnapshot snapshot) : IResourceSnapshotSource
    {
        public Task<ResourceSnapshot> CaptureAsync(CancellationToken ct = default) => Task.FromResult(snapshot);
    }

    private static ResourceConsumerDescriptor Descriptor(string id, ResourceConsumerKind kind) => new(
        id,
        kind,
        ResourceOwnerIdentity.InProcess(id + "-owner"),
        "ResourceLifecycle",
        ResourcePriorityClass.Background,
        ResourceReclaimability.Cooperative,
        [ResourceKind.DeviceMemory, ResourceKind.SystemResidentMemory]);

    private static HardwareIdentityV2 Hardware() => new(
        "test-os", "x64", "test-backend", "test-gpu", 1024, 4096, "driver", "one-device", IdentityCompleteness.Complete);
}
