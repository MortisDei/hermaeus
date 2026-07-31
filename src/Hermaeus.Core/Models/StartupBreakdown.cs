namespace Hermaeus.Core.Models;

/// <summary>
/// One measured startup phase, optionally broken down into the steps inside it.
/// r27 05-small-open-items.md 5.3: when those steps ran concurrently their
/// durations overlap and no longer sum to the parent, so
/// <see cref="ChildrenRanConcurrently"/> says so rather than leaving a reader to
/// add three numbers that were never a sequence.
/// </summary>
public sealed record StartupPhase(
    string Name,
    long Ms,
    IReadOnlyList<StartupPhase> Children,
    bool ChildrenRanConcurrently = false)
{
    public StartupPhase(string name, long ms) : this(name, ms, []) { }

    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// How long one managed server took to report healthy at startup.
/// r27 01-startup-that-never-waits.md 1.5: recorded separately because
/// auto-start is no longer inside the startup total, and this is the number
/// doc 03's speculative decoding is expected to move.
/// </summary>
public sealed record StartupServerStart(string ServerName, long ElapsedMs, bool ReachedHealthy);

/// <summary>The last startup's measured phases, as shown in System Overview.</summary>
public sealed record StartupBreakdown(
    DateTime RecordedUtc,
    IReadOnlyList<StartupPhase> Phases,
    long TotalMs,
    IReadOnlyList<StartupServerStart> ServerStarts);
