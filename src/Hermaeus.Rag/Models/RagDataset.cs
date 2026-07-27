namespace Hermaeus.Rag.Models;

public class RagDataset
{
    public string Id          { get; set; } = Guid.NewGuid().ToString();
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int    ChunkCount  { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastIngestUtc { get; set; }
    public string LastIngestPath { get; set; } = string.Empty;

    /// <summary>r24 doc 01: project this dataset belongs to, or empty for none. A
    /// default and a filter only; the dataset stays usable from anywhere.</summary>
    public string ProjectId { get; set; } = string.Empty;
    public RagDatasetConfig Config { get; set; } = new();

    /// <summary>
    /// Deep-clones this dataset via a JSON round trip so a reindex can target
    /// the new embedding model on a working copy without flipping the live,
    /// UI-bound instance's recorded model before the pipeline actually
    /// commits the re-embedded vectors (r12 03-runtime-vm-correctness.md 3.7).
    /// </summary>
    public RagDataset Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<RagDataset>(json) ?? new RagDataset();
    }
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

        /// <summary>r24 doc 03 3.1: zero or more folders this dataset watches for drift.
        /// Persisted as part of config_json - no schema migration needed.</summary>
        public List<RagWatchedSource> WatchedSources { get; set; } = [];
}

/// <summary>r24 doc 03 3.1: one watched folder. Change detection prefers
/// SourceHash on the dataset's existing chunk rows, falling back to
/// SourceModifiedUtc only when a stored hash is absent.</summary>
public sealed class RagWatchedSource
{
    public string Root { get; set; } = string.Empty;
    public List<string> IncludeGlobs { get; set; } = [];

    /// <summary>Ships non-empty by default (doc 03 3.1): a user who points a watched
    /// source at a repo root and gets build output swept in will never use this again.</summary>
    public List<string> ExcludeGlobs { get; set; } = DefaultExcludeGlobs();
    public bool Recursive { get; set; } = true;
    public DateTime? LastRefreshUtc { get; set; }

    public static List<string> DefaultExcludeGlobs() =>
        ["**/.git/**", "**/node_modules/**", "**/bin/**", "**/obj/**", "**/.venv/**", "**/__pycache__/**", "**/dist/**", "**/target/**"];
}

public enum RagExtractionMode
{
    TextMarkdown,
    PdfOcrPlaceholder,
    WebUrl,
    FirecrawlUrlPlaceholder
}
