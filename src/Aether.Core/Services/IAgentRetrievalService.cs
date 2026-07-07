namespace Aether.Core.Services;

/// <summary>One retrieved chunk, shaped for context-pack budgeting rather than a
/// chat-style answer: the agent packs these into its own prompt via
/// <see cref="ContextPackBuilder"/>, it does not render a RAG answer.</summary>
public sealed record RetrievedChunk(
    string Title,
    string Content,
    int TokenCount,
    double Score,
    DateTime? SourceModifiedUtc,
    string? Locator = null);

/// <summary>The minimal retrieval seam the agent depends on. Implemented by
/// Aether.Rag so Aether.Agent never references the retrieval implementation
/// project directly (docs/review/06-technical-debt.md item 11).</summary>
public interface IAgentRetrievalService
{
    Task<bool> DatasetExistsAsync(string datasetId, CancellationToken ct = default);

    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string datasetId, string query, int topK, CancellationToken ct = default);
}
