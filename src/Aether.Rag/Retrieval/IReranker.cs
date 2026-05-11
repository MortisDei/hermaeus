using Aether.Rag.Models;

namespace Aether.Rag.Retrieval;

public interface IReranker
{
    Task<List<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topK,
        CancellationToken ct = default);
}

public sealed class NoOpReranker : IReranker
{
    public Task<List<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topK,
        CancellationToken ct = default)
    {
        return Task.FromResult(candidates.Take(topK).ToList());
    }
}
