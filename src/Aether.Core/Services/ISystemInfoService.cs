using Aether.Core.Models;

namespace Aether.Core.Services;

public interface ISystemInfoService
{
    Task<SystemSnapshot> CaptureAsync(CancellationToken ct = default);

    /// <summary>Cached hardware facts (total RAM, max GPU VRAM). Cheap to call repeatedly.</summary>
    Task<HardwareProfile> GetHardwareProfileAsync(CancellationToken ct = default);
}
