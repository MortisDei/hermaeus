using Avalonia.Controls;
using Hermaeus.ViewModels;
using System.Diagnostics;

namespace Hermaeus.Desktop.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not LogsViewModel vm) return;
        vm.RequestCopyToClipboard = async text =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is null) return false;
            try { await top.Clipboard.SetTextAsync(text); return true; }
            catch { return false; }
        };
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
