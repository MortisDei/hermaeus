namespace Aether.Core.Services;

public record RagSource(string Title, string Type, string Url, double Score);

public record RagResult(
    string Answer,
    IReadOnlyList<RagSource> Sources,
    double RetrievalMs,
    double GenerationMs);

public interface IRagService
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<string>> GetDatasetsAsync(CancellationToken ct = default);
    Task<RagResult> QueryAsync(string dataset, string question, int topK = 5, CancellationToken ct = default);
    Task<bool> CheckHealthAsync(CancellationToken ct = default);
}
