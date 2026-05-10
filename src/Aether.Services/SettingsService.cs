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

    public async Task SaveAsync(string? previousDataRootDirectory = null)
    {
        MigrateDataRoot(previousDataRootDirectory, Settings.DataRootDirectory);
        Directory.CreateDirectory(ResolveDataRoot(Settings));
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(Settings, Opts));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void MigrateDataRoot(string? previousDataRootDirectory, string? nextDataRootDirectory)
    {
        var previous = ResolveDataRoot(new AppSettings { DataRootDirectory = previousDataRootDirectory ?? string.Empty });
        var next = ResolveDataRoot(new AppSettings { DataRootDirectory = nextDataRootDirectory ?? string.Empty });
        if (string.Equals(previous, next, StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(next);
        if (!Directory.Exists(previous))
            return;

        foreach (var name in Directory.EnumerateFiles(previous, "conversations.db*"))
        {
            var target = Path.Combine(next, Path.GetFileName(name));
            if (File.Exists(target))
                throw new IOException($"Cannot move Aether data because '{target}' already exists.");

            File.Move(name, target);
        }

        if (!string.Equals(previous, DefaultDir, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(previous)
            && !Directory.EnumerateFileSystemEntries(previous).Any())
        {
            Directory.Delete(previous);
        }
    }
}
