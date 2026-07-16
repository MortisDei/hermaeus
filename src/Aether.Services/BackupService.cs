using System.IO.Compression;
using Aether.Core.Services;
using Microsoft.Data.Sqlite;

namespace Aether.Services;

public sealed class BackupService
{
    private readonly ISettingsService _settings;

    public BackupService(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task<BackupResult> BackupAsync(string targetDirectory, CancellationToken ct = default)
    {
        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        Directory.CreateDirectory(targetDirectory);
        var path = Path.Combine(targetDirectory, $"aether-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
        // Same manifest data-root migration moves (r11 3.1): everything under
        // the root except the fallback secrets vault, which is excluded from
        // backups by design (security-posture skill).
        var files = DataRootManifest.EnumerateAll(root)
            .Where(f =>
            {
                var name = Path.GetFileName(f.SourcePath);
                return !name.Equals("secrets.local.json", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("secrets.local.key", StringComparison.OrdinalIgnoreCase);
            })
            // WAL/rollback-journal sidecars describe a database file that is
            // about to be replaced by a consistent snapshot (below); zipping
            // them independently would describe a different, pre-snapshot
            // state, so they are dropped rather than backed up raw.
            .Where(f => !IsSqliteSidecarFile(f.SourcePath))
            .ToList();

        var snapshotDir = Path.Combine(Path.GetTempPath(), $"aether-backup-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(snapshotDir);
        try
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

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

    private static bool IsSqliteDatabaseFile(string path) =>
        path.EndsWith(".db", StringComparison.OrdinalIgnoreCase);

    private static bool IsSqliteSidecarFile(string path) =>
        path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("-journal", StringComparison.OrdinalIgnoreCase);

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

    public Task RestoreAsync(string backupPath, bool allowOverwrite, CancellationToken ct = default)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file was not found.", backupPath);

        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        Directory.CreateDirectory(root);
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        using var zip = ZipFile.OpenRead(backupPath);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(rootWithSeparator, comparison)
                && !string.Equals(target, root, comparison))
                throw new InvalidOperationException("Backup contains an unsafe path.");
            if (File.Exists(target) && !allowOverwrite)
                throw new IOException($"Restore refused because '{target}' already exists.");

            var targetDirectory = Path.GetDirectoryName(target);
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new InvalidOperationException("Backup entry target directory could not be resolved.");

            Directory.CreateDirectory(targetDirectory);
            entry.ExtractToFile(target, allowOverwrite);
        }

        return Task.CompletedTask;
    }
}
