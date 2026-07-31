namespace Hermaeus.Core.Services;

/// <summary>
/// The one managed server Chat is currently waiting on, if any: its name and
/// how long it has been starting (r27 01-startup-that-never-waits.md 1.3).
/// </summary>
public sealed record ChatWarmingServer(string Name, TimeSpan Elapsed);

/// <summary>
/// r27 01 1.3: the copy for "the model dropdown is empty because a server is
/// still loading a model". Pure, so the wording and the 90 second threshold are
/// testable without a process or a clock.
/// llama-server reports nothing between launch and healthy, so this states
/// elapsed time and never a percentage or an estimate.
/// </summary>
public static class ChatWarmingState
{
    /// <summary>Past this, a start is worth mentioning as unusual rather than just reporting.</summary>
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(90);

    public static bool IsSlow(TimeSpan elapsed) => elapsed >= SlowThreshold;

    public static string Describe(string serverName, TimeSpan elapsed)
    {
        var name = string.IsNullOrWhiteSpace(serverName) ? "The server" : serverName;
        var line = $"{name} is starting, {FormatElapsed(elapsed)} so far.";
        return IsSlow(elapsed)
            ? $"{line} That is longer than usual. The server log is in Services."
            : line;
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var seconds = (int)elapsed.TotalSeconds;
        if (seconds < 60)
            return $"{seconds}s";

        return $"{seconds / 60}m {seconds % 60}s";
    }
}
