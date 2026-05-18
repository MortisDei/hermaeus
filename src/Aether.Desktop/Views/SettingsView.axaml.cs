using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Aether.ViewModels;

namespace Aether.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var max = Math.Max(0, PageScroller.Extent.Height - PageScroller.Viewport.Height);
        if (max <= 0) return;

        var next = Math.Clamp(PageScroller.Offset.Y - e.Delta.Y * 56, 0, max);
        if (Math.Abs(next - PageScroller.Offset.Y) < 0.1) return;

        PageScroller.Offset = new Vector(PageScroller.Offset.X, next);
        e.Handled = true;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        vm.Data.RequestDataRootPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose Aether data folder");
            if (folders.Count > 0)
                vm.Data.DataRootDirectory = folders[0].Path.LocalPath;
        };

        vm.Data.RequestLocalAiAssetsRootPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose local AI assets folder");
            if (folders.Count > 0)
                vm.Data.LocalAiAssetsRoot = folders[0].Path.LocalPath;
        };

        vm.Data.RequestBackupDirectoryPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose backup folder");
            if (folders.Count > 0)
                vm.Data.BackupDirectory = folders[0].Path.LocalPath;
        };

        vm.Data.RequestRestoreBackupPicker = async () =>
        {
            var files = await PickFileAsync(
                "Choose Aether backup zip",
                [
                    new FilePickerFileType("Aether backup") { Patterns = ["*.zip"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0)
                vm.Data.RestoreBackupPath = files[0].Path.LocalPath;
        };

        vm.Data.RequestRestoreBackupConfirmation = async () =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new RestoreBackupConfirmationDialog();
            return await dialog.ShowDialog<bool>(owner);
        };

        vm.Tts.RequestTtsScriptPicker = async () =>
        {
            var files = await PickFileAsync(
                "Choose xtts_api_server.py",
                [
                    new FilePickerFileType("Python script") { Patterns = ["*.py"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0)
                vm.Tts.TtsScriptPath = files[0].Path.LocalPath;
        };

        vm.Tts.RequestTtsPythonPicker = async () =>
        {
            var files = await PickFileAsync(
                "Choose XTTS venv Python",
                [
                    new FilePickerFileType("Python") { Patterns = OperatingSystem.IsWindows() ? ["python.exe"] : ["python"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0)
                vm.Tts.TtsPythonPath = files[0].Path.LocalPath;
        };

        vm.Tts.RequestTtsOutputPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose XTTS output folder");
            if (folders.Count > 0)
                vm.Tts.TtsOutputDirectory = folders[0].Path.LocalPath;
        };

        vm.Tts.RequestTtsModelDirectoryPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose XTTS v2 model folder");
            if (folders.Count > 0)
                vm.Tts.TtsModelDirectory = folders[0].Path.LocalPath;
        };

        vm.Tts.RequestTtsVoiceDirectoryPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose XTTS voice sample folder");
            if (folders.Count > 0)
                vm.Tts.TtsVoiceDirectory = folders[0].Path.LocalPath;
        };

        vm.Tts.RequestTtsVoiceSamplePicker = async () =>
        {
            var files = await PickFileAsync(
                "Import XTTS voice sample",
                [
                    new FilePickerFileType("Audio sample") { Patterns = ["*.wav", "*.mp3", "*.flac"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0)
                await vm.Tts.ImportTtsVoiceSampleAsync(files[0].Path.LocalPath);
        };

        vm.LocalAiSetup.RequestCopyToClipboard = async text =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is not null)
                await top.Clipboard.SetTextAsync(text);
        };
    }

    private async Task<IReadOnlyList<IStorageFolder>> PickFolderAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        return top is null
            ? []
            : await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });
    }

    private async Task<IReadOnlyList<IStorageFile>> PickFileAsync(string title, IReadOnlyList<FilePickerFileType> filters)
    {
        var top = TopLevel.GetTopLevel(this);
        return top is null
            ? []
            : await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters
            });
    }
}
