using Avalonia;
using Avalonia.Controls;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

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
            vm.Agent.RequestRewindConfirmation = async plan =>
            {
                var dialog = new TaskRewindConfirmationDialog();
                dialog.SetPlan(plan);
                return await dialog.ShowDialog<bool>(this);
            };
            vm.RequestDeleteConversationConfirmation = async item =>
            {
                var dialog = new ConfirmActionDialog(
                    "Delete conversation",
                    $"Permanently delete \"{item.Title}\"? This cannot be undone.");
                return await dialog.ShowDialog<bool>(this);
            };
            vm.Projects.RequestOpenEditor = () =>
            {
                var editor = new ProjectEditorWindow { DataContext = vm.Projects };
                _ = editor.ShowDialog(this);
            };
            vm.Projects.RequestConfirmDelete = async name =>
            {
                var dialog = new ConfirmActionDialog(
                    "Delete project",
                    $"Delete \"{name}\"? This removes only the project label and its defaults. " +
                    "Every conversation, agent task, RAG dataset and memory that pointed at it is " +
                    "kept exactly as it is - nothing is deleted.");
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
