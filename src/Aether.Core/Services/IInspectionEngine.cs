using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// Aggregates checks from every registered IInspectionCheckProvider. A null
/// view runs every provider; a named view (e.g. "doctor") runs only the
/// providers that contribute to it.
/// </summary>
public interface IInspectionEngine
{
    Task<InspectionReport> RunAsync(string? view = null, CancellationToken ct = default);
}
