namespace Aether.Rag.Chunking;

public record TextChunk(
    string Content,
    string SourceFile,
    string SourceTitle,
    int    Index,
    int    Total,
    string? ParentContent = null);   // non-null when parent-child mode
