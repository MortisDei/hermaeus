using Aether.Core.Models;

namespace Aether.Core.Services;

public interface ISystemInfoService
{
    Task<SystemSnapshot> CaptureAsync(CancellationToken ct = default);
}
