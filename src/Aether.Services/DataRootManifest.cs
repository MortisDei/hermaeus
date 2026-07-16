namespace Aether.Services;

/// <summary>
/// The single source of truth for "everything under the data root" (r11
/// 3.1). Data-root migration, its preview, and BackupService all enumerate
/// through this instead of each keeping its own allow-list, so they can
/// never disagree again about what a move or a backup covers - a new
/// persistent file family (a new store, a new subfolder) is swept up
/// automatically because this walks the whole tree, no manifest edit
/// required.
/// </summary>
public static class DataRootManifest
{
    /// <summary>
    /// settings.json bootstraps the data root and always lives in
    /// LocalApplicationData by design (explicitly rejected as a migration
    /// target: docs/review/r11/05-roadmap.md). It is excluded here rather
    /// than only in the migration/backup call sites because, when the data
    /// root has never been customized, settings.json's directory IS the
    /// default data root - "everything under the root" would otherwise
    /// sweep it up the first time a user points DataRootDirectory somewhere
    /// new, moving the very file that recorded that choice.
    /// </summary>
    private const string SettingsFileName = "settings.json";

    public static IEnumerable<(string SourcePath, string RelativePath)> EnumerateAll(string root)
    {
        if (!Directory.Exists(root))
            yield break;

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(path), SettingsFileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(path), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                continue;

            yield return (path, Path.GetRelativePath(root, path));
        }
    }
}
