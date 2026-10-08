using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
        ModalWindowPlacement.ScheduleCenterOnOwner(this);
        KeyDown += OnKeyDown;
        Opened += OnOpened;
    }

    public ConfirmActionDialog(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    public ConfirmActionDialog(string title, string message, string cancelLabel, string confirmLabel) : this(title, message)
    {
        CancelButton.Content = cancelLabel;
        ConfirmButton.Content = confirmLabel;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnOpened(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => CancelButton.Focus(), DispatcherPriority.Input);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ConfirmButton.IsEnabled)
        {
            Close(true);
            e.Handled = true;
        }
    }
}
