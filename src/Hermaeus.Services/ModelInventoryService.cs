namespace Hermaeus.Services;

public sealed record ModelInventoryEntry(
    string Path,
    long SizeBytes,
    DateTime ModifiedAtUtc,
    GgufModelInfo? GgufInfo,
    ModelManifestEntry? Manifest);

public sealed record ModelInventorySnapshot(
    string Root,
    IReadOnlyList<ModelInventoryEntry> Entries,
    IReadOnlyList<ModelManifestEntry> ManifestEntries,
    bool FromCache,
    bool IsTruncated,
    int Generation,
    DateTime ScannedAtUtc)
{
    public ModelInventoryEntry? Find(string path) =>
        Entries.FirstOrDefault(entry => ModelPathSafety.AreSameLocalPath(entry.Path, path));
}

/// <summary>
/// Services-owned inventory of local chat GGUF files. The file tree is
/// re-enumerated on a cheap identity check so additions, removals, moves, size
/// changes, and timestamp changes are seen without a filesystem watcher. GGUF
/// metadata and manifest attachment are reused until that identity changes or
/// the caller explicitly invalidates the root.
/// </summary>
public sealed class ModelInventoryService
{
    public const int DefaultMaximumEntries = 2_048;
    private const int MaximumCachedRoots = 16;
    private static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromMinutes(2);

    private readonly ModelManifestStore _manifest;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, CachedSnapshot> _snapshots = new(ModelPathSafety.LocalPathComparer);
    private readonly Dictionary<string, CachedMetadata> _metadata = new(ModelPathSafety.LocalPathComparer);
    private readonly HashSet<string> _invalidatedRoots = new(ModelPathSafety.LocalPathComparer);
    private readonly TimeSpan _cacheLifetime;
    private bool _allRootsInvalidated;
    private int _generation;

    public int MaximumEntries { get; }

    public ModelInventoryService(
        ModelManifestStore manifest,
        int maximumEntries = DefaultMaximumEntries,
        TimeSpan? cacheLifetime = null)
    {
        if (maximumEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));

