using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Aether.Core.Models;
using Aether.ViewModels;
using System.Globalization;

namespace Aether.Desktop.Views;

public partial class MainWindow : Window
{
    public static readonly IValueConverter AnyRunning = new AnyRunningConverter();

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel { Settings.StartMinimized: true })
            WindowState = WindowState.Minimized;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Shutdown();
    }
}

public sealed class AnyRunningConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
    {
        if (v is not AvaloniaList<ServerProcessViewModel> servers) return false;
        return servers.Any(s => s.Status == ServerStatus.Running);
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotImplementedException();
}
