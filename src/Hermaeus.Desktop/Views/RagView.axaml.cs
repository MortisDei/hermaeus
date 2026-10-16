using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hermaeus.ViewModels;
using System.Globalization;

namespace Hermaeus.Desktop.Views;

public partial class RagView : UserControl
{
    public static readonly IValueConverter IsZero    = new IsZeroConverter();
    public static readonly IValueConverter IsNonZero = new IsNonZeroConverter();
    public static readonly IValueConverter NotEmpty  = new NotEmptyConverter();

    private RagViewModel? _vm;
    private EventHandler? _scrollHandler;
    private EventHandler<ScrollChangedEventArgs>? _scrollChangedHandler;
    private ScrollViewer? _contentScroller;
    private bool _pinnedToBottom = true;
    private Action? _releaseCallbacks;
    private Func<bool>? _ownsCallbacks;
    private long _callbackGeneration;

    public RagView()
    {
        InitializeComponent();
        if (this.FindControl<TextBox>("QuestionBox") is { } questionBox)
            questionBox.AddHandler(InputElement.KeyDownEvent, OnQuestionKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => WireViewModel();
        Unloaded += (_, _) => UnwireViewModel();
        DetachedFromVisualTree += (_, _) => UnwireViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnwireViewModel();
        WireViewModel();
    }

    private void UnwireViewModel()
    {
        _callbackGeneration++;
        if (_vm is not null && _scrollHandler is not null)
            _vm.ScrollToBottom -= _scrollHandler;
        if (_contentScroller is not null && _scrollChangedHandler is not null)
            _contentScroller.ScrollChanged -= _scrollChangedHandler;
        _releaseCallbacks?.Invoke();
        _releaseCallbacks = null;
        _ownsCallbacks = null;

        _vm = null;
        _scrollHandler = null;
        _scrollChangedHandler = null;
        _contentScroller = null;
        _pinnedToBottom = true;
    }

    private void WireViewModel()
    {
        if (!IsLoaded || !this.IsAttachedToVisualTree() || ReferenceEquals(_vm, DataContext)) return;
        _vm = DataContext as RagViewModel;
        if (_vm is null)
        {
            return;
        }
        var vm = _vm;
        var generation = _callbackGeneration;

        _scrollHandler = (_, _) =>
        {
            if (!OwnsCallbacks(generation)) return;
            _pinnedToBottom = true;
            Dispatcher.UIThread.Post(
                () => { if (OwnsCallbacks(generation)) SnapToBottomIfPinned(force: true); },
                DispatcherPriority.Background);
        };
        _vm.ScrollToBottom += _scrollHandler;

        if (this.FindControl<ScrollViewer>("ContentScroller") is { } scroll)
        {
            _contentScroller = scroll;
            _scrollChangedHandler = OnContentScrollChanged;
            scroll.ScrollChanged += _scrollChangedHandler;
        }

        _vm.RequestCopyToClipboard = async text =>
        {
            if (!OwnsCallbacks(generation)) return false;
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb)
                return false;
            try { await cb.SetTextAsync(text); return OwnsCallbacks(generation); }
            catch { return false; }
        };

        _vm.RequestDeleteDatasetConfirmation = async item =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new DeleteDatasetDialog(item.Name, item.ChunkCount);
            var result = await dialog.ShowDialog<bool>(owner);
            return result && OwnsCallbacks(generation);
        };

        _vm.RequestRemoveMissingSourcesConfirmation = async item =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new RemoveMissingSourcesDialog(item.Name, item.MissingSourcePaths);
            var result = await dialog.ShowDialog<bool>(owner);
            return result && OwnsCallbacks(generation);
        };

        _vm.RequestConfirmWatchedRefresh = async (item, plan) =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var warning = plan.MissingIsOverHalf(item.SourceCount)
                ? "\n\nMore than half this dataset's sources are missing - this usually means an unmounted drive or a wrong folder, not an intended purge. Missing files are never removed by this action."
                : string.Empty;
            var dialog = new ConfirmActionDialog(
                "Refresh watched sources",
                $"Ingest {plan.NewFiles.Count} new and {plan.ChangedFiles.Count} changed file(s) into '{item.Name}'? " +
                $"{plan.MissingFiles.Count} missing file(s) are left untouched - remove them separately with \"Remove missing\" if intended.{warning}");
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };

        _vm.RequestWatchedFolderPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return null;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return null;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose folder to watch",
                AllowMultiple = false
            });
            return folders.Count > 0 && OwnsCallbacks(generation) ? folders[0].Path.LocalPath : null;
        };
        CaptureCallbacks(vm);
    }

    private bool OwnsCallbacks(long generation) =>
        generation == _callbackGeneration && _ownsCallbacks?.Invoke() == true;

    private void CaptureCallbacks(RagViewModel vm)
    {
        var copy = vm.RequestCopyToClipboard;
        var delete = vm.RequestDeleteDatasetConfirmation;
        var remove = vm.RequestRemoveMissingSourcesConfirmation;
        var refresh = vm.RequestConfirmWatchedRefresh;
        var folder = vm.RequestWatchedFolderPicker;
        _ownsCallbacks = () => ReferenceEquals(_vm, vm) && vm.RequestCopyToClipboard == copy;
        _releaseCallbacks = () =>
        {
            if (vm.RequestCopyToClipboard == copy) vm.RequestCopyToClipboard = null;
            if (vm.RequestDeleteDatasetConfirmation == delete) vm.RequestDeleteDatasetConfirmation = null;
            if (vm.RequestRemoveMissingSourcesConfirmation == remove) vm.RequestRemoveMissingSourcesConfirmation = null;
            if (vm.RequestConfirmWatchedRefresh == refresh) vm.RequestConfirmWatchedRefresh = null;
            if (vm.RequestWatchedFolderPicker == folder) vm.RequestWatchedFolderPicker = null;
        };
    }

    private void OnContentScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll)
            return;

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
        if (!force && !_pinnedToBottom)
            return;
        if (_contentScroller is not { } scroll)
            return;

        var maxOffsetY = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, maxOffsetY);
    }

    private void OnQuestionKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || _vm.IsQuerying) return;
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
                if (_vm.QueryCommand.CanExecute(null))
                    _vm.QueryCommand.Execute(null);
                break;
            case ChatInputKeyAction.Newline:
                e.Handled = true;
                InsertAtCursor(Environment.NewLine);
                break;
        }
    }

    private void InsertAtCursor(string text)
    {
        if (_vm is null || this.FindControl<TextBox>("QuestionBox") is not { } questionBox)
            return;

        var current = _vm.QuestionText;
        var caret = Math.Clamp(questionBox.CaretIndex, 0, current.Length);
        _vm.QuestionText = current.Insert(caret, text);
        questionBox.CaretIndex = caret + text.Length;
    }

    private async void OnBrowseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select document directory", AllowMultiple = false });

        if (folders.Count > 0)
            _vm.IngestPath = folders[0].Path.LocalPath;
    }
}

public class IsZeroConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is int n && n == 0;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => AvaloniaProperty.UnsetValue;
}

public class IsNonZeroConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is int n && n > 0;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => AvaloniaProperty.UnsetValue;
}
