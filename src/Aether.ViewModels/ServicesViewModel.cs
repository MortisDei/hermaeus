using System.Collections.ObjectModel;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

// ── Per-server VM ─────────────────────────────────────────────────────────────

public partial class ServerProcessViewModel : ObservableObject, IDisposable
{
    private readonly ServerProcessManager  _mgr = new();
    private readonly ISettingsService      _settings;
    private readonly ServerConfig          _config;

    [ObservableProperty] private string       _name;
    [ObservableProperty] private string       _executablePath;
    [ObservableProperty] private string       _modelPath;
    [ObservableProperty] private int          _port;
    [ObservableProperty] private int          _contextSize;
    [ObservableProperty] private int          _gpuLayers;
    [ObservableProperty] private int          _threads;
    [ObservableProperty] private bool         _embeddingsMode;
    [ObservableProperty] private bool         _autoStart;
    [ObservableProperty] private string       _extraArgs = string.Empty;
    [ObservableProperty] private ServerStatus _status    = ServerStatus.Stopped;
    [ObservableProperty] private string       _logOutput = string.Empty;
    [ObservableProperty] private string       _errorMessage = string.Empty;
    [ObservableProperty] private bool         _logExpanded  = false;

    public bool IsRunning  => Status == ServerStatus.Running;
    public bool IsStopped  => Status is ServerStatus.Stopped or ServerStatus.Error;
    public bool IsStarting => Status == ServerStatus.Starting;
    public bool IsError    => Status == ServerStatus.Error;

    public string StatusLabel => Status switch
    {
        ServerStatus.Running  => "Running",
        ServerStatus.Starting => "Starting…",
        ServerStatus.Error    => "Error",
        _                     => "Stopped"
    };

    public Action<string>? RequestFilePicker  { get; set; }
    public Action<string>? RequestFolderPicker { get; set; }

    public ServerProcessViewModel(ServerConfig config, ISettingsService settings)
    {
        _config   = config;
        _settings = settings;

        _name           = config.Name;
        _executablePath = config.ExecutablePath;
        _modelPath      = config.ModelPath;
        _port           = config.Port;
        _contextSize    = config.ContextSize;
        _gpuLayers      = config.GpuLayers;
        _threads        = config.Threads;
        _embeddingsMode = config.EmbeddingsMode;
        _autoStart      = config.AutoStart;
        _extraArgs      = config.ExtraArgs;

        _mgr.StatusChanged += s =>
        {
            Status       = s;
            ErrorMessage = _mgr.ErrorMessage;
            NotifyStatusProps();
        };

        _mgr.LogLine += _ =>
            LogOutput = _mgr.GetLog();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        SyncToConfig();
        await SaveConfigAsync();
        await _mgr.StartAsync(BuildConfig());
    }

    [RelayCommand]
    private void Stop()
    {
        _mgr.Stop();
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        SyncToConfig();
        await _settings.SaveAsync();
    }

    [RelayCommand]
    private void ClearLog()
    {
        _mgr.ClearLog();
        LogOutput = string.Empty;
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        RequestFilePicker?.Invoke(nameof(ExecutablePath));
    }

    [RelayCommand]
    private void BrowseModel()
    {
        RequestFilePicker?.Invoke(nameof(ModelPath));
    }

    [RelayCommand]
    private void ToggleLog() => LogExpanded = !LogExpanded;

    public async Task AutoStartIfConfiguredAsync()
    {
        if (AutoStart && !string.IsNullOrWhiteSpace(ModelPath))
            await StartAsync();
    }

    public void StopIfRunning() => _mgr.Stop();

    private void SyncToConfig()
    {
        _config.Name           = Name;
        _config.ExecutablePath = ExecutablePath;
        _config.ModelPath      = ModelPath;
        _config.Port           = Port;
        _config.ContextSize    = ContextSize;
        _config.GpuLayers      = GpuLayers;
        _config.Threads        = Threads;
        _config.EmbeddingsMode = EmbeddingsMode;
        _config.AutoStart      = AutoStart;
        _config.ExtraArgs      = ExtraArgs;
    }

    private ServerConfig BuildConfig() => new()
    {
        Name           = Name,
        ExecutablePath = ExecutablePath,
        ModelPath      = ModelPath,
        Port           = Port,
        ContextSize    = ContextSize,
        GpuLayers      = GpuLayers,
        Threads        = Threads,
        EmbeddingsMode = EmbeddingsMode,
        AutoStart      = AutoStart,
        ExtraArgs      = ExtraArgs
    };

    private void NotifyStatusProps()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(StatusLabel));
    }

    partial void OnStatusChanged(ServerStatus value) => NotifyStatusProps();

    public void Dispose() => _mgr.Dispose();
}

// ── ServicesViewModel ─────────────────────────────────────────────────────────

public partial class ServicesViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public ObservableCollection<ServerProcessViewModel> Servers { get; } = [];

    public ServicesViewModel(ISettingsService settings)
    {
        _settings = settings;
        Rebuild();
        _settings.SettingsChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        // Keep existing running instances if config IDs match
        var existing = Servers.ToDictionary(s => s.Name);

        foreach (var srv in Servers) srv.PropertyChanged -= OnServerPropertyChanged;
        Servers.Clear();

        var configs = _settings.Settings.ManagedServers;

        // Ensure we always have the two default slots
        while (configs.Count < 2)
            configs.Add(new ServerConfig
            {
                Name = configs.Count == 0 ? "Embeddings" : "Chat",
                Port = configs.Count == 0 ? 8080 : 8081,
                EmbeddingsMode = configs.Count == 0
            });

        foreach (var cfg in configs)
        {
            var vm = new ServerProcessViewModel(cfg, _settings);
            vm.PropertyChanged += OnServerPropertyChanged;
            Servers.Add(vm);
        }
    }

    public async Task AutoStartAllAsync()
    {
        foreach (var srv in Servers)
            await srv.AutoStartIfConfiguredAsync();
    }

    public void StopAll()
    {
        foreach (var srv in Servers)
            srv.StopIfRunning();
    }

    private void OnServerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Could add cross-server notifications here (e.g. port conflict detection)
    }
}
