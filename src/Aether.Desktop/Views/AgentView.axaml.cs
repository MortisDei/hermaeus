using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;

namespace Aether.Desktop.Views;

public partial class AgentView : UserControl
{
    public AgentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not Aether.ViewModels.AgentViewModel vm) return;
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
    }
}
