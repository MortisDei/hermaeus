using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// Formats per-phase startup timings into a single human-readable log line,
/// e.g. "Startup: settings 12 ms, stores 85 ms, total 97 ms".
/// r27 05-small-open-items.md 5.3: a concurrent block prints its own wall-clock
/// duration with its parts in brackets, so nobody reads three overlapping
/// numbers as a sequence and then wonders why they do not add up to the total.
/// </summary>
public static class StartupTimingFormatter
{
    public static string Format(IReadOnlyList<(string Phase, long Ms)> phases)
        => Format(phases.Select(p => new StartupPhase(p.Phase, p.Ms)).ToList());

    public static string Format(IReadOnlyList<StartupPhase> phases)
    {
        if (phases.Count == 0)
            return "Startup: no phases recorded";

        return "Startup: " + string.Join(", ", phases.Select(FormatPhase));
    }

    private static string FormatPhase(StartupPhase phase)
    {
        var head = $"{phase.Name} {phase.Ms} ms";
        if (!phase.HasChildren)
            return head;

        var parts = string.Join(", ", phase.Children.Select(FormatPhase));
        return phase.ChildrenRanConcurrently
            ? $"{head} (concurrent: {parts})"
            : $"{head} ({parts})";
    }

    /// <summary>
    /// r27 01 1.5: auto-start is no longer inside the startup total, so it is
    /// reported separately, attributed by server name.
    /// </summary>
    public static string FormatServerStarts(IReadOnlyList<StartupServerStart> starts)
    {
        if (starts.Count == 0)
            return "Server auto-start: nothing configured";

        return "Server auto-start: " + string.Join(", ", starts.Select(s =>
            s.ReachedHealthy
                ? $"{s.ServerName} healthy in {s.ElapsedMs} ms"
                : $"{s.ServerName} did not reach healthy after {s.ElapsedMs} ms"));
    }
}
