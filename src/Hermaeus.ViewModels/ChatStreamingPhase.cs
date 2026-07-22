namespace Hermaeus.ViewModels;

/// <summary>
/// Client-side phase label for a chat send that has not produced visible
/// content yet (r14 4.2): a long prompt eval renders as a frozen empty bubble,
/// so this drives a lightweight placeholder from elapsed time and the little we
/// know client-side. No server polling; /slots polling is out of scope.
/// </summary>
public static class ChatStreamingPhase
{
    /// <summary>Below this elapsed time nothing is shown, so a fast send never flickers a placeholder.</summary>
    public const long GraceMs = 2_000;

    /// <summary>
    /// r19 6.4: rotating status words for the "thinking" gap, where nothing
    /// concrete is known. "Reading prompt" is left alone below - it IS
    /// concrete phase information, so it always wins over whimsy. "Thinking"
    /// stays first in the list so the default (<paramref name="whimsyIndex"/>
    /// = 0) reproduces the original steady text exactly.
    /// </summary>
    public static readonly IReadOnlyList<string> WhimsyWords =
    [
        "Thinking", "Pondering", "Herding tokens", "Warming the cache",
        "Consulting the weights", "Brewing", "Untangling", "Sharpening pencils"
    ];

    /// <summary>
    /// Returns the placeholder text ("Reading prompt... 5s" / "Pondering...
    /// 12s"), or empty when a visible token has arrived or the grace
    /// threshold has not been crossed. Pure so it is testable without a live
    /// send or a real timer; <paramref name="whimsyIndex"/> selects the
    /// rotating word deterministically (the caller advances it, e.g. once
    /// per 2.5s of elapsed time) rather than this method owning a timer.
    /// </summary>
    public static string Describe(long elapsedMs, bool sawFirstEvent, bool sawContent, int whimsyIndex = 0)
    {
        if (sawContent || elapsedMs < GraceMs)
            return string.Empty;
        var seconds = elapsedMs / 1_000;
        if (!sawFirstEvent)
            return $"Reading prompt... {seconds}s";
        var index = ((whimsyIndex % WhimsyWords.Count) + WhimsyWords.Count) % WhimsyWords.Count;
        return $"{WhimsyWords[index]}... {seconds}s";
    }
}
