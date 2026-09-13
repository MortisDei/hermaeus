using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// Repairs the common native-window placement edge case where a modal is
/// centred from a scaled frame size and lands to the side of its owner. The
/// calculation uses both windows' client sizes in the owner's desktop scale,
/// then clamps the result to the owner's screen working area.
/// </summary>
internal static class ModalWindowPlacement
{
    public static void ScheduleCenterOnOwner(Window dialog)
    {
        dialog.Opened += OnOpened;
    }

    private static void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Window dialog)
            return;

        dialog.Opened -= OnOpened;
        // Window.ShowDialog assigns Owner after raising Opened. Posting keeps
        // the correction after that assignment and after the first layout.
        Dispatcher.UIThread.Post(() => CenterOnOwner(dialog), DispatcherPriority.Loaded);
    }

    private static void CenterOnOwner(Window dialog)
    {
        if (dialog.PlatformImpl is null || dialog.Owner is not Window owner || !owner.IsVisible)
            return;

        var screen = owner.Screens.ScreenFromWindow(owner)
            ?? owner.Screens.ScreenFromPoint(owner.Position);
        if (screen is null)
            return;

        var scaling = owner.DesktopScaling;
        var ownerSize = PixelSize.FromSize(owner.ClientSize, scaling);
        var dialogSize = PixelSize.FromSize(dialog.ClientSize, scaling);
        var desiredX = owner.Position.X + (ownerSize.Width - dialogSize.Width) / 2;
        var desiredY = owner.Position.Y + (ownerSize.Height - dialogSize.Height) / 2;
        var workArea = screen.WorkingArea;
        var maxX = Math.Max(workArea.X, workArea.Right - dialogSize.Width);
        var maxY = Math.Max(workArea.Y, workArea.Bottom - dialogSize.Height);

        dialog.Position = new PixelPoint(
            Math.Clamp(desiredX, workArea.X, maxX),
            Math.Clamp(desiredY, workArea.Y, maxY));
    }
}
