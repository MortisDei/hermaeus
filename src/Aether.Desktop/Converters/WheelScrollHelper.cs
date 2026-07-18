using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Aether.Desktop.Views;

/// <summary>
/// Shared tunnel-phase wheel hijack (r16 03-workbench-and-desktop.md 3.5),
/// previously duplicated identically in ModelManagementView, ServicesView,
/// and SettingsView (r13 02-model-library.md 2.2 root cause): a card full of
/// NumericUpDowns means the pointer is almost always over one, and
/// Avalonia's NumericUpDown consumes every wheel notch as a spin before it
/// ever reaches the outer ScrollViewer. Each owner subscribes in the tunnel
/// phase (runs before any child control's own handler) and always drives
/// its page ScrollViewer directly, regardless of what is under the pointer.
/// Known trade-off, unchanged from the original three copies: an inner
/// scrollable under the pointer (e.g. a nested list) loses the wheel to the
/// outer page scroll too. Not fixed this round; accepted as-is.
/// </summary>
public static class WheelScrollHelper
{
    private const double StepPixels = 56;

    public static void Handle(ScrollViewer target, PointerWheelEventArgs e)
    {
        var max = System.Math.Max(0, target.Extent.Height - target.Viewport.Height);
        if (max <= 0) return;

        var next = System.Math.Clamp(target.Offset.Y - e.Delta.Y * StepPixels, 0, max);
        if (System.Math.Abs(next - target.Offset.Y) < 0.1) return;

        target.Offset = new Vector(target.Offset.X, next);
        e.Handled = true;
    }
}
