using Aether.Rag.Models;

namespace Aether.Rag;

/// <summary>
/// Health signals for one dataset's on-disk sources: how many distinct
/// source files feed it, how many chunk rows look like accidental
/// duplicates, and how many source files are missing or have changed on
/// disk since they were ingested.
/// </summary>
public sealed record RagDatasetHealth(int SourceCount, int DuplicateSources, int MissingFiles, int StaleFiles);

/// <summary>
/// Computes dataset health from already-loaded chunks plus a file-system
/// check against each distinct source path. Extracted from
/// <c>RagViewModel.RefreshDatasetManagerAsync</c> (docs/review/01-architecture-review.md
/// item 5) so it's testable without a live dataset store.
/// </summary>
public static class RagDatasetHealthService
{
    public static RagDatasetHealth Compute(IReadOnlyList<RagChunk> chunks)
    {
        var sources = chunks.GroupBy(c => c.SourcePath, StringComparer.OrdinalIgnoreCase).ToList();
        var duplicateSources = chunks
            .GroupBy(c => $"{c.SourcePath}::{c.ChunkIndex}", StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Count() > 1);

        var missingFiles = 0;
        var staleFiles = 0;
        foreach (var source in sources)
        {
            var path = source.Key;
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!File.Exists(path))
            {
                missingFiles++;
                continue;
            }

            var sourceModified = source
                .Select(c => c.SourceModifiedUtc)
                .Where(x => x.HasValue)
                .OrderByDescending(x => x!.Value)
                .FirstOrDefault();
            if (sourceModified.HasValue && File.GetLastWriteTimeUtc(path) > sourceModified.Value.AddSeconds(1))
                staleFiles++;
        }

        return new RagDatasetHealth(sources.Count, duplicateSources, missingFiles, staleFiles);
    }
}
