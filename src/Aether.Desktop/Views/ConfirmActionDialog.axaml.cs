using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Desktop.Views;

/// <summary>
/// A small reusable yes/no confirmation dialog with a caller-supplied title and
/// message (r14 3.2 prune confirm). Returns true from ShowDialog when confirmed.
/// </summary>
public partial class ConfirmActionDialog : Window
{
    public ConfirmActionDialog()
    {
        InitializeComponent();
    }

    public ConfirmActionDialog(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
