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
        var distance = Math.Max(0, extentHeight - viewportHeight - offsetY);
        // A remeasure can grow the extent after a user has scrolled away. The
        // offset is authoritative for that event, otherwise streaming content
        // silently steals the user's scroll position and repins the view.
        if (distance > BottomThreshold)
            return new(false, false);

        if (extentDeltaY != 0 && wasPinned)
            return new(true, true);

        return new(distance <= BottomThreshold, false);
    }
}
