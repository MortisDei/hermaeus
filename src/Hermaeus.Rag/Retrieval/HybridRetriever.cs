using System.Numerics.Tensors;
using Hermaeus.Rag.Models;

namespace Hermaeus.Rag.Retrieval;

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
        // Belt-and-braces: TensorPrimitives.CosineSimilarity throws on
        // mismatched vector lengths. An embedding-model switch (r10
        // 01-rag-correctness.md 1.4) is the query-level guard that normally
        // prevents this; filtering here means a dimension mismatch never
        // surfaces as a raw exception regardless of how it occurred.
        return chunks
            .Where(c => c.Embedding.Length > 0 && c.Embedding.Length == query.Length)
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

        // r10 02-rag-quality.md 2.3: boosts are a proportional multiplier on
        // the fused RRF score, not an absolute addend. RRF scores are tiny
        // (rank 1 contributes only semanticWeight/(0+rrfK), around 0.012 at
        // defaults), so adding these same constants directly let a single
        // phrase-match boost outrank being the top semantic hit. As a
        // multiplier, capped at MaxBoostFactor, structural matches can only
        // break ties and lift near-ties, never let a low-ranked candidate
        // leapfrog a clearly stronger one.
        foreach (var (id, score) in scores.ToList())
        {
            if (!chunkMap.TryGetValue(id, out var chunk))
                continue;

            var boostFactor = Math.Min(ComputeBoostFactor(query, queryTerms, chunk), MaxBoostFactor);
            scores[id] = score * (1f + boostFactor);
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select((kv, rank) => new ScoredChunk(chunkMap[kv.Key], kv.Value, ScoreSource.Hybrid))
            .ToList();
    }

    /// <summary>Safety cap: even if every structural signal fires at once, the fused score can be lifted by at most this fraction.</summary>
    private const float MaxBoostFactor = 0.5f;

    private static float ComputeBoostFactor(string query, IReadOnlyCollection<string> queryTerms, RagChunk chunk)
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
