using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public sealed record SettingsSaveResult(
    bool DataMigrated,
    string? PreviousDataRoot,
    string? CurrentDataRoot,
    string? BackupDirectory,
    int FilesMoved,
    DataMigrationEvidence? MigrationEvidence = null);

public sealed record DataMigrationExclusion(string RelativePath, string Reason);

public sealed record DataMigrationEvidence(
    int InitiallyDiscovered,
    int Excluded,
    int DiscoveredAtMigration,
    int CopiedOrMoved,
    int Verified,
    int RemovedFromSource,
    int Retained,
    int Failures,
    int Skipped,
    IReadOnlyList<DataMigrationExclusion> Exclusions,
    IReadOnlyList<string> RetainedPaths,
    IReadOnlyList<string> FailureDetails)
{
    public string ToReceipt(string destination, string? backupDirectory)
    {
        var exclusions = Exclusions.Count == 0
            ? "none"
            : string.Join(", ", Exclusions.Select(e => $"{e.RelativePath} ({e.Reason})"));
        var retained = RetainedPaths.Count == 0
            ? "none"
            : string.Join(", ", RetainedPaths.Take(8)) + (RetainedPaths.Count > 8 ? ", ..." : string.Empty);
        var failures = FailureDetails.Count == 0
            ? "none"
            : string.Join(" | ", FailureDetails.Take(4));
        var backup = string.IsNullOrWhiteSpace(backupDirectory) ? "none" : backupDirectory;
        return $"Data migration completed at startup: initially discovered {InitiallyDiscovered}; excluded {Excluded} ({exclusions}); discovered at restart {DiscoveredAtMigration}; copied/moved {CopiedOrMoved}; verified {Verified}; removed from source {RemovedFromSource}; retained {Retained} ({retained}); failures {Failures} ({failures}); skipped {Skipped}; destination {destination}; backup {backup}.";
    }
}

public sealed record DataMigrationPlan(
    bool WillMove,
    string PreviousDataRoot,
    string CurrentDataRoot,
    int FilesToMove,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string>? InitiallyDiscoveredFiles = null,
    IReadOnlyList<DataMigrationExclusion>? Exclusions = null);

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
