using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class ProjectEditorWindow : Window
{
    public ProjectEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ProjectViewModel vm) return;

        vm.RequestFolderRootPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose project folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
                vm.SetEditingFolderRoot(folders[0].Path.LocalPath);
        };

        vm.RequestCloseEditor = Close;
    }
}
