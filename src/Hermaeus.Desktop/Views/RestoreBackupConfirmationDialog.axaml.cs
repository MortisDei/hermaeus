using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermaeus.Desktop.Views;

public partial class RestoreBackupConfirmationDialog : Window
{
    public RestoreBackupConfirmationDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnRestoreClick(object? sender, RoutedEventArgs e) => Close(true);
}
