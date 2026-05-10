namespace Aether.Rag.Models;

public class RagDataset
{
    public string Id          { get; set; } = Guid.NewGuid().ToString();
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int    ChunkCount  { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public RagDatasetConfig Config { get; set; } = new();
}

public class RagDatasetConfig
{
    public int    TargetChunkChars  { get; set; } = 1600;
    public int    OverlapChars      { get; set; } = 320;
    public int    ParentChunkChars  { get; set; } = 3200;
    public bool   UseParentChild    { get; set; } = false;
    public string AliasFilePath     { get; set; } = string.Empty;
}
