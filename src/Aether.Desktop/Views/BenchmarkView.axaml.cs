using Avalonia.Controls;
using Avalonia.Interactivity;
using Aether.Desktop.Views;
using Aether.ViewModels;

namespace Aether.Desktop.Views;

public partial class BenchmarkView : UserControl
{
    private bool _loadRequested;

    public BenchmarkView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loadRequested || DataContext is not BenchmarkViewModel vm)
            return;

        if (vm.Suites.Count > 0 || vm.Models.Count > 0 || vm.Runs.Count > 0)
            return;

        _loadRequested = true;
        await vm.LoadAsync();
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

        vm.RequestShowCaseInfo = async (result) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return;

            var infoVm = new BenchmarkCaseInfoViewModel(result.Result);
            var dialog = new BenchmarkCaseInfoDialog { DataContext = infoVm };
            await dialog.ShowDialog(owner);
        };
        vm.RequestShowRunInfo = async (runVm) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner || runVm is null)
                return;

            var infoVm = new BenchmarkRunInfoViewModel(runVm.Run, vm);
            var dialog = new BenchmarkRunInfoDialog { DataContext = infoVm };
            await dialog.ShowDialog(owner);
        };
    }
}
