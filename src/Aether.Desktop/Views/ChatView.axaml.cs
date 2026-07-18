using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Aether.Core.Services;
using Aether.ViewModels;

namespace Aether.Desktop.Views;

public partial class ChatView : UserControl
{
    private ChatViewModel? _vm;
    private EventHandler? _scrollHandler;
    private EventHandler? _settingsChangedHandler;

    public ChatView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm is not null && _scrollHandler is not null)
                _vm.ScrollToBottom -= _scrollHandler;
            if (_vm is not null && _settingsChangedHandler is not null)
                _vm.Settings.SettingsChanged -= _settingsChangedHandler;
            if (_vm is not null)
            {
                _vm.RequestCopyToClipboard = null;
                _vm.RequestContextFilePicker = null;
                _vm.RequestConversationExportPath = null;
            }

            if (DataContext is not ChatViewModel vm)
            {
                _vm = null;
                _scrollHandler = null;
                _settingsChangedHandler = null;
                return;
            }

            _vm = vm;

            _settingsChangedHandler = (_, _) => ApplyAcceptsReturn();
            vm.Settings.SettingsChanged += _settingsChangedHandler;
            ApplyAcceptsReturn();

            _scrollHandler = (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (this.FindControl<ScrollViewer>("MessagesScroll") is { } scroll)
                    {
                        scroll.Offset = new Vector(scroll.Offset.X, scroll.Extent.Height);
                    }
                }, DispatcherPriority.Background);
            vm.ScrollToBottom += _scrollHandler;

            vm.RequestCopyToClipboard = async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    await cb.SetTextAsync(text);
            };

            vm.RequestContextFilePicker = async () =>
            {
                var top = TopLevel.GetTopLevel(this);
                if (top is null) return;

                var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Attach files as chat context",
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Text and code files")
                        {
                            Patterns =
                            [
                                "*.txt", "*.md", "*.cs", "*.fs", "*.vb", "*.csproj", "*.props", "*.json",
                                "*.xml", "*.xaml", "*.axaml", "*.yaml", "*.yml", "*.toml", "*.sh", "*.ps1",
                                "*.py", "*.js", "*.jsx", "*.ts", "*.tsx", "*.css", "*.html", "*.razor",
                                "*.sql", "*.rs", "*.go", "*.java", "*.c", "*.h", "*.cpp", "*.hpp"
                            ]
                        },
                        new FilePickerFileType("All files") { Patterns = ["*"] }
                    ]
                });

                if (files.Count > 0)
                    await vm.AddContextFilesAsync(files.Select(f => f.Path.LocalPath));
            };

            vm.RequestConversationExportPath = async format =>
            {
                var top = TopLevel.GetTopLevel(this);
                if (top is null) return null;

                var ext = format == ConversationExportFormat.Json ? "json" : "md";
                var label = format == ConversationExportFormat.Json ? "JSON" : "Markdown";
                var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = $"Export conversation as {label}",
                    SuggestedFileName = $"aether-conversation.{ext}",
                    FileTypeChoices =
                    [
                        new FilePickerFileType(label) { Patterns = [$"*.{ext}"] },
                        new FilePickerFileType("All files") { Patterns = ["*"] }
                    ]
                });
                return file?.Path.LocalPath;
            };
        };
    }

    /// <summary>
    /// AcceptsReturn must reflect Ui.CtrlEnterToSend: when it's true (plain
    /// Enter inserts a newline, Ctrl+Enter sends), the TextBox needs to own
    /// Enter for its normal newline insertion. When it's false, AcceptsReturn
    /// must be false too, otherwise Avalonia's own Enter handling inserts a
    /// newline and marks the key handled before <see cref="OnInputKeyDown"/>
    /// ever gets to send on plain Enter.
    /// </summary>
    private void ApplyAcceptsReturn()
    {
        if (_vm is null) return;
        if (this.FindControl<TextBox>("InputBox") is { } input)
            input.AcceptsReturn = _vm.Settings.Settings.Ui.CtrlEnterToSend;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || _vm.IsGenerating) return;
        var ctrlEnter = _vm.Settings.Settings.Ui.CtrlEnterToSend;
        var sendModifier = ctrlEnter ? KeyModifiers.Control : KeyModifiers.None;
        if (e.Key == Key.Return && e.KeyModifiers == sendModifier)
        {
            e.Handled = true;
            if (_vm.SendCommand.CanExecute(null))
                _vm.SendCommand.Execute(null);
        }
    }

    private void OnContextDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.Data.Contains(DataFormats.Files);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (_vm is not null)
            _vm.IsContextDragOver = hasFiles;
        e.Handled = true;
    }

    private void OnContextDragLeave(object? sender, DragEventArgs e)
    {
        if (_vm is not null)
            _vm.IsContextDragOver = false;
        e.Handled = true;
    }

    private async void OnContextDrop(object? sender, DragEventArgs e)
    {
        if (_vm is not null)
            _vm.IsContextDragOver = false;
        if (_vm is null || !e.Data.Contains(DataFormats.Files)) return;
        var files = e.Data.GetFiles();
        if (files is null) return;

        var paths = files
            .OfType<IStorageFile>()
            .Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        if (paths.Count > 0)
            await _vm.AddContextFilesAsync(paths);
    }
}
