namespace Hermaeus.Desktop;

/// <summary>
/// Avalonia-free transition logic for Chat's bottom-pinned transcript.
/// Extent growth only requests a snap when the user was already pinned.
/// </summary>
public static class ChatScrollPinState
{
    public const double BottomThreshold = 40;

    public readonly record struct Transition(bool IsPinned, bool ShouldSnap);

    public static Transition Apply(bool wasPinned, double extentHeight, double viewportHeight,
        double offsetY, double extentDeltaY)
    {
        if (extentDeltaY != 0 && wasPinned)
            return new(true, true);

        var distance = Math.Max(0, extentHeight - viewportHeight - offsetY);
        return new(distance <= BottomThreshold, false);
    }
}
