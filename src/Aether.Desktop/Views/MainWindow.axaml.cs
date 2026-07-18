using Avalonia;
using Avalonia.Controls;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.ViewModels;

namespace Aether.Desktop.Views;

public partial class MainWindow : Window
{
    public DesktopIntegrationService? DesktopIntegration { get; set; }
    public IPatchDiffService? PatchDiffService { get; set; }

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
            vm.RequestDeleteConversationConfirmation = async item =>
            {
                var dialog = new ConfirmActionDialog(
                    "Delete conversation",
                    $"Permanently delete \"{item.Title}\"? This cannot be undone.");
                return await dialog.ShowDialog<bool>(this);
            };
            if (vm.Settings.StartMinimized)
                WindowState = WindowState.Minimized;
        }
    }

    private async Task<bool> ShowDraftPatchPreviewAsync(DraftPatchPreviewRequest request)
    {
        if (PatchDiffService is null)
            throw new InvalidOperationException("Patch diff service is not configured.");

        var viewModel = new DraftPatchDiffViewModel(PatchDiffService);
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
