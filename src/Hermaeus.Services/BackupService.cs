using System.IO.Compression;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

public sealed record BackupRestoreLimits(
    int MaxEntries = 10_000,
    long MaxEntryBytes = 128L * 1024 * 1024,
    long MaxTotalUncompressedBytes = 512L * 1024 * 1024);

public sealed class BackupService
{
    private static readonly uint[] ZipCrc32Table = CreateZipCrc32Table();
    private readonly ISettingsService _settings;
    private readonly Action<string, string> _restoreMove;

    public BackupService(ISettingsService settings) : this(settings, File.Move) { }

    internal BackupService(ISettingsService settings, Action<string, string> restoreMove)
    {
        _settings = settings;
        _restoreMove = restoreMove;
    }

    public async Task<BackupResult> BackupAsync(string targetDirectory, CancellationToken ct = default)
    {
        var root = Path.GetFullPath(SettingsService.ResolveDataRoot(_settings.Settings));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        EnsureNoReparsePoints(root);
        targetDirectory = Path.GetFullPath(targetDirectory);
        EnsureNoReparsePoints(targetDirectory);
        Directory.CreateDirectory(targetDirectory);
        EnsureNoReparsePoints(targetDirectory);
        var path = Path.Combine(targetDirectory, $"hermaeus-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
        // Same manifest data-root migration moves (r11 3.1): everything under
        // the root except the fallback secrets vault, which is excluded from
        // backups by design (security-posture skill).
        var files = DataRootManifest.EnumerateAll(root)
            .Select(file =>
            {
                EnsureNoReparsePoints(file.SourcePath);
                return file;
            })
            .Where(f => !IsFallbackSecretArtifact(f.SourcePath) && !IsRebuildableArtwork(f.RelativePath))
            // WAL/rollback-journal sidecars describe a database file that is
            // about to be replaced by a consistent snapshot (below); zipping
            // them independently would describe a different, pre-snapshot
            // state, so they are dropped rather than backed up raw.
            .Where(f => !IsSqliteSidecarFile(f.SourcePath))
            .ToList();

        var snapshotDir = CreateSnapshotDirectory();
        try
        {
            EnsureNoReparsePoints(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                // Repeat at the read site, including the SQLite snapshot path.
                // Metadata checks do not claim atomic filesystem ownership.
                EnsureNoReparsePoints(file.SourcePath);

                if (!IsSqliteDatabaseFile(file.SourcePath))
                {
                    zip.CreateEntryFromFile(file.SourcePath, file.RelativePath, CompressionLevel.Fastest);
                    continue;
                }

                // A live SQLite database can be mid-write (open transaction, WAL
                // not checkpointed); zipping the raw file risks an internally
                // inconsistent copy. SQLite's own online-backup API produces a
                // consistent snapshot regardless (r11 3.6), so the archive gets
                // that snapshot instead of the raw file.
                var snapshotPath = Path.Combine(snapshotDir, $"{Guid.NewGuid():N}.db");
                await SnapshotSqliteDatabaseAsync(file.SourcePath, snapshotPath, ct);
                zip.CreateEntryFromFile(snapshotPath, file.RelativePath, CompressionLevel.Fastest);
            }
        }
        finally
        {
            try { Directory.Delete(snapshotDir, recursive: true); }
            catch { }
        }

        return new BackupResult(path, files.Count);
    }

    internal static string CreateSnapshotDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hermaeus-backup-snapshot-{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static bool IsSqliteDatabaseFile(string path) =>
        path.EndsWith(".db", StringComparison.OrdinalIgnoreCase);

    private static bool IsRebuildableArtwork(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("cache/huggingface-artwork/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSqliteSidecarFile(string path) =>
        path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("-journal", StringComparison.OrdinalIgnoreCase);

    private static bool IsFallbackSecretArtifact(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("secrets.local.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("secrets.local.key", StringComparison.OrdinalIgnoreCase)
            || name.Equals("secrets.local.lock", StringComparison.OrdinalIgnoreCase)
            || MatchesSecretArtifactName(name, "secrets.local.json.", ".tmp", 32)
            || MatchesSecretArtifactName(name, "secrets.local.key.", ".tmp", 32)
            || MatchesSecretArtifactName(name, ".vault-", ".tmp", 32)
            || MatchesSecretArtifactName(name, "", ".pre-v3.snapshot", 64)
            || MatchesSecretArtifactName(name, "", ".pre-v3.locator", 64);
    }

    private static bool MatchesSecretArtifactName(string name, string prefix, string suffix, int tokenLength)
    {
        if (name.Length != prefix.Length + tokenLength + suffix.Length
            || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var character in name.AsSpan(prefix.Length, tokenLength))
            if (!char.IsAsciiHexDigit(character)) return false;
        return true;
    }

    private static async Task SnapshotSqliteDatabaseAsync(string sourcePath, string destinationPath, CancellationToken ct)
    {
        // Pooling=False: the destination is a use-once temp file zipped and
        // deleted moments later; pooled connections keep their file handle
        // open across Dispose for reuse, which would race the zip/delete step.
        await using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Pooling=False");
        await source.OpenAsync(ct);
        await using var destination = new SqliteConnection($"Data Source={destinationPath};Pooling=False");
        await destination.OpenAsync(ct);
        source.BackupDatabase(destination);
    }

    public Task RestoreAsync(string backupPath, CancellationToken ct = default) =>
        RestoreAsync(backupPath, allowOverwrite: false, ct);

    public Task RestoreAsync(string backupPath, bool allowOverwrite, CancellationToken ct = default) =>
        RestoreAsync(backupPath, allowOverwrite, limits: null, ct);

    public async Task RestoreAsync(
        string backupPath,
        bool allowOverwrite,
        BackupRestoreLimits? limits,
        CancellationToken ct = default)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file was not found.", backupPath);

        limits ??= new BackupRestoreLimits();
        if (limits.MaxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits), "MaxEntries must be greater than zero.");
        if (limits.MaxEntryBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits), "MaxEntryBytes must be greater than zero.");
        if (limits.MaxTotalUncompressedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits), "MaxTotalUncompressedBytes must be greater than zero.");

        var root = Path.GetFullPath(SettingsService.ResolveDataRoot(_settings.Settings));
        Directory.CreateDirectory(root);
        EnsureNoReparsePoints(root);

        using var zip = ZipFile.OpenRead(backupPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var targets = new HashSet<string>(comparison);
        var files = new List<(ZipArchiveEntry Entry, string Target, string RelativePath)>();
        long totalUncompressedBytes = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            if (files.Count >= limits.MaxEntries)
                throw new InvalidDataException($"Backup contains more than {limits.MaxEntries} file entries.");
            if (entry.Length < 0 || entry.Length > limits.MaxEntryBytes)
                throw new InvalidDataException($"Backup entry '{entry.FullName}' exceeds the per-file restore limit.");
            if (totalUncompressedBytes > limits.MaxTotalUncompressedBytes - entry.Length)
                throw new InvalidDataException("Backup exceeds the total uncompressed restore limit.");
            totalUncompressedBytes += entry.Length;

            var target = ResolveRestoreTarget(root, entry.FullName);
            // Classify the normalised target, so separators and dot segments
            // cannot disguise an excluded credential artifact. Credential
            // recovery has its own explicit review protocol, not ZIP restore.
            if (IsFallbackSecretArtifact(target))
                throw new InvalidDataException("Backup restore refuses fallback credential artifacts.");
            if (!targets.Add(target))
                throw new InvalidDataException($"Backup contains duplicate file entries for '{entry.FullName}'.");
            if ((File.Exists(target) || Directory.Exists(target)) && !allowOverwrite)
                throw new IOException($"Restore refused because '{target}' already exists.");

            var targetDirectory = Path.GetDirectoryName(target);
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new InvalidOperationException("Backup entry target directory could not be resolved.");

            files.Add((entry, target, Path.GetRelativePath(root, target)));
        }

        var stagingRoot = Path.Combine(root, $".hermaeus-restore-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(root, $".hermaeus-restore-backup-{Guid.NewGuid():N}");
        var committed = new List<(string Target, string? Backup)>();
        var preserveRecovery = false;
        long actualTotalUncompressedBytes = 0;
        Directory.CreateDirectory(stagingRoot);
        EnsureNoReparsePoints(stagingRoot);
        try
        {
            // Extract everything beneath a root-owned staging directory first.
            // A cancellation, malformed stream, or late budget violation then
            // leaves the configured data root untouched.
            foreach (var item in files)
            {
                ct.ThrowIfCancellationRequested();
                var staged = ResolveRestoreTarget(stagingRoot, item.RelativePath);
                var stagingDirectory = Path.GetDirectoryName(staged);
                if (string.IsNullOrWhiteSpace(stagingDirectory))
                    throw new InvalidOperationException("Backup staging directory could not be resolved.");

                Directory.CreateDirectory(stagingDirectory);
                EnsureNoReparsePoints(staged);
                var temporary = staged + "." + Guid.NewGuid().ToString("N") + ".restore.tmp";
                try
                {
                    await using (var input = item.Entry.Open())
                    await using (var output = new FileStream(
                        temporary,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 64 * 1024,
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        var buffer = new byte[64 * 1024];
                        long written = 0;
                        var crc = uint.MaxValue;
                        while (true)
                        {
                            var read = await input.ReadAsync(buffer.AsMemory(), ct);
                            if (read == 0)
                                break;

                            written += read;
                            actualTotalUncompressedBytes += read;
                            if (written > limits.MaxEntryBytes)
                                throw new InvalidDataException($"Backup entry '{item.Entry.FullName}' exceeded the per-file restore limit while extracting.");
                            if (actualTotalUncompressedBytes > limits.MaxTotalUncompressedBytes)
                                throw new InvalidDataException("Backup exceeded the total uncompressed restore limit while extracting.");
                            crc = AppendZipCrc32(crc, buffer.AsSpan(0, read));
                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        }

                        if (written != item.Entry.Length)
                            throw new InvalidDataException($"Backup entry '{item.Entry.FullName}' did not extract to its declared size.");
                        // The .NET ZIP reader can stop at a forged declared
                        // length. Equal byte counts alone do not establish
                        // intact extraction, so verify the ZIP checksum too.
                        // CRC detects corruption; it does not authenticate an archive.
                        if ((crc ^ uint.MaxValue) != item.Entry.Crc32)
                            throw new InvalidDataException($"Backup entry '{item.Entry.FullName}' failed its ZIP checksum.");
                        ct.ThrowIfCancellationRequested();
                        output.Flush(flushToDisk: true);
                    }

                    File.Move(temporary, staged);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }

            // Once commit begins, cancellation is no longer observed between
            // individual moves. This avoids reporting a cancelled restore
            // after only half the archive reached the data root; cancellation
            // during extraction remains fully transactional above.
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var item in files)
                {
                    var target = item.Target;
                    EnsureNoReparsePoints(target);
                    if (Directory.Exists(target))
                        throw new IOException($"Restore target '{target}' is a directory.");
                    if (File.Exists(target) && !allowOverwrite)
                        throw new IOException($"Restore refused because '{target}' already exists.");

                    string? backup = null;
                    if (File.Exists(target))
                    {
                        backup = ResolveRestoreTarget(backupRoot, item.RelativePath);
                        var backupDirectory = Path.GetDirectoryName(backup);
                        if (string.IsNullOrWhiteSpace(backupDirectory))
                            throw new InvalidOperationException("Backup rollback directory could not be resolved.");
                        Directory.CreateDirectory(backupDirectory);
                        EnsureNoReparsePoints(backup);
                        _restoreMove(target, backup);
                    }

                    committed.Add((target, backup));
                    var staged = ResolveRestoreTarget(stagingRoot, item.RelativePath);
                    EnsureNoReparsePoints(staged);
                    var targetDirectory = Path.GetDirectoryName(target);
                    if (string.IsNullOrWhiteSpace(targetDirectory))
                        throw new InvalidOperationException("Restore target directory could not be resolved.");
                    Directory.CreateDirectory(targetDirectory);
                    EnsureNoReparsePoints(target);
                    _restoreMove(staged, target);
                }
            }
            catch (Exception commitFailure)
            {
                var rollbackFailures = new List<Exception>();
                for (var index = committed.Count - 1; index >= 0; index--)
                {
                    var item = committed[index];
                    try
                    {
                        EnsureNoReparsePoints(item.Target);
                        if (item.Backup is not null) EnsureNoReparsePoints(item.Backup);
                        if (File.Exists(item.Target))
                            File.Delete(item.Target);
                        if (item.Backup is not null && File.Exists(item.Backup))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
                            EnsureNoReparsePoints(item.Target);
                            _restoreMove(item.Backup, item.Target);
                        }
                    }
                    catch (Exception rollbackFailure)
                    {
                        rollbackFailures.Add(rollbackFailure);
                    }
                }
                if (rollbackFailures.Count > 0)
                {
                    // Do not destroy the only remaining originals after a
                    // failed rollback. Report both causes and their location.
                    preserveRecovery = true;
                    throw new AggregateException($"Restore failed and rollback was incomplete. Retained recovery files: {backupRoot}",
                        new[] { commitFailure }.Concat(rollbackFailures));
                }
                throw;
            }
        }
        finally
        {
            try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true); }
            catch { }
            try { if (!preserveRecovery && Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true); }
            catch { }
        }
    }

    private static uint AppendZipCrc32(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
            crc = ZipCrc32Table[(byte)(crc ^ value)] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CreateZipCrc32Table()
    {
        // Reflected CRC-32/ISO-HDLC polynomial required by the ZIP format.
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }

    private static string ResolveRestoreTarget(string root, string entryName)
    {
        var normalized = entryName.Replace('\\', '/');
        if (normalized.Length == 0
            || normalized.IndexOf('\0') >= 0
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(normalized)
            || IsDriveQualified(normalized))
            throw new InvalidOperationException("Backup contains an unsafe path.");

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToArray();
        if (segments.Length == 0 || segments.Any(segment => segment == ".."))
            throw new InvalidOperationException("Backup contains an unsafe path.");

        if (OperatingSystem.IsWindows() && segments.Any(segment => segment.Contains(':')))
            throw new InvalidOperationException("Backup contains an unsafe path.");

        var safeRelativePath = string.Join(Path.DirectorySeparatorChar, segments);
        var target = Path.GetFullPath(Path.Combine(root, safeRelativePath));
        var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!target.StartsWith(rootPrefix, comparison))
            throw new InvalidOperationException("Backup contains an unsafe path.");

        EnsureNoReparsePoints(target);
        return target;
    }

    private static bool IsDriveQualified(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private static void EnsureNoReparsePoints(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (TryGetAttributes(current.FullName, out var attributes)
                && (attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Backup operations refuse reparse-point paths.");

            current = current.Parent;
        }
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }
}
