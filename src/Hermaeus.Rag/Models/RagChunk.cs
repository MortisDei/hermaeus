namespace Hermaeus.Rag.Models;

/// <summary>
/// Chunk kind for structure-aware retrieval and scoring boosts.
/// </summary>
public enum RagChunkKind
{
    PlainText,           // Unstructured plain text or generic content
    MarkdownSection,     // Markdown section with heading hierarchy preserved
    PdfPageSection,      // PDF page with page number and section preserved
    CodeSymbol,          // Code symbol (class, method, function) with namespace/class path
    LogEvent,            // Log event with timestamp and severity preserved
    WebPageSection       // Web page section with page URL preserved
}

public class RagChunk
{
    public string   Id           { get; set; } = Guid.NewGuid().ToString();
    public string   DatasetId    { get; set; } = string.Empty;
    public string   SourceFile   { get; set; } = string.Empty;
    public string   SourcePath   { get; set; } = string.Empty;
    public string   SourceHash   { get; set; } = string.Empty;
    public string   SourceId     { get; set; } = string.Empty;
    public string   SourceRevisionId { get; set; } = string.Empty;
    public string   GenerationId { get; set; } = string.Empty;
    public DateTime? SourceModifiedUtc { get; set; }
    public string   SourceTitle  { get; set; } = string.Empty;
    public string   Content      { get; set; } = string.Empty;
    public int      ChunkIndex   { get; set; }
    public int      ChunkTotal   { get; set; }
    public string?  ParentId     { get; set; }   // non-null = child chunk
    public bool     IsParent     { get; set; }   // true = parent body row, excluded from retrieval candidates
    public int      TokenCount   { get; set; }
    public float[]  Embedding    { get; set; } = [];
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    
    // Structure-aware metadata for scoring and diagnostics
    public RagChunkKind ChunkKind        { get; set; } = RagChunkKind.PlainText;
    public string?  HeadingPath         { get; set; }   // For markdown: "# Title / ## Section" path
    public string?  CodeSymbolInfo      { get; set; }   // For code: "namespace.class.method" or similar
    public int?     PageNumber          { get; set; }   // For PDF: the page number
    public string?  EventType           { get; set; }   // For logs: error, warning, info, debug
    public string?  SourceUrl           { get; set; }   // For web: the original URL of the page
}
