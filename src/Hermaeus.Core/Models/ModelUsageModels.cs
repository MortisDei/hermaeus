namespace Hermaeus.Core.Models;

/// <summary>One model's share of calls within a <see cref="TraceKind"/> over a window.</summary>
public sealed record ModelUsageShare(string ModelId, long CallCount, long TotalTokens, double Share);

/// <summary>
/// Usage summary for one activity kind (Chat/Rag/Agent/LocalApi): total calls
/// and each model's share, dominant model first. See r6
/// 02-usage-history-recommendations.md 2.2.
/// </summary>
public sealed record KindUsageSummary(TraceKind Kind, long TotalCalls, IReadOnlyList<ModelUsageShare> Models)
{
    public ModelUsageShare? Dominant => Models.Count > 0 ? Models[0] : null;
}
