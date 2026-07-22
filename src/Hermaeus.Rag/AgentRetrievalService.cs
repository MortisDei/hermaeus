using Hermaeus.Core.Services;
using Hermaeus.Rag.Storage;

namespace Hermaeus.Rag;

/// <summary>Implements the agent's minimal retrieval seam over the real RAG
/// pipeline, so Hermaeus.Agent depends on Hermaeus.Core's interface instead of
/// this project directly.</summary>
public sealed class AgentRetrievalService : IAgentRetrievalService
{
    private readonly RagQueryService _query;
    private readonly SqliteRagStore _store;

    public AgentRetrievalService(RagQueryService query, SqliteRagStore store)
    {
        _query = query;
        _store = store;
    }

    public async Task<bool> DatasetExistsAsync(string datasetId, CancellationToken ct = default)
    {
        var datasets = await _store.GetDatasetsAsync(ct);
        return datasets.Any(d => d.Id == datasetId);
    }

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string datasetId, string query, int topK, CancellationToken ct = default)
    {
        var retrieval = await _query.RetrieveAsync(datasetId, query, new RagQueryOptions(TopK: topK), ct);
        return retrieval.Selected
            .Select(s => new RetrievedChunk(
                Title: s.Chunk.SourceTitle,
                Content: s.Chunk.Content,
                TokenCount: Math.Max(s.Chunk.TokenCount, 1),
                Score: s.Score,
                SourceModifiedUtc: s.Chunk.SourceModifiedUtc,
                Locator: string.IsNullOrWhiteSpace(s.Chunk.SourcePath) ? s.Chunk.SourceFile : s.Chunk.SourcePath))
            .ToList();
    }
}
