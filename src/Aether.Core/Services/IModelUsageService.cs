using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// Read-only access to the local model_usage rollup, feeding usage-aware
/// benchmark insights (r6 02-usage-history-recommendations.md). Never leaves
/// the machine; disclosed as its own Privacy Audit item.
/// </summary>
public interface IModelUsageService
{
    /// <summary>Raw per-model call/token totals over the trailing window.</summary>
    Task<IReadOnlyList<ModelUsageRow>> GetUsageAsync(TraceKind? kind, int days, CancellationToken ct = default);

    /// <summary>
    /// Per-kind dominant-model summaries over the trailing window, sorted by
    /// call count within each kind. Kinds with no usage in the window are
    /// omitted.
    /// </summary>
    Task<IReadOnlyList<KindUsageSummary>> GetKindSummariesAsync(int days, CancellationToken ct = default);
}
