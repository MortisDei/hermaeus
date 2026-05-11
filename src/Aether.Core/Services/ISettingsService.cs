using Aether.Core.Models;

namespace Aether.Core.Services;

public sealed record SettingsSaveResult(
    bool DataMigrated,
    string? PreviousDataRoot,
    string? CurrentDataRoot,
    string? BackupDirectory,
    int FilesMoved);

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadAsync();
    Task<SettingsSaveResult> SaveAsync(string? previousDataRootDirectory = null);
    event EventHandler? SettingsChanged;
}
