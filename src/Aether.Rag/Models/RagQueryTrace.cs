namespace Aether.Rag.Models;

public enum RagGroundingMode
{
    TokenOverlap,
    SemanticPlaceholder
}

public sealed class RagQueryTrace
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DatasetId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string ExpandedQuestion { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public long RetrievalLatencyMs { get; set; }
    public long TotalLatencyMs { get; set; }
    public float GroundingScore { get; set; }
    public RagGroundingMode GroundingMode { get; set; } = RagGroundingMode.TokenOverlap;
    public List<RagTraceChunk> RetrievedChunks { get; set; } = [];
    public List<RagTraceChunk> SelectedContext { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class RagTraceChunk
{
    public int Rank { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public float Score { get; set; }
    public string Content { get; set; } = string.Empty;
}
