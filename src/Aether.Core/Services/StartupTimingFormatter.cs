namespace Aether.Core.Services;

/// <summary>
/// Formats per-phase startup timings into a single human-readable log line,
/// e.g. "Startup: settings 12 ms, stores 85 ms, total 97 ms".
/// </summary>
public static class StartupTimingFormatter
{
    public static string Format(IReadOnlyList<(string Phase, long Ms)> phases)
    {
        if (phases.Count == 0)
            return "Startup: no phases recorded";

        return "Startup: " + string.Join(", ", phases.Select(p => $"{p.Phase} {p.Ms} ms"));
    }
}
