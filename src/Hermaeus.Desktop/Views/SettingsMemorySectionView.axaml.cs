using Avalonia.Controls;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class SettingsMemorySectionView : UserControl
{
    private MemorySettingsViewModel? _wiredViewModel;
    private Func<Task<bool>>? _confirmation;
    private long _callbackGeneration;

    public SettingsMemorySectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => WireViewModel();
        Unloaded += (_, _) => UnwireViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnwireViewModel();
        WireViewModel();
    }

    private void UnwireViewModel()
    {
        _callbackGeneration++;
        if (_wiredViewModel is { } vm && vm.RequestConfirmClearIndex == _confirmation)
            vm.RequestConfirmClearIndex = null;
        _wiredViewModel = null;
        _confirmation = null;
    }

    private void WireViewModel()
    {
        if (!IsLoaded) return;
        if (ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not MemorySettingsViewModel vm) return;
        _wiredViewModel = vm;
        var generation = _callbackGeneration;

        _confirmation = async () =>
        {
            if (generation != _callbackGeneration
                || !ReferenceEquals(_wiredViewModel, vm)
                || vm.RequestConfirmClearIndex != _confirmation
                || TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ConfirmActionDialog(
                "Clear Recall index",
                "This removes the Recall index only - the searchable copy of your message and task text - and vacuums the file. It does not touch a single conversation, memory, task or dataset.");
            return await dialog.ShowDialog<bool>(owner)
                && generation == _callbackGeneration
                && ReferenceEquals(_wiredViewModel, vm)
                && vm.RequestConfirmClearIndex == _confirmation;
        };
        vm.RequestConfirmClearIndex = _confirmation;
    }
}