        _manifest = manifest;
        MaximumEntries = maximumEntries;
        _cacheLifetime = cacheLifetime ?? DefaultCacheLifetime;
    }

    /// <summary>Invalidates one assets root, or all roots when no root is supplied.
    /// Metadata entries remain reusable when the file identity still matches.</summary>
    public void Invalidate(string? assetsRoot = null)
    {
        lock (_stateLock)
        {
            if (string.IsNullOrWhiteSpace(assetsRoot))
            {
                _snapshots.Clear();
                _allRootsInvalidated = true;
                _invalidatedRoots.Clear();
                return;
            }

            var root = NormalizeRoot(assetsRoot);
            _snapshots.Remove(root);
            _invalidatedRoots.Add(root);
        }
    }

    public async Task<ModelInventorySnapshot> ScanAsync(
        string assetsRoot,
        CancellationToken ct = default)
    {
        var root = NormalizeRoot(assetsRoot);
        if (string.IsNullOrWhiteSpace(root))
            return EmptySnapshot(root, fromCache: false);

        await _scanGate.WaitAsync(ct);
        try
        {
            CachedSnapshot? previous;
            bool explicitlyInvalidated;
            lock (_stateLock)
            {
                explicitlyInvalidated = _allRootsInvalidated || _invalidatedRoots.Remove(root);
                _allRootsInvalidated = false;
                _snapshots.TryGetValue(root, out previous);
            }

            var scan = LocalAiAssetLocator.FindGgufInventoryFilesBounded(root, MaximumEntries);
            var identities = ReadIdentities(root, scan.Paths, ct);
            var now = DateTime.UtcNow;
            if (!explicitlyInvalidated
                && previous is not null
                && SameIdentities(previous.Identities, identities)
                && now - previous.Snapshot.ScannedAtUtc < _cacheLifetime)
            {
                var cached = previous.Snapshot with { FromCache = true, ScannedAtUtc = now };
                StoreSnapshot(root, identities, cached, now);
                return cached;
            }

            var manifestEntries = await _manifest.LoadAsync(ct);
            var manifestByPath = manifestEntries
                .GroupBy(entry => NormalizePath(entry.FilePath), ModelPathSafety.LocalPathComparer)
                .ToDictionary(group => group.Key, group => group.First(), ModelPathSafety.LocalPathComparer);
            var entries = new List<ModelInventoryEntry>(identities.Count);
            foreach (var identity in identities)
            {
                ct.ThrowIfCancellationRequested();
                var metadata = await ReadMetadataAsync(identity, ct);
                manifestByPath.TryGetValue(identity.Path, out var manifest);
                entries.Add(new ModelInventoryEntry(
                    identity.Path,
                    identity.SizeBytes,
                    identity.ModifiedAtUtc,
                    metadata,
                    manifest));
            }

            var generation = NextGeneration();
            var snapshot = new ModelInventorySnapshot(
                root,
                entries,
                manifestEntries,
                FromCache: false,
                scan.IsTruncated,
                generation,
                now);
            StoreSnapshot(root, identities, snapshot, now);
            PruneMetadata(root, identities.Select(identity => identity.Path).ToHashSet(ModelPathSafety.LocalPathComparer));
            return snapshot;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task<GgufModelInfo?> ReadMetadataAsync(FileIdentity identity, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        lock (_stateLock)
        {
            if (_metadata.TryGetValue(identity.Path, out var cached)
                && cached.SizeBytes == identity.SizeBytes
                && cached.ModifiedAtUtc == identity.ModifiedAtUtc)
            {
                _metadata[identity.Path] = cached with { LastAccessedUtc = now };
                return cached.Info;
            }
        }

        var info = await Task.Run(() => GgufMetadataReader.TryRead(identity.Path), ct);
        lock (_stateLock)
        {
            _metadata[identity.Path] = new CachedMetadata(identity.SizeBytes, identity.ModifiedAtUtc, info, now);
            TrimMetadataCache();
        }
        return info;
    }

    private void StoreSnapshot(
        string root,
        IReadOnlyList<FileIdentity> identities,
        ModelInventorySnapshot snapshot,
        DateTime now)
    {
        lock (_stateLock)
        {
            _snapshots[root] = new CachedSnapshot(identities, snapshot, now);
            while (_snapshots.Count > MaximumCachedRoots)
            {
                var oldest = _snapshots.MinBy(pair => pair.Value.LastAccessedUtc);
                if (oldest.Key is null)
                    break;
                _snapshots.Remove(oldest.Key);
            }
        }
    }

    private void PruneMetadata(string root, HashSet<string> activePaths)
    {
        lock (_stateLock)
        {
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            foreach (var path in _metadata.Keys
                         .Where(path => path.StartsWith(prefix, ModelPathSafety.LocalPathComparison)
                                        && !activePaths.Contains(path))
                         .ToList())
            {
                _metadata.Remove(path);
            }
        }
    }

    private void TrimMetadataCache()
    {
        var maximum = Math.Max(256, MaximumEntries * 2);
        while (_metadata.Count > maximum)
        {
            var oldest = _metadata.MinBy(pair => pair.Value.LastAccessedUtc);
            if (oldest.Key is null)
                break;
            _metadata.Remove(oldest.Key);
        }
    }

    private int NextGeneration()
    {
        lock (_stateLock)
        {
            return ++_generation;
        }
    }

    private static IReadOnlyList<FileIdentity> ReadIdentities(
        string root,
        IReadOnlyList<string> paths,
        CancellationToken ct)
    {
        var identities = new List<FileIdentity>(paths.Count);
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!ModelPathSafety.TryResolveFileUnderRoot(root, path, out var normalized, out _))
                    continue;

                var file = new FileInfo(normalized);
                if (!file.Exists)
                    continue;

                identities.Add(new FileIdentity(normalized, file.Length, file.LastWriteTimeUtc));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return identities
            .OrderBy(identity => identity.Path, ModelPathSafety.LocalPathComparer)
            .ToArray();
    }

    private static bool SameIdentities(
        IReadOnlyList<FileIdentity> left,
        IReadOnlyList<FileIdentity> right) => left.SequenceEqual(right);

    private static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return string.Empty;

        try { return Path.GetFullPath(root.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try { return Path.GetFullPath(path.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static ModelInventorySnapshot EmptySnapshot(string root, bool fromCache) =>
        new(root, [], [], fromCache, false, 0, DateTime.UtcNow);

    private sealed record FileIdentity(string Path, long SizeBytes, DateTime ModifiedAtUtc);

    private sealed record CachedMetadata(
        long SizeBytes,
        DateTime ModifiedAtUtc,
        GgufModelInfo? Info,
        DateTime LastAccessedUtc);

    private sealed record CachedSnapshot(
        IReadOnlyList<FileIdentity> Identities,
        ModelInventorySnapshot Snapshot,
        DateTime LastAccessedUtc);
}
