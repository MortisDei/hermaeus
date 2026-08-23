using Avalonia.Controls;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class MemoriesView : UserControl
{
    public MemoriesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MemoriesViewModel vm)
                return;
            vm.RequestDeleteConfirmation = async item =>
            {
                if (TopLevel.GetTopLevel(this) is not Window owner)
                    return false;
                var dialog = new ConfirmActionDialog("Delete memory", $"Permanently delete this memory?\n\n{item.Content}");
                return await dialog.ShowDialog<bool>(owner);
            };
        };
    }
}
