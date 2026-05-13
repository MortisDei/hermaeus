using System.Collections.Concurrent;
using System.Globalization;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class RuntimeLogService : IRuntimeLogService
{
    private const int MaxEntries = 2000;
    private readonly ConcurrentQueue<RuntimeLogEntry> _entries = new();
    private readonly ISettingsService _settings;

    public event Action<RuntimeLogEntry>? LogAdded;

    public RuntimeLogService(ISettingsService settings)
    {
        _settings = settings;
    }

    public void Add(RuntimeLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }

        TryAppendToDisk(entry);
        LogAdded?.Invoke(entry);
    }

    public IReadOnlyList<RuntimeLogEntry> GetEntries() => _entries.ToList();

    public void ClearInMemory()
    {
        while (_entries.TryDequeue(out _)) { }
    }

    public string GetLogDirectory()
    {
        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        var dir = Path.Combine(root, "logs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string GetLogFilePath() => Path.Combine(GetLogDirectory(), "runtime.log");

    private void TryAppendToDisk(RuntimeLogEntry entry)
    {
        try
        {
            var path = GetLogFilePath();
            var line = $"{entry.Timestamp:O} [{entry.Level}] [{entry.Category}] {entry.Message}";
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
        }
    }
}
