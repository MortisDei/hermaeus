using Hermaeus.Rag.Models;

namespace Hermaeus.Rag;

/// <summary>
/// Builds or updates the RagDataset an ingest run targets, and renders the
/// ingest health summary line. Extracted from RagViewModel.IngestAsync.
/// </summary>
public static class RagIngestRequestBuilder
{
    public static RagDataset PrepareDataset(
        RagDataset? existing,
        string newDatasetName,
        bool enableWebLoader,
        string ingestPath,
        string webUrlList,
        int webMaxPages,
        bool useParentChild,
        string embeddingModel)
    {
        var ds = existing ?? new RagDataset();

        if (existing is null)
        {
            ds.Name = newDatasetName.Trim();
            ds.Description = enableWebLoader
                ? "Ingested from explicitly configured web URLs"
                : $"Ingested from {ingestPath}";
            ds.Config = new RagDatasetConfig
            {
                UseParentChild = useParentChild,
                EmbeddingModel = embeddingModel,
                EnableWebLoader = enableWebLoader,
                WebUrlList = enableWebLoader ? webUrlList.Trim() : string.Empty,
                WebMaxPages = Math.Clamp(webMaxPages <= 0 ? 5 : webMaxPages, 1, 20),
                ExtractionMode = enableWebLoader ? RagExtractionMode.WebUrl : RagExtractionMode.TextMarkdown
            };
        }

        // r10 01-rag-correctness.md 1.7: set on first ingest too, not only
        // re-ingest, so SaveDatasetAsync always has a value to persist and
        // the Add-to-dataset folder pre-fill survives a restart.
        ds.LastIngestPath = enableWebLoader ? webUrlList.Trim() : ingestPath;
        ds.LastIngestUtc = DateTime.UtcNow;

        return ds;
    }

    public static string BuildHealthSummary(RagIngestHealth health)
    {
        var parts = new List<string> { $"Files: {health.FileCount}" };
        if (health.DuplicateChunkCount > 0) parts.Add($"Duplicate chunks: {health.DuplicateChunkCount}");
        if (health.EmptyChunkCount > 0) parts.Add($"Empty chunks: {health.EmptyChunkCount}");
        if (health.OversizedFileCount > 0) parts.Add($"Oversized files: {health.OversizedFileCount}");
        if (health.UnsupportedFileCount > 0) parts.Add($"Unsupported files: {health.UnsupportedFileCount}");
        if (health.StaleSourceCount > 0) parts.Add($"Stale sources: {health.StaleSourceCount}");
        if (health.Warnings?.Count > 0) parts.Add(string.Join("; ", health.Warnings));
        return string.Join("; ", parts);
    }
}
