using Aether.Core.Models;

namespace Aether.Core.Services;

public sealed record SettingsSaveResult(
    bool DataMigrated,
    string? PreviousDataRoot,
    string? CurrentDataRoot,
    string? BackupDirectory,
    int FilesMoved);

public sealed record DataMigrationPlan(
    bool WillMove,
    string PreviousDataRoot,
    string CurrentDataRoot,
    int FilesToMove,
    IReadOnlyList<string> Conflicts);

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadAsync();
    Task<SettingsSaveResult> SaveAsync(string? previousDataRootDirectory = null);
    DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory);
    event EventHandler? SettingsChanged;
}
