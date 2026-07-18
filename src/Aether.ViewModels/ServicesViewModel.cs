using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

// ── Per-server VM ─────────────────────────────────────────────────────────────

public partial class ServerProcessViewModel : ViewModelBase, IDisposable
{
    private readonly ServerProcessManager  _mgr;
    private readonly ISettingsService      _settings;
    private readonly TrustService         _trust;
    private readonly IToastService         _toasts;
    private readonly IRuntimeLogService    _runtimeLogs;
    private readonly OrphanServerDetector  _orphanDetector;
    private readonly ServerConfig          _config;
    private OrphanServerInfo? _orphanInfo;

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
    [ObservableProperty] private bool         _hasOrphan;
    [ObservableProperty] private bool         _canStopOrphan;
    [ObservableProperty] private string       _orphanBannerText = string.Empty;

    public string Id => _config.Id;
    public bool IsRunning  => Status == ServerStatus.Running;
    public bool IsStopped  => Status is ServerStatus.Stopped or ServerStatus.Error;
    public bool IsStarting => Status == ServerStatus.Starting;
    public bool IsError    => Status == ServerStatus.Error;
    public bool CanEdit => IsStopped && !IsAutoTuning;
    public bool HasUnsavedChanges =>
        _config.Name != Name ||
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
        _                     => "Stopped, click Start to launch"
    };

    public string StatusTooltip => Status switch
    {
        ServerStatus.Running  => "The server process is running and passed its health check.",
        ServerStatus.Starting => "Launching the server process and waiting for it to become healthy.",
        ServerStatus.Error    => "The server process failed to start or exited unexpectedly. See the log below.",
        _                     => "The executable is configured but the server process has not been started. This is separate from Doctor's \"found on disk\" check."
    };

    public Action<string>? RequestFilePicker  { get; set; }
    public Action<string>? RequestFolderPicker { get; set; }
    public Func<ServerProcessViewModel, Task>? BeforeStartAsync { get; set; }

    private const int LargeContextSizeThreshold = 16384;

    /// <summary>Drives the inline oversized-context note (r9 01-send-path-latency.md 1.5). Advisory only.</summary>
    public bool HasOversizedContext => ContextSize > LargeContextSizeThreshold;
    public string OversizedContextNote =>
        $"Large context ({ContextSize:N0} tokens) can spill out of VRAM, slowing prompt processing and increasing memory use.";

    public UiBoundCollection<string> DetectedModelPaths { get; } = [];

    /// <summary>Folder a model-path file picker should open in: the embeddings subfolder for the embeddings server, otherwise the models folder.</summary>
    public string SuggestedModelBrowseDirectory
    {
        get
        {
            var root = _settings.Settings.DataManagement.LocalAiAssetsRoot;
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;
            return EmbeddingsMode
                ? Aether.Services.LocalAiAssetLocator.GetPreferredEmbeddingsDirectory(root)
                : Aether.Services.LocalAiAssetLocator.Detect(root).ModelsDirectory;
        }
    }

    public void RefreshDetectedModels()
    {
        var root = _settings.Settings.DataManagement.LocalAiAssetsRoot;
        var found = EmbeddingsMode
            ? Aether.Services.LocalAiAssetLocator.FindEmbeddingModels(root)
            : Aether.Services.LocalAiAssetLocator.FindGgufModels(root);
        var current = ModelPath;
        DetectedModelPaths.Clear();
        foreach (var path in found)
            DetectedModelPaths.Add(path);
        // Re-notify SelectedItem binding: ComboBox only highlights the match if the
        // items collection already contains it by the time the binding evaluates.
        if (!string.IsNullOrWhiteSpace(current))
            OnPropertyChanged(nameof(ModelPath));
    }

    public ServerProcessViewModel(
        ServerConfig config,
        ISettingsService settings,
        RedactionService redactor,
        TrustService trust,
        IToastService toasts,
        IRuntimeLogService runtimeLogs,
        OrphanServerDetector? orphanDetector = null)
    {
        _mgr = new ServerProcessManager(redactor);
        _config   = config;
        _settings = settings;
        _trust = trust;
        _toasts = toasts;
        _runtimeLogs = runtimeLogs;
        _orphanDetector = orphanDetector ?? new OrphanServerDetector();

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

        _mgr.StatusChanged += s => RunOnUi(() =>
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
        });

        _mgr.LogLine += line => RunOnUi(() =>
        {
            LogOutput = _mgr.GetLog();
            if (!string.IsNullOrWhiteSpace(line))
                _runtimeLogs.Add(MapLog(line));
        });

        RefreshDetectedModels();
    }

    /// <summary>
    /// Checks whether this server's configured port is held by a leftover
    /// process from a previous session (r9 02-server-lifecycle.md 2.3).
    /// Called at startup and on Services view refresh; a no-op while this
    /// server is Running (nothing "leftover" about the process we manage).
    /// </summary>
    public async Task RefreshOrphanStatusAsync()
    {
        if (Status == ServerStatus.Running)
        {
            ClearOrphanBanner();
            return;
        }

        // r12 01-settings-lifecycle.md 1.4: port/process scanning is
        // synchronous OS work; running it off the UI thread keeps a
        // Services-panel refresh (or the settings-save-triggered rebuild
        // storm this round also fixes) from blocking the UI while it scans.
        var config = BuildConfig();
        var info = await Task.Run(() => _orphanDetector.Detect(config));
        RunOnUi(() => ApplyOrphanDetectionResult(info));
    }

    private void ApplyOrphanDetectionResult(OrphanServerInfo? info)
    {
        if (info is null)
        {
            ClearOrphanBanner();
            return;
        }

        _orphanInfo = info;
        HasOrphan = true;
        CanStopOrphan = info.IsOwnBinary;
        OrphanBannerText = info.IsOwnBinary
            ? $"A {Name} process from a previous session is still running on port {info.Port} (PID {info.Pid})."
            : $"Port {info.Port} is already in use by {info.ProcessName} (PID {info.Pid}).";
    }

    private void ClearOrphanBanner()
    {
        _orphanInfo = null;
        HasOrphan = false;
        CanStopOrphan = false;
        OrphanBannerText = string.Empty;
    }

    [RelayCommand]
    private void StopOrphan()
    {
        var info = _orphanInfo;
        if (info is null || !info.IsOwnBinary) return;

        var result = _orphanDetector.TryStop(BuildConfig(), info.Pid);
        if (result.Success)
        {
            _runtimeLogs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Service,
                $"Stopped orphaned {Name} process from a previous session (PID {info.Pid})."));
            ClearOrphanBanner();
        }
        else
        {
            _toasts.Show("Could not stop process", result.Message, ToastKind.Warning, 7000);
        }
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
        var profile = LlamaTuneProfileStore.Find(_settings.Settings, ModelPath);
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
        LlamaTuneProfileStore.Upsert(_settings.Settings, ModelPath, ContextSize, ExtraArgs, GpuLayers, Threads, result);
        return Task.CompletedTask;
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

        // Managed servers are always local processes, so their configured port is the
        // single source of truth. Without this, changing the port here left
        // Llm.LlamaCppBaseUrl / Rag.EmbeddingBaseUrl (what chat/benchmark/RAG actually
        // connect to) pointing at the old port, silently breaking model listing,
        // generation, and embedding health checks.
        if (EmbeddingsMode)
            _settings.Settings.Rag.EmbeddingBaseUrl = $"http://localhost:{Port}";
        else
            SyncChatBaseUrlToPort();
    }

    private void SyncChatBaseUrlToPort()
    {
        var url = $"http://localhost:{Port}";
        _settings.Settings.Llm.LlamaCppBaseUrl = url;

        var linked = _settings.Settings.RuntimeProfiles
            .FirstOrDefault(p => p.Kind == RuntimeKind.LlamaCpp && p.LinkedServerId == _config.Id);
        if (linked is not null)
            linked.BaseUrl = url;
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
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnExecutablePathChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnModelPathChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnContextSizeChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasOversizedContext));
        OnPropertyChanged(nameof(OversizedContextNote));
    }
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

    /// <summary>Set by <see cref="Dispose"/>; lets tests confirm a row dropped from settings during <see cref="ServicesViewModel.Rebuild"/> was actually disposed, not just removed from the collection.</summary>
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
        _mgr.Dispose();
    }
}

