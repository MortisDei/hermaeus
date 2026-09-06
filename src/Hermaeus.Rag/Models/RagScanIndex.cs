namespace Hermaeus.Rag.Models;

/// <summary>
/// r27 02-retrieval-that-scales.md 2.3: a dataset's semantic scan index. The
/// in-memory cache used to hold whole <see cref="RagChunk"/> records, text and
/// all, so the per-chunk footprint varied with document size and the cache
/// ceiling arrived at an unpredictable corpus size.
/// This holds only what a cosine scan actually reads: the chunk ids in scan
/// order and one contiguous block of <c>Count * Dimension</c> floats. Content,
/// paths, titles and heading paths stay in SQLite and are read for the small
/// number of chunks that survive ranking (2.5).
/// </summary>
public sealed class RagScanIndex
{
    public static readonly RagScanIndex Empty = new([], [], 0, string.Empty);

    /// <summary>Chunk ids in scan order; index i owns block slice [i * Dimension, Dimension).</summary>
    public string[] ChunkIds { get; }

    /// <summary>One contiguous <c>Count * Dimension</c> float block. Never jagged.</summary>
    public float[] Block { get; }

    /// <summary>The embedding dimension every row in this block shares. Zero when empty.</summary>
    public int Dimension { get; }

    /// <summary>The embedding model the dataset was built with, for the mismatch check.</summary>
    public string EmbeddingModel { get; }

    /// <summary>The published dataset generation this index was loaded from.</summary>
    public string GenerationId { get; }

    public int Count => ChunkIds.Length;

    /// <summary>
    /// Exact, not estimated: the block's size is arithmetic over count and
    /// dimension rather than a sum over strings whose length nobody controls.
    /// </summary>
    public long ByteSize => ByteSizeFor(Count, Dimension);

    /// <summary>
    /// The same arithmetic without a loaded index, so the RAG panel can show a
    /// dataset's scan-index size against the budget while it is being ingested,
    /// rather than the user discovering the ceiling as a slow query (2.7).
    /// </summary>
    public static long ByteSizeFor(int chunkCount, int dimension) =>
        ((long)chunkCount * dimension * sizeof(float)) + ((long)chunkCount * IdOverheadBytes);

    /// <summary>A chunk id string plus its slot in the array and the dictionary that finds it.</summary>
    private const int IdOverheadBytes = 128;

    public RagScanIndex(string[] chunkIds, float[] block, int dimension, string embeddingModel, string generationId = "")
    {
        ChunkIds = chunkIds;
        Block = block;
        Dimension = dimension;
        EmbeddingModel = embeddingModel;
        GenerationId = generationId;
    }

    public ReadOnlySpan<float> RowAt(int index) => Block.AsSpan(index * Dimension, Dimension);

    /// <summary>
    /// Builds the block from chunks that carry embeddings, dropping any whose
    /// embedding length disagrees with the majority. With one contiguous block a
    /// dataset has exactly one dimension by construction, which is what lets the
    /// dimension-mismatch check move from the chunk to the block (2.4).
    /// </summary>
    public static RagScanIndex Build(IReadOnlyList<RagChunk> chunks, string embeddingModel)
    {
        var dimension = chunks
            .Where(c => c.Embedding.Length > 0)
            .GroupBy(c => c.Embedding.Length)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => (int?)g.Key)
            .FirstOrDefault();

        if (dimension is null or 0)
            return new RagScanIndex([], [], 0, embeddingModel);

        var usable = chunks.Where(c => c.Embedding.Length == dimension.Value).ToList();
        var ids = new string[usable.Count];
        var block = new float[(long)usable.Count * dimension.Value <= int.MaxValue ? usable.Count * dimension.Value : 0];
        if (block.Length == 0 && usable.Count > 0)
            return new RagScanIndex([], [], 0, embeddingModel);

        for (var i = 0; i < usable.Count; i++)
        {
            ids[i] = usable[i].Id;
            usable[i].Embedding.CopyTo(block, i * dimension.Value);
        }

        return new RagScanIndex(ids, block, dimension.Value, embeddingModel);
    }
}
