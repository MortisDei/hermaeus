using Avalonia.Controls;
using Aether.ViewModels;

namespace Aether.Desktop.Views;

public partial class BenchmarkView : UserControl
{
    public BenchmarkView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not BenchmarkViewModel vm)
            return;

        vm.RequestClearRunHistoryConfirmation = async () =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ClearBenchmarkHistoryDialog();
            return await dialog.ShowDialog<bool>(owner);
        };
    }
}