// ── ServicesViewModel ─────────────────────────────────────────────────────────

public partial class ServicesViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly RuntimeProfileService _runtimeProfiles;
    private readonly IToastService _toasts;
    private readonly RedactionService _redactor;
    private readonly TrustService _trust;
    private readonly IRuntimeLogService _runtimeLogs;
    private readonly OrphanServerDetector _orphanDetector;

    public UiBoundCollection<ServerProcessViewModel> Servers { get; } = [];
    public UiBoundCollection<RuntimeProfileViewModel> RuntimeProfiles { get; } = [];
    public event EventHandler? ServerAvailabilityChanged;
    private string? _lastAvailabilityFingerprint;

    public RuntimeKind[] RuntimeKinds { get; } =
    [
        RuntimeKind.LlamaCpp,
        RuntimeKind.Ollama,
        RuntimeKind.OpenAiCompatible
    ];

    public ServicesViewModel(
        ISettingsService settings,
        RuntimeProfileService runtimeProfiles,
        IToastService toasts,
        RedactionService redactor,
        TrustService trust,
        IRuntimeLogService runtimeLogs,
        OrphanServerDetector? orphanDetector = null)
    {
        _settings = settings;
        _runtimeProfiles = runtimeProfiles;
        _toasts = toasts;
        _redactor = redactor;
        _trust = trust;
        _runtimeLogs = runtimeLogs;
        _orphanDetector = orphanDetector ?? new OrphanServerDetector();
        Rebuild();
        _settings.SettingsChanged += (_, _) => RunOnUi(Rebuild);
    }

    /// <summary>Re-checks every non-Running server's port for a leftover process (r9 02-server-lifecycle.md 2.3). Startup and Services-view-refresh entry point.</summary>
    [RelayCommand]
    public async Task RefreshOrphanDetectionAsync()
    {
        foreach (var server in Servers.ToList())
            await server.RefreshOrphanStatusAsync();
    }

    /// <summary>
    /// r12 01-settings-lifecycle.md 1.4: <see cref="ISettingsService.SettingsChanged"/>
    /// fires after every save, so this used to run on every settings save of
    /// anything (a UI font-size tweak included): Clear/re-add churned every
    /// <see cref="ServerProcessViewModel"/> (losing UI state like expanded
    /// logs, leaking the <see cref="ServerProcessManager"/> of any row whose
    /// config was removed), and unconditionally fired
    /// <see cref="ServerAvailabilityChanged"/> plus a full orphan port scan.
    /// Now it diffs by config id (reusing unchanged rows, disposing dropped
    /// ones) and only fires the availability event/orphan scan when the
    /// server set, ports, or paths actually changed.
    /// </summary>
    private void Rebuild()
    {
        Aether.Services.SettingsService.NormalizeManagedServers(_settings.Settings.ManagedServers);
        var configs = _settings.Settings.ManagedServers;

        // Ensure we always have the two default slots
        while (configs.Count < 2)
            configs.Add(new ServerConfig
            {
                Name = configs.Count == 0 ? "Chat" : "Embeddings",
                Port = configs.Count == 0 ? 8080 : 8081,
                EmbeddingsMode = configs.Count == 1
            });

        var configIds = new HashSet<string>(configs.Select(c => c.Id));
        var existing = Servers.ToDictionary(s => s.Id);

        foreach (var stale in Servers.Where(s => !configIds.Contains(s.Id)).ToList())
        {
            stale.PropertyChanged -= OnServerPropertyChanged;
            Servers.Remove(stale);
            stale.Dispose();
        }

        for (var index = 0; index < configs.Count; index++)
        {
            var cfg = configs[index];
            if (existing.TryGetValue(cfg.Id, out var current))
            {
                var currentIndex = Servers.IndexOf(current);
                if (currentIndex != index)
                    Servers.Move(currentIndex, index);
                current.RefreshDetectedModels();
            }
            else
            {
                var vm = new ServerProcessViewModel(cfg, _settings, _redactor, _trust, _toasts, _runtimeLogs, _orphanDetector)
                {
                    BeforeStartAsync = StopSamePortPeersBeforeStartAsync
                };
                vm.PropertyChanged += OnServerPropertyChanged;
                Servers.Insert(index, vm);
            }
        }

        RuntimeProfiles.Clear();
        foreach (var profile in _runtimeProfiles.Profiles)
            RuntimeProfiles.Add(new RuntimeProfileViewModel(profile));

        var fingerprint = BuildAvailabilityFingerprint(configs);
        if (!string.Equals(_lastAvailabilityFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _lastAvailabilityFingerprint = fingerprint;
            ServerAvailabilityChanged?.Invoke(this, EventArgs.Empty);
            _ = RefreshOrphanDetectionAsync();
        }
    }

    private static string BuildAvailabilityFingerprint(IEnumerable<ServerConfig> configs) =>
        string.Join("|", configs.Select(c => $"{c.Id}:{c.Port}:{c.ExecutablePath}:{c.ModelPath}"));

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

        // Guard: If the path is invalid, do NOT restart the server.
        // This prevents the "Death Spiral" where a stale path kills a working server.
        if (!File.Exists(modelPath))
        {
            _toasts.Show("Model Load Error", $"The model file does not exist: {Path.GetFileName(modelPath)}", ToastKind.Error);
            return;
        }

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

    /// <summary>
    /// StartManagedLlamaServer/LinkedServerId are llama.cpp-only settings (see
    /// RuntimeProfile's doc comments); only show them for that runtime kind.
    /// </summary>
    public bool IsLlamaCpp => Kind == RuntimeKind.LlamaCpp;

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
    partial void OnKindChanged(RuntimeKind value)
    {
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(IsLlamaCpp));
    }
}
