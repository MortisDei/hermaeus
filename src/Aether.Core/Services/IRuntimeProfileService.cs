using Aether.Core.Models;

namespace Aether.Core.Services;

public sealed record RuntimeHealth(string ProfileId, bool IsHealthy, string Message);

public interface IRuntimeProfileService
{
    IReadOnlyList<RuntimeProfile> Profiles { get; }
    Task SaveAsync(RuntimeProfile profile, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<RuntimeHealth> CheckHealthAsync(RuntimeProfile profile, CancellationToken ct = default);
}
