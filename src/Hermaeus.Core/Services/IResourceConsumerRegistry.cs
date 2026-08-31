using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public sealed class ResourceSnapshotCapture
{
    public HardwareIdentityV2 HardwareIdentity { get; }
    public IReadOnlyList<ResourceObservation> Observations { get; }
    public IReadOnlyList<ResourceDeviceTotal> DeviceTotals { get; }

    public ResourceSnapshotCapture(
        HardwareIdentityV2 hardwareIdentity,
        IEnumerable<ResourceObservation>? observations = null,
        IEnumerable<ResourceDeviceTotal>? deviceTotals = null)
    {
        HardwareIdentity = hardwareIdentity ?? throw new ArgumentNullException(nameof(hardwareIdentity));
        Observations = observations?.ToArray() ?? [];
        DeviceTotals = deviceTotals?.ToArray() ?? [];
    }
}

public interface IResourceConsumerAdapter
{
    ResourceConsumerDescriptor Descriptor { get; }
    Task<ResourceAllocation?> CaptureAsync(CancellationToken ct = default);
}

public interface IResourceConsumerRegistry
{
    IReadOnlyList<ResourceConsumerDescriptor> Consumers { get; }

    void RegisterConsumer(ResourceConsumerDescriptor descriptor);
    void RegisterAllocation(ResourceAllocation allocation);
    void UpdateAllocation(ResourceAllocation allocation);
    bool RemoveAllocation(string allocationId);
    Task<ResourceSnapshot> CaptureSnapshotAsync(ResourceSnapshotCapture capture, CancellationToken ct = default);
}

public interface IResourceSnapshotStore
{
    Task SaveAsync(ResourceSnapshot snapshot, CancellationToken ct = default);
    Task<IReadOnlyList<PersistedResourceSnapshot>> LoadRecentAsync(int maximum = 32, CancellationToken ct = default);
}
