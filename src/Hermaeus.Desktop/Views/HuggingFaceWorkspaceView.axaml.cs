using Avalonia.Controls;
using Avalonia.Input;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class HuggingFaceWorkspaceView : UserControl
{
    public HuggingFaceWorkspaceView() => InitializeComponent();

    private void OnHfSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ModelManagementViewModel vm
            || !vm.SearchHuggingFaceCommand.CanExecute(null))
            return;

        vm.SearchHuggingFaceCommand.Execute(null);
        e.Handled = true;
    }
}
