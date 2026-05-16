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
    public bool   EnableWebLoader   { get; set; } = false;
    public string WebUrlList        { get; set; } = string.Empty;
    public int    WebMaxPages       { get; set; } = 5;
    public string EmbeddingModel    { get; set; } = string.Empty;
    public int    EmbeddingDimensions { get; set; }
    public string FirecrawlApiUrl { get; set; } = string.Empty;
    public string FirecrawlApiKey { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = "{context}\n\nQuestion: {question}";

        // Hybrid retriever tuning: semantic vs BM25 fusion weights and RRF k
        public float HybridSemanticWeight { get; set; } = 0.7f;
        public float HybridBm25Weight { get; set; } = 0.3f;
        public float HybridRrfK { get; set; } = 60f;
}

public enum RagExtractionMode
{
    TextMarkdown,
    PdfOcrPlaceholder,
    WebUrl,
    FirecrawlUrlPlaceholder
}
