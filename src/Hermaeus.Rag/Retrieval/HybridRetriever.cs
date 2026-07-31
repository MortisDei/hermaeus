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
    /// r27 02-retrieval-that-scales.md 2.4: the same scan over a contiguous
    /// embedding block. The chunk-list version above allocates one ScoredChunk
    /// per chunk and sorts the whole corpus (n log n) to select topK of them.
    /// This is one pass with a bounded min-heap of size topK: one
    /// <see cref="TensorPrimitives.CosineSimilarity"/> call per chunk against a
    /// slice of the block, no per-chunk allocation, and no full sort.
    /// Returns ids and scores; content is loaded for the survivors only (2.5).
    /// </summary>
    public static List<ScoredChunkId> CosineScan(float[] query, RagScanIndex index, int topK)
    {
        // The dimension-mismatch guard moves from the chunk to the block,
        // because a contiguous block has exactly one dimension. A query whose
        // dimension differs returns no semantic results, exactly as before.
        if (topK <= 0 || index.Count == 0 || index.Dimension == 0 || query.Length != index.Dimension)
            return [];

        var heap = new BoundedTopK(Math.Min(topK, index.Count));
        for (var i = 0; i < index.Count; i++)
            heap.Offer(i, TensorPrimitives.CosineSimilarity(query.AsSpan(), index.RowAt(i)));

        // A heap and a sort disagree about equal scores, so ties break on scan
        // position: deterministic across runs, which keeps the eval harness from
        // becoming noisy for reasons unrelated to retrieval quality.
        return heap.Drain()
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.Index)
            .Select(e => new ScoredChunkId(index.ChunkIds[e.Index], e.Score))
            .ToList();
    }

    /// <summary>
    /// A fixed-capacity min-heap over (score, scan index). The root is the worst
    /// entry currently held, so an incoming score only has to beat one
    /// comparison to be considered. Ties prefer the lower scan index, so the
    /// entry evicted first is always the later one.
    /// </summary>
    private sealed class BoundedTopK(int capacity)
    {
        private readonly (int Index, float Score)[] _items = new (int, float)[Math.Max(capacity, 1)];
        private readonly int _capacity = Math.Max(capacity, 1);
        private int _count;

        public void Offer(int index, float score)
        {
            if (_count < _capacity)
            {
                _items[_count] = (index, score);
                SiftUp(_count++);
                return;
            }

            if (!IsWorseThan(_items[0], (index, score)))
                return;

            _items[0] = (index, score);
            SiftDown(0);
        }

        public IEnumerable<(int Index, float Score)> Drain() => _items.Take(_count);

        /// <summary>True when <paramref name="a"/> ranks below <paramref name="b"/> and should be evicted for it.</summary>
        private static bool IsWorseThan((int Index, float Score) a, (int Index, float Score) b) =>
            a.Score < b.Score || (a.Score == b.Score && a.Index > b.Index);

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                var parent = (i - 1) / 2;
                if (!IsWorseThan(_items[i], _items[parent]))
                    break;
                (_items[i], _items[parent]) = (_items[parent], _items[i]);
                i = parent;
            }
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                var smallest = i;
                var left = (2 * i) + 1;
                var right = left + 1;
                if (left < _count && IsWorseThan(_items[left], _items[smallest])) smallest = left;
                if (right < _count && IsWorseThan(_items[right], _items[smallest])) smallest = right;
                if (smallest == i)
                    return;
                (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                i = smallest;
            }
        }
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
