using Avalonia.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Aether.Core.Models;
using Aether.Services;
using Aether.ViewModels;
using System.Globalization;

namespace Aether.Desktop.Views;

public partial class MainWindow : Window
{
    public static readonly IValueConverter AnyRunning = new AnyRunningConverter();
    public DesktopIntegrationService? DesktopIntegration { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Agent.RequestDraftPatchPreview = ShowDraftPatchPreviewAsync;
            if (vm.Settings.StartMinimized)
                WindowState = WindowState.Minimized;
        }
    }

    private async Task<bool> ShowDraftPatchPreviewAsync(DraftPatchPreviewRequest request)
    {
        var viewModel = new DraftPatchDiffViewModel(new PatchDiffService());
        await viewModel.LoadAsync(request.RelativePath, request.OldContent, request.NewContent);
        var modal = new DraftPatchPreviewModalView
        {
            DataContext = viewModel
        };
        return await modal.ShowDialog<bool>(this);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DesktopIntegration?.ShouldCancelCloseForTray() == true)
        {
            e.Cancel = true;
            Hide();
            return;
        }

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
        => AvaloniaProperty.UnsetValue;
}
