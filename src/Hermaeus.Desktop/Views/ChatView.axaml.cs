using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class ChatView : UserControl
{
    private ChatViewModel? _vm;
    private EventHandler? _scrollHandler;
    private EventHandler<ScrollChangedEventArgs>? _scrollChangedHandler;
    private Action<string>? _micTranscriptHandler;
    private Action? _releaseCallbacks;
    private Func<bool>? _ownsCallbacks;
    private long _callbackGeneration;
    // r19 6.3: content (action row, sources, memory pills, the incremental
    // MarkdownViewer re-render timer) keeps materializing AFTER the VM's last
    // ScrollToBottom raise, so a single programmatic scroll always lands
    // short of the true final extent. Pinning re-snaps to the bottom on every
    // extent growth while pinned, and un-pins the instant the user scrolls
    // away, so their position is never fought mid-stream.
    private bool _pinnedToBottom = true;

    private async void OnTelemetryClosed(object? sender, EventArgs e)
    {
        if (_vm is { } vm && _ownsCallbacks?.Invoke() == true)
            await vm.CloseTelemetryCommand.ExecuteAsync(null);
    }

    public ChatView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ScheduleInputFocus();
        DetachedFromVisualTree += (_, _) => UnwireViewModel();

        // Ctrl+V (or right-click Paste) with an image on the clipboard attaches it the
        // same way a dragged-in or browsed-for image file would, instead of doing
        // nothing - TextBox only ever knew how to paste text, so a copied screenshot
        // silently went nowhere. Attached once here since InputBox itself never
        // changes, only its DataContext.
        if (this.FindControl<TextBox>("InputBox") is { } inputBox)
        {
            inputBox.AddHandler(TextBox.PastingFromClipboardEvent, OnInputPastingFromClipboard);
            // r29 doc 01 1.4: tunnelling, so Enter reaches OnInputKeyDown before
            // TextBox's own class handler inserts a newline and marks it handled.
            inputBox.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        }

        DataContextChanged += (_, _) => { UnwireViewModel(); WireViewModel(); };
        Loaded += (_, _) => WireViewModel();
        Unloaded += (_, _) => UnwireViewModel();
    }

    private void UnwireViewModel()
    {
        _callbackGeneration++;
        if (_vm is { } previous)
        {
            // An obsolete overlapping visual must not stop the active view's telemetry.
            if (_ownsCallbacks?.Invoke() == true)
                _ = previous.CloseTelemetryCommand.ExecuteAsync(null);
            if (_scrollHandler is not null) previous.ScrollToBottom -= _scrollHandler;
            if (_micTranscriptHandler is not null) previous.ChatMic.TranscriptReady -= _micTranscriptHandler;
        }
        if (_scrollChangedHandler is not null && this.FindControl<ScrollViewer>("MessagesScroll") is { } scroll)
            scroll.ScrollChanged -= _scrollChangedHandler;
        if (this.FindControl<Controls.MicButton>("ChatMicButton") is { } mic)
            mic.ViewModel = null;
        _releaseCallbacks?.Invoke();
        _releaseCallbacks = null;
        _ownsCallbacks = null;
        _vm = null;
        _scrollHandler = null;
        _scrollChangedHandler = null;
        _micTranscriptHandler = null;
    }

    private void WireViewModel()
    {
        if (!IsLoaded || !this.IsAttachedToVisualTree()
            || ReferenceEquals(_vm, DataContext) || DataContext is not ChatViewModel vm) return;
        var generation = _callbackGeneration;
        _vm = vm;

        if (this.FindControl<Controls.MicButton>("ChatMicButton") is { } micButton)
            micButton.ViewModel = vm.ChatMic;
        _micTranscriptHandler = text => { if (OwnsCallbacks(generation)) InsertDictatedTextAtCursor(text); };
        vm.ChatMic.TranscriptReady += _micTranscriptHandler;

        _pinnedToBottom = true;
        _scrollHandler = (_, _) =>
        {
            if (!OwnsCallbacks(generation)) return;
            // Sending (or resuming) a message re-pins even if the user had
            // scrolled up to read earlier history.
            _pinnedToBottom = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (OwnsCallbacks(generation)) SnapToBottomIfPinned(force: true);
            }, DispatcherPriority.Background);
        };
        vm.ScrollToBottom += _scrollHandler;

        if (this.FindControl<ScrollViewer>("MessagesScroll") is { } scroll)
        {
            _scrollChangedHandler = OnMessagesScrollChanged;
            scroll.ScrollChanged += _scrollChangedHandler;
        }

        vm.RequestCopyToClipboard = async text =>
        {
            if (!OwnsCallbacks(generation)) return false;
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb)
                return false;
            try { await cb.SetTextAsync(text); return OwnsCallbacks(generation); }
            catch { return false; }
        };

        vm.RequestInputFocus = () => { if (OwnsCallbacks(generation)) ScheduleInputFocus(); };

        // A ContentControl recreates ChatView on panel navigation. On a
        // cold start its DataContext can arrive before the input is ready
        // to receive keyboard focus; returning to Chat creates a second
        // view and happened to hide that race. Schedule focus after the
        // layout turn that attached this instance instead.
        ScheduleInputFocus();

        vm.RequestContextFilePicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var textAndCodePatterns = SupportedTextFileTypes.PickerPatterns.ToArray();
            var documentPatterns = new[] { "*.docx", "*.pdf" };
            var imagePatterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" };

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Attach files as chat context",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    // The OS dialog defaults to whichever filter is listed first, so
                    // every supported type needs to be in that first entry - otherwise
                    // e.g. images are invisible until the user manually switches the
                    // dropdown, which read as "attaching images doesn't work".
                    new FilePickerFileType("All supported files")
                    {
                        Patterns = [.. textAndCodePatterns, .. documentPatterns, .. imagePatterns]
                    },
                    new FilePickerFileType("Text and code files") { Patterns = textAndCodePatterns },
                    new FilePickerFileType("Documents") { Patterns = documentPatterns },
                    new FilePickerFileType("Images") { Patterns = imagePatterns },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]
            });

            if (files.Count > 0 && OwnsCallbacks(generation))
                await vm.AddContextFilesAsync(files.Select(f => f.Path.LocalPath));
        };

        vm.RequestConversationExportPath = async format =>
        {
            if (!OwnsCallbacks(generation)) return null;
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
            return OwnsCallbacks(generation) ? file?.Path.LocalPath : null;
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
        CaptureCallbacks(vm);
    }

    private bool OwnsCallbacks(long generation) =>
        generation == _callbackGeneration && _ownsCallbacks?.Invoke() == true;

    private void CaptureCallbacks(ChatViewModel vm)
    {
        var copy = vm.RequestCopyToClipboard;
        var focus = vm.RequestInputFocus;
        var context = vm.RequestContextFilePicker;
        var export = vm.RequestConversationExportPath;
        _ownsCallbacks = () => ReferenceEquals(_vm, vm) && vm.RequestCopyToClipboard == copy;
        _releaseCallbacks = () =>
        {
            if (vm.RequestCopyToClipboard == copy) vm.RequestCopyToClipboard = null;
            if (vm.RequestInputFocus == focus) vm.RequestInputFocus = null;
            if (vm.RequestContextFilePicker == context) vm.RequestContextFilePicker = null;
            if (vm.RequestConversationExportPath == export) vm.RequestConversationExportPath = null;
        };
    }

    private void ScheduleInputFocus()
    {
        var generation = _callbackGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (OwnsCallbacks(generation)
                && this.FindControl<TextBox>("InputBox") is { IsEnabled: true } input)
                input.Focus();
        }, DispatcherPriority.Background);
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

        var transition = ChatScrollPinState.Apply(
            _pinnedToBottom,
            scroll.Extent.Height,
            scroll.Viewport.Height,
            scroll.Offset.Y,
            e.ExtentDelta.Y,
            e.OffsetDelta.Y);
        _pinnedToBottom = transition.IsPinned;
        if (transition.ShouldSnap)
            SnapToBottomIfPinned(force: true);
    }

    private void SnapToBottomIfPinned(bool force = false)
    {
        if (!force && !_pinnedToBottom) return;
        if (this.FindControl<ScrollViewer>("MessagesScroll") is not { } scroll) return;
        var maxOffsetY = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, maxOffsetY);
    }

    /// <summary>
    /// r29 doc 01 1.4: AcceptsReturn is true always and this handler owns both
    /// Enter combinations, send and newline. It used to own only send and leave
    /// newline to AcceptsReturn, which meant that with Ui.CtrlEnterToSend false
    /// (the default) AcceptsReturn was false and nothing produced a newline at
    /// all. Registered with RoutingStrategies.Tunnel so the app sees Enter
    /// before TextBox's own class handler consumes it - the mechanism the
    /// original 0.24.x fix worked around rather than used.
    /// </summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || _vm.IsGenerating) return;

        var modifiers = ChatInputModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= ChatInputModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= ChatInputModifiers.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= ChatInputModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= ChatInputModifiers.Meta;

        var action = ChatInputKeys.Resolve(
            e.Key is Key.Return or Key.Enter,
            modifiers,
            _vm.Settings.Settings.Ui.CtrlEnterToSend);

        switch (action)
        {
            case ChatInputKeyAction.Send:
                e.Handled = true;
                if (_vm.SendCommand.CanExecute(null))
                    _vm.SendCommand.Execute(null);
                break;
            case ChatInputKeyAction.Newline:
                e.Handled = true;
                InsertAtCursor(Environment.NewLine);
                break;
        }
    }

    /// <summary>
    /// Inserts literal text at the caret and leaves the caret after it. Shares
    /// the caret arithmetic with <see cref="InsertDictatedTextAtCursor"/>, which
    /// adds its own leading-space rule on top.
    /// </summary>
    private void InsertAtCursor(string text)
    {
        if (_vm is null || this.FindControl<TextBox>("InputBox") is not { } inputBox) return;
        var current = _vm.InputText;
        var caret = Math.Clamp(inputBox.CaretIndex, 0, current.Length);
        _vm.InputText = current.Insert(caret, text);
        inputBox.CaretIndex = caret + text.Length;
    }

    /// <summary>r24 doc 05 5.4: dictation inserts at the cursor for editing - it never
    /// sends by itself. A single space is inserted before the transcript when the
    /// cursor sits right after a non-space character, so two dictated phrases (or a
    /// dictated phrase after typed text) don't run together with no separator.</summary>
    private void InsertDictatedTextAtCursor(string text)
    {
        var generation = _callbackGeneration;
        var vm = _vm;
        Dispatcher.UIThread.Post(() =>
        {
            if (!OwnsCallbacks(generation) || vm is null
                || this.FindControl<TextBox>("InputBox") is not { } inputBox) return;

            var current = vm.InputText;
            var caret = Math.Clamp(inputBox.CaretIndex, 0, current.Length);
            var needsLeadingSpace = caret > 0 && !char.IsWhiteSpace(current[caret - 1]);
            var insertion = needsLeadingSpace ? " " + text : text;

            vm.InputText = current.Insert(caret, insertion);
            inputBox.CaretIndex = caret + insertion.Length;
            inputBox.Focus();
        });
    }

    /// <summary>
    /// TextBox's own paste handling only ever knew how to insert text, so a copied
    /// screenshot silently did nothing. Handled always (Handled = true up front,
    /// before any await) because Avalonia checks Handled synchronously right after
    /// raising this event - an async handler that only sets it after the first
    /// await would already be too late, letting the default (text-only) paste run
    /// regardless of what this handler later decides. When there's no image on the
    /// clipboard, or reading it fails, the suppressed default is replayed manually
    /// via SelectedText so plain text paste keeps working exactly as before.
    /// </summary>
    private async void OnInputPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not TextBox textBox) return;
        var vm = _vm;
        var generation = _callbackGeneration;
        if (!OwnsCallbacks(generation)) return;
        e.Handled = true;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        try
        {
            if (await clipboard.TryGetBitmapAsync() is { } bitmap)
            {
                if (!OwnsCallbacks(generation))
                {
                    bitmap.Dispose();
                    return;
                }
                var tempPath = Path.Combine(Path.GetTempPath(), $"hermaeus-paste-{Guid.NewGuid():N}.png");
                try
                {
                    using (bitmap)
                        bitmap.Save(tempPath, PngBitmapEncoderOptions.Default);
                    await vm.AddContextFilesAsync([tempPath]);
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }
                return;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Clipboard image paste failed: {ex.Message}");
        }

        var text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text) && OwnsCallbacks(generation))
            textBox.SelectedText = text;
    }

    /// <summary>r21 1.2: picking a dataset (or None) from the Knowledge flyout
    /// closes it - a Button click inside a Flyout does not auto-dismiss it.</summary>
    private void OnRagDatasetSelected(object? sender, RoutedEventArgs e) =>
        (this.FindControl<Button>("RagPickerButton"))?.Flyout?.Hide();

    private void OnContextDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
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
        if (_vm is null || !e.DataTransfer.Contains(DataFormat.File)) return;
        var files = e.DataTransfer.TryGetFiles();
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
