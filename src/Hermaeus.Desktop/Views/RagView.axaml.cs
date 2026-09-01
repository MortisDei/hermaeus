using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

    public RagView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null && _scrollHandler is not null)
            _vm.ScrollToBottom -= _scrollHandler;
        if (_contentScroller is not null && _scrollChangedHandler is not null)
            _contentScroller.ScrollChanged -= _scrollChangedHandler;
        if (_vm is not null)
        {
            _vm.RequestCopyToClipboard = null;
            _vm.RequestDeleteDatasetConfirmation = null;
            _vm.RequestRemoveMissingSourcesConfirmation = null;
            _vm.RequestConfirmWatchedRefresh = null;
            _vm.RequestWatchedFolderPicker = null;
        }

        _vm = DataContext as RagViewModel;
        _scrollHandler = null;
        _scrollChangedHandler = null;
        _contentScroller = null;
        _pinnedToBottom = true;
        if (_vm is null)
        {
            return;
        }

        _scrollHandler = (_, _) =>
        {
            _pinnedToBottom = true;
            Dispatcher.UIThread.Post(
                () => SnapToBottomIfPinned(force: true),
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
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb)
                return false;
            try { await cb.SetTextAsync(text); return true; }
            catch { return false; }
        };

        _vm.RequestDeleteDatasetConfirmation = async item =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new DeleteDatasetDialog(item.Name, item.ChunkCount);
            var result = await dialog.ShowDialog<bool>(owner);
            return result;
        };

        _vm.RequestRemoveMissingSourcesConfirmation = async item =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new RemoveMissingSourcesDialog(item.Name, item.MissingSourcePaths);
            var result = await dialog.ShowDialog<bool>(owner);
            return result;
        };

        _vm.RequestConfirmWatchedRefresh = async (item, plan) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var warning = plan.MissingIsOverHalf(item.SourceCount)
                ? "\n\nMore than half this dataset's sources are missing - this usually means an unmounted drive or a wrong folder, not an intended purge. Missing files are never removed by this action."
                : string.Empty;
            var dialog = new ConfirmActionDialog(
                "Refresh watched sources",
                $"Ingest {plan.NewFiles.Count} new and {plan.ChangedFiles.Count} changed file(s) into '{item.Name}'? " +
                $"{plan.MissingFiles.Count} missing file(s) are left untouched - remove them separately with \"Remove missing\" if intended.{warning}");
            return await dialog.ShowDialog<bool>(owner);
        };

        _vm.RequestWatchedFolderPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return null;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose folder to watch",
                AllowMultiple = false
            });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
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
        if (e.Key == Key.Return && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            if (_vm.QueryCommand.CanExecute(null))
                _vm.QueryCommand.Execute(null);
        }
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
