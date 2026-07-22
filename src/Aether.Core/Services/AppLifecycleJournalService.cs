using System.Text.Json;

namespace Aether.Core.Services;

/// <summary>
/// Fields tracked across app runs so Doctor can report "Aether did not shut
/// down cleanly last time" and name the last thing it was doing when it
/// stopped (docs/review/03-next-level-roadmap.md Phase 4). Local-only: no
/// telemetry, no upload, nothing beyond this one small file in the data root.
/// </summary>
public sealed record AppLifecycleRecord(DateTime StartedAtUtc, bool CleanExit, string LastOperation, DateTime LastOperationAtUtc);

/// <summary>
/// Generalizes the ad-hoc preflight logging added for the 0.9.38-0.9.40
/// native Kokoro ONNX crash (a native fault there bypasses all managed
/// exception handling and kills the process with no other trace) into a
/// small, reusable app-lifecycle journal: one atomic-write JSON file
/// recording when the app started, whether it exited cleanly, and the last
/// notable operation it was performing. Doctor reads it on the next startup
/// scan to surface an unclean-shutdown warning naming that last operation.
/// Lives in Aether.Core (rather than Aether.Services, alongside the concrete
/// <c>SettingsService</c>) so the two other ONNX Runtime consumers,
/// Aether.Rag and Aether.Voice, can both use it without a reference against
/// the Desktop/ViewModels to (Services, Agent, Rag) to Core dependency
/// direction; it resolves the data root itself rather than depending on
/// <c>SettingsService.ResolveDataRoot</c>, matching the same
/// LocalApplicationData fallback already duplicated for the same reason in
/// <c>NativeKokoroVoiceProvider.ResolveAssetsDirectory</c>.
/// </summary>
public sealed class AppLifecycleJournalService
{
    private const string FileName = "lifecycle.json";
    private readonly ISettingsService _settings;
    private readonly object _sync = new();
    private bool _startupRecorded;

    public AppLifecycleJournalService(ISettingsService settings)
    {
        _settings = settings;
    }

    private string JournalPath
    {
        get
        {
            var configured = _settings.Settings.DataManagement.DataRootDirectory?.Trim();
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
                : Path.GetFullPath(configured);
            return Path.Combine(root, FileName);
        }
    }

    /// <summary>
    /// The previous session's record, captured once by <see cref="RecordStartup"/>.
    /// Doctor reads this (potentially across several re-scans in one running
    /// session) rather than calling <see cref="RecordStartup"/> itself, which
    /// must run exactly once per process or it would overwrite the very
    /// record it is meant to preserve.
    /// </summary>
    public AppLifecycleRecord? PreviousSession { get; private set; }

    /// <summary>
    /// Reads the previous session's record (if any) into
    /// <see cref="PreviousSession"/> and starts a fresh one for the current
    /// session. Call exactly once, early at app startup. Defense in depth
    /// beyond the caller's own one-shot guard (r19 1.4: Avalonia's
    /// Window.Opened re-fires on every tray restore): a second call on the
    /// same instance is a no-op that returns the already-captured
    /// <see cref="PreviousSession"/> instead of overwriting it with the
    /// current, still-running session.
    /// </summary>
    public AppLifecycleRecord? RecordStartup()
    {
        lock (_sync)
        {
            if (_startupRecorded)
                return PreviousSession;

            var path = JournalPath;
            var previous = TryRead(path);
            PreviousSession = previous;
            TryWrite(path, new AppLifecycleRecord(DateTime.UtcNow, CleanExit: false, "startup", DateTime.UtcNow));
            _startupRecorded = true;
            return previous;
        }
    }

    /// <summary>
    /// Best-effort breadcrumb for a notable, potentially risky operation
    /// (e.g. "loading Cross-Encoder reranker ONNX session"), so a crash
    /// mid-operation still leaves a record of what the app was doing.
    /// </summary>
    public void RecordOperation(string operation)
    {
        lock (_sync)
        {
            var path = JournalPath;
            var current = TryRead(path) ?? new AppLifecycleRecord(DateTime.UtcNow, false, operation, DateTime.UtcNow);
            TryWrite(path, current with { LastOperation = operation, LastOperationAtUtc = DateTime.UtcNow });
        }
    }

    /// <summary>Marks the current session as having exited cleanly. Call on graceful shutdown.</summary>
    public void RecordCleanExit()
    {
        lock (_sync)
        {
            var path = JournalPath;
            var current = TryRead(path);
            if (current is not null)
                TryWrite(path, current with { CleanExit = true });
        }
    }

    private static AppLifecycleRecord? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<AppLifecycleRecord>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static void TryWrite(string path, AppLifecycleRecord record)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(record));
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch { }
            }
        }
        catch
        {
            // The journal is a best-effort diagnostic; never let a write
            // failure here break the operation it was describing.
        }
    }
}
