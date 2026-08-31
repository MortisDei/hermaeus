using System.Runtime.InteropServices;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>Adapts the existing system probe into the shared R32 resource snapshot.</summary>
public sealed class SystemResourceSnapshotSource : IResourceSnapshotSource
{
    private readonly ISystemInfoService _system;
    private readonly IResourceConsumerRegistry _registry;

    public SystemResourceSnapshotSource(ISystemInfoService system, IResourceConsumerRegistry registry)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<ResourceSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        var system = await _system.CaptureAsync(ct);
        var capturedAt = system.CapturedAt.Kind == DateTimeKind.Utc
            ? system.CapturedAt
            : system.CapturedAt.ToUniversalTime();
        var observations = new List<ResourceObservation>();
        var totals = new List<ResourceDeviceTotal>();
        var unknowns = new List<ResourceUnknown>();
        for (var index = 0; index < system.Gpus.Count; index++)
        {
            var gpu = system.Gpus[index];
            var deviceId = $"gpu-{index}";
            var observationId = $"gpu-total-{index}";
            observations.Add(new ResourceObservation(
                observationId,
                ResourceKind.DeviceMemory,
                gpu.MemoryUsedBytes,
                gpu.MemoryTotalBytes,
                ResourceObservationScope.Device,
                null,
                deviceId,
                "system-info",
                ResourceObservationTrustState.DeviceTotal,
                capturedAt,
                "device-total",
                gpu.Status));
            totals.Add(new ResourceDeviceTotal(deviceId, gpu.MemoryUsedBytes, gpu.MemoryTotalBytes, observationId));
            if (gpu.MemoryTotalBytes is null || gpu.MemoryUsedBytes is null)
                unknowns.Add(new ResourceUnknown(
                    "resource-device-total-partial",
                    $"GPU '{deviceId}' did not provide both total and used memory.",
                    null,
                    deviceId));
        }

        if (system.TotalMemoryBytes > 0)
        {
            var used = Math.Max(0, system.TotalMemoryBytes - system.AvailableMemoryBytes);
            observations.Add(new ResourceObservation(
                "system-resident-memory",
                ResourceKind.SystemResidentMemory,
                used,
                system.TotalMemoryBytes,
                ResourceObservationScope.System,
                null,
                null,
                "system-info",
                ResourceObservationTrustState.Unknown,
                capturedAt,
                "system-memory-total",
                "System resident memory is a host total and is not attributed to a process."));
        }
        else
        {
            unknowns.Add(new ResourceUnknown(
                "resource-system-total-unknown",
                "The system probe did not provide total memory."));
        }

        if (!string.IsNullOrWhiteSpace(system.GpuProbeError))
            unknowns.Add(new ResourceUnknown("resource-gpu-probe", system.GpuProbeError));

        var hardware = new HardwareIdentityV2(
            OperatingSystem: RuntimeInformation.OSDescription,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            GpuBackend: system.GpuProbeMethod,
            GpuDevice: system.Gpus.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            VramBytes: system.Gpus.Select(g => g.MemoryTotalBytes).Where(v => v is not null).DefaultIfEmpty().Max(),
            RamBytes: system.TotalMemoryBytes > 0 ? system.TotalMemoryBytes : null,
            DriverVersion: string.Empty,
            DeviceLayout: system.Gpus.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Completeness: system.Gpus.Count > 0 && system.TotalMemoryBytes > 0
                ? IdentityCompleteness.Complete
                : IdentityCompleteness.Incomplete);
        return await _registry.CaptureSnapshotAsync(new ResourceSnapshotCapture(
            hardware, observations, totals, unknowns), ct);
    }
}
