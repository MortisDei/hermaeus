namespace Aether.Rag.Models;

public enum ScoreSource { Semantic, Bm25, Hybrid }

public record ScoredChunk(RagChunk Chunk, float Score, ScoreSource Source);
