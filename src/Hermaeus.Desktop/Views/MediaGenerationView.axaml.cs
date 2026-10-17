using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class MediaGenerationView : UserControl
{
    private MediaGenerationViewModel? _vm;
    private Action? _releaseCallbacks;
    private long _version;
    private Bitmap? _preview;

    public MediaGenerationView()
    {
        InitializeComponent();
        Loaded += (_, _) => Wire();
        Unloaded += (_, _) => Unwire();
        DataContextChanged += (_, _) => { Unwire(); Wire(); };
    }

    private bool Owns(MediaGenerationViewModel vm, long version) =>
        IsLoaded && version == _version && ReferenceEquals(_vm, vm) && ReferenceEquals(DataContext, vm);

    private void Wire()
    {
        if (!IsLoaded || _vm is not null || DataContext is not MediaGenerationViewModel vm) return;
        _vm = vm;
        var version = ++_version;
        Func<GeneratedMedia, Task<string?>> save = async media =>
        {
            if (!Owns(vm, version) || TopLevel.GetTopLevel(this) is not { } top) return null;
            var extension = Path.GetExtension(media.SuggestedFileName);
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export generated media", SuggestedFileName = media.SuggestedFileName,
                DefaultExtension = extension.TrimStart('.'), ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(media.MediaType) { Patterns = ["*" + extension] }]
            });
            return Owns(vm, version) && file?.Path.IsFile == true ? file.Path.LocalPath : null;
        };
        Func<CancellationToken, Task<string?>> load = async ct =>
        {
            if (!Owns(vm, version) || TopLevel.GetTopLevel(this) is not { } top) return null;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load a trusted ComfyUI API workflow", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("API workflow JSON") { Patterns = ["*.json"] }]
            });
            if (!Owns(vm, version) || files.Count != 1 || !files[0].Path.IsFile) return null;
            ct.ThrowIfCancellationRequested();
            var value = await MediaFileService.ReadWorkflowAsync(files[0].Path.LocalPath, ct);
            return Owns(vm, version) ? value : null;
        };
        vm.RequestExportPath = save;
        vm.RequestWorkflow = load;
        vm.PropertyChanged += OnPropertyChanged;
        _releaseCallbacks = () =>
        {
            vm.PropertyChanged -= OnPropertyChanged;
            if (vm.RequestExportPath == save) vm.RequestExportPath = null;
            if (vm.RequestWorkflow == load) vm.RequestWorkflow = null;
        };
        UpdatePreview();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MediaGenerationViewModel.SelectedOutput)) UpdatePreview();
    }

    private void UpdatePreview()
    {
        PreviewImage.Source = null;
        _preview?.Dispose();
        _preview = null;
        PreviewStatus.Text = string.Empty;
        if (_vm?.SelectedOutput is not { } media) return;
        if (!media.MediaType.StartsWith("image/", StringComparison.Ordinal))
        { PreviewStatus.Text = "Export this file to open it in your preferred audio or video player."; return; }
        try
        {
            using var input = new MemoryStream(media.Data, writable: false);
            using var codec = SkiaSharp.SKCodec.Create(input);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0
                || codec.Info.Width > 16_384 || codec.Info.Height > 16_384
                || (long)codec.Info.Width * codec.Info.Height > 32_000_000)
            { PreviewStatus.Text = "Image preview unavailable or exceeds the preview dimension budget. The file can still be exported."; return; }
            input.Position = 0;
            _preview = codec.Info.Width >= codec.Info.Height
                ? Bitmap.DecodeToWidth(input, Math.Min(codec.Info.Width, 1024))
                : Bitmap.DecodeToHeight(input, Math.Min(codec.Info.Height, 1024));
            PreviewImage.Source = _preview;
        }
        catch (Exception)
        { PreviewStatus.Text = "The image could not be previewed. You can still export the original bytes."; }
    }

    private void Unwire()
    {
        ++_version;
        _releaseCallbacks?.Invoke();
        _releaseCallbacks = null;
        _vm = null;
        PreviewImage.Source = null;
        _preview?.Dispose();
        _preview = null;
    }
}
