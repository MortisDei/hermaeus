using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// r27 01-startup-that-never-waits.md 1.5: the last startup's phase breakdown,
/// kept so it can be read back and shown in System Overview. The phases were
/// already measured and already formatted into one Info line in the runtime log,
/// which nobody reads, and which is the only evidence a round changed startup
/// at all.
/// In memory only, and deliberately not persisted: this is the startup you are
/// currently in, not a history, and a history would be a benchmark suite.
/// </summary>
public interface IStartupTimingService
{
    /// <summary>The last recorded startup, or null before one has been recorded.</summary>
    StartupBreakdown? Last { get; }

    void Record(StartupBreakdown breakdown);

    /// <summary>
    /// Auto-start is off the critical path (1.1), so a server reporting healthy
    /// arrives after <see cref="Record"/> has already run. Appended as it lands.
    /// </summary>
    void RecordServerStart(StartupServerStart start);

    event Action? Changed;
}

/// <inheritdoc />
public sealed class StartupTimingService : IStartupTimingService
{
    private readonly object _sync = new();
    private StartupBreakdown? _last;

    public StartupBreakdown? Last
    {
        get { lock (_sync) return _last; }
    }

    public event Action? Changed;

    public void Record(StartupBreakdown breakdown)
    {
        lock (_sync)
            _last = breakdown;
        Changed?.Invoke();
    }

    public void RecordServerStart(StartupServerStart start)
    {
        lock (_sync)
        {
            // A server start with no recorded startup is possible when the user
            // starts a server by hand before the breakdown lands; keep it rather
            // than discard it, attributed to an empty startup.
            _last ??= new StartupBreakdown(DateTime.UtcNow, [], 0, []);
            var starts = _last.ServerStarts
                .Where(s => !string.Equals(s.ServerName, start.ServerName, StringComparison.Ordinal))
                .Append(start)
                .ToList();
            _last = _last with { ServerStarts = starts };
        }

        Changed?.Invoke();
    }
}
