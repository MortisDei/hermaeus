namespace Aether.Rag.Models;

public sealed class RagIngestHealth
{
    public int FileCount { get; set; }
    public int DuplicateChunkCount { get; set; }
    public int EmptyChunkCount { get; set; }
    public int OversizedFileCount { get; set; }
    public int UnsupportedFileCount { get; set; }
    public int StaleSourceCount { get; set; }
    public List<string> Warnings { get; set; } = [];
}
