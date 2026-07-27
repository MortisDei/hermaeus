using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;

namespace Hermaeus.Services.Recall;

/// <summary>
/// r24 doc 02 2.4: fans a query out to the four <see cref="IRecallSource"/>
/// implementations concurrently and fuses their independently-ranked lists
/// with reciprocal rank fusion (k=60, the same constant
/// <see cref="Hermaeus.Rag.Retrieval.HybridRetriever"/> uses) - the sources
/// are disjoint by kind, so fusion here is "rank every hit by
/// 1/(its rank + k) and sort", not a duplicate-id merge.
/// </summary>
public sealed class RecallService
{
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(3);
    private const double RrfK = 60.0;
    private const int TopK = 50;

    private readonly IReadOnlyList<IRecallSource> _sources;
    private readonly IEmbeddingService? _embeddings;

    public RecallService(IEnumerable<IRecallSource> sources, IEmbeddingService? embeddings = null)
    {
        _sources = sources.ToList();
        _embeddings = embeddings;
    }

    public async Task<RecallResult> SearchAsync(string query, string projectScope = "", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new RecallResult([], [], _embeddings is null);

        var results = await Task.WhenAll(_sources.Select(s => RunOneAsync(s, query, projectScope, ct)));

        var omitted = new List<string>();
        var ranked = new List<IReadOnlyList<RecallHit>>();
        foreach (var (source, hits, failed) in results)
        {
            if (failed) omitted.Add(source.Name);
            else ranked.Add(hits);
        }

        return new RecallResult(Fuse(ranked), omitted, _embeddings is null);
    }

    private static async Task<(IRecallSource Source, IReadOnlyList<RecallHit> Hits, bool Failed)> RunOneAsync(
        IRecallSource source, string query, string projectScope, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(SourceTimeout);
            var hits = await source.SearchAsync(query, projectScope, timeoutCts.Token);
            return (source, hits, false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The per-source timeout fired, not the caller's own cancellation.
            return (source, [], true);
        }
        catch (Exception)
        {
            // A source that throws is omitted and named, never let it crash the search.
            return (source, [], true);
        }
    }

    private static List<RecallHit> Fuse(List<IReadOnlyList<RecallHit>> perSourceRanked)
    {
        var scored = new List<RecallHit>();
        foreach (var list in perSourceRanked)
        {
            for (var i = 0; i < list.Count; i++)
                scored.Add(list[i] with { Score = 1.0 / (i + RrfK) });
        }

        return scored.OrderByDescending(h => h.Score).Take(TopK).ToList();
    }
}
