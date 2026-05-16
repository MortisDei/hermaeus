using Aether.Rag.Models;

namespace Aether.Rag.Chunking;

public record TextChunk(
    string Content,
    string SourceFile,
    string SourceTitle,
    int    Index,
    int    Total,
    string? ParentContent = null,
    RagChunkKind ChunkKind = RagChunkKind.PlainText,
    string? HeadingPath = null,
    string? CodeSymbolInfo = null,
    int? PageNumber = null,
    string? EventType = null,
    string? SourceUrl = null);
