using System.Diagnostics;
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
    /// <summary>
    /// How long one recall source may take before it is omitted and named.
    ///
    /// r29 doc 04 4.5: injectable, because the test that proves a slow source
    /// is omitted proved it by waiting out the real three seconds on every run,
    /// on both CI legs. Production behaviour is unchanged: the default is this
    /// value.
    /// </summary>
    private static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(3);
    private const double RrfK = 60.0;
    private const int TopK = 50;
    private const double MinimumSourceRelevance = 0.40;

    private readonly IReadOnlyList<IRecallSource> _sources;
    private readonly IEmbeddingService? _embeddings;
    private readonly TimeSpan _sourceTimeout;
    private readonly IRuntimeLogService? _runtimeLogs;

    public RecallService(
        IEnumerable<IRecallSource> sources,
        IEmbeddingService? embeddings = null,
        TimeSpan? sourceTimeout = null,
        IRuntimeLogService? runtimeLogs = null)
    {
        _sources = sources.ToList();
        _embeddings = embeddings;
        _sourceTimeout = sourceTimeout ?? DefaultSourceTimeout;
        _runtimeLogs = runtimeLogs;
    }

    public async Task<RecallResult> SearchAsync(string query, string projectScope = "", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new RecallResult([], [], _embeddings is null);

        var totalClock = Stopwatch.StartNew();
        var results = await Task.WhenAll(_sources.Select(s => RunOneAsync(s, query, projectScope, ct)));

        _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Debug,
            RuntimeLogCategory.Rag,
            $"Recall search completed; total_ms={totalClock.ElapsedMilliseconds}, sources={string.Join(",", results.Select(result => $"{result.Source.Name}:{result.Hits.Count} hits/{result.ElapsedMs} ms"))}, omitted={string.Join(",", results.Where(result => result.Failed).Select(result => result.Source.Name))}."));

        var omitted = new List<string>();
        var ranked = new List<IReadOnlyList<RecallHit>>();
        foreach (var (source, hits, failed, _) in results)
        {
            if (failed) omitted.Add(source.Name);
            else ranked.Add(hits);
        }

        return new RecallResult(Fuse(ranked), omitted, _embeddings is null);
    }

    private async Task<(IRecallSource Source, IReadOnlyList<RecallHit> Hits, bool Failed, long ElapsedMs)> RunOneAsync(
        IRecallSource source, string query, string projectScope, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_sourceTimeout);
            var hits = await source.SearchAsync(query, projectScope, timeoutCts.Token);
            return (source, hits, false, clock.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The per-source timeout fired, not the caller's own cancellation.
            return (source, [], true, clock.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A source that throws is omitted and named, never let it crash the search.
            return (source, [], true, clock.ElapsedMilliseconds);
        }
    }

    private static List<RecallHit> Fuse(List<IReadOnlyList<RecallHit>> perSourceRanked)
    {
        var scored = new List<RecallHit>();
        foreach (var list in perSourceRanked)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var hit = list[i];
                if (hit.Score < MinimumSourceRelevance)
                    continue;
                scored.Add(hit with { Score = 1.0 / (i + RrfK) });
            }
        }

        return scored.OrderByDescending(h => h.Score).Take(TopK).ToList();
    }
}
