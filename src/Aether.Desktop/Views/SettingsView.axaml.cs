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
        SizeChanged += (_, _) => UpdateCardWidths();
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

        vm.RequestBackupDirectoryPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose backup folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
                vm.BackupDirectory = folders[0].Path.LocalPath;
        };

        vm.RequestRestoreBackupPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose Aether backup zip",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Aether backup") { Patterns = ["*.zip"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]
            });

            if (files.Count > 0)
                vm.RestoreBackupPath = files[0].Path.LocalPath;
        };

        vm.RequestTtsScriptPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose xtts_api_server.py",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Python script") { Patterns = ["*.py"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]
            });

            if (files.Count > 0)
                vm.TtsScriptPath = files[0].Path.LocalPath;
        };

        vm.RequestTtsPythonPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose XTTS venv Python",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Python") { Patterns = OperatingSystem.IsWindows() ? ["python.exe"] : ["python"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]
            });

            if (files.Count > 0)
                vm.TtsPythonPath = files[0].Path.LocalPath;
        };

        vm.RequestTtsOutputPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose XTTS output folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
                vm.TtsOutputDirectory = folders[0].Path.LocalPath;
        };

        vm.RequestTtsModelDirectoryPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose XTTS v2 model folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
                vm.TtsModelDirectory = folders[0].Path.LocalPath;
        };

        vm.RequestTtsVoiceDirectoryPicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose XTTS voice sample folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
                vm.TtsVoiceDirectory = folders[0].Path.LocalPath;
        };

        vm.RequestTtsVoiceSamplePicker = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import XTTS voice sample",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Audio sample") { Patterns = ["*.wav", "*.mp3", "*.flac"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]
            });

            if (files.Count > 0)
                await vm.ImportTtsVoiceSampleAsync(files[0].Path.LocalPath);
        };

        vm.RequestCopyToClipboard = async text =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is not null)
                await top.Clipboard.SetTextAsync(text);
        };
    }

    private void UpdateCardWidths()
    {
        var available = Math.Max(320, Bounds.Width - 96);
        var cardWidth = available < 900 ? available : Math.Min(464, (available - 20) / 2);
        foreach (var child in SettingsCards.Children.OfType<Border>())
            child.Width = Grid.GetColumnSpan(child) > 1
                ? available
                : cardWidth;
    }
}
