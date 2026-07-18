namespace Aether.ViewModels;

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
    /// Returns the placeholder text ("Reading prompt... 5s" / "Thinking...
    /// 12s"), or empty when a visible token has arrived or the grace threshold
    /// has not been crossed. Pure so it is testable without a live send.
    /// </summary>
    public static string Describe(long elapsedMs, bool sawFirstEvent, bool sawContent)
    {
        if (sawContent || elapsedMs < GraceMs)
            return string.Empty;
        var seconds = elapsedMs / 1_000;
        var phase = sawFirstEvent ? "Thinking" : "Reading prompt";
        return $"{phase}... {seconds}s";
    }
}
