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

    /// <summary>
    /// Validates and saves <paramref name="settings"/> as the new committed
    /// state, swapping it into <see cref="Settings"/> only once the save
    /// (including any data-root migration) actually succeeds; on failure,
    /// <see cref="Settings"/> is left exactly as it was before the call
    /// (r12 01-settings-lifecycle.md 1.2). Callers that build edits into a
    /// <see cref="AppSettings.Clone"/> of the live settings, rather than
    /// mutating it directly, get this rollback for free.
    /// </summary>
    Task<SettingsSaveResult> SaveAsync(AppSettings settings, string? previousDataRootDirectory = null);
    DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory);
    event EventHandler? SettingsChanged;
}
