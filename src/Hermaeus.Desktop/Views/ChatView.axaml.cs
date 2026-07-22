using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class ChatView : UserControl
{
    private ChatViewModel? _vm;
    private EventHandler? _scrollHandler;
    private EventHandler? _settingsChangedHandler;
    private EventHandler<ScrollChangedEventArgs>? _scrollChangedHandler;
    // r19 6.3: content (action row, sources, memory pills, the incremental
    // MarkdownViewer re-render timer) keeps materializing AFTER the VM's last
    // ScrollToBottom raise, so a single programmatic scroll always lands
    // short of the true final extent. Pinning re-snaps to the bottom on every
    // extent growth while pinned, and un-pins the instant the user scrolls
    // away, so their position is never fought mid-stream.
    private bool _pinnedToBottom = true;
    private const double BottomPinThreshold = 40;

    public ChatView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm is not null && _scrollHandler is not null)
                _vm.ScrollToBottom -= _scrollHandler;
            if (_vm is not null && _settingsChangedHandler is not null)
                _vm.Settings.SettingsChanged -= _settingsChangedHandler;
            if (_scrollChangedHandler is not null && this.FindControl<ScrollViewer>("MessagesScroll") is { } previousScroll)
                previousScroll.ScrollChanged -= _scrollChangedHandler;
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
                _scrollChangedHandler = null;
                return;
            }

            _vm = vm;

            _settingsChangedHandler = (_, _) => ApplyAcceptsReturn();
            vm.Settings.SettingsChanged += _settingsChangedHandler;
            ApplyAcceptsReturn();

            _pinnedToBottom = true;
            _scrollHandler = (_, _) =>
            {
                // Sending (or resuming) a message re-pins even if the user had
                // scrolled up to read earlier history.
                _pinnedToBottom = true;
                Dispatcher.UIThread.Post(() => SnapToBottomIfPinned(force: true), DispatcherPriority.Background);
            };
            vm.ScrollToBottom += _scrollHandler;

            if (this.FindControl<ScrollViewer>("MessagesScroll") is { } scroll)
            {
                _scrollChangedHandler = OnMessagesScrollChanged;
                scroll.ScrollChanged += _scrollChangedHandler;
            }

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
                        new FilePickerFileType("Documents") { Patterns = ["*.docx", "*.pdf"] },
                        new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] },
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
                    SuggestedFileName = $"hermaeus-conversation.{ext}",
                    FileTypeChoices =
                    [
                        new FilePickerFileType(label) { Patterns = [$"*.{ext}"] },
                        new FilePickerFileType("All files") { Patterns = ["*"] }
                    ]
                });
                return file?.Path.LocalPath;
            };

            // r19 5.4: chat artifacts - open with the OS default handler, reveal in
            // the file manager, or open the conversation's whole artifacts folder.
            vm.RequestOpenFile = path =>
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    _ = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to open artifact '{path}': {ex.Message}");
                }
            };

            vm.RequestRevealInFolder = path =>
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        var psi = new ProcessStartInfo { FileName = "explorer", UseShellExecute = true };
                        psi.ArgumentList.Add($"/select,{path}");
                        _ = Process.Start(psi);
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        var psi = new ProcessStartInfo { FileName = "open", UseShellExecute = true };
                        psi.ArgumentList.Add("-R");
                        psi.ArgumentList.Add(path);
                        _ = Process.Start(psi);
                    }
                    else
                    {
                        var psi = new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = true };
                        psi.ArgumentList.Add(Path.GetDirectoryName(path) ?? path);
                        _ = Process.Start(psi);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to reveal artifact '{path}': {ex.Message}");
                }
            };

            vm.RequestOpenArtifactsFolder = path =>
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    Directory.CreateDirectory(path);
                    var psi = new ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "explorer" : OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                        UseShellExecute = true
                    };
                    psi.ArgumentList.Add(path);
                    _ = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to open artifacts folder '{path}': {ex.Message}");
                }
            };
        };
    }

    /// <summary>
    /// While pinned, any extent growth (new content materializing - a render
    /// batch, the action row, sources, memory pills) snaps the offset to the
    /// new bottom instead of leaving it wherever the last programmatic scroll
    /// landed. A user-driven scroll away from the bottom unpins; scrolling
    /// back within <see cref="BottomPinThreshold"/> px re-pins - this keeps
    /// the existing "don't fight the user mid-stream" behavior while fixing
    /// the case where the true final extent was never actually reached.
    /// </summary>
    private void OnMessagesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll) return;

        if (e.ExtentDelta.Y != 0 && _pinnedToBottom)
        {
            SnapToBottomIfPinned(force: true);
            return;
        }

        _pinnedToBottom = DistanceFromBottom(scroll) <= BottomPinThreshold;
    }

    private void SnapToBottomIfPinned(bool force = false)
    {
        if (!force && !_pinnedToBottom) return;
        if (this.FindControl<ScrollViewer>("MessagesScroll") is not { } scroll) return;
        var maxOffsetY = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, maxOffsetY);
    }

    private static double DistanceFromBottom(ScrollViewer scroll) =>
        Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y);

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
