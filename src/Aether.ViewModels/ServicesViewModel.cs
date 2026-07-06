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
    private readonly ServerProcessManager  _mgr;
    private readonly ISettingsService      _settings;
    private readonly ITrustService         _trust;
    private readonly IToastService         _toasts;
    private readonly IRuntimeLogService    _runtimeLogs;
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
    public string ExtraArgsTrustWarning
    {
        get
        {
            var warning = _trust.AnalyzeServerExtraArgs(BuildConfig(), DateTime.UtcNow).FirstOrDefault();
            return warning?.Recommendation ?? string.Empty;
        }
    }

    public string StatusLabel => Status switch
    {
        ServerStatus.Running  => "Running",
        ServerStatus.Starting => "Starting…",
        ServerStatus.Error    => "Error",
        _                     => "Stopped"
    };

    public Action<string>? RequestFilePicker  { get; set; }
    public Action<string>? RequestFolderPicker { get; set; }
    public Func<ServerProcessViewModel, Task>? BeforeStartAsync { get; set; }

    public ServerProcessViewModel(
        ServerConfig config,
        ISettingsService settings,
        IRedactionService redactor,
        ITrustService trust,
        IToastService toasts,
        IRuntimeLogService runtimeLogs)
    {
        _mgr = new ServerProcessManager(redactor);
        _config   = config;
        _settings = settings;
        _trust = trust;
        _toasts = toasts;
        _runtimeLogs = runtimeLogs;

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
            _runtimeLogs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                s == ServerStatus.Error ? RuntimeLogLevel.Error : RuntimeLogLevel.Info,
                RuntimeLogCategory.Service,
                $"{Name} status: {StatusLabel}"));
            NotifyStatusProps();
        };

        _mgr.LogLine += line =>
        {
            LogOutput = _mgr.GetLog();
            if (!string.IsNullOrWhiteSpace(line))
                _runtimeLogs.Add(MapLog(line));
        };
    }

    [RelayCommand]
    private async Task StartAsync()
        => await StartCoreAsync(CancellationToken.None);

    private async Task StartCoreAsync(CancellationToken ct)
    {
        ApplyTuneProfileIfAvailable();
        SyncToConfig();
        await SaveConfigAsync();
        if (BeforeStartAsync is not null)
            await BeforeStartAsync(this);
        await _mgr.StartAsync(BuildConfig(), ct);
    }

    [RelayCommand]
    private void Stop()
    {
        _mgr.Stop();
    }

    [RelayCommand]
    private void ClearError()
    {
        if (Status == ServerStatus.Error)
        {
            ErrorMessage = string.Empty;
            // Try to detect actual status by attempting minimal communication
            // If process is actually running, the next status update will fix it
            _mgr.RefreshStatus();
        }
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        SyncToConfig();
        await PersistTuneProfileAsync();
        await _settings.SaveAsync();
        WarnForExtraArgs();
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
        AutoTuneStatus = "Testing llama.cpp GPU layer candidates...";
        _mgr.ClearLog();
        LogOutput = string.Empty;

        try
        {
            var result = await ServerProcessManager.AutoTuneAsync(
                BuildConfig(),
                new Progress<string>(line =>
                {
                    LogOutput = string.IsNullOrEmpty(LogOutput)
                        ? line
                        : $"{LogOutput}\n{line}";
                }));

            GpuLayers = result.GpuLayers;
            Threads = result.Threads;
            await PersistTuneProfileAsync(result);
            await _settings.SaveAsync();
            AutoTuneStatus = result.TotalLayers is int total
                ? $"Auto-tune verified {result.GpuLayers}/{total} GPU layers with {result.Threads} thread(s). Save and start the service."
                : $"Auto-tune verified {result.GpuLayers} GPU layers with {result.Threads} thread(s). Save and start the service.";
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
            await StartCoreAsync(CancellationToken.None);
    }

    public async Task StartIfStoppedAsync()
    {
        if (!IsStopped)
            return;

        await StartCoreAsync(CancellationToken.None);
    }

    public void StopIfRunning() => _mgr.Stop();

    public async Task SelectModelAndRestartAsync(string modelPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            return;

        var normalized = Path.GetFullPath(modelPath);
        var current = string.IsNullOrWhiteSpace(ModelPath) ? string.Empty : Path.GetFullPath(ModelPath);
        var modelChanged = !string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase);

        if (Status is ServerStatus.Running or ServerStatus.Starting && modelChanged)
            StopIfRunning();

        if (modelChanged)
        {
            ModelPath = normalized;
            ApplyTuneProfileIfAvailable();
            SyncToConfig();
            await SaveConfigAsync();
        }

        if (IsStarting)
            await WaitUntilStartedAsync(ct);

        if (IsStopped)
            await StartCoreAsync(ct);
        else if (IsError)
            throw new InvalidOperationException(ErrorMessage);
    }

    private void ApplyTuneProfileIfAvailable()
    {
        var profile = FindTuneProfile(ModelPath);
        if (profile is null)
            return;

        GpuLayers = profile.GpuLayers;
        Threads = profile.Threads;
        if (profile.ContextSize > 0)
            ContextSize = profile.ContextSize;
        if (!string.IsNullOrWhiteSpace(profile.ExtraArgs))
            ExtraArgs = profile.ExtraArgs;
    }

    private Task PersistTuneProfileAsync(ServerTuneResult? result = null)
    {
        var normalized = ResolveExistingModelPath(ModelPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return Task.CompletedTask;

        var file = new FileInfo(normalized);
        var profile = FindTuneProfile(normalized);
        if (profile is null)
        {
            profile = new LlamaTuneProfile();
            _settings.Settings.LlamaTuneProfiles.Add(profile);
        }

        profile.ModelPath = normalized;
        profile.ModelSizeBytes = file.Length;
        profile.ModelModifiedAtUtc = file.LastWriteTimeUtc;
        profile.GpuLayers = result?.GpuLayers ?? GpuLayers;
        profile.TotalLayers = result?.TotalLayers ?? profile.TotalLayers;
        profile.Threads = result?.Threads ?? Threads;
        profile.ContextSize = ContextSize;
        profile.ExtraArgs = ExtraArgs;
        profile.LlamaServerVersion = result?.LlamaServerVersion ?? profile.LlamaServerVersion;
        profile.TunedAtUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    private LlamaTuneProfile? FindTuneProfile(string modelPath)
    {
        var normalized = ResolveExistingModelPath(modelPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var file = new FileInfo(normalized);
        return _settings.Settings.LlamaTuneProfiles.FirstOrDefault(profile =>
            string.Equals(Path.GetFullPath(profile.ModelPath), normalized, StringComparison.OrdinalIgnoreCase)
            && profile.ModelSizeBytes == file.Length
            && profile.ModelModifiedAtUtc == file.LastWriteTimeUtc);
    }

    private static string ResolveExistingModelPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            return string.Empty;

        var trimmed = modelPath.Trim();
        if (File.Exists(trimmed))
            return Path.GetFullPath(trimmed);

        if (!Directory.Exists(trimmed))
            return string.Empty;

        try
        {
            var models = Directory.EnumerateFiles(trimmed, "*.gguf", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            return models.Length == 1 ? Path.GetFullPath(models[0]) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task WaitUntilStartedAsync(CancellationToken ct)
    {
        if (!IsStarting)
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(ServerStatus status)
        {
            if (status is ServerStatus.Running or ServerStatus.Error or ServerStatus.Stopped)
                tcs.TrySetResult();
        }

        _mgr.StatusChanged += Handler;
        try
        {
            if (!IsStarting)
                tcs.TrySetResult();
            using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task;
        }
        finally
        {
            _mgr.StatusChanged -= Handler;
        }
    }

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
        OnPropertyChanged(nameof(ExtraArgsTrustWarning));
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
    partial void OnExtraArgsChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ExtraArgsTrustWarning));
    }

    private void WarnForExtraArgs()
    {
        var warning = _trust.AnalyzeServerExtraArgs(BuildConfig(), DateTime.UtcNow).FirstOrDefault();
        if (warning is not null)
            _toasts.Show("Network exposure warning", warning.Recommendation, ToastKind.Warning, 7000);
    }

    private RuntimeLogEntry MapLog(string line)
    {
        var lowered = line.ToLowerInvariant();
        var level = lowered.Contains("error") || lowered.Contains("failed")
            ? RuntimeLogLevel.Error
            : lowered.Contains("warn") ? RuntimeLogLevel.Warning : RuntimeLogLevel.Info;

        var category = lowered.Contains("starting") || lowered.Contains("launched") || lowered.Contains("ready")
            ? RuntimeLogCategory.Startup
            : lowered.Contains("model") || lowered.Contains("gguf")
                ? RuntimeLogCategory.ModelLoad
                : lowered.Contains("http") || lowered.Contains("port") || lowered.Contains("listen")
                    ? RuntimeLogCategory.Network
                    : lowered.Contains("voice") || lowered.Contains("tts")
                        ? RuntimeLogCategory.Voice
                        : RuntimeLogCategory.Service;

        return new RuntimeLogEntry(DateTime.UtcNow, level, category, line);
    }

    public void Dispose() => _mgr.Dispose();
}

// ── ServicesViewModel ─────────────────────────────────────────────────────────

public partial class ServicesViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IRuntimeProfileService _runtimeProfiles;
    private readonly IToastService _toasts;
    private readonly IRedactionService _redactor;
    private readonly ITrustService _trust;
    private readonly IRuntimeLogService _runtimeLogs;

    public ObservableCollection<ServerProcessViewModel> Servers { get; } = [];
    public ObservableCollection<RuntimeProfileViewModel> RuntimeProfiles { get; } = [];
    public event EventHandler? ServerAvailabilityChanged;

    public RuntimeKind[] RuntimeKinds { get; } =
    [
        RuntimeKind.LlamaCpp,
        RuntimeKind.Ollama,
        RuntimeKind.OpenAiCompatible
    ];

    public ServicesViewModel(
        ISettingsService settings,
        IRuntimeProfileService runtimeProfiles,
        IToastService toasts,
        IRedactionService redactor,
        ITrustService trust,
        IRuntimeLogService runtimeLogs)
    {
        _settings = settings;
        _runtimeProfiles = runtimeProfiles;
        _toasts = toasts;
        _redactor = redactor;
        _trust = trust;
        _runtimeLogs = runtimeLogs;
        Rebuild();
        _settings.SettingsChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        Aether.Services.SettingsService.NormalizeManagedServers(_settings.Settings.ManagedServers);
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
                : new ServerProcessViewModel(cfg, _settings, _redactor, _trust, _toasts, _runtimeLogs);

            vm.BeforeStartAsync = StopSamePortPeersBeforeStartAsync;
            vm.PropertyChanged += OnServerPropertyChanged;
            Servers.Add(vm);
        }

        RuntimeProfiles.Clear();
        foreach (var profile in _runtimeProfiles.Profiles)
            RuntimeProfiles.Add(new RuntimeProfileViewModel(profile));

        ServerAvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task AddRuntimeProfileAsync()
    {
        var profile = new RuntimeProfile
        {
            Name = "New runtime",
            Kind = RuntimeKind.OpenAiCompatible,
            BaseUrl = "http://127.0.0.1:8080",
            Enabled = true
        };
        await _runtimeProfiles.SaveAsync(profile);
        _toasts.Show("Runtime added", "Configure the new runtime profile before using it.", ToastKind.Info);
    }

    [RelayCommand]
    private async Task SaveRuntimeProfileAsync(RuntimeProfileViewModel? item)
    {
        if (item is null) return;
        await _runtimeProfiles.SaveAsync(item.ToProfile());
        if (item.HasUnsafeHost)
            _toasts.Show("Unsafe host warning", "0.0.0.0 exposes this runtime beyond localhost. Use it only when you intend network access.", ToastKind.Warning, 7000);
        else
            _toasts.Show("Runtime saved", $"{item.Name} was updated.", ToastKind.Success);
    }

    [RelayCommand]
    private async Task DeleteRuntimeProfileAsync(RuntimeProfileViewModel? item)
    {
        if (item is null) return;
        await _runtimeProfiles.DeleteAsync(item.Id);
        _toasts.Show("Runtime deleted", $"{item.Name} was removed.", ToastKind.Info);
    }

    [RelayCommand]
    private async Task CheckRuntimeProfileAsync(RuntimeProfileViewModel? item)
    {
        if (item is null) return;
        item.IsChecking = true;
        try
        {
            var health = await _runtimeProfiles.CheckHealthAsync(item.ToProfile());
            item.HealthMessage = health.Message;
            item.IsHealthy = health.IsHealthy;
            _runtimeLogs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                health.IsHealthy ? RuntimeLogLevel.Info : RuntimeLogLevel.Warning,
                RuntimeLogCategory.Network,
                $"Runtime {item.Name} health: {health.Message}"));
            _toasts.Show(health.IsHealthy ? "Runtime healthy" : "Runtime unavailable",
                $"{item.Name}: {health.Message}",
                health.IsHealthy ? ToastKind.Success : ToastKind.Warning);
        }
        finally
        {
            item.IsChecking = false;
        }
    }

    public async Task AutoStartAllAsync()
    {
        foreach (var srv in Servers)
            await srv.AutoStartIfConfiguredAsync();
    }

    public async Task<IReadOnlyList<string>> StopRunningNonEmbeddingServersAsync()
    {
        var suspended = Servers
            .Where(s => s.IsRunning && !s.EmbeddingsMode)
            .Select(s => s.Id)
            .ToList();

        foreach (var serverId in suspended)
        {
            var server = Servers.FirstOrDefault(s => s.Id == serverId);
            server?.StopIfRunning();
        }

        return await Task.FromResult(suspended);
    }

    public async Task<IReadOnlyList<string>> PrepareEmbeddingServerForWorkAsync()
    {
        var suspended = await StopRunningNonEmbeddingServersAsync();
        var embeddingServer = Servers.FirstOrDefault(s => s.EmbeddingsMode);
        if (embeddingServer is not null)
            await embeddingServer.StartIfStoppedAsync();
        return suspended;
    }

    public async Task RestartServersAsync(IEnumerable<string> serverIds)
    {
        foreach (var serverId in serverIds)
        {
            var server = Servers.FirstOrDefault(s => s.Id == serverId);
            if (server is null)
                continue;

            await server.StartIfStoppedAsync();
        }
    }

    public void StopAll()
    {
        foreach (var srv in Servers)
            srv.StopIfRunning();
    }

    public async Task SelectChatModelAndRestartAsync(string modelPath, CancellationToken ct = default)
    {
        var server = Servers.FirstOrDefault(s => !s.EmbeddingsMode) ?? Servers.FirstOrDefault();
        if (server is null)
            return;

        await server.SelectModelAndRestartAsync(modelPath, ct);
    }

    private Task StopSamePortPeersBeforeStartAsync(ServerProcessViewModel starting)
    {
        foreach (var peer in Servers)
        {
            if (ReferenceEquals(peer, starting))
                continue;

            if (peer.Port == starting.Port && peer.IsRunning)
                peer.StopIfRunning();
        }

        return Task.CompletedTask;
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

public partial class RuntimeProfileViewModel : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private RuntimeKind _kind;
    [ObservableProperty] private string _baseUrl;
    [ObservableProperty] private string _apiKey;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _startManagedLlamaServer;
    [ObservableProperty] private string _linkedServerId;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isHealthy;
    [ObservableProperty] private string _healthMessage = string.Empty;

    public string Id { get; }
    public bool HasUnsafeHost => BaseUrl.Contains("://0.0.0.0", StringComparison.OrdinalIgnoreCase)
                                 || BaseUrl.Contains("//0.0.0.0", StringComparison.OrdinalIgnoreCase);
    public string KindLabel => Aether.Services.CompositeLlmService.DescriptorFor(Kind).DisplayName;

    public RuntimeProfileViewModel(RuntimeProfile profile)
    {
        Id = profile.Id;
        _name = profile.Name;
        _kind = profile.Kind;
        _baseUrl = profile.BaseUrl;
        _apiKey = profile.ApiKey;
        _enabled = profile.Enabled;
        _startManagedLlamaServer = profile.StartManagedLlamaServer;
        _linkedServerId = profile.LinkedServerId;
    }

    public RuntimeProfile ToProfile() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        Enabled = Enabled,
        StartManagedLlamaServer = StartManagedLlamaServer,
        LinkedServerId = LinkedServerId
    };

    partial void OnBaseUrlChanged(string value) => OnPropertyChanged(nameof(HasUnsafeHost));
    partial void OnKindChanged(RuntimeKind value) => OnPropertyChanged(nameof(KindLabel));
}
