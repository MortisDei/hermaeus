using Avalonia.Controls;
using Avalonia.Input;
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

    private void OnTitleLostFocus(object? sender, RoutedEventArgs e) => CommitRename(sender);

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitRename(sender);
        e.Handled = true;
        if (sender is Control control)
            control.Focus(NavigationMethod.Unspecified);
    }

    private void CommitRename(object? sender)
    {
        if (sender is TextBox { DataContext: ConversationItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.RenameConversationCommand.Execute(item);
        }
    }
}
