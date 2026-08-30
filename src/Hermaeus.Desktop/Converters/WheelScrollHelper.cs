using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// Shared tunnel-phase wheel hijack (r16 03-workbench-and-desktop.md 3.5),
/// previously duplicated identically in ModelManagementView, ServicesView,
/// and SettingsView (r13 02-model-library.md 2.2 root cause): a card full of
/// NumericUpDowns means the pointer is almost always over one, and
/// Avalonia's NumericUpDown consumes every wheel notch as a spin before it
/// ever reaches the outer ScrollViewer. Each owner subscribes in the tunnel
/// phase (runs before any child control's own handler). It gives the nearest
/// ScrollViewer first refusal, then bubbles to an ancestor when the child is
/// already at the relevant edge. The same policy is used by the main window
/// for views that do not have a page-specific handler.
/// </summary>
public static class WheelScrollHelper
{
    private const double StepPixels = 56;

    public static void Handle(ScrollViewer target, PointerWheelEventArgs e)
    {
        for (var current = e.Source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is not ScrollViewer scrollViewer)
                continue;
            if (TryScroll(scrollViewer, e.Delta))
            {
                e.Handled = true;
                return;
            }
            if (ReferenceEquals(scrollViewer, target))
                return;
        }

        if (TryScroll(target, e.Delta))
            e.Handled = true;
    }

    /// <summary>
    /// Handles wheel input for the nearest ScrollViewer and its ancestors.
    /// This is used at the shell boundary so nested lists, JSON panes, and
    /// page scroll viewers share one predictable edge-bubbling policy.
    /// </summary>
    public static void Handle(PointerWheelEventArgs e)
    {
        for (var current = e.Source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is ScrollViewer scrollViewer && TryScroll(scrollViewer, e.Delta))
            {
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>
    /// Calculates a clamped wheel offset without requiring a live visual tree.
    /// It is kept public so the boundary policy can be regression-tested.
    /// </summary>
    public static bool TryCalculateOffset(Vector current, Size extent, Size viewport, Vector delta, out Vector next)
    {
        var maxX = System.Math.Max(0, extent.Width - viewport.Width);
        var maxY = System.Math.Max(0, extent.Height - viewport.Height);
        var candidateX = System.Math.Clamp(current.X - delta.X * StepPixels, 0, maxX);
        var candidateY = System.Math.Clamp(current.Y - delta.Y * StepPixels, 0, maxY);
        next = new Vector(candidateX, candidateY);
        return System.Math.Abs(candidateX - current.X) >= 0.1
            || System.Math.Abs(candidateY - current.Y) >= 0.1;
    }

    private static bool TryScroll(ScrollViewer target, Vector delta)
    {
        if (!TryCalculateOffset(target.Offset, target.Extent, target.Viewport, delta, out var next))
            return false;

        target.Offset = next;
        return true;
    }
}
