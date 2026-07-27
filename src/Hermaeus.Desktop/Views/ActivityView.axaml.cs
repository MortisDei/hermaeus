using Avalonia.Controls;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class ActivityView : UserControl
{
    public ActivityView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ActivityViewModel vm) return;

        vm.RequestConfirmClear = async () =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ConfirmActionDialog(
                "Clear activity history",
                "This removes the recorded activity events only. It does not touch model usage totals or any conversation, memory, task or dataset.");
            return await dialog.ShowDialog<bool>(owner);
        };
    }
}
