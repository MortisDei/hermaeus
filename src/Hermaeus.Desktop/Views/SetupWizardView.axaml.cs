using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class SetupWizardView : UserControl
{
    private SetupWizardViewModel? _wiredViewModel;
    private Action? _releaseCallbacks;
    private Func<bool>? _ownsCallbacks;
    private long _callbackGeneration;

    public SetupWizardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => WireViewModel();
        Unloaded += (_, _) => UnwireViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnwireViewModel();
        WireViewModel();
    }

    private void UnwireViewModel()
    {
        _callbackGeneration++;
        _releaseCallbacks?.Invoke();
        _releaseCallbacks = null;
        _ownsCallbacks = null;
        _wiredViewModel = null;
    }

    private bool OwnsCallbacks(long generation) =>
        generation == _callbackGeneration && _ownsCallbacks?.Invoke() == true;

    private void WireViewModel()
    {
        if (!IsLoaded || ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not SetupWizardViewModel vm) return;
        _wiredViewModel = vm;
        var generation = _callbackGeneration;
        vm.RequestDataRootPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose Hermaeus data folder",
                AllowMultiple = false
            });
            if (folders.Count > 0 && OwnsCallbacks(generation))
                vm.DataRootDirectory = folders[0].Path.LocalPath;
        };

        vm.RequestLocalAiAssetsRootPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose local AI assets folder",
                AllowMultiple = false
            });
            if (folders.Count > 0 && OwnsCallbacks(generation))
                vm.LocalAiAssetsRoot = folders[0].Path.LocalPath;
        };

        vm.RequestModelFolderPicker = async () =>
        {
            if (!OwnsCallbacks(generation)) return;
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
            if (files.Count > 0 && OwnsCallbacks(generation))
                vm.ModelFolder = files[0].Path.LocalPath;
        };
        var ownedRequestDataRootPicker = vm.RequestDataRootPicker;
        var ownedRequestLocalAiAssetsRootPicker = vm.RequestLocalAiAssetsRootPicker;
        var ownedRequestModelFolderPicker = vm.RequestModelFolderPicker;
        _ownsCallbacks = () => ReferenceEquals(_wiredViewModel, vm)
            && vm.RequestDataRootPicker == ownedRequestDataRootPicker
            && vm.RequestLocalAiAssetsRootPicker == ownedRequestLocalAiAssetsRootPicker
            && vm.RequestModelFolderPicker == ownedRequestModelFolderPicker;
        _releaseCallbacks = () =>
        {
            if (vm.RequestDataRootPicker == ownedRequestDataRootPicker) vm.RequestDataRootPicker = null;
            if (vm.RequestLocalAiAssetsRootPicker == ownedRequestLocalAiAssetsRootPicker) vm.RequestLocalAiAssetsRootPicker = null;
            if (vm.RequestModelFolderPicker == ownedRequestModelFolderPicker) vm.RequestModelFolderPicker = null;
        };
    }
}
