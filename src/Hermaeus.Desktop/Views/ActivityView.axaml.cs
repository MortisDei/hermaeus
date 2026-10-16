using Avalonia.Controls;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class ActivityView : UserControl
{
    private ActivityViewModel? _wiredViewModel;
    private Func<Task<bool>>? _confirmation;

    public ActivityView()
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
        if (_wiredViewModel is { } vm && vm.RequestConfirmClear == _confirmation)
            vm.RequestConfirmClear = null;
        _wiredViewModel = null;
        _confirmation = null;
    }

    private void WireViewModel()
    {
        if (!IsLoaded || ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not ActivityViewModel vm) return;
        _wiredViewModel = vm;

        Func<Task<bool>>? confirmation = null;
        bool OwnsCallback() => ReferenceEquals(_wiredViewModel, vm)
            && vm.RequestConfirmClear == confirmation;
        confirmation = async () =>
        {
            if (!OwnsCallback() || TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ConfirmActionDialog(
                "Clear activity history",
                "This removes the recorded activity events only. It does not touch model usage totals or any conversation, memory, task or dataset.");
            return await dialog.ShowDialog<bool>(owner) && OwnsCallback();
        };
        _confirmation = confirmation;
        vm.RequestConfirmClear = confirmation;
    }
}
