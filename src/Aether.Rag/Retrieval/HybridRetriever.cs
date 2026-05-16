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
    // Default fusion parameters. These can be overridden via RagDatasetConfig.
    private const float DefaultRrfK           = 60f;
    private const float DefaultSemanticWeight = 0.7f;
    private const float DefaultBm25Weight     = 0.3f;

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
        string query,
        IReadOnlyList<ScoredChunk> semantic,
        IReadOnlyList<ScoredChunk> bm25,
        int topK,
        float semanticWeight = DefaultSemanticWeight,
        float bm25Weight = DefaultBm25Weight,
        float rrfK = DefaultRrfK)
    {
        var scores   = new Dictionary<string, float>();
        var chunkMap = new Dictionary<string, RagChunk>();
        var queryTerms = Bm25Scorer.Tokenize(query);

        for (int i = 0; i < semantic.Count; i++)
        {
            var id = semantic[i].Chunk.Id;
            scores[id] = scores.GetValueOrDefault(id) + semanticWeight / (i + rrfK);
            chunkMap[id] = semantic[i].Chunk;
        }

        for (int i = 0; i < bm25.Count; i++)
        {
            var id = bm25[i].Chunk.Id;
            scores[id] = scores.GetValueOrDefault(id) + bm25Weight / (i + rrfK);
            chunkMap.TryAdd(id, bm25[i].Chunk);
        }

        foreach (var (id, score) in scores.ToList())
        {
            if (!chunkMap.TryGetValue(id, out var chunk))
                continue;

            scores[id] = score + ComputeBoost(query, queryTerms, chunk);
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select((kv, rank) => new ScoredChunk(chunkMap[kv.Key], kv.Value, ScoreSource.Hybrid))
            .ToList();
    }

    private static float ComputeBoost(string query, IReadOnlyCollection<string> queryTerms, RagChunk chunk)
    {
        var boost = 0f;
        var phrase = query.Trim();

        if (!string.IsNullOrWhiteSpace(phrase))
        {
            if (chunk.Content.Contains(phrase, StringComparison.OrdinalIgnoreCase)) boost += 0.015f;
            if (!string.IsNullOrWhiteSpace(chunk.SourceTitle) && chunk.SourceTitle.Contains(phrase, StringComparison.OrdinalIgnoreCase)) boost += 0.012f;
            if (!string.IsNullOrWhiteSpace(chunk.HeadingPath) && chunk.HeadingPath.Contains(phrase, StringComparison.OrdinalIgnoreCase)) boost += 0.012f;
            if (!string.IsNullOrWhiteSpace(chunk.CodeSymbolInfo) && chunk.CodeSymbolInfo.Contains(phrase, StringComparison.OrdinalIgnoreCase)) boost += 0.012f;
        }

        if (!string.IsNullOrWhiteSpace(chunk.HeadingPath) && queryTerms.Any(t => chunk.HeadingPath.Contains(t, StringComparison.OrdinalIgnoreCase)))
            boost += 0.008f;

        if (!string.IsNullOrWhiteSpace(chunk.CodeSymbolInfo) && queryTerms.Any(t => chunk.CodeSymbolInfo.Contains(t, StringComparison.OrdinalIgnoreCase)))
            boost += 0.010f;

        if (!string.IsNullOrWhiteSpace(chunk.EventType) && queryTerms.Any(t => chunk.EventType.Contains(t, StringComparison.OrdinalIgnoreCase)))
            boost += 0.008f;

        if (chunk.PageNumber.HasValue && queryTerms.Any(t => int.TryParse(t, out var page) && page == chunk.PageNumber.Value))
            boost += 0.008f;

        if (chunk.SourceModifiedUtc.HasValue)
        {
            var ageDays = (DateTime.UtcNow - chunk.SourceModifiedUtc.Value).TotalDays;
            if (ageDays < 30) boost += 0.006f;
            else if (ageDays < 180) boost += 0.003f;
        }

        return boost;
    }
}
