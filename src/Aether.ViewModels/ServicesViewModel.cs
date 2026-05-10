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
    [ObservableProperty] private bool         _isAutoTuning;
    [ObservableProperty] private string       _autoTuneStatus = string.Empty;

    public string Id => _config.Id;
    public bool IsRunning  => Status == ServerStatus.Running;
    public bool IsStopped  => Status is ServerStatus.Stopped or ServerStatus.Error;
    public bool IsStarting => Status == ServerStatus.Starting;
    public bool IsError    => Status == ServerStatus.Error;
    public bool CanEdit => IsStopped && !IsAutoTuning;
    public bool HasUnsavedChanges =>
        _config.ExecutablePath != ExecutablePath ||
        _config.ModelPath != ModelPath ||
        _config.Port != Port ||
        _config.ContextSize != ContextSize ||
        _config.GpuLayers != GpuLayers ||
        _config.Threads != Threads ||
        _config.EmbeddingsMode != EmbeddingsMode ||
        _config.AutoStart != AutoStart ||
        _config.ExtraArgs != ExtraArgs;

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
            if (s is ServerStatus.Starting or ServerStatus.Error)
                LogExpanded = true;
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
    private async Task AutoTuneAsync()
    {
        if (!CanEdit) return;

        IsAutoTuning = true;
        LogExpanded = true;
        AutoTuneStatus = "Probing llama.cpp auto-fit...";
        _mgr.ClearLog();
        LogOutput = string.Empty;

        try
        {
            var config = BuildConfig();
            config.GpuLayers = 0;

            var result = await ServerProcessManager.AutoTuneAsync(
                config,
                new Progress<string>(line =>
                {
                    LogOutput = string.IsNullOrEmpty(LogOutput)
                        ? line
                        : $"{LogOutput}\n{line}";
                }));

            GpuLayers = result.GpuLayers;
            Threads = Math.Max(Threads, Environment.ProcessorCount);
            ExtraArgs = MergeExtraArgs(ExtraArgs, "--device VULKAN0");
            AutoTuneStatus = result.TotalLayers is int total
                ? $"Auto-tune chose {result.GpuLayers}/{total} GPU layers. Save and start the service."
                : $"Auto-tune chose {result.GpuLayers} GPU layers. Save and start the service.";
        }
        catch (Exception ex)
        {
            AutoTuneStatus = ex.Message;
            ErrorMessage = ex.Message;
            Status = ServerStatus.Error;
        }
        finally
        {
            IsAutoTuning = false;
        }
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
        OnPropertyChanged(nameof(HasUnsavedChanges));
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
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(StatusLabel));
    }

    partial void OnStatusChanged(ServerStatus value) => NotifyStatusProps();
    partial void OnIsAutoTuningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        AutoTuneCommand.NotifyCanExecuteChanged();
    }
    partial void OnExecutablePathChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnModelPathChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnContextSizeChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnGpuLayersChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnThreadsChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnEmbeddingsModeChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAutoStartChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnExtraArgsChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));

    private static string MergeExtraArgs(string current, string arg)
    {
        if (current.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(arg.Split(' ')[0]))
            return current;

        return string.IsNullOrWhiteSpace(current)
            ? arg
            : $"{current.Trim()} {arg}";
    }

    public void Dispose() => _mgr.Dispose();
}

// ── ServicesViewModel ─────────────────────────────────────────────────────────

public partial class ServicesViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public ObservableCollection<ServerProcessViewModel> Servers { get; } = [];
    public event EventHandler? ServerAvailabilityChanged;

    public ServicesViewModel(ISettingsService settings)
    {
        _settings = settings;
        Rebuild();
        _settings.SettingsChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        var existing = Servers.ToDictionary(s => s.Id);

        foreach (var srv in Servers) srv.PropertyChanged -= OnServerPropertyChanged;
        Servers.Clear();

        var configs = _settings.Settings.ManagedServers;

        // Ensure we always have the two default slots
        while (configs.Count < 2)
            configs.Add(new ServerConfig
            {
                Name = configs.Count == 0 ? "Chat" : "Embeddings",
                Port = configs.Count == 0 ? 8080 : 8081,
                EmbeddingsMode = configs.Count == 1
            });

        foreach (var cfg in configs)
        {
            var vm = existing.TryGetValue(cfg.Id, out var current)
                ? current
                : new ServerProcessViewModel(cfg, _settings);

            vm.PropertyChanged += OnServerPropertyChanged;
            Servers.Add(vm);
        }

        ServerAvailabilityChanged?.Invoke(this, EventArgs.Empty);
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
        if (e.PropertyName is nameof(ServerProcessViewModel.Status)
            or nameof(ServerProcessViewModel.ModelPath)
            or nameof(ServerProcessViewModel.ExecutablePath)
            or nameof(ServerProcessViewModel.Port))
        {
            ServerAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
