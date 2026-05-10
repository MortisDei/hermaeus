namespace Aether.Rag.Models;

public class RagResult
{
    public string Question      { get; init; } = string.Empty;
    public string Answer        { get; init; } = string.Empty;
    public IReadOnlyList<RagSourceRef> Sources { get; init; } = [];
    public float  GroundingScore { get; init; }
    public double RetrievalMs   { get; init; }
    public double GenerationMs  { get; init; }
}

public class RagSourceRef
{
    public string Title  { get; init; } = string.Empty;
    public string File   { get; init; } = string.Empty;
    public float  Score  { get; init; }
    public int    Rank   { get; init; }
}
