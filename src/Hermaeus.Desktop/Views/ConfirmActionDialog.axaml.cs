using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// A small reusable yes/no confirmation dialog with a caller-supplied title and
/// message (r14 3.2 prune confirm). Returns true from ShowDialog when confirmed.
/// </summary>
public partial class ConfirmActionDialog : Window
{
    public ConfirmActionDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    public ConfirmActionDialog(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    internal static PixelPoint CenterOnOwner(PixelPoint ownerPosition, Size ownerClientSize, Size dialogClientSize) =>
        new(
            ownerPosition.X + Math.Max(0, (int)Math.Round((ownerClientSize.Width - dialogClientSize.Width) / 2)),
            ownerPosition.Y + Math.Max(0, (int)Math.Round((ownerClientSize.Height - dialogClientSize.Height) / 2)));

    private void OnOpened(object? sender, EventArgs e)
    {
        if (Owner is not Window owner)
            return;

        // Some Linux window managers interpret CenterOwner as CenterScreen when
        // the owner is on a large or secondary display. Keep the dialog modal
        // and owned, but anchor its actual pixel position to the invoking window.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = CenterOnOwner(owner.Position, owner.ClientSize, ClientSize);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
