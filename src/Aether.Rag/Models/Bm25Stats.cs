namespace Aether.Rag.Models;

public class Bm25Stats
{
    public int   TotalDocuments       { get; set; }
    public float AverageDocumentLength { get; set; }
    // term -> number of documents containing that term
    public Dictionary<string, int> DocumentFrequencies { get; set; } = [];
}
