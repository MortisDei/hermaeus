using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        WheelScrollHelper.Handle(PageScroller, e);

    private static void OnUiAppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not UiSettingsViewModel ui) return;
        if (e.PropertyName is not (nameof(UiSettingsViewModel.HeadingFontFamily)
            or nameof(UiSettingsViewModel.BodyFontFamily)
            or nameof(UiSettingsViewModel.MonoFontFamily)
            or nameof(UiSettingsViewModel.FontSize))) return;

        AppFontService.Apply(ui.HeadingFontFamily, ui.BodyFontFamily, ui.MonoFontFamily, ui.FontSize);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        // r21: live-preview font/size changes as the user types, mirroring
        // what AppFontService applies from persisted settings at startup.
        // -= before += keeps this idempotent if DataContext is set again.
        vm.Ui.PropertyChanged -= OnUiAppearanceChanged;
        vm.Ui.PropertyChanged += OnUiAppearanceChanged;

        vm.Data.RequestDataRootPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose Hermaeus data folder");
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
                "Choose Hermaeus backup zip",
                [
                    new FilePickerFileType("Hermaeus backup") { Patterns = ["*.zip"] },
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
