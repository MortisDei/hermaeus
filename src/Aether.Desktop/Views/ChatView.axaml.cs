using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Aether.ViewModels;

namespace Aether.Desktop.Views;

public partial class ChatView : UserControl
{
    private ChatViewModel? _vm;

    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext as ChatViewModel;
            if (_vm is null) return;

            _vm.ScrollToBottom += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (this.FindControl<ListBox>("MessagesList") is { } list
                        && _vm.Messages.LastOrDefault() is { } last)
                    {
                        list.ScrollIntoView(last);
                    }
                }, DispatcherPriority.Background);

            _vm.RequestCopyToClipboard = async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    await cb.SetTextAsync(text);
            };
        };
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || _vm.IsGenerating) return;
        if (e.Key == Key.Return && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            if (_vm.SendCommand.CanExecute(null))
                _vm.SendCommand.Execute(null);
        }
    }
}
