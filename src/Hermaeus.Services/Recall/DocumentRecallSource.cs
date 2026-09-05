using System.Diagnostics;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;

namespace Hermaeus.Services.Recall;

/// <summary>
/// Wraps the existing RAG retrieval machinery across every dataset in scope.
/// Semantic scans read the bounded storage index, while BM25 reads only FTS
/// candidates. Content is hydrated after both candidate sets are known.
/// </summary>
public sealed class DocumentRecallSource : IRecallSource
{
    private const int MaxDatasetsSearched = 6;
    private const int MaxConcurrentDatasetSearches = 3;
    private const int SemanticCandidateLimit = 20;
    private const int Bm25CandidateLimit = 400;
    private const int ResultLimit = 5;
    private const float SemanticRelevanceFloor = 0.40f;
    private readonly SqliteRagStore _store;
    private readonly IEmbeddingService? _embeddings;
    private readonly IRuntimeLogService? _runtimeLogs;

    public DocumentRecallSource(
        SqliteRagStore store,
        IEmbeddingService? embeddings = null,
        IRuntimeLogService? runtimeLogs = null)
    {
        _store = store;
        _embeddings = embeddings;
        _runtimeLogs = runtimeLogs;
    }

    public string Name => "Documents";

    public async Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct)
    {
        var datasets = await _store.GetDatasetsAsync(ct);
        var inScope = (string.IsNullOrEmpty(projectScope) ? datasets : datasets.Where(d => d.ProjectId == projectScope))
            .Take(MaxDatasetsSearched)
            .ToList();
        if (inScope.Count == 0)
            return [];

        float[]? queryVector = null;
        if (_embeddings is not null)
        {
            try
            {
                queryVector = await _embeddings.EmbedAsync(query, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Keyword-only is an honest degraded path for this source.
            }
        }

        using var datasetGate = new SemaphoreSlim(MaxConcurrentDatasetSearches);
        var tasks = inScope.Select(async dataset =>
        {
            await datasetGate.WaitAsync(ct);
            try
            {
                return await SearchDatasetAsync(dataset, query, queryVector, ct);
            }
            finally
            {
                datasetGate.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(result => result).ToList();
    }

    private async Task<IReadOnlyList<RecallHit>> SearchDatasetAsync(
        RagDataset dataset,
        string query,
        float[]? queryVector,
        CancellationToken ct)
    {
        var totalClock = Stopwatch.StartNew();

        var scanClock = Stopwatch.StartNew();
        var semanticIds = queryVector is null
            ? []
            : HybridRetriever.CosineScan(
                queryVector,
                await _store.GetScanIndexAsync(dataset.Id, string.Empty, ct),
                SemanticCandidateLimit);
        var scanMs = scanClock.ElapsedMilliseconds;

        var ftsClock = Stopwatch.StartNew();
        var lexicalIds = await _store.SearchChunkIdsAsync(dataset.Id, query, Bm25CandidateLimit, ct);
        var ftsMs = ftsClock.ElapsedMilliseconds;

        var candidateIds = semanticIds.Select(result => result.ChunkId)
            .Concat(lexicalIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidateIds.Length == 0)
        {
            LogDataset(dataset, queryVector is not null, semanticIds.Count, lexicalIds.Count, 0, 0,
                scanMs, ftsMs, 0, 0, totalClock.ElapsedMilliseconds);
            return [];
        }

        var hydrateClock = Stopwatch.StartNew();
        var candidates = (await _store.GetChunksByIdsAsync(candidateIds, ct))
            .Where(chunk => !chunk.IsParent)
            .ToList();
        var hydrateMs = hydrateClock.ElapsedMilliseconds;
        if (candidates.Count == 0)
        {
            LogDataset(dataset, queryVector is not null, semanticIds.Count, lexicalIds.Count, candidateIds.Length, 0,
                scanMs, ftsMs, hydrateMs, 0, totalClock.ElapsedMilliseconds);
            return [];
        }

        var scoreClock = Stopwatch.StartNew();
        var chunksById = candidates.ToDictionary(chunk => chunk.Id, StringComparer.Ordinal);
        var semantic = semanticIds
            .Where(result => chunksById.ContainsKey(result.ChunkId))
            .Select(result => new ScoredChunk(chunksById[result.ChunkId], result.Score, ScoreSource.Semantic))
            .ToList();

        var stats = await _store.GetBm25StatsAsync(dataset.Id, ct);
        var bm25 = stats is null
            ? []
            : new Bm25Scorer()
                .Score(query, candidates, stats, Bm25Scorer.BuildTfIndex(candidates))
                .Take(SemanticCandidateLimit)
                .ToList();

        var fused = queryVector is not null
            ? HybridRetriever.Fuse(query, semantic, bm25, ResultLimit)
            : bm25.Take(ResultLimit).ToList();

        // RecallService has a 0.40 source-relevance floor. RRF is an ordering
        // score, not a calibrated relevance score, so return the strongest
        // underlying semantic or lexical signal instead of making every
        // document hit look like an unrelated 0.01 RRF score.
        var semanticScores = semantic.ToDictionary(result => result.Chunk.Id, result => result.Score, StringComparer.Ordinal);
        var lexicalScores = bm25
            .Select((result, index) => (Id: result.Chunk.Id, Score: 1f - (0.5f * index / Math.Max(1, bm25.Count))))
            .ToDictionary(result => result.Id, result => result.Score, StringComparer.Ordinal);
        var useful = fused
            .Select(result =>
            {
                var semanticScore = semanticScores.GetValueOrDefault(result.Chunk.Id);
                var lexicalScore = lexicalScores.GetValueOrDefault(result.Chunk.Id);
                return (result, relevance: Math.Max(semanticScore, lexicalScore));
            })
            .Where(result => result.relevance >= SemanticRelevanceFloor)
            .ToList();
        var scoreMs = scoreClock.ElapsedMilliseconds;

        var hits = useful.Select(result => new RecallHit(
            RecallKind.Document,
            result.result.Chunk.SourceTitle.Length > 0 ? result.result.Chunk.SourceTitle : result.result.Chunk.SourceFile,
            RecallSnippet.Build(result.result.Chunk.Content, query),
            result.result.Chunk.CreatedAt,
            dataset.ProjectId,
            result.relevance,
            new RecallTarget(DatasetId: dataset.Id, ChunkId: result.result.Chunk.Id)))
            .ToList();

        LogDataset(dataset, queryVector is not null, semanticIds.Count, lexicalIds.Count, candidateIds.Length, hits.Count,
            scanMs, ftsMs, hydrateMs, scoreMs, totalClock.ElapsedMilliseconds);
        return hits;
    }

    private void LogDataset(
        RagDataset dataset,
        bool semantic,
        int semanticCandidates,
        int lexicalCandidates,
        int hydrated,
        int returned,
        long scanMs,
        long ftsMs,
        long hydrateMs,
        long scoreMs,
        long totalMs)
    {
        _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Debug,
            RuntimeLogCategory.Rag,
            $"Document recall dataset completed; dataset={dataset.Id}, semantic={semantic}, semantic_candidates={semanticCandidates}, lexical_candidates={lexicalCandidates}, hydrated={hydrated}, returned={returned}, scan_ms={scanMs}, fts_ms={ftsMs}, hydrate_ms={hydrateMs}, score_ms={scoreMs}, total_ms={totalMs}."));
    }
}
