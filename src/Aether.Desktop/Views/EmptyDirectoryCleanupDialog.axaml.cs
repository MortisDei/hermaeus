using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Desktop.Views;

public partial class EmptyDirectoryCleanupDialog : Window
{
    public EmptyDirectoryCleanupDialog()
    {
        InitializeComponent();
    }

    public void SetCount(int count) =>
        MessageText.Text = $"The move left {count} empty folder(s) behind under the old location. Remove them?";

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
