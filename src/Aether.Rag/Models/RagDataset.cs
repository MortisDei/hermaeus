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
    public bool   PrependTitleToEmbedding { get; set; } = true;
    public string AliasFilePath     { get; set; } = string.Empty;
    public RagExtractionMode ExtractionMode { get; set; } = RagExtractionMode.TextMarkdown;
    public string FirecrawlApiUrl { get; set; } = string.Empty;
    public string FirecrawlApiKey { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = "{context}\n\nQuestion: {question}";
}

public enum RagExtractionMode
{
    TextMarkdown,
    PdfOcrPlaceholder,
    WebUrlPlaceholder,
    FirecrawlUrlPlaceholder
}
