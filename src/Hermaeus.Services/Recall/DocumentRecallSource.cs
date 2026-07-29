using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;

namespace Hermaeus.Services.Recall;

/// <summary>
/// Wraps the existing RAG retrieval machinery (semantic scan, BM25, RRF
/// fusion via <see cref="HybridRetriever"/>) across every dataset in scope,
/// respecting <c>IsParent</c> exclusion the same way RAG query does (doc 02
/// 2.4). Skips the LLM query-planning step RagQueryService.RetrieveAsync
/// does for a single-dataset question - Recall fans out across many
/// datasets per keystroke and must stay fast and deterministic.
/// </summary>
public sealed class DocumentRecallSource : IRecallSource
{
    private const int MaxDatasetsSearched = 6;
    private readonly SqliteRagStore _store;
    private readonly IEmbeddingService? _embeddings;

    public DocumentRecallSource(SqliteRagStore store, IEmbeddingService? embeddings = null)
    {
        _store = store;
        _embeddings = embeddings;
    }

    public string Name => "Documents";

    public async Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct)
    {
        var datasets = await _store.GetDatasetsAsync(ct);
        var inScope = (string.IsNullOrEmpty(projectScope) ? datasets : datasets.Where(d => d.ProjectId == projectScope))
            .Take(MaxDatasetsSearched)
            .ToList();
        if (inScope.Count == 0) return [];

        float[]? queryVector = null;
        if (_embeddings is not null)
        {
            try { queryVector = await _embeddings.EmbedAsync(query, ct); }
            catch { /* keyword-only for this source; the fusion-level footer covers degraded mode */ }
        }

        var hits = new List<RecallHit>();
        foreach (var dataset in inScope)
        {
            ct.ThrowIfCancellationRequested();
            var chunks = await _store.GetChunksAsync(dataset.Id, includeEmbeddings: queryVector is not null, ct);
            var searchable = chunks.Where(c => !c.IsParent).ToList();
            if (searchable.Count == 0) continue;

            List<ScoredChunk> semantic = [];
            if (queryVector is not null)
                semantic = HybridRetriever.CosineScan(queryVector, searchable, 20);

            List<ScoredChunk> bm25 = [];
            var stats = await _store.GetBm25StatsAsync(dataset.Id, ct);
            if (stats is not null)
            {
                var scorer = new Bm25Scorer();
                var tfIndex = Bm25Scorer.BuildTfIndex(searchable);
                bm25 = scorer.Score(query, searchable, stats, tfIndex).Take(20).ToList();
            }

            var fused = queryVector is not null
                ? HybridRetriever.Fuse(query, semantic, bm25, 5)
                : bm25.Take(5).ToList();

            hits.AddRange(fused.Select(s => new RecallHit(
                RecallKind.Document,
                s.Chunk.SourceTitle.Length > 0 ? s.Chunk.SourceTitle : s.Chunk.SourceFile,
                RecallSnippet.Build(s.Chunk.Content, query),
                s.Chunk.CreatedAt,
                dataset.ProjectId,
                s.Score,
                new RecallTarget(DatasetId: dataset.Id, ChunkId: s.Chunk.Id))));
        }

        return hits;
    }
}
