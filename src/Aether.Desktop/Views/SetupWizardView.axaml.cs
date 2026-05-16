using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Aether.ViewModels;

namespace Aether.Desktop.Views;

public partial class SetupWizardView : UserControl
{
    public SetupWizardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SetupWizardViewModel vm) return;
        vm.RequestDataRootPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose Aether data folder",
                AllowMultiple = false
            });
            if (folders.Count > 0)
                vm.DataRootDirectory = folders[0].Path.LocalPath;
        };

        vm.RequestLocalAiAssetsRootPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose local AI assets folder",
                AllowMultiple = false
            });
            if (folders.Count > 0)
                vm.LocalAiAssetsRoot = folders[0].Path.LocalPath;
        };

        vm.RequestModelFolderPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose GGUF model",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("GGUF model") { Patterns = ["*.gguf", "*.bin"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]
            });
            if (files.Count > 0)
                vm.ModelFolder = files[0].Path.LocalPath;
        };
    }
}
