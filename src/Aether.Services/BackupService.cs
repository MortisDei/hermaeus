using System.IO.Compression;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class BackupService : IBackupService
{
    private readonly ISettingsService _settings;

    public BackupService(ISettingsService settings)
    {
        _settings = settings;
    }

    public Task<BackupResult> BackupAsync(string targetDirectory, CancellationToken ct = default)
    {
        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        Directory.CreateDirectory(targetDirectory);
        var path = Path.Combine(targetDirectory, $"aether-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return !name.Equals("secrets.local.json", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("secrets.local.key", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var entryName = Path.GetRelativePath(root, file);
            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
        }

        return Task.FromResult(new BackupResult(path, files.Count));
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
        using var zip = ZipFile.OpenRead(backupPath);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
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
