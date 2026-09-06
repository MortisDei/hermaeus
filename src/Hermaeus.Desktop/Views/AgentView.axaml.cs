using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;

namespace Hermaeus.Desktop.Views;

public partial class AgentView : UserControl
{
    public AgentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not Hermaeus.ViewModels.AgentViewModel vm) return;
        vm.RequestWorkspaceRootPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose workspace folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
                vm.WorkspaceRoot = folders[0].Path.LocalPath;
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
                Console.Error.WriteLine($"Failed to open '{path}': {ex.Message}");
            }
        };
        vm.RequestDeleteTaskConfirmation = async item =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new ConfirmActionDialog(
                "Delete agent run",
                $"Permanently delete the historical run '{item.Goal}' and its persisted sub-tasks? This removes its transcript, trace, log, and report.");
            return await dialog.ShowDialog<bool>(owner);
        };
    }
}
