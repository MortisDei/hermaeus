using Avalonia.Controls;
using Avalonia.Interactivity;
using Aether.ViewModels;

namespace Aether.Desktop.Views;

public partial class ConversationListView : UserControl
{
    public ConversationListView() => InitializeComponent();

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb
            && lb.SelectedItem is ConversationItemViewModel item
            && DataContext is MainWindowViewModel vm)
        {
            vm.SelectConversationCommand.Execute(item);
        }
    }
}
