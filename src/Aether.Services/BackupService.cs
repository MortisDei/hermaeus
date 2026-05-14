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
            .Where(f => !Path.GetFileName(f).Equals("secrets.local.json", StringComparison.OrdinalIgnoreCase))
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

    public Task RestoreAsync(string backupPath, CancellationToken ct = default)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file was not found.", backupPath);

        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        Directory.CreateDirectory(root);
        using var zip = ZipFile.OpenRead(backupPath);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Backup contains an unsafe path.");
            if (File.Exists(target))
                throw new IOException($"Restore refused because '{target}' already exists.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }

        return Task.CompletedTask;
    }
}
