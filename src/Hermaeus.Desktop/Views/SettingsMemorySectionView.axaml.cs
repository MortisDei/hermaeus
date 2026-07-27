using Avalonia.Controls;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class SettingsMemorySectionView : UserControl
{
    public SettingsMemorySectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MemorySettingsViewModel vm) return;

        vm.RequestConfirmClearIndex = async () =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ConfirmActionDialog(
                "Clear Recall index",
                "This removes the Recall index only - the searchable copy of your message and task text - and vacuums the file. It does not touch a single conversation, memory, task or dataset.");
            return await dialog.ShowDialog<bool>(owner);
        };
    }
}
