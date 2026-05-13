using Avalonia.Controls;
using Aether.ViewModels;
using System.Diagnostics;

namespace Aether.Desktop.Views;

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
            if (top?.Clipboard is not null)
                await top.Clipboard.SetTextAsync(text);
        };
        vm.RequestOpenFolder = path =>
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows()
                    ? "explorer"
                    : OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                UseShellExecute = true
            };
            psi.ArgumentList.Add(path);
            Process.Start(psi);
        };
    }
}