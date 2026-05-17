using System.Collections.Concurrent;
using System.Globalization;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class RuntimeLogService : IRuntimeLogService
{
    private const int MaxEntries = 1000;
    private const long MaxLogFileBytes = 10 * 1024 * 1024; // 10 MB
    private readonly ConcurrentQueue<RuntimeLogEntry> _entries = new();
    private readonly ISettingsService _settings;
    private readonly IRedactionService? _redactor;
    private readonly object _fileLock = new();

    public event Action<RuntimeLogEntry>? LogAdded;

    public RuntimeLogService(ISettingsService settings, IRedactionService? redactor = null)
    {
        _settings = settings;
        _redactor = redactor;
    }

    public void Add(RuntimeLogEntry entry)
    {
        if (_redactor is not null)
            entry = entry with { Message = _redactor.Redact(entry.Message) };

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
            var line = $"{entry.Timestamp:O} [{entry.Level}] [{entry.Category}] {entry.Message}" + Environment.NewLine;

            lock (_fileLock)
            {
                try
                {
                    RotateIfNeeded(path);
                }
                catch
                {
                    // ignore rotation failures and still attempt to append
                }

                // Use FileStream for atomic append
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                var bytes = System.Text.Encoding.UTF8.GetBytes(line);
                fs.Write(bytes, 0, bytes.Length);
            }
        }
        catch
        {
        }
    }

    private void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var fi = new FileInfo(path);
            if (fi.Length < MaxLogFileBytes) return;

            var dir = Path.GetDirectoryName(path) ?? GetLogDirectory();
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            var dest = Path.Combine(dir, $"{name}.{stamp}{ext}");
            for (var i = 1; File.Exists(dest) && i <= 1000; i++)
                dest = Path.Combine(dir, $"{name}.{stamp}.{i}{ext}");
            if (File.Exists(dest))
                dest = Path.Combine(dir, $"{name}.{stamp}.{Guid.NewGuid():N}{ext}");
            File.Move(path, dest);
        }
        catch
        {
            // best-effort rotation; swallow errors
        }
    }
}
