using System.Security.Cryptography;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Storage;

namespace Hermaeus.Rag.Pipeline;

/// <summary>Result of a drift scan (doc 03 3.2): a plan, not a mutation. Nothing changes
/// until <see cref="WatchedSourceService.ApplyNewAndChangedAsync"/> or a separate,
/// explicitly confirmed missing-file removal runs.</summary>
public sealed record RagRefreshPlan(
    IReadOnlyList<string> NewFiles,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> MissingFiles,
    int UnchangedCount,
    IReadOnlyList<string> Errors)
{
    public int TotalOnDisk => NewFiles.Count + ChangedFiles.Count + UnchangedCount;
    public bool HasDrift => NewFiles.Count > 0 || ChangedFiles.Count > 0 || MissingFiles.Count > 0;

    /// <summary>doc 03 3.3: warn prominently before confirmation when a refresh would
    /// remove more than half a dataset's sources - almost always an unmounted drive
    /// or a wrong glob, not an intended purge.</summary>
    public bool MissingIsOverHalf(int existingSourceCount) =>
        existingSourceCount > 0 && MissingFiles.Count > existingSourceCount / 2.0;
}

/// <summary>
/// r24 doc 03: turns the drift RagDatasetHealthService already detects into
/// an action. No FileSystemWatcher (doc 06 "Explicit rejections") - a
/// deterministic, cancellable scan the user (or an optional schedule)
/// triggers. Reuses the ingest pipeline for the apply step and the one glob
/// engine (GlobMatcher) for both include/exclude and change classification.
/// </summary>
public sealed class WatchedSourceService
{
    private static readonly TimeSpan MtimeTolerance = TimeSpan.FromSeconds(1);
    private readonly SqliteRagStore _store;
    private readonly RagPipeline _pipeline;
    private readonly HashSet<string> _refreshing = new(StringComparer.Ordinal);
    private readonly object _refreshingLock = new();

    public WatchedSourceService(SqliteRagStore store, RagPipeline pipeline)
    {
        _store = store;
        _pipeline = pipeline;
    }

    /// <summary>Walks every watched root under its globs and classifies drift against
    /// the dataset's stored source rows. Changes nothing.</summary>
    public async Task<RagRefreshPlan> ScanAsync(RagDataset dataset, CancellationToken ct = default)
    {
        var chunks = await _store.GetChunksAsync(dataset.Id, includeEmbeddings: false, ct);
        var bySource = chunks
            .Where(c => !string.IsNullOrWhiteSpace(c.SourcePath))
            .GroupBy(c => c.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newFiles = new List<string>();
        var changed = new List<string>();
        var errors = new List<string>();
        var unchanged = 0;

        foreach (var watched in dataset.Config.WatchedSources)
        {
            ct.ThrowIfCancellationRequested();
            if (!PathRootValidator.TryValidate(watched.Root, out var root, out var rootError))
            {
                errors.Add($"{watched.Root}: {rootError}");
                continue;
            }

            IReadOnlyList<string> files;
            try
            {
                files = EnumerateMatchingFiles(root, watched);
            }
            catch (Exception ex)
            {
                errors.Add($"{root}: {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                seen.Add(file);

                if (!bySource.TryGetValue(file, out var existing))
                {
                    newFiles.Add(file);
                    continue;
                }

                try
                {
                    if (IsChanged(file, existing.SourceHash, existing.SourceModifiedUtc))
                        changed.Add(file);
                    else
                        unchanged++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{file}: {ex.Message}");
                }
            }
        }

        // Missing: an indexed source path that falls under a watched root but
        // was not found on this scan.
        var missing = bySource.Keys
            .Where(path => !seen.Contains(path) && IsUnderAnyWatchedRoot(path, dataset.Config.WatchedSources))
            .ToList();

        return new RagRefreshPlan(newFiles, changed, missing, unchanged, errors);
    }

    /// <summary>doc 03 3.3: new and changed files only, via the same ingest pipeline as a
    /// manual ingest (chunking, embedding, BM25 rebuild, cache invalidation). One
    /// refresh per dataset at a time; a concurrent request is refused, not queued.</summary>
    public async Task<IngestReport> ApplyNewAndChangedAsync(RagDataset dataset, RagRefreshPlan plan, CancellationToken ct = default)
    {
        var files = plan.NewFiles.Concat(plan.ChangedFiles).ToList();
        if (files.Count == 0)
            return new IngestReport();

        lock (_refreshingLock)
        {
            if (!_refreshing.Add(dataset.Id))
                throw new InvalidOperationException($"A refresh for dataset '{dataset.Name}' is already running.");
        }

        try
        {
            var report = await _pipeline.IngestDirectoryAsync(
                dataset,
                Path.GetDirectoryName(files[0]) ?? files[0],
                progress: null,
                ct,
                new IngestOptions { DuplicatePolicy = IngestDuplicatePolicy.Replace },
                explicitFiles: files);

            foreach (var watched in dataset.Config.WatchedSources)
                watched.LastRefreshUtc = DateTime.UtcNow;
            await _store.SaveDatasetAsync(dataset, ct);

            return report;
        }
        finally
        {
            lock (_refreshingLock) _refreshing.Remove(dataset.Id);
        }
    }

    public bool IsRefreshing(string datasetId)
    {
        lock (_refreshingLock) return _refreshing.Contains(datasetId);
    }

    private static bool IsChanged(string file, string existingHash, DateTime? existingModifiedUtc)
    {
        if (!string.IsNullOrWhiteSpace(existingHash))
            return ComputeHash(file) != existingHash;

        if (existingModifiedUtc is null) return true;
        var mtime = File.GetLastWriteTimeUtc(file);
        return (mtime - existingModifiedUtc.Value).Duration() > MtimeTolerance;
    }

    private static string ComputeHash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsUnderAnyWatchedRoot(string path, IReadOnlyList<RagWatchedSource> watched) =>
        watched.Any(w =>
        {
            if (!PathRootValidator.TryValidate(w.Root, out var root, out _)) return false;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        });

    private static IReadOnlyList<string> EnumerateMatchingFiles(string root, RagWatchedSource watched)
    {
        var option = watched.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var all = Directory.EnumerateFiles(root, "*", option);

        var includes = watched.IncludeGlobs.Count > 0 ? watched.IncludeGlobs : ["**/*.txt", "**/*.md", "**/*.pdf"];
        var excludes = watched.ExcludeGlobs;

        var results = new List<string>();
        foreach (var file in all)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (excludes.Any(g => GlobMatcher.IsMatch(g, relative))) continue;
            if (!includes.Any(g => GlobMatcher.IsMatch(g, relative))) continue;
            results.Add(file);
        }

        return results;
    }
}
