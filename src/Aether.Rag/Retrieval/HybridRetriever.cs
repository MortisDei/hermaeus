using System.Numerics.Tensors;
using Aether.Rag.Models;

namespace Aether.Rag.Retrieval;

/// <summary>
/// Combines semantic (cosine similarity) and BM25 rankings via
/// Reciprocal Rank Fusion (k=60). RRF handles mismatched score
/// distributions cleanly without manual weight tuning.
/// </summary>
public sealed class HybridRetriever
{
    private const float RrfK           = 60f;
    private const float SemanticWeight = 0.7f;
    private const float Bm25Weight     = 0.3f;

    /// <summary>
    /// Full cosine search over all chunks with embeddings.
    /// Uses TensorPrimitives.CosineSimilarity for SIMD-accelerated ops.
    /// </summary>
    public static List<ScoredChunk> CosineScan(
        float[] query,
        IReadOnlyList<RagChunk> chunks,
        int topK)
    {
        return chunks
            .Where(c => c.Embedding.Length > 0)
            .Select(c => new ScoredChunk(
                c,
                TensorPrimitives.CosineSimilarity(query.AsSpan(), c.Embedding.AsSpan()),
                ScoreSource.Semantic))
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// Fuse semantic and BM25 ranked lists into a single hybrid ranking.
    /// </summary>
    public static List<ScoredChunk> Fuse(
        IReadOnlyList<ScoredChunk> semantic,
        IReadOnlyList<ScoredChunk> bm25,
        int topK)
    {
        var scores   = new Dictionary<string, float>();
        var chunkMap = new Dictionary<string, RagChunk>();

        for (int i = 0; i < semantic.Count; i++)
        {
            var id = semantic[i].Chunk.Id;
            scores[id] = scores.GetValueOrDefault(id) + SemanticWeight / (i + RrfK);
            chunkMap[id] = semantic[i].Chunk;
        }

        for (int i = 0; i < bm25.Count; i++)
        {
            var id = bm25[i].Chunk.Id;
            scores[id] = scores.GetValueOrDefault(id) + Bm25Weight / (i + RrfK);
            chunkMap.TryAdd(id, bm25[i].Chunk);
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select((kv, rank) => new ScoredChunk(chunkMap[kv.Key], kv.Value, ScoreSource.Hybrid))
            .ToList();
    }
}
