using Hermaeus.Rag.Models;

namespace Hermaeus.Rag;

/// <summary>
/// Health signals for one dataset's on-disk sources: how many distinct
/// source files feed it, how many chunk rows look like accidental
/// duplicates, and how many source files are missing or have changed on
/// disk since they were ingested.
/// </summary>
public sealed record RagDatasetHealth(
    int SourceCount,
    int DuplicateSources,
    int MissingFiles,
    int StaleFiles,
    IReadOnlyList<string> MissingSourcePaths);

/// <summary>
/// The columns <see cref="RagDatasetHealthService"/> actually needs from a
/// chunk row (r10 02-rag-quality.md 2.5): source path, chunk index, and
/// modified timestamp, never content or embeddings.
/// </summary>
public readonly record struct RagChunkHealthInfo(string SourcePath, int ChunkIndex, DateTime? SourceModifiedUtc);

/// <summary>
/// Computes dataset health from already-loaded chunk metadata plus a
/// file-system check against each distinct source path. Extracted from
/// <c>RagViewModel.RefreshDatasetManagerAsync</c> (docs/review/archived/r1/01-architecture-review.md
/// item 5) so it's testable without a live dataset store.
/// </summary>
public static class RagDatasetHealthService
{
    /// <summary>
    /// Convenience overload over full chunk objects, kept for callers and
    /// tests that already have them loaded. Prefer
    /// <see cref="Compute(IReadOnlyList{RagChunkHealthInfo})"/> with the
    /// lightweight store projection on any hot refresh path: it runs after
    /// every ingest, delete, and app load, and full chunks carry content
    /// health never needs.
    /// </summary>
    public static RagDatasetHealth Compute(IReadOnlyList<RagChunk> chunks) =>
        Compute(chunks.Select(c => new RagChunkHealthInfo(c.SourcePath, c.ChunkIndex, c.SourceModifiedUtc)).ToList());

    public static RagDatasetHealth Compute(IReadOnlyList<RagChunkHealthInfo> chunks)
    {
        var sources = chunks.GroupBy(c => c.SourcePath, StringComparer.OrdinalIgnoreCase).ToList();
        var duplicateSources = chunks
            .GroupBy(c => $"{c.SourcePath}::{c.ChunkIndex}", StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Count() > 1);

        var missingFiles = 0;
        var staleFiles = 0;
        var missingSourcePaths = new List<string>();
        foreach (var source in sources)
        {
            var path = source.Key;
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!File.Exists(path))
            {
                missingFiles++;
                missingSourcePaths.Add(path);
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

        return new RagDatasetHealth(sources.Count, duplicateSources, missingFiles, staleFiles, missingSourcePaths);
    }
}
