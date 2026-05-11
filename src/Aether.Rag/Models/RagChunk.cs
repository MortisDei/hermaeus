namespace Aether.Rag.Models;

public class RagChunk
{
    public string   Id           { get; set; } = Guid.NewGuid().ToString();
    public string   DatasetId    { get; set; } = string.Empty;
    public string   SourceFile   { get; set; } = string.Empty;
    public string   SourcePath   { get; set; } = string.Empty;
    public string   SourceHash   { get; set; } = string.Empty;
    public DateTime? SourceModifiedUtc { get; set; }
    public string   SourceTitle  { get; set; } = string.Empty;
    public string   Content      { get; set; } = string.Empty;
    public int      ChunkIndex   { get; set; }
    public int      ChunkTotal   { get; set; }
    public string?  ParentId     { get; set; }   // non-null = child chunk
    public int      TokenCount   { get; set; }
    public float[]  Embedding    { get; set; } = [];
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
}
