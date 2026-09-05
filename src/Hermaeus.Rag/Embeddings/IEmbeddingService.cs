namespace Hermaeus.Rag.Embeddings;

public interface IEmbeddingService
{
    int Dimensions { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>Deferred index maintenance must yield to interactive embedding requests.</summary>
public interface IBackgroundEmbeddingService
{
    Task<float[]> EmbedBackgroundAsync(string text, CancellationToken ct = default);
}
