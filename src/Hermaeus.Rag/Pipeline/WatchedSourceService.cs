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
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var bySource = chunks
            .Where(c => !string.IsNullOrWhiteSpace(c.SourcePath))
            .GroupBy(c => c.SourcePath, pathComparer)
            .ToDictionary(g => g.Key, g => g.First(), pathComparer);

        var seen = new HashSet<string>(pathComparer);
        var newFiles = new List<string>();
        var changed = new List<string>();
        var errors = new List<string>();
        var blockedRoots = new List<string>();
        var unchanged = 0;

        foreach (var watched in dataset.Config.WatchedSources)
        {
            ct.ThrowIfCancellationRequested();
            if (!PathRootValidator.TryValidate(watched.Root, out var root, out var rootError))
            {
                errors.Add($"{watched.Root}: {rootError}");
                AddBlockedRoot(blockedRoots, watched.Root);
                continue;
            }

            var rootIdentity = RagSourceIdentity.TryGetRootIdentity(root);
            if (rootIdentity is null)
            {
                errors.Add($"{root}: root identity is Unknown; no removal or refresh plan was created.");
                blockedRoots.Add(root);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(watched.LastConfirmedRootIdentity)
                && !string.Equals(watched.LastConfirmedRootIdentity, rootIdentity, StringComparison.Ordinal))
            {
                errors.Add($"{root}: root identity changed; confirm the replacement folder before refreshing.");
                blockedRoots.Add(root);
                continue;
            }
            if (string.IsNullOrWhiteSpace(watched.LastConfirmedRootIdentity))
            {
                errors.Add($"{root}: root identity has not been confirmed; no refresh or missing-source plan was created.");
                blockedRoots.Add(root);
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
                blockedRoots.Add(root);
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSafeFileUnderRoot(root, file))
                {
                    errors.Add($"{file}: symbolic-link or reparse ancestor rejected.");
                    continue;
                }
                seen.Add(file);

                if (!bySource.TryGetValue(file, out var existing))
                {
                    newFiles.Add(file);
                    continue;
                }

                try
                {
                    if (await IsChangedAsync(file, existing.SourceHash, existing.SourceModifiedUtc, ct))
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
            .Where(path => !seen.Contains(path)
                && !blockedRoots.Any(root => IsUnderRoot(root, path))
                && IsUnderAnyWatchedRoot(path, dataset.Config.WatchedSources))
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

    private static async Task<bool> IsChangedAsync(
        string file, string existingHash, DateTime? existingModifiedUtc, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(existingHash))
            return !string.Equals(await ComputeSourceHashAsync(file, ct), existingHash, StringComparison.Ordinal);

        if (existingModifiedUtc is null) return true;
        var mtime = File.GetLastWriteTimeUtc(file);
        return (mtime - existingModifiedUtc.Value).Duration() > MtimeTolerance;
    }

    private static async Task<string> ComputeSourceHashAsync(string file, CancellationToken ct)
    {
        if (Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var extracted = await PdfTextExtractor.ExtractAsync(file, ct);
            return extracted.HasText ? ComputeHash(extracted.Text) : string.Empty;
        }

        return ComputeHash(await File.ReadAllTextAsync(file, ct));
    }

    private static string ComputeHash(string text) => Convert.ToHexString(
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private static bool IsUnderAnyWatchedRoot(string path, IReadOnlyList<RagWatchedSource> watched) =>
        watched.Any(w =>
        {
            if (!PathRootValidator.TryValidate(w.Root, out var root, out _)) return false;
            return IsUnderRoot(root, path);
        });

    private static void AddBlockedRoot(List<string> blockedRoots, string root)
    {
        try
        {
            blockedRoots.Add(Path.GetFullPath(root.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    private static bool IsUnderRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static IReadOnlyList<string> EnumerateMatchingFiles(string root, RagWatchedSource watched)
    {
        var option = watched.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var all = Directory.EnumerateFiles(root, "*", option);

        var includes = watched.IncludeGlobs.Count > 0 ? watched.IncludeGlobs : ["**/*.txt", "**/*.md", "**/*.pdf"];
        var excludes = watched.ExcludeGlobs;

        var results = new List<string>();
        foreach (var file in all)
        {
            if (!IsSafeFileUnderRoot(root, file))
                continue;
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (excludes.Any(g => GlobMatcher.IsMatch(g, relative))) continue;
            if (!includes.Any(g => GlobMatcher.IsMatch(g, relative))) continue;
            results.Add(file);
        }

        return results;
    }

    private static bool IsSafeFileUnderRoot(string root, string file)
    {
        var fullFile = Path.GetFullPath(file);
        var relative = Path.GetRelativePath(root, fullFile);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return false;

        var current = root;
        foreach (var segment in relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return true;
    }
}
