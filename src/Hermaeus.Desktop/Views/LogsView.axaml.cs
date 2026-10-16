using Avalonia.Controls;
using Avalonia.Input.Platform;
using Hermaeus.ViewModels;
using System.Diagnostics;

namespace Hermaeus.Desktop.Views;

public partial class LogsView : UserControl
{
    private LogsViewModel? _wiredViewModel;
    private Func<string, Task<bool>>? _copy;

    public LogsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => WireViewModel();
        Unloaded += (_, _) => UnwireViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnwireViewModel();
        WireViewModel();
    }

    private void UnwireViewModel()
    {
        if (_wiredViewModel is { } vm && vm.RequestCopyToClipboard == _copy)
            vm.RequestCopyToClipboard = null;
        _wiredViewModel = null;
        _copy = null;
    }

    private void WireViewModel()
    {
        if (!IsLoaded || ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not LogsViewModel vm) return;
        _wiredViewModel = vm;
        Func<string, Task<bool>>? copy = null;
        bool OwnsCallback() => ReferenceEquals(_wiredViewModel, vm) && vm.RequestCopyToClipboard == copy;
        copy = async text =>
        {
            if (!OwnsCallback()) return false;
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is null) return false;
            try { await top.Clipboard.SetTextAsync(text); return OwnsCallback(); }
            catch { return false; }
        };
        _copy = copy;
        vm.RequestCopyToClipboard = copy;
        vm.RequestOpenFolder = path =>
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows()
                        ? "explorer"
                        : OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                    UseShellExecute = true
                };
                psi.ArgumentList.Add(path);
                _ = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to open log folder '{path}': {ex.Message}");
            }
        };
    }
}
