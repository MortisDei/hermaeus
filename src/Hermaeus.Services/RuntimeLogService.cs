using System.Collections.Concurrent;
using System.Globalization;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class RuntimeLogService : IRuntimeLogService
{
    private const int MaxEntries = 1000;
    private const long MaxLogFileBytes = 1 * 1024 * 1024;
    private const long MaxTotalLogBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan MaxArchiveAge = TimeSpan.FromDays(7);
    private readonly ConcurrentQueue<RuntimeLogEntry> _entries = new();
    private readonly ISettingsService _settings;
    private readonly RedactionService? _redactor;
    private readonly object _fileLock = new();
    private readonly object _dedupeLock = new();
    private RuntimeLogEntry? _lastPersistentFailure;
    private int _suppressedFailureCount;

    public event Action<RuntimeLogEntry>? LogAdded;

    public RuntimeLogService(ISettingsService settings, RedactionService? redactor = null)
    {
        _settings = settings;
        _redactor = redactor;
        if (settings is SettingsService concrete)
            concrete.NormalizationWarning += message => Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service, message));
    }

    public void Add(RuntimeLogEntry entry)
    {
        if (_redactor is not null)
            entry = entry with { Message = _redactor.Redact(entry.Message) };

        if (!RuntimeLogClassifier.ShouldPersist(entry))
            return;

        lock (_dedupeLock)
        {
            if (IsRepeatedPersistentFailure(entry))
            {
                _suppressedFailureCount++;
                return;
            }

            FlushSuppressedFailureSummary();
            _lastPersistentFailure = IsPersistentFailure(entry) ? entry : null;
            AppendEntry(entry);
        }
    }

    private void AppendEntry(RuntimeLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }

        TryAppendToDisk(entry);
        LogAdded?.Invoke(entry);
    }

    private bool IsRepeatedPersistentFailure(RuntimeLogEntry entry) =>
        IsPersistentFailure(entry)
        && _lastPersistentFailure is not null
        && _lastPersistentFailure.Level == entry.Level
        && _lastPersistentFailure.Category == entry.Category
        && string.Equals(_lastPersistentFailure.Message, entry.Message, StringComparison.Ordinal);

    private static bool IsPersistentFailure(RuntimeLogEntry entry) =>
        entry.Level is RuntimeLogLevel.Warning or RuntimeLogLevel.Error;

    private void FlushSuppressedFailureSummary()
    {
        if (_suppressedFailureCount == 0 || _lastPersistentFailure is null)
            return;

        var summary = new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Info,
            _lastPersistentFailure.Category,
            $"Suppressed {_suppressedFailureCount} repeated {_lastPersistentFailure.Level.ToString().ToLowerInvariant()} runtime log entr{(_suppressedFailureCount == 1 ? "y" : "ies")}; state changed.");
        _suppressedFailureCount = 0;
        AppendEntry(summary);
    }

    public IReadOnlyList<RuntimeLogEntry> GetEntries() => _entries.ToList();

    public void ClearInMemory()
    {
        while (_entries.TryDequeue(out _)) { }
        lock (_dedupeLock)
        {
            _lastPersistentFailure = null;
            _suppressedFailureCount = 0;
        }
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
                fs.Flush(true);
                PruneArchives(Path.GetDirectoryName(path) ?? GetLogDirectory(),
                    Path.GetFileNameWithoutExtension(path), Path.GetExtension(path),
                    fs.Length, DateTimeOffset.UtcNow);
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
            PruneArchives(dir, name, ext, 0, DateTimeOffset.UtcNow);
        }
        catch
        {
            // best-effort rotation; swallow errors
        }
    }

    private const int MaxArchivedLogFiles = 7;

    internal static void PruneArchives(string dir, string name, string ext,
        long activeBytes, DateTimeOffset now)
    {
        try
        {
            var archives = Directory.EnumerateFiles(dir, $"{name}.*{ext}")
                .Select(path => new ArchiveFile(path, TryArchiveTimestamp(path, name, ext)))
                .OrderBy(item => item.Timestamp ?? DateTimeOffset.MinValue)
                .ThenBy(item => Path.GetFileName(item.Path), StringComparer.Ordinal)
                .ToList();

            foreach (var archive in archives.Where(item => item.Timestamp is { } timestamp
                         && now - timestamp > MaxArchiveAge).ToArray())
            {
                File.Delete(archive.Path);
                archives.Remove(archive);
            }

            long totalBytes = activeBytes + archives.Sum(item => new FileInfo(item.Path).Length);
            while (archives.Count > 0
                && (archives.Count > MaxArchivedLogFiles || totalBytes > MaxTotalLogBytes))
            {
                var oldest = archives[0];
                var length = new FileInfo(oldest.Path).Length;
                File.Delete(oldest.Path);
                archives.RemoveAt(0);
                totalBytes -= length;
            }
        }
        catch
        {
            // best-effort retention; swallow errors
        }
    }

    private static DateTimeOffset? TryArchiveTimestamp(string path, string name, string ext)
    {
        var file = Path.GetFileName(path);
        var prefix = name + ".";
        if (!file.StartsWith(prefix, StringComparison.Ordinal)
            || !file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            return null;

        var token = file[prefix.Length..^ext.Length].Split('.')[0];
        return DateTimeOffset.TryParseExact(token, "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            ? timestamp : null;
    }

    private sealed record ArchiveFile(string Path, DateTimeOffset? Timestamp);
}
