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
    /// r19 6.4 / field report follow-up: rotating status words for the whole
    /// "nothing visible yet" gap. Originally "Reading prompt" was held apart
    /// from this list as fixed, non-rotating text until the server's first
    /// stream event arrived - but llama-server sends nothing at all during
    /// prompt eval (no early role/metadata chunk the way OpenAI's API does),
    /// so that first event coincides with the first visible token and the
    /// gate never actually opens: real sends showed only a frozen "Reading
    /// prompt... Ns" for the entire wait, defeating the point of rotating at
    /// all. "Reading prompt" now just lives in the pool like everything else.
    /// </summary>
    public static readonly IReadOnlyList<string> WhimsyWords =
    [
        "Reading prompt", "Thinking", "Pondering", "Herding tokens", "Warming the cache",
        "Consulting the weights", "Brewing", "Untangling", "Sharpening pencils"
    ];

    /// <summary>
    /// Returns the placeholder text ("Reading prompt... 5s" / "Pondering...
    /// 12s"), or empty when a visible token has arrived or the grace
    /// threshold has not been crossed. Pure so it is testable without a live
    /// send or a real timer; <paramref name="wordIndex"/> selects the
    /// rotating word deterministically (the caller advances it, e.g. once
    /// per 2.5s of elapsed time, from a per-send random starting offset so
    /// the same cycle doesn't repeat on every send) rather than this method
    /// owning a timer or randomness.
    /// </summary>
    public static string Describe(long elapsedMs, bool sawContent, int wordIndex = 0)
    {
        if (sawContent || elapsedMs < GraceMs)
            return string.Empty;
        var seconds = elapsedMs / 1_000;
        var index = ((wordIndex % WhimsyWords.Count) + WhimsyWords.Count) % WhimsyWords.Count;
        return $"{WhimsyWords[index]}... {seconds}s";
    }
}
