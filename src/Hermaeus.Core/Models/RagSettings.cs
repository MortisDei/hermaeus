namespace Hermaeus.Core.Models;

/// <summary>
/// Retrieval-Augmented Generation (RAG) configuration including service endpoints,
/// reranking, and embedding model settings.
/// </summary>
public class RagSettings
{
    /// <summary>
    /// Enable or disable RAG functionality.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// URL of the RAG service endpoint.
    /// </summary>
    public string ServiceUrl { get; set; } = "http://localhost:8765";

    /// <summary>
    /// Enable semantic reranking of RAG results.
    /// </summary>
    public bool RerankerEnabled { get; set; } = true;

    /// <summary>
    /// Automatically download reranker models if missing.
    /// </summary>
    public bool RerankerAutoDownload { get; set; } = true;

    /// <summary>
    /// Path to the reranker model file (ONNX format).
    /// </summary>
    public string RerankerModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Maximum sequence length for reranking.
    /// </summary>
    public int RerankerMaxLength { get; set; } = 256;

    /// <summary>
    /// Maximum number of candidates to rerank.
    /// </summary>
    public int RerankerMaxCandidates { get; set; } = 20;

    /// <summary>
    /// Base URL used for embedding requests (/v1/embeddings).
    /// This can point to a dedicated embeddings server running on a different port.
    /// </summary>
    public string EmbeddingBaseUrl { get; set; } = "http://localhost:39202";

    /// <summary>
    /// Embedding model name used for document chunking and retrieval.
    /// </summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// r21: token budget for the Knowledge context block injected into chat
    /// when a conversation has a RAG dataset attached. Separate from the RAG
    /// panel's own query token budget.
    /// </summary>
    public int ChatInjectionTokenBudget { get; set; } = 2000;
}
