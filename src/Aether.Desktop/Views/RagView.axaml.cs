using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Aether.ViewModels;
using System.Globalization;

namespace Aether.Desktop.Views;

public partial class RagView : UserControl
{
    public static readonly IValueConverter IsZero    = new IsZeroConverter();
    public static readonly IValueConverter IsNonZero = new IsNonZeroConverter();
    public static readonly IValueConverter NotEmpty  = new NotEmptyConverter();

    private RagViewModel? _vm;

    public RagView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext as RagViewModel;
            if (_vm is null) return;

            _vm.ScrollToBottom += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (this.FindControl<ScrollViewer>("ContentScroller") is { } sv)
                        sv.ScrollToEnd();
                }, DispatcherPriority.Background);

            _vm.RequestCopyToClipboard = async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    await cb.SetTextAsync(text);
            };
        };
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
