using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

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
    private void OnMetadataLostFocus(object? sender, RoutedEventArgs e) => CommitMetadata(sender);

    private void OnDetailsPinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConversationItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.TogglePinConversationCommand.Execute(item);
        }
    }

    private void OnDetailsArchiveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConversationItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.ToggleArchiveConversationCommand.Execute(item);
        }
    }

    private void OnDetailsRecallExclusionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConversationItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.ToggleRecallExclusionCommand.Execute(item);
        }
    }

    private void OnDetailsDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConversationItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.DeleteConversationCommand.Execute(item);
        }
    }

    private void OnDetailsExportMarkdownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConversationItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.ExportConversationMarkdownCommand.Execute(item);
        }
    }

    private void OnDetailsExportJsonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConversationItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.ExportConversationJsonCommand.Execute(item);
        }
    }

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitRename(sender);
        e.Handled = true;
        if (sender is Control control)
            control.Focus(NavigationMethod.Unspecified);
    }

    private void OnMetadataKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitMetadata(sender);
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

    private void CommitMetadata(object? sender)
    {
        if (sender is TextBox { DataContext: ConversationItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.SaveConversationMetadataCommand.Execute(item);
        }
    }

    private void OnContextMenuPinClick(object? sender, RoutedEventArgs e)
    {
        if (ConvList.SelectedItem is ConversationItemViewModel item
            && DataContext is MainWindowViewModel vm)
        {
            vm.TogglePinConversationCommand.Execute(item);
        }
    }

    private void OnContextMenuArchiveClick(object? sender, RoutedEventArgs e)
    {
        if (ConvList.SelectedItem is ConversationItemViewModel item
            && DataContext is MainWindowViewModel vm)
        {
            vm.ToggleArchiveConversationCommand.Execute(item);
        }
    }

    private void OnContextMenuRecallExclusionClick(object? sender, RoutedEventArgs e)
    {
        if (ConvList.SelectedItem is ConversationItemViewModel item
            && DataContext is MainWindowViewModel vm)
        {
            vm.ToggleRecallExclusionCommand.Execute(item);
        }
    }

    private void OnContextMenuDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (ConvList.SelectedItem is ConversationItemViewModel item
            && DataContext is MainWindowViewModel vm)
        {
            vm.DeleteConversationCommand.Execute(item);
        }
    }

    private void OnContextMenuExportMarkdownClick(object? sender, RoutedEventArgs e)
    {
        if (ConvList.SelectedItem is ConversationItemViewModel item
            && DataContext is MainWindowViewModel vm)
        {
            vm.ExportConversationMarkdownCommand.Execute(item);
        }
    }

    private void OnContextMenuExportJsonClick(object? sender, RoutedEventArgs e)
    {
        if (ConvList.SelectedItem is ConversationItemViewModel item
            && DataContext is MainWindowViewModel vm)
        {
            vm.ExportConversationJsonCommand.Execute(item);
        }
    }
}
