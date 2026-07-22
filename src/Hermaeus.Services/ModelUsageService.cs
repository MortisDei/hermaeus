using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class ModelUsageService : IModelUsageService
{
    private readonly ITraceStore _traces;

    public ModelUsageService(ITraceStore traces)
    {
        _traces = traces;
    }

    public async Task<IReadOnlyList<ModelUsageRow>> GetUsageAsync(TraceKind? kind, int days, CancellationToken ct = default) =>
        await _traces.GetModelUsageAsync(kind, days, ct);

    public async Task<IReadOnlyList<KindUsageSummary>> GetKindSummariesAsync(int days, CancellationToken ct = default)
    {
        var rows = await _traces.GetModelUsageAsync(null, days, ct);
        return Summarize(rows);
    }

    /// <summary>Pure grouping/ranking, split out so it is unit-testable without SQLite.</summary>
    internal static IReadOnlyList<KindUsageSummary> Summarize(IReadOnlyList<ModelUsageRow> rows)
    {
        var summaries = new List<KindUsageSummary>();
        foreach (var group in rows.GroupBy(r => r.Kind))
        {
            var totalCalls = group.Sum(r => r.CallCount);
            if (totalCalls == 0) continue;

            var models = group
                .OrderByDescending(r => r.CallCount)
                .Select(r => new ModelUsageShare(r.ModelId, r.CallCount, r.TotalTokens, (double)r.CallCount / totalCalls))
                .ToList();
            summaries.Add(new KindUsageSummary(group.Key, totalCalls, models));
        }

        return summaries;
    }
}
