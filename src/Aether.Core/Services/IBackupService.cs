namespace Aether.Core.Services;

public sealed record BackupResult(string Path, int FilesIncluded);

public interface IBackupService
{
    Task<BackupResult> BackupAsync(string targetDirectory, CancellationToken ct = default);
    Task RestoreAsync(string backupPath, CancellationToken ct = default);
    Task RestoreAsync(string backupPath, bool allowOverwrite, CancellationToken ct = default);
}
