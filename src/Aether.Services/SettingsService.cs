using System.Text.Json;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private static readonly string DefaultDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether");
    private readonly string _path;

    public AppSettings Settings { get; private set; } = new();
    public event EventHandler? SettingsChanged;

    public SettingsService()
    {
        Directory.CreateDirectory(DefaultDir);
        _path = Path.Combine(DefaultDir, "settings.json");
    }

    public SettingsService(string settingsPath)
    {
        _path = Path.GetFullPath(settingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public static string ResolveDataRoot(AppSettings settings)
    {
        var configured = settings.DataRootDirectory?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? DefaultDir : Path.GetFullPath(configured);
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_path);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, Opts) ?? new();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { Settings = new(); }
    }

    public async Task<SettingsSaveResult> SaveAsync(string? previousDataRootDirectory = null)
    {
        var migration = MigrateDataRoot(previousDataRootDirectory, Settings.DataRootDirectory);
        Directory.CreateDirectory(ResolveDataRoot(Settings));
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(Settings, Opts));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return migration;
    }

    public DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory)
    {
        var previous = ResolveDataRoot(new AppSettings { DataRootDirectory = previousDataRootDirectory ?? string.Empty });
        var next = ResolveDataRoot(new AppSettings { DataRootDirectory = nextDataRootDirectory ?? string.Empty });
        if (string.Equals(previous, next, StringComparison.OrdinalIgnoreCase))
            return new DataMigrationPlan(false, previous, next, 0, []);

        if (!Directory.Exists(previous))
            return new DataMigrationPlan(false, previous, next, 0, []);

        var files = Directory.EnumerateFiles(previous, "conversations.db*").ToList();
        var conflicts = files
            .Select(f => Path.Combine(next, Path.GetFileName(f)))
            .Where(File.Exists)
            .ToList();

        return new DataMigrationPlan(files.Count > 0 && conflicts.Count == 0, previous, next, files.Count, conflicts);
    }

    private static SettingsSaveResult MigrateDataRoot(string? previousDataRootDirectory, string? nextDataRootDirectory)
    {
        var previous = ResolveDataRoot(new AppSettings { DataRootDirectory = previousDataRootDirectory ?? string.Empty });
        var next = ResolveDataRoot(new AppSettings { DataRootDirectory = nextDataRootDirectory ?? string.Empty });
        ValidateDataRoot(next);
        if (string.Equals(previous, next, StringComparison.OrdinalIgnoreCase))
            return new SettingsSaveResult(false, previous, next, null, 0);

        Directory.CreateDirectory(next);
        if (!Directory.Exists(previous))
            return new SettingsSaveResult(false, previous, next, null, 0);

        var files = Directory.EnumerateFiles(previous, "conversations.db*").ToList();
        if (files.Count == 0)
            return new SettingsSaveResult(false, previous, next, null, 0);

        foreach (var name in files)
        {
            var target = Path.Combine(next, Path.GetFileName(name));
            if (File.Exists(target))
                throw new IOException($"Cannot move Aether data because '{target}' already exists.");
        }

        var backupDir = Path.Combine(next, ".aether-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupDir);
        foreach (var name in files)
            File.Copy(name, Path.Combine(backupDir, Path.GetFileName(name)));

        foreach (var name in files)
        {
            var target = Path.Combine(next, Path.GetFileName(name));
            File.Move(name, target);
        }

        if (!string.Equals(previous, DefaultDir, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(previous)
            && !Directory.EnumerateFileSystemEntries(previous).Any())
        {
            Directory.Delete(previous);
        }

        return new SettingsSaveResult(true, previous, next, backupDir, files.Count);
    }

    private static void ValidateDataRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            throw new IOException("Data root must be an absolute path.");

        var full = Path.GetFullPath(path);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Aether data root cannot be the filesystem root.");
    }
}
