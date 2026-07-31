namespace Hermaeus.ViewModels;

/// <summary>
/// The Agent workbench's equivalent of <see cref="ChatStreamingPhase"/>: a
/// rotating "still working" line for the gap where the agent is thinking and
/// nothing on screen moves.
///
/// The workbench set one status message when a run started and did not touch
/// it again until a step finished, so a ninety second model call showed frozen
/// text and read as a hung app. Unlike chat there is no token stream to end
/// the wait, so this runs for as long as the step does.
///
/// Pure, so it tests without a live run or a real timer: the caller owns the
/// clock and advances <paramref name="wordIndex"/> (once per 2.5s of elapsed
/// time, from a per-step random offset, exactly as chat does).
/// </summary>
public static class AgentActivityPhase
{
    /// <summary>Below this elapsed time nothing is shown, so a fast step never flickers a placeholder.</summary>
    public const long GraceMs = 1_500;

    /// <summary>
    /// Deliberately about the work the agent is actually doing (reading a
    /// workspace, weighing a plan) rather than chat's generic pondering, so
    /// the two panels do not read as the same wait.
    /// </summary>
    public static readonly IReadOnlyList<string> WhimsyWords =
    [
        "Thinking", "Reading the workspace", "Weighing the plan", "Consulting the weights",
        "Checking its notes", "Considering the next step", "Retracing its steps", "Untangling"
    ];

    /// <summary>
    /// Returns the placeholder ("Step 3: Thinking... 12s"), or empty while the
    /// agent is idle or the grace threshold has not been crossed.
    /// <paramref name="step"/> of 0 or less omits the step prefix, which is the
    /// case before the first step of a brand new task has been numbered.
    /// </summary>
    public static string Describe(long elapsedMs, bool isRunning, int step = 0, int wordIndex = 0)
    {
        if (!isRunning || elapsedMs < GraceMs)
            return string.Empty;

        var seconds = elapsedMs / 1_000;
        var index = ((wordIndex % WhimsyWords.Count) + WhimsyWords.Count) % WhimsyWords.Count;
        var prefix = step > 0 ? $"Step {step}: " : string.Empty;
        return $"{prefix}{WhimsyWords[index]}... {seconds}s";
    }
}
