using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Hermaeus.Desktop.Controls;

/// <summary>
/// The app's tooltip layer, replacing Avalonia's built-in ToolTipService.
///
/// Avalonia shows tooltips in a Popup. The very edge of that popup is a region
/// that counts as being over the popup but produces no hit-test result, so
/// ToolTipService sees "nothing under the pointer", closes the tooltip, and
/// immediately reopens it because the pointer is still on the control. The
/// resulting open/close loop rewrites TopLevel.PointerOverElement on every
/// iteration, and TopLevel calls SetCursor on every such change, so the visible
/// symptom is the mouse cursor flickering between the control's cursor and the
/// default arrow, usually without the tooltip ever painting. It is upstream
/// AvaloniaUI/Avalonia#19218, which remains open. Its public report targets
/// Avalonia 11.3.2 on macOS, and this migration does not assume that 12.1.1
/// fixes the same feedback loop on Hermaeus's supported platforms. Placement
/// and offset workarounds only help when
/// they happen to move the popup clear of the pointer's path, which is why four
/// attempts at fixing this in the XAML did not hold.
///
/// This layer avoids the bug by construction rather than working around it:
///   - there is no popup and no second window; the tooltip is a Border in the
///     TopLevel's OverlayLayer,
///   - the Border is IsHitTestVisible false, so it can never be a hit-test
///     result and can never influence which element the pointer is over,
///   - show and hide are driven purely by which control the pointer is over,
///     never by hit-testing the tooltip itself, so there is no feedback loop
///     even when the tooltip is directly beneath the pointer.
///
/// It reads the same ToolTip.Tip attached values the XAML already sets, so the
/// views and the icon-only-control tooltip guard test are unaffected;
/// AppStyles.axaml sets ToolTip.ServiceEnabled false to stop Avalonia's own
/// service from also handling them.
/// </summary>
internal static class OverlayToolTip
{
    /// <summary>Distance between the adorned control and the tooltip.</summary>
    private const double Gap = 8;

    /// <summary>Minimum distance kept between the tooltip and the window edge.</summary>
    private const double EdgePadding = 4;

    /// <summary>Matches the MaxWidth the previous ToolTip style used.</summary>
    private const double MaxTipWidth = 360;

    private static readonly DispatcherTimer ShowTimer = new();

    private static Control? _target;
    private static Border? _visual;
    private static OverlayLayer? _layer;
    private static bool _installed;

    /// <summary>
    /// Registers the process-wide pointer handlers. Class handlers apply to
    /// every InputElement in every window, so dialogs get tooltips without any
    /// per-window wiring.
    /// </summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        ShowTimer.Tick += OnShowTimerTick;

        InputElement.PointerMovedEvent.AddClassHandler<InputElement>(
            OnPointerMoved, handledEventsToo: true);
        InputElement.PointerExitedEvent.AddClassHandler<InputElement>(
            OnPointerExited, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<InputElement>(
            (_, _) => Hide(), handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<InputElement>(
            (_, _) => Hide(), handledEventsToo: true);
    }

    private static void OnPointerMoved(InputElement sender, PointerEventArgs e)
    {
        // PointerMoved bubbles, so this handler runs once per ancestor. Only
        // act on the innermost element, or the walk below would start part-way
        // up the tree and could pick a different host on each pass.
        if (!ReferenceEquals(sender, e.Source)) return;

        SetTarget(FindTipHost(e.Source as Visual));
    }

    private static void OnPointerExited(InputElement sender, PointerEventArgs e)
    {
        // Covers the pointer leaving the window entirely, where no further
        // PointerMoved arrives to clear the target.
        if (_target is null) return;
        if (sender is not Visual visual) return;
        if (ReferenceEquals(visual, _target) || visual.IsVisualAncestorOf(_target))
            Hide();
    }

    /// <summary>
    /// Walks up from the element under the pointer to the nearest control that
    /// carries a tooltip, mirroring what ToolTipService does with the hit-test
    /// result.
    /// </summary>
    private static Control? FindTipHost(Visual? from)
    {
        for (var v = from; v is not null; v = v.GetVisualParent())
        {
            if (v is not Control control) continue;
            if (!control.IsEffectivelyEnabled) continue;
            if (HasTipText(control)) return control;
        }
        return null;
    }

    private static bool HasTipText(Control control) =>
        ToolTip.GetTip(control) switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true
        };

    private static void SetTarget(Control? host)
    {
        // No churn while the pointer stays on the same control: without this
        // every pointer move would restart the timer and the tooltip would
        // never appear.
        if (ReferenceEquals(host, _target)) return;

        Hide();
        _target = host;
        if (host is null) return;

        var delay = ToolTip.GetShowDelay(host);
        if (delay <= 0)
        {
            Show(host);
            return;
        }

        ShowTimer.Interval = TimeSpan.FromMilliseconds(delay);
        ShowTimer.Start();
    }

    private static void OnShowTimerTick(object? sender, EventArgs e)
    {
        ShowTimer.Stop();
        if (_target is not null) Show(_target);
    }

    private static void Show(Control host)
    {
        var layer = OverlayLayer.GetOverlayLayer(host);
        if (layer is null) return;

        var tip = ToolTip.GetTip(host);
        if (tip is null) return;

        var content = tip as Control ?? new TextBlock
        {
            Text = tip.ToString(),
            TextWrapping = TextWrapping.Wrap
        };

        var visual = new Border
        {
            Classes = { "app-tooltip" },
            MaxWidth = MaxTipWidth,
            // The whole point: a tooltip that cannot be hit-tested cannot feed
            // back into which element the pointer is over, so it cannot drive
            // the open/close loop that flickers the cursor. Safe here, unlike
            // on Avalonia's own ToolTip, because nothing in this class closes
            // the tooltip based on a hit-test result.
            IsHitTestVisible = false,
            Child = content
        };

        layer.Children.Add(visual);
        _visual = visual;
        _layer = layer;

        Position(visual, host, layer);
    }

    private static void Position(Border visual, Control host, OverlayLayer layer)
    {
        var origin = host.TranslatePoint(default, layer);
        if (origin is null) return;

        // Attached to the layer above, so styles are applied and this measure
        // returns the size the tooltip will actually take.
        visual.Measure(new Size(MaxTipWidth, double.PositiveInfinity));
        var size = visual.DesiredSize;
        var at = origin.Value;

        var x = at.X + ((host.Bounds.Width - size.Width) / 2);
        var y = at.Y + host.Bounds.Height + Gap;

        // Flip above when there is no room below. Clamping to the layer keeps
        // the tooltip inside the window in every case, which is also the
        // situation (control near the screen edge) that reproduces the upstream
        // bug most reliably.
        if (layer.Bounds.Height > 0 && y + size.Height > layer.Bounds.Height - EdgePadding)
            y = at.Y - size.Height - Gap;

        if (layer.Bounds.Width > 0)
            x = Math.Clamp(x, EdgePadding, Math.Max(EdgePadding, layer.Bounds.Width - size.Width - EdgePadding));
        if (layer.Bounds.Height > 0)
            y = Math.Clamp(y, EdgePadding, Math.Max(EdgePadding, layer.Bounds.Height - size.Height - EdgePadding));

        Canvas.SetLeft(visual, x);
        Canvas.SetTop(visual, y);
    }

    private static void Hide()
    {
        ShowTimer.Stop();
        _target = null;

        if (_visual is null) return;

        // Detach the content first: a Control used as ToolTip.Tip is shared
        // with the next show, and Avalonia will not accept it under a second
        // parent.
        _visual.Child = null;
        _layer?.Children.Remove(_visual);
        _visual = null;
        _layer = null;
    }
}
