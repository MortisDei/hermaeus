using Hermaeus.Rag.Models;

namespace Hermaeus.Rag.Pipeline;

// Resource policy for one ingest operation, not settings or a persisted format.
internal sealed record RagIngestLimits(
    long MaxSourceBytes = 512L * 1024 * 1024,
    int MaxExtractedCharacters = 16 * 1024 * 1024,
    int MaxPdfPages = 2_000,
    long MaxStagingBytes = 256L * 1024 * 1024)
{
    internal static RagIngestLimits Default { get; } = new();

    internal void Validate()
    {
        if (MaxSourceBytes <= 0 || MaxExtractedCharacters <= 0 || MaxPdfPages <= 0 || MaxStagingBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(RagIngestLimits), "Ingest limits must be positive.");
    }
}

// Account candidate text and vectors once, independently of parser/provider
// temporaries, object overhead, old generations and total process working set.
internal sealed class RagIngestBudget(long maximumBytes)
{
    internal long Bytes { get; private set; }

    internal void AddText(string text) => AddBytes(2L * text.Length);
    internal void AddVector(float[] vector) => AddBytes(4L * vector.Length);

    internal void AddChunk(RagChunk chunk)
    {
        AddText(chunk.Content);
        AddVector(chunk.Embedding);
    }

    internal void AddBytes(long bytes)
    {
        if (bytes < 0 || bytes > maximumBytes - Bytes)
            throw new InvalidOperationException($"RAG candidate text and vectors exceed the {maximumBytes} byte staging limit.");
        Bytes += bytes;
    }

    internal static void ValidateCandidate(IEnumerable<RagChunk> chunks, long maximumBytes)
    {
        var budget = new RagIngestBudget(maximumBytes);
        foreach (var chunk in chunks)
            budget.AddChunk(chunk);
    }
}
