using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hermaeus.Core.Models;
using Hermaeus.ViewModels;
using System.ComponentModel;
using System.Globalization;

namespace Hermaeus.Desktop.Views;

public partial class ServicesView : UserControl
{
    public static readonly IValueConverter StatusColor = new StatusColorConverter();
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _collectionChangedHandler;
    private ServicesViewModel? _wiredViewModel;
    private PropertyChangedEventHandler? _propertyChangedHandler;
    private readonly Dictionary<ServerProcessViewModel, Action<string>> _serverPickers = [];
    private Action? _releaseVoiceCallbacks;
    private Func<bool>? _ownsVoiceCallbacks;
    private long _callbackGeneration;

    public ServicesView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => WireViewModel();
        Unloaded += OnUnloaded;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        WheelScrollHelper.Handle(PageScroller, e);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnwireViewModel();
        WireViewModel();
    }

    private void WireViewModel()
    {
        if (!IsLoaded) return;
        if (ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not ServicesViewModel vm) return;
        _wiredViewModel = vm;

        SynchroniseServerPickers();
        WireVoiceFilePickers(vm.Tts);
        if (vm.Stt is not null)
            WireSttFilePickers(vm.Stt);
        CaptureVoiceCallbacks(vm);

        _collectionChangedHandler = (_, _) => SynchroniseServerPickers();
        
        vm.Servers.CollectionChanged += _collectionChangedHandler;
        _propertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(ServicesViewModel.PendingDoctorTarget))
                Dispatcher.UIThread.Post(FocusPendingDoctorTarget);
        };
        vm.PropertyChanged += _propertyChangedHandler;
        Dispatcher.UIThread.Post(FocusPendingDoctorTarget);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
        => UnwireViewModel();

    private void UnwireViewModel()
    {
        _callbackGeneration++;
        if (_wiredViewModel is not null && _collectionChangedHandler is not null)
            _wiredViewModel.Servers.CollectionChanged -= _collectionChangedHandler;
        if (_wiredViewModel is not null && _propertyChangedHandler is not null)
            _wiredViewModel.PropertyChanged -= _propertyChangedHandler;

        foreach (var (server, picker) in _serverPickers)
            if (server.RequestFilePicker == picker)
                server.RequestFilePicker = null;
        _serverPickers.Clear();
        _releaseVoiceCallbacks?.Invoke();
        _releaseVoiceCallbacks = null;
        _ownsVoiceCallbacks = null;

        _wiredViewModel = null;
        _collectionChangedHandler = null;
        _propertyChangedHandler = null;
    }

    private void SynchroniseServerPickers()
    {
        if (_wiredViewModel is not { } vm) return;
        foreach (var server in _serverPickers.Keys.Where(server => !vm.Servers.Contains(server)).ToArray())
        {
            if (server.RequestFilePicker == _serverPickers[server])
                server.RequestFilePicker = null;
            _serverPickers.Remove(server);
        }
        foreach (var server in vm.Servers)
            if (!_serverPickers.ContainsKey(server))
                WireFilePickers(server);
    }

    private void CaptureVoiceCallbacks(ServicesViewModel vm)
    {
        var tts = vm.Tts;
        var script = tts.RequestTtsScriptPicker;
        var python = tts.RequestTtsPythonPicker;
        var output = tts.RequestTtsOutputPicker;
        var model = tts.RequestTtsModelDirectoryPicker;
        var directory = tts.RequestTtsVoiceDirectoryPicker;
        var sample = tts.RequestTtsVoiceSamplePicker;
        var stt = vm.Stt;
        var audio = stt?.RequestAudioFilePicker;
        var clipboard = stt?.RequestCopyToClipboard;
        _ownsVoiceCallbacks = () => ReferenceEquals(_wiredViewModel, vm)
            && tts.RequestTtsScriptPicker == script;
        _releaseVoiceCallbacks = () =>
        {
            if (tts.RequestTtsScriptPicker == script) tts.RequestTtsScriptPicker = null;
            if (tts.RequestTtsPythonPicker == python) tts.RequestTtsPythonPicker = null;
            if (tts.RequestTtsOutputPicker == output) tts.RequestTtsOutputPicker = null;
            if (tts.RequestTtsModelDirectoryPicker == model) tts.RequestTtsModelDirectoryPicker = null;
            if (tts.RequestTtsVoiceDirectoryPicker == directory) tts.RequestTtsVoiceDirectoryPicker = null;
            if (tts.RequestTtsVoiceSamplePicker == sample) tts.RequestTtsVoiceSamplePicker = null;
            if (stt is null) return;
            if (stt.RequestAudioFilePicker == audio) stt.RequestAudioFilePicker = null;
            if (stt.RequestCopyToClipboard == clipboard) stt.RequestCopyToClipboard = null;
        };
    }

    private void FocusPendingDoctorTarget()
    {
        var vm = _wiredViewModel;
        var target = vm?.PendingDoctorTarget;
        var server = vm?.SelectedServer;
        if (vm is null || target is null || server is null
            || !string.Equals(target.Area, "services", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(target.ItemId, server.Id, StringComparison.Ordinal))
            return;

        ServerList.ScrollIntoView(server);
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_wiredViewModel, vm)
                || !ReferenceEquals(vm.PendingDoctorTarget, target)
                || !ReferenceEquals(vm.SelectedServer, server))
                return;
            var container = ServerList.ContainerFromItem(server);
            if (container is null)
                return;

            var className = target.Focus switch
            {
                "context-size" => "doctor-context-size",
                "gpu-placement" => "doctor-gpu-placement",
                "draft-model" => "doctor-draft-model",
                _ => string.Empty
            };
            var focus = string.IsNullOrEmpty(className)
                ? container
                : container.GetVisualDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(control => control.Classes.Contains(className));
            focus?.Focus();
        }, DispatcherPriority.Background);
    }

    private void WireFilePickers(ServerProcessViewModel srv)
    {
        var generation = _callbackGeneration;
        srv.RequestFilePicker = async propertyName =>
        {
            if (generation != _callbackGeneration
                || !_serverPickers.TryGetValue(srv, out var ownedPicker)
                || srv.RequestFilePicker != ownedPicker)
                return;
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
            if (files.Count == 0 || generation != _callbackGeneration
                || !_serverPickers.TryGetValue(srv, out var picker)
                || srv.RequestFilePicker != picker) return;

            var path = files[0].Path.LocalPath;
            if (isExe)         srv.ExecutablePath = path;
            else if (isMmproj) srv.MmprojPath      = path;
            else if (isDraft)  srv.DraftModelPath  = path;
            else               srv.ModelPath       = path;
        };
        _serverPickers[srv] = srv.RequestFilePicker;
    }

    private bool OwnsVoiceCallbacks(long generation) =>
        generation == _callbackGeneration && _ownsVoiceCallbacks?.Invoke() == true;

    private void WireVoiceFilePickers(TtsSettingsViewModel tts)
    {
        var generation = _callbackGeneration;
        tts.RequestTtsScriptPicker = async () =>
        {
            if (!OwnsVoiceCallbacks(generation)) return;
            var files = await PickFileAsync(
                "Choose xtts_api_server.py",
                [
                    new FilePickerFileType("Python script") { Patterns = ["*.py"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0 && OwnsVoiceCallbacks(generation))
                tts.TtsScriptPath = files[0].Path.LocalPath;
        };

        tts.RequestTtsPythonPicker = async () =>
        {
            if (!OwnsVoiceCallbacks(generation)) return;
            var files = await PickFileAsync(
                "Choose XTTS venv Python",
                [
                    new FilePickerFileType("Python") { Patterns = OperatingSystem.IsWindows() ? ["python.exe"] : ["python"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0 && OwnsVoiceCallbacks(generation))
                tts.TtsPythonPath = files[0].Path.LocalPath;
        };

        tts.RequestTtsOutputPicker = async () =>
        {
            if (!OwnsVoiceCallbacks(generation)) return;
            var folders = await PickFolderAsync("Choose XTTS output folder");
            if (folders.Count > 0 && OwnsVoiceCallbacks(generation))
                tts.TtsOutputDirectory = folders[0].Path.LocalPath;
        };

        tts.RequestTtsModelDirectoryPicker = async () =>
        {
            if (!OwnsVoiceCallbacks(generation)) return;
            var folders = await PickFolderAsync("Choose XTTS v2 model folder");
            if (folders.Count > 0 && OwnsVoiceCallbacks(generation))
                tts.TtsModelDirectory = folders[0].Path.LocalPath;
        };

        tts.RequestTtsVoiceDirectoryPicker = async () =>
        {
            if (!OwnsVoiceCallbacks(generation)) return;
            var folders = await PickFolderAsync("Choose XTTS voice sample folder");
            if (folders.Count > 0 && OwnsVoiceCallbacks(generation))
                tts.TtsVoiceDirectory = folders[0].Path.LocalPath;
        };

        tts.RequestTtsVoiceSamplePicker = async () =>
        {
            if (!OwnsVoiceCallbacks(generation)) return;
            var files = await PickFileAsync(
                "Import XTTS voice sample",
                [
                    new FilePickerFileType("Audio sample") { Patterns = ["*.wav", "*.mp3", "*.flac"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            if (files.Count > 0 && OwnsVoiceCallbacks(generation))
                await tts.ImportTtsVoiceSampleAsync(files[0].Path.LocalPath);
        };
    }

    private async Task<IReadOnlyList<IStorageFolder>> PickFolderAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        return top is null || _ownsVoiceCallbacks?.Invoke() != true
            ? []
            : await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });
    }

    private void WireSttFilePickers(SttSettingsViewModel stt)
    {
        var generation = _callbackGeneration;
        stt.RequestAudioFilePicker = async () =>
        {
            if (!OwnsVoiceCallbacks(generation)) return null;
            var files = await PickFileAsync(
                "Choose a WAV file to transcribe",
                [
                    new FilePickerFileType("WAV audio") { Patterns = ["*.wav"] },
                    new FilePickerFileType("All files") { Patterns = ["*"] }
                ]);
            return files.Count > 0 && OwnsVoiceCallbacks(generation) ? files[0].Path.LocalPath : null;
        };

        stt.RequestCopyToClipboard = async text =>
        {
            if (!OwnsVoiceCallbacks(generation)) return false;
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb)
                return false;
            try { await cb.SetTextAsync(text); return true; }
            catch { return false; }
        };
    }

    private async Task<IReadOnlyList<IStorageFile>> PickFileAsync(string title, IReadOnlyList<FilePickerFileType> filters)
    {
        var top = TopLevel.GetTopLevel(this);
        return top is null || _ownsVoiceCallbacks?.Invoke() != true
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
