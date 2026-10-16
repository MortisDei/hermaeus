using Avalonia.Controls;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class MemoriesView : UserControl
{
    private MemoriesViewModel? _wiredViewModel;
    private Func<MemoryItemViewModel, Task<bool>>? _confirmation;

    public MemoriesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { UnwireViewModel(); WireViewModel(); };
        Loaded += (_, _) => WireViewModel();
        Unloaded += (_, _) => UnwireViewModel();
    }

    private void UnwireViewModel()
    {
        if (_wiredViewModel is { } vm && vm.RequestDeleteConfirmation == _confirmation)
            vm.RequestDeleteConfirmation = null;
        _wiredViewModel = null;
        _confirmation = null;
    }

    private void WireViewModel()
    {
        if (!IsLoaded || ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not MemoriesViewModel vm) return;
        _wiredViewModel = vm;
        Func<MemoryItemViewModel, Task<bool>>? confirmation = null;
        bool OwnsCallback() => ReferenceEquals(_wiredViewModel, vm)
            && vm.RequestDeleteConfirmation == confirmation;
        confirmation = async item =>
        {
            if (!OwnsCallback() || TopLevel.GetTopLevel(this) is not Window owner)
                return false;
            var dialog = new ConfirmActionDialog("Delete memory", $"Permanently delete this memory?\n\n{item.Content}");
            return await dialog.ShowDialog<bool>(owner) && OwnsCallback();
        };
        _confirmation = confirmation;
        vm.RequestDeleteConfirmation = confirmation;
    }
}
