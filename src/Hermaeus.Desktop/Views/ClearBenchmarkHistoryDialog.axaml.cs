using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermaeus.Desktop.Views;

public partial class ClearBenchmarkHistoryDialog : Window
{
    public ClearBenchmarkHistoryDialog()
    {
        InitializeComponent();
        ModalWindowPlacement.ScheduleCenterOnOwner(this);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnClearClick(object? sender, RoutedEventArgs e) => Close(true);
}
