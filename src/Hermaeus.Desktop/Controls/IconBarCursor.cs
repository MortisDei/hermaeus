using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Hermaeus.Desktop.Controls;

/// <summary>
/// Fills the dead gaps between icon buttons so the mouse cursor stays a hand
/// across a row of them.
///
/// Icon buttons are transparent, rounded and packed tightly, so the pixels
/// between them - the notches where two rounded corners meet, and any Spacing or
/// ColumnSpacing the container adds - are not part of any button. A panel with no
/// Background is not a hit-test result either, so the pointer falls all the way
/// through to the window's root panel, whose cursor is the default arrow.
/// TopLevel takes the cursor from the hit element and does not walk up to
/// ancestors, so crossing a row flickered hand/arrow/hand a few pixels before
/// each button boundary. A pointer-event log measured the pointer-over chain
/// collapsing to the window root and rebuilding about 80 times a second.
///
/// Giving the container a transparent background makes it the hit-test result in
/// those gaps, and giving it the hand cursor makes the whole row read as one
/// continuous target. The background must go on the container itself rather than
/// on a sibling laid over the buttons: a control's own background is painted
/// behind its children and is only hit where no child is hit, so it fills the
/// gaps without ever stealing hover from a button. A sibling Border was tried in
/// the chat toolbar first and swallowed the buttons' hover highlight.
///
/// Done here rather than by hand in each view because icon buttons appear in
/// fifteen axaml files and a missed container is an invisible regression. Only
/// containers whose children are all buttons are touched, so the hand cursor can
/// never spread across a panel that also holds text or inputs; containers with
/// mixed content set it explicitly (MainWindow's nav rail, ChatView's toolbar).
/// </summary>
internal static class IconBarCursor
{
    private static readonly Cursor Hand = new(StandardCursorType.Hand);

    public static void Install() =>
        Button.LoadedEvent.AddClassHandler<Button>(OnButtonLoaded);

    private static void OnButtonLoaded(Button button, RoutedEventArgs e)
    {
        if (!button.Classes.Contains("icon-btn")) return;
        if (button.GetVisualParent() is not Panel panel) return;

        // An explicit background means the view already decided what this
        // container is; leave it alone.
        if (panel.Background is not null) return;

        // Buttons only. A panel holding a label or a text box must not become a
        // hand-cursor region.
        foreach (var child in panel.Children)
        {
            if (child is not Button) return;
        }

        panel.Background = Brushes.Transparent;
        panel.Cursor = Hand;
    }
}
