using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Hermaeus.Core.Models;
using Hermaeus.ViewModels;
using System.Globalization;

namespace Hermaeus.Desktop.Views;

public partial class ServicesView : UserControl
{
    public static readonly IValueConverter StatusColor = new StatusColorConverter();
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _collectionChangedHandler;
    private ServicesViewModel? _wiredViewModel;

    public ServicesView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        WheelScrollHelper.Handle(PageScroller, e);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_wiredViewModel is not null && _collectionChangedHandler is not null)
            _wiredViewModel.Servers.CollectionChanged -= _collectionChangedHandler;

        if (DataContext is not ServicesViewModel vm) return;
        _wiredViewModel = vm;

        foreach (var srv in vm.Servers)
            WireFilePickers(srv);
        WireVoiceFilePickers(vm.Tts);
        if (vm.Stt is not null)
            WireSttFilePickers(vm.Stt);

        // Create and store handler to allow unsubscribing later
        _collectionChangedHandler = (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (ServerProcessViewModel srv in e.NewItems)
                    WireFilePickers(srv);
            }
        };
        
        vm.Servers.CollectionChanged += _collectionChangedHandler;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        // Clean up event subscriptions when view is unloaded
        if (_wiredViewModel is not null && _collectionChangedHandler is not null)
            _wiredViewModel.Servers.CollectionChanged -= _collectionChangedHandler;

        _wiredViewModel = null;
        _collectionChangedHandler = null;
    }

    private void WireFilePickers(ServerProcessViewModel srv)
    {
        srv.RequestFilePicker = async propertyName =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var isExe    = propertyName == nameof(ServerProcessViewModel.ExecutablePath);
            var isMmproj = propertyName == nameof(ServerProcessViewModel.MmprojPath);
            var isDraft  = propertyName == nameof(ServerProcessViewModel.DraftModelPath);
            var options = new FilePickerOpenOptions
            {
                Title         = isExe ? "Select llama-server executable" : isMmproj ? "Select vision projector (mmproj)" : isDraft ? "Select MTP draft model (.gguf)" : "Select model (.gguf)",
                AllowMultiple = false,
                FileTypeFilter = isExe
                    ? [new FilePickerFileType("Executable") { Patterns = ["*"] }]
                    : [new FilePickerFileType("GGUF model") { Patterns = ["*.gguf", "*.bin"] },
                       new FilePickerFileType("All files")  { Patterns = ["*"] }]
            };

            if (!isExe && !string.IsNullOrWhiteSpace(srv.SuggestedModelBrowseDirectory))
            {
                var startFolder = await top.StorageProvider.TryGetFolderFromPathAsync(srv.SuggestedModelBrowseDirectory);
                if (startFolder is not null)
                    options.SuggestedStartLocation = startFolder;
            }

            var files = await top.StorageProvider.OpenFilePickerAsync(options);
            if (files.Count == 0) return;

            var path = files[0].Path.LocalPath;
            if (isExe)         srv.ExecutablePath = path;
            else if (isMmproj) srv.MmprojPath      = path;
            else if (isDraft)  srv.DraftModelPath  = path;
            else               srv.ModelPath       = path;
        };
    }

    private void WireVoiceFilePickers(TtsSettingsViewModel tts)
    {
        tts.RequestTtsScriptPicker = async () =>
        {
            var files = await PickFileAsync(
                "Choose xtts_api_server.py",
                [
                    new FilePickerFileType("Python script") { Patterns = ["*.py"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0)
                tts.TtsScriptPath = files[0].Path.LocalPath;
        };

        tts.RequestTtsPythonPicker = async () =>
        {
            var files = await PickFileAsync(
                "Choose XTTS venv Python",
                [
                    new FilePickerFileType("Python") { Patterns = OperatingSystem.IsWindows() ? ["python.exe"] : ["python"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0)
                tts.TtsPythonPath = files[0].Path.LocalPath;
        };

        tts.RequestTtsOutputPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose XTTS output folder");
            if (folders.Count > 0)
                tts.TtsOutputDirectory = folders[0].Path.LocalPath;
        };

        tts.RequestTtsModelDirectoryPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose XTTS v2 model folder");
            if (folders.Count > 0)
                tts.TtsModelDirectory = folders[0].Path.LocalPath;
        };

        tts.RequestTtsVoiceDirectoryPicker = async () =>
        {
            var folders = await PickFolderAsync("Choose XTTS voice sample folder");
            if (folders.Count > 0)
                tts.TtsVoiceDirectory = folders[0].Path.LocalPath;
        };

        tts.RequestTtsVoiceSamplePicker = async () =>
        {
            var files = await PickFileAsync(
                "Import XTTS voice sample",
                [
                    new FilePickerFileType("Audio sample") { Patterns = ["*.wav", "*.mp3", "*.flac"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0)
                await tts.ImportTtsVoiceSampleAsync(files[0].Path.LocalPath);
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

    private void WireSttFilePickers(SttSettingsViewModel stt)
    {
        stt.RequestAudioFilePicker = async () =>
        {
            var files = await PickFileAsync(
                "Choose a WAV file to transcribe",
                [
                    new FilePickerFileType("WAV audio") { Patterns = ["*.wav"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        };

        stt.RequestCopyToClipboard = async text =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb)
                return false;
            try { await cb.SetTextAsync(text); return true; }
            catch { return false; }
        };
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

public sealed class StatusColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        (ServerStatus)(v ?? ServerStatus.Stopped) switch
        {
            ServerStatus.Running  => new SolidColorBrush(Color.Parse("#4CAF50")),
            ServerStatus.Starting => new SolidColorBrush(Color.Parse("#FF9800")),
            ServerStatus.Error    => new SolidColorBrush(Color.Parse("#F44336")),
            _                     => new SolidColorBrush(Color.Parse("#757575"))
        };

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) =>
        Avalonia.AvaloniaProperty.UnsetValue;
}
