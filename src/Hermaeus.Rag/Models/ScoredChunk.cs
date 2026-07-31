namespace Hermaeus.Rag.Models;

public enum ScoreSource { Semantic, Bm25, Hybrid, Reranker }

public record ScoredChunk(RagChunk Chunk, float Score, ScoreSource Source);

/// <summary>
/// r27 02-retrieval-that-scales.md 2.4/2.5: a scan result before content has
/// been loaded. The semantic scan runs over ids and embeddings only; the
/// handful of chunks that survive ranking are then read from SQLite by id and
/// become ordinary <see cref="ScoredChunk"/> records with their text.
/// </summary>
public readonly record struct ScoredChunkId(string ChunkId, float Score);
