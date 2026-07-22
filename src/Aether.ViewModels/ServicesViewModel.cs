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
    private readonly ModelProfileService?  _modelProfiles;
    private readonly ServerConfig          _config;
    private OrphanServerInfo? _orphanInfo;
    private string? _lastModelPathForDefaults;

    [ObservableProperty] private string       _name;
    [ObservableProperty] private string       _executablePath;
    [ObservableProperty] private string       _modelPath;
    /// <summary>r19 5.3: optional vision projector (--mmproj); empty means text-only.</summary>
    [ObservableProperty] private string       _mmprojPath = string.Empty;
    [ObservableProperty] private int          _port;
    [ObservableProperty] private int          _contextSize;
    [ObservableProperty] private int          _gpuLayers;
    [ObservableProperty] private int          _threads;
    [ObservableProperty] private int          _slots;
    [ObservableProperty] private bool         _embeddingsMode;
    [ObservableProperty] private bool         _autoStart;
    [ObservableProperty] private string       _extraArgs = string.Empty;

    // r18 04-llama-server-engine-options.md 4.1: first-class engine options, editable-form
    // fields on the server editor next to Context Size/GPU Layers/Threads/Slots.
    [ObservableProperty] private string       _kvCacheTypeK = "f16";
    [ObservableProperty] private string       _kvCacheTypeV = "f16";
    [ObservableProperty] private string       _flashAttention = "auto";
    [ObservableProperty] private bool         _contextShift;
    [ObservableProperty] private bool         _memoryLock;
    [ObservableProperty] private bool         _noMemoryMap;
    [ObservableProperty] private bool         _ngramSpeculative;
    [ObservableProperty] private string       _suggestEngineSettingsPreview = string.Empty;

    /// <summary>Verified accepted value set minus f32 (r18 04-llama-server-engine-options.md
    /// 4.0/4.1): f32 only wastes VRAM relative to f16/bf16 for the same precision class, so it
    /// is not offered as a first-class recommendation.</summary>
    public static IReadOnlyList<string> KvCacheTypeOptions { get; } =
        ["f16", "bf16", "q8_0", "q5_1", "q5_0", "q4_1", "q4_0", "iq4_nl"];

    public static IReadOnlyList<string> FlashAttentionOptions { get; } = ["auto", "on", "off"];

    [ObservableProperty] private ServerStatus _status    = ServerStatus.Stopped;
    [ObservableProperty] private string       _logOutput = string.Empty;
    [ObservableProperty] private string       _errorMessage = string.Empty;
    [ObservableProperty] private bool         _logExpanded  = false;
    [ObservableProperty] private bool         _isAutoTuning;
    [ObservableProperty] private string       _autoTuneStatus = string.Empty;
    [ObservableProperty] private bool         _hasOrphan;
    [ObservableProperty] private bool         _canStopOrphan;
    [ObservableProperty] private string       _orphanBannerText = string.Empty;
    [ObservableProperty] private string       _contextFitNote = string.Empty;
    [ObservableProperty] private bool         _hasContextFitWarning;

    /// <summary>r19 2.1: names where the current Context Size value came from ("Context from model card" / "Context from Auto Tune"), empty when the user set it directly.</summary>
    [ObservableProperty] private string       _contextSourceLabel = string.Empty;
    public bool HasContextSourceLabel => !string.IsNullOrEmpty(ContextSourceLabel);
    partial void OnContextSourceLabelChanged(string value) => OnPropertyChanged(nameof(HasContextSourceLabel));

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
        _config.MmprojPath != MmprojPath ||
        _config.Port != Port ||
        _config.ContextSize != ContextSize ||
        _config.GpuLayers != GpuLayers ||
        _config.Threads != Threads ||
        _config.Slots != Slots ||
        _config.EmbeddingsMode != EmbeddingsMode ||
        _config.AutoStart != AutoStart ||
        _config.ExtraArgs != ExtraArgs ||
        _config.KvCacheTypeK != KvCacheTypeK ||
        _config.KvCacheTypeV != KvCacheTypeV ||
        _config.FlashAttention != FlashAttention ||
        _config.ContextShift != ContextShift ||
        _config.MemoryLock != MemoryLock ||
        _config.NoMemoryMap != NoMemoryMap ||
        _config.NgramSpeculative != NgramSpeculative;

    /// <summary>
    /// Human-readable effective GPU offload for the Services card (r14 1.3):
    /// "all layers" for -1, "0 (CPU)" for 0, or the explicit layer count.
    /// </summary>
    public string EffectiveOffloadLabel => GpuLayers switch
    {
        < 0 => "all layers",
        0 => "0 (CPU)",
        var n => n.ToString()
    };
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
    private const long RamHeadroomBytes = 2_147_483_648; // 2 GiB, matches ModelFitEstimator's RAM headroom

    private HardwareProfile? _hardwareProfile;
    private GgufModelInfo? _ggufInfo;
    private int _contextFitGeneration;

    public UiBoundCollection<string> DetectedModelPaths { get; } = [];
    public UiBoundCollection<string> DetectedMmprojPaths { get; } = [];

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
        // r19 2.5: a model path browsed (or previously saved) from outside
        // the scanned assets root never appears in `found`; without the
        // manual free-text fallback box this round removed, the ComboBox
        // would otherwise render blank for it after every rescan.
        if (!string.IsNullOrWhiteSpace(current) && !DetectedModelPaths.Contains(current, StringComparer.OrdinalIgnoreCase))
            DetectedModelPaths.Insert(0, current);
        // DetectedModelPaths.Clear() fires a CollectionChanged Reset, which the
        // ComboBox bound to it (SelectedItem="{Binding ModelPath}", TwoWay by
        // default) reacts to by resetting its own selection to null - and because
        // the binding is TwoWay, that null immediately writes back into ModelPath,
        // before the foreach above ever repopulates the list. A bare
        // OnPropertyChanged(nameof(ModelPath)) only re-announces whatever ModelPath
        // holds *now* (already nulled by that point), so it cannot undo the loss.
        // Re-assigning the value captured before the refresh pushes the real path
        // back through the binding again, this time against the now-populated list.
        if (!string.IsNullOrWhiteSpace(current) && ModelPath != current)
            ModelPath = current;
    }

    /// <summary>r19 5.3: rescans for `mmproj-*.gguf` files beside the selected model whenever
    /// it changes, auto-filling the sole candidate when the field is still empty (never
    /// overwrites an explicit choice, following the same repair-on-Clear() pattern as
    /// <see cref="RefreshDetectedModels"/>).</summary>
    private void RefreshDetectedMmprojPaths(string modelPath)
    {
        var current = MmprojPath;
        DetectedMmprojPaths.Clear();

        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            var dir = Path.GetDirectoryName(modelPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "mmproj-*.gguf").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    DetectedMmprojPaths.Add(file);
            }
        }

        if (!string.IsNullOrWhiteSpace(current) && !DetectedMmprojPaths.Contains(current, StringComparer.OrdinalIgnoreCase))
            DetectedMmprojPaths.Insert(0, current);

        if (string.IsNullOrWhiteSpace(current) && DetectedMmprojPaths.Count == 1)
            MmprojPath = DetectedMmprojPaths[0];
        else if (!string.IsNullOrWhiteSpace(current) && MmprojPath != current)
            MmprojPath = current;
    }

    public ServerProcessViewModel(
        ServerConfig config,
        ISettingsService settings,
        RedactionService redactor,
        TrustService trust,
        IToastService toasts,
        IRuntimeLogService runtimeLogs,
        OrphanServerDetector? orphanDetector = null,
        HardwareProfile? hardwareProfile = null,
        ModelProfileService? modelProfiles = null)
    {
        _mgr = new ServerProcessManager(redactor);
        _config   = config;
        _settings = settings;
        _trust = trust;
        _toasts = toasts;
        _runtimeLogs = runtimeLogs;
        _orphanDetector = orphanDetector ?? new OrphanServerDetector();
        _hardwareProfile = hardwareProfile;
        _modelProfiles = modelProfiles;

        _name           = config.Name;
        _executablePath = config.ExecutablePath;
        _modelPath      = config.ModelPath;
        _mmprojPath     = config.MmprojPath;
        _lastModelPathForDefaults = string.IsNullOrWhiteSpace(config.ModelPath) ? null : config.ModelPath;
        _port           = config.Port;
        _contextSize    = config.ContextSize;
        _gpuLayers      = config.GpuLayers;
        _threads        = config.Threads;
        _slots          = config.Slots;
        _embeddingsMode = config.EmbeddingsMode;
        _autoStart      = config.AutoStart;
        _extraArgs      = config.ExtraArgs;
        _kvCacheTypeK   = config.KvCacheTypeK;
        _kvCacheTypeV   = config.KvCacheTypeV;
        _flashAttention = config.FlashAttention;
        _contextShift   = config.ContextShift;
        _memoryLock     = config.MemoryLock;
        _noMemoryMap    = config.NoMemoryMap;
        _ngramSpeculative = config.NgramSpeculative;

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
        RefreshDetectedMmprojPaths(ModelPath);
        ScheduleContextFitRefresh();
    }

    /// <summary>Called once by the parent <see cref="ServicesViewModel"/> when its
    /// process-lifetime hardware fetch completes (r17 01-gguf-context-and-tuning.md 1.4);
    /// a no-op constructor-time value means the very first render falls back to the flat
    /// threshold rule until this arrives.</summary>
    public void SetHardwareProfile(HardwareProfile profile)
    {
        _hardwareProfile = profile;
        ScheduleContextFitRefresh();
    }

    /// <summary>
    /// Kicks off (or re-kicks) the background GGUF header read backing
    /// <see cref="ContextFitNote"/>/<see cref="HasContextFitWarning"/>. A generation counter
    /// (r12 02-async-and-threading.md 2.3 pattern) discards a slower, older read that completes
    /// after a newer one was already scheduled, so a rapid second edit never gets overwritten
    /// by a stale result.
    /// </summary>
    private void ScheduleContextFitRefresh()
    {
        var generation = ++_contextFitGeneration;
        var modelPath = ModelPath;
        _ = RefreshContextFitAsync(generation, modelPath);
    }

    private async Task RefreshContextFitAsync(int generation, string modelPath)
    {
        GgufModelInfo? info = null;
        if (!string.IsNullOrWhiteSpace(modelPath)
            && modelPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
            && File.Exists(modelPath))
        {
            info = await Task.Run(() => GgufMetadataReader.TryRead(modelPath));
        }

        RunOnUi(() =>
        {
            if (generation != _contextFitGeneration)
                return;
            _ggufInfo = info;
            ApplyContextFitNote();
        });
    }

    private void ApplyContextFitNote()
    {
        var note = ComputeContextFitNote();
        ContextFitNote = note;
        HasContextFitWarning = !string.IsNullOrEmpty(note);
    }

    /// <summary>
    /// Hardware-aware context-fit assessment (r17 01-gguf-context-and-tuning.md 1.4), replacing
    /// the old flat <see cref="LargeContextSizeThreshold"/> rule with KV-cache-aware VRAM/RAM
    /// math whenever a GGUF header and hardware profile are both available. When either is
    /// unavailable this falls back to the old flat rule and wording so the warning never
    /// silently disappears on machines this can't do better for. Independent of that verdict,
    /// a training-context advisory (1.6) is appended whenever the configured context exceeds
    /// what the model was trained at - advisory only, never blocks Start, never edits the value.
    /// </summary>
    private string ComputeContextFitNote()
    {
        var hw = _hardwareProfile;
        var info = _ggufInfo;
        var flatNote = ContextSize > LargeContextSizeThreshold
            ? $"Large context ({ContextSize:N0} tokens) can spill out of VRAM, slowing prompt processing and increasing memory use."
            : string.Empty;

        if (hw is null || info is null)
            return flatNote;

        var fileSizeBytes = TryGetModelFileSizeBytes();
        var bpeK = KvCacheMath.ResolveBytesPerElement(KvCacheTypeK, ExtraArgs, isKeyCache: true);
        var bpeV = KvCacheMath.ResolveBytesPerElement(KvCacheTypeV, ExtraArgs, isKeyCache: false);
        var swaFull = KvCacheMath.HasSwaFull(ExtraArgs);
        var primary = string.Empty;

        if (fileSizeBytes is long size)
        {
            if (hw.MaxGpuVramBytes > 0 && GpuLayers != 0)
            {
                var projection = KvCacheMath.Project(size, info, ContextSize, GpuLayers, bpeK, bpeV, swaFull);
                if (projection is not null)
                {
                    var needed = projection.TotalBytes + KvCacheMath.GpuHeadroomBytes;
                    if (needed > hw.MaxGpuVramBytes)
                        primary = $"At {ContextSize:N0} context this model needs ~{FormatGb(needed)} (weights ~{FormatGb(projection.WeightsBytes)} + KV cache ~{FormatGb(projection.KvBytes)}); this GPU has {FormatGb(hw.MaxGpuVramBytes)}. Prompt processing will spill to system RAM.";
                }
                else
                {
                    primary = flatNote;
                }
            }
            else if (GpuLayers == 0)
            {
                var projection = KvCacheMath.Project(size, info, ContextSize, gpuLayers: -1, bpeK, bpeV, swaFull);
                if (projection is not null && hw.TotalRamBytes > 0)
                {
                    var needed = projection.TotalBytes + RamHeadroomBytes;
                    if (needed > hw.TotalRamBytes)
                        primary = $"At {ContextSize:N0} context this model needs ~{FormatGb(needed)} of RAM (weights ~{FormatGb(projection.WeightsBytes)} + KV cache ~{FormatGb(projection.KvBytes)}); this machine has {FormatGb(hw.TotalRamBytes)}.";
                }
                else
                {
                    primary = flatNote;
                }
            }
            else
            {
                primary = flatNote;
            }
        }
        else
        {
            primary = flatNote;
        }

        if (info.TrainingContextLength is int trainingCtx && trainingCtx > 0 && ContextSize > trainingCtx)
        {
            var advisory = $"This model was trained at {trainingCtx:N0} context; running beyond that can degrade quality.";
            primary = string.IsNullOrEmpty(primary) ? advisory : $"{primary} {advisory}";
        }

        return primary;
    }

    private long? TryGetModelFileSizeBytes()
    {
        try
        {
            return File.Exists(ModelPath) ? new FileInfo(ModelPath).Length : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatGb(long bytes) => $"{bytes / 1024d / 1024 / 1024:0.0} GB";

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
    private void BrowseMmproj()
    {
        RequestFilePicker?.Invoke(nameof(MmprojPath));
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
            // r17 01-gguf-context-and-tuning.md 1.5: a fresh read (cheap - the reader is
            // process-lifetime-cached) rather than the field this VM keeps for the context-fit
            // note, so a tune started right after a model-path edit never races that note's
            // own background refresh.
            var ggufInfo = File.Exists(ModelPath) ? await Task.Run(() => GgufMetadataReader.TryRead(ModelPath)) : null;
            var previousContext = ContextSize;

            var result = await ServerProcessManager.AutoTuneAsync(
                BuildConfig(),
                new Progress<string>(line =>
                {
                    LogOutput = string.IsNullOrEmpty(LogOutput)
                        ? line
                        : $"{LogOutput}\n{line}";
                }),
                ggufInfo: ggufInfo,
                hardware: _hardwareProfile);

            GpuLayers = result.GpuLayers;
            Threads = result.Threads;
            if (result.TunedContextSize is int tunedContext)
                ContextSize = tunedContext;
            await PersistTuneProfileAsync(result);
            await _settings.SaveAsync();
            AutoTuneStatus = BuildAutoTuneStatus(result, previousContext);
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

    /// <summary>
    /// r18 04-llama-server-engine-options.md 4.3: hardware-tier recommendation for Context Size,
    /// KV cache type, and Flash Attention. Same contract as <see cref="AutoTuneAsync"/>'s result:
    /// fills the editable form only, describes what changed, and saves nothing until the user
    /// clicks Save Config.
    /// </summary>
    [RelayCommand]
    private void SuggestEngineSettings()
    {
        if (!CanEdit) return;

        var hw = _hardwareProfile;
        if (hw is null || hw.MaxGpuVramBytes <= 0)
        {
            SuggestEngineSettingsPreview = "No GPU VRAM detected; nothing to suggest.";
            return;
        }

        var preset = EngineOptionPresets.Recommend(hw.MaxGpuVramBytes, _ggufInfo?.TrainingContextLength);

        var changes = new List<string>();
        if (ContextSize != preset.ContextSize)
            changes.Add($"Context Size {ContextSize:N0} -> {preset.ContextSize:N0}");
        if (!string.Equals(KvCacheTypeK, preset.KvCacheType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(KvCacheTypeV, preset.KvCacheType, StringComparison.OrdinalIgnoreCase))
            changes.Add($"KV cache {KvCacheTypeK}/{KvCacheTypeV} -> {preset.KvCacheType}");
        if (!string.Equals(FlashAttention, "auto", StringComparison.OrdinalIgnoreCase))
            changes.Add($"Flash Attention {FlashAttention} -> auto");

        if (changes.Count == 0)
        {
            SuggestEngineSettingsPreview = $"Already at the suggested settings for {FormatGb(hw.MaxGpuVramBytes)} VRAM.";
            return;
        }

        ContextSize = preset.ContextSize;
        KvCacheTypeK = preset.KvCacheType;
        KvCacheTypeV = preset.KvCacheType;
        FlashAttention = "auto";

        var kvNote = string.Equals(preset.KvCacheType, "f16", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" {preset.KvCacheType.ToUpperInvariant()} KV cache halves-to-quarters context memory, near-lossless; keep f16 if you prefer.";
        SuggestEngineSettingsPreview = $"Suggested for {FormatGb(hw.MaxGpuVramBytes)} VRAM: {string.Join("; ", changes)}. Not saved until you click Save Config.{kvNote}";
    }

    private string BuildAutoTuneStatus(ServerTuneResult result, int previousContext)
    {
        if (result.TunedContextSize is int tunedContext)
        {
            var layers = result.TotalLayers is int total ? $"all {total} GPU layers" : "all GPU layers";
            var vram = _hardwareProfile is { MaxGpuVramBytes: > 0 } hw ? $" in {FormatGb(hw.MaxGpuVramBytes)} VRAM" : string.Empty;
            // r18 01-finish-the-open-work.md 1.3: SuggestContextSize can now suggest raising
            // context, not just downshifting it, so the status must read correctly either way.
            return tunedContext > previousContext
                ? $"Auto-tune found headroom for a larger context: raised to {tunedContext:N0}{vram} (configured was {previousContext:N0}). Save and start the service."
                : $"Auto-tune verified {layers} at {tunedContext:N0} context (configured {previousContext:N0} does not fit{vram} with this model). Save and start the service.";
        }

        return result.TotalLayers is int totalLayers
            ? $"Auto-tune verified {result.GpuLayers}/{totalLayers} GPU layers with {result.Threads} thread(s). Save and start the service."
            : $"Auto-tune verified {result.GpuLayers} GPU layers with {result.Threads} thread(s). Save and start the service.";
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

    /// <summary>
    /// r19 2.2: after an llama.cpp update rewrites <c>ExecutablePath</c>
    /// directly on the underlying <see cref="ServerConfig"/> (a live
    /// in-place mutation, not a settings reload), this VM's own bound
    /// property is stale until re-synced - and <see cref="StartCoreAsync"/>
    /// would otherwise overwrite the fresh config value right back to the
    /// stale one via <c>SyncToConfig()</c> before ever starting. Call this
    /// before restarting a server programmatically after such a mutation.
    /// </summary>
    public void SyncExecutablePathFromConfig() => ExecutablePath = _config.ExecutablePath;

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

    private bool ApplyTuneProfileIfAvailable()
    {
        var profile = LlamaTuneProfileStore.Find(_settings.Settings, ModelPath);
        if (profile is null)
            return false;

        GpuLayers = profile.GpuLayers;
        Threads = profile.Threads;
        if (profile.ContextSize > 0)
            ContextSize = profile.ContextSize;
        if (!string.IsNullOrWhiteSpace(profile.ExtraArgs))
            ExtraArgs = profile.ExtraArgs;
        return true;
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
        _config.MmprojPath     = MmprojPath;
        _config.Port           = Port;
        _config.ContextSize    = ContextSize;
        _config.GpuLayers      = GpuLayers;
        _config.Threads        = Threads;
        _config.Slots          = Slots;
        _config.EmbeddingsMode = EmbeddingsMode;
        _config.AutoStart      = AutoStart;
        _config.ExtraArgs      = ExtraArgs;
        _config.KvCacheTypeK   = KvCacheTypeK;
        _config.KvCacheTypeV   = KvCacheTypeV;
        _config.FlashAttention = FlashAttention;
        _config.ContextShift   = ContextShift;
        _config.MemoryLock     = MemoryLock;
        _config.NoMemoryMap    = NoMemoryMap;
        _config.NgramSpeculative = NgramSpeculative;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(EffectiveOffloadLabel));
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
        MmprojPath     = MmprojPath,
        Port           = Port,
        ContextSize    = ContextSize,
        GpuLayers      = GpuLayers,
        Threads        = Threads,
        Slots          = Slots,
        EmbeddingsMode = EmbeddingsMode,
        AutoStart      = AutoStart,
        ExtraArgs      = ExtraArgs,
        KvCacheTypeK   = KvCacheTypeK,
        KvCacheTypeV   = KvCacheTypeV,
        FlashAttention = FlashAttention,
        ContextShift   = ContextShift,
        MemoryLock     = MemoryLock,
        NoMemoryMap    = NoMemoryMap,
        NgramSpeculative = NgramSpeculative
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
    partial void OnModelPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ScheduleContextFitRefresh();
        RepairDetectedModelPathsIfBrowsedOutsideRoot(value);
        ApplyModelDefaultsIfPathActuallyChanged(value);
        RefreshDetectedMmprojPaths(value);
    }
    partial void OnMmprojPathChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));

    /// <summary>
    /// r19 2.5: the ComboBox's SelectedItem binding can only display a value
    /// present in its ItemsSource. A path browsed (or previously saved) from
    /// outside the detected-models scan would otherwise render blank once
    /// the free-text fallback TextBox was removed.
    /// </summary>
    private void RepairDetectedModelPathsIfBrowsedOutsideRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!DetectedModelPaths.Contains(value, StringComparer.OrdinalIgnoreCase))
            DetectedModelPaths.Insert(0, value);
    }

    /// <summary>
    /// r19 2.1: applies precedence tune-profile &gt; model-card default &gt;
    /// leave-as-is when the selected model actually changes to a different
    /// file. <see cref="RefreshDetectedModels"/> re-assigns <see cref="ModelPath"/>
    /// back to its own current value to repair the ComboBox binding after a
    /// list rebuild; that reassignment must never re-apply defaults on top
    /// of values the user already edited.
    /// </summary>
    private void ApplyModelDefaultsIfPathActuallyChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _lastModelPathForDefaults = value;
            return;
        }
        if (string.Equals(value, _lastModelPathForDefaults, StringComparison.OrdinalIgnoreCase))
            return;
        _lastModelPathForDefaults = value;

        if (ApplyTuneProfileIfAvailable())
        {
            ContextSourceLabel = "Context from Auto Tune";
            return;
        }

        var card = _modelProfiles?.Get(value)
            ?? _modelProfiles?.Profiles.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p.ModelId), Path.GetFileName(value), StringComparison.OrdinalIgnoreCase));
        if (card is { DefaultContextSize: > 0 })
        {
            ContextSize = card.DefaultContextSize.Value;
            ContextSourceLabel = "Context from model card";
        }
        else
        {
            ContextSourceLabel = string.Empty;
        }
    }
    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnContextSizeChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ApplyContextFitNote();
    }
    partial void OnGpuLayersChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(EffectiveOffloadLabel));
        ApplyContextFitNote();
    }
    partial void OnThreadsChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnSlotsChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnEmbeddingsModeChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAutoStartChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnExtraArgsChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ExtraArgsTrustWarning));
        ApplyContextFitNote();
    }

    // r18 04-llama-server-engine-options.md 4.2: KV cache type changes the fit-math answer, so
    // the context-fit note must recompute when either dropdown changes, same as ExtraArgs above.
    partial void OnKvCacheTypeKChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ApplyContextFitNote();
    }
    partial void OnKvCacheTypeVChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(NeedsFlashAttentionForQuantizedV));
        ApplyContextFitNote();
    }
    partial void OnFlashAttentionChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(NeedsFlashAttentionForQuantizedV));
    }
    partial void OnContextShiftChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnMemoryLockChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnNoMemoryMapChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnNgramSpeculativeChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));

    /// <summary>
    /// r18 04-llama-server-engine-options.md 4.0: llama.cpp historically requires flash
    /// attention enabled to use a quantized V cache. Inform-only - never auto-changes either
    /// field; the user can launch with exactly what they chose regardless.
    /// </summary>
    public bool NeedsFlashAttentionForQuantizedV =>
        !string.Equals(KvCacheTypeV, "f16", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(KvCacheTypeV, "bf16", StringComparison.OrdinalIgnoreCase)
        && string.Equals(FlashAttention, "off", StringComparison.OrdinalIgnoreCase);

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
    private readonly ISystemInfoService? _systemInfo;
    private readonly ModelProfileService _modelProfiles;
    private HardwareProfile? _hardwareProfile;

    public UiBoundCollection<ServerProcessViewModel> Servers { get; } = [];
    public UiBoundCollection<RuntimeProfileViewModel> RuntimeProfiles { get; } = [];
    public event EventHandler? ServerAvailabilityChanged;
    private string? _lastAvailabilityFingerprint;

    /// <summary>
    /// Live "any managed server running" flag (r16 03-workbench-and-desktop.md
    /// 3.2), replacing the nav dot's old <c>AnyRunningConverter</c> binding.
    /// That converter only re-ran when the <see cref="Servers"/> PROPERTY
    /// changed (a full rebuild), not on a per-item <c>Status</c> transition
    /// or in-place collection mutation, so Start/Stop/crash left the dot
    /// showing a stale snapshot - a window r12's rebuild-storm fingerprint
    /// fix made longer, not shorter. Raised in the same places per-server
    /// Status changes already flow through.
    /// </summary>
    public bool AnyServerRunning => Servers.Any(s => s.Status == ServerStatus.Running);

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
        OrphanServerDetector? orphanDetector = null,
        ISystemInfoService? systemInfo = null,
        ModelProfileService? modelProfiles = null)
    {
        _settings = settings;
        _runtimeProfiles = runtimeProfiles;
        _toasts = toasts;
        _redactor = redactor;
        _trust = trust;
        _runtimeLogs = runtimeLogs;
        _orphanDetector = orphanDetector ?? new OrphanServerDetector();
        _systemInfo = systemInfo;
        _modelProfiles = modelProfiles ?? new ModelProfileService(settings);
        Rebuild();
        _settings.SettingsChanged += (_, _) => RunOnUi(Rebuild);
        if (_systemInfo is not null)
            _ = LoadHardwareProfileAsync();
    }

    /// <summary>Fetches the process-lifetime <see cref="HardwareProfile"/> once (r13 1.5 cache,
    /// reused here) and hands it to every server row so each can compute a KV-cache-aware
    /// context-fit warning (r17 01-gguf-context-and-tuning.md 1.4) instead of the flat
    /// threshold fallback.</summary>
    private async Task LoadHardwareProfileAsync()
    {
        var profile = await _systemInfo!.GetHardwareProfileAsync();
        RunOnUi(() =>
        {
            _hardwareProfile = profile;
            foreach (var server in Servers)
                server.SetHardwareProfile(profile);
        });
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
                var vm = new ServerProcessViewModel(cfg, _settings, _redactor, _trust, _toasts, _runtimeLogs, _orphanDetector, _hardwareProfile, _modelProfiles)
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

        OnPropertyChanged(nameof(AnyServerRunning));

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

    /// <summary>
    /// r19 2.2: stops every currently-Running managed server whose executable
    /// looks like a llama-server binary, ahead of an in-place llama.cpp
    /// update, so the update flow can restart precisely that set afterward
    /// (and so a superseded version directory becomes prunable immediately
    /// instead of staying locked until the next app restart). Returns the
    /// stopped servers' ids.
    /// </summary>
    public IReadOnlyList<string> StopRunningLlamaServersForUpdate()
    {
        var stopped = new List<string>();
        foreach (var server in Servers)
        {
            if (server.Status == ServerStatus.Running && LooksLikeLlamaServerExecutable(server.ExecutablePath))
            {
                server.StopIfRunning();
                stopped.Add(server.Id);
            }
        }
        return stopped;
    }

    /// <summary>Restarts exactly the servers named by id (r19 2.2), re-syncing each from its
    /// possibly just-updated <see cref="ServerConfig.ExecutablePath"/> first. Safe to call with
    /// ids for servers that no longer exist or are already running; both are no-ops.</summary>
    public async Task RestartServersAsync(IReadOnlyList<string> serverIds)
    {
        foreach (var id in serverIds)
        {
            var server = Servers.FirstOrDefault(s => s.Id == id);
            if (server is null || !server.IsStopped) continue;
            server.SyncExecutablePathFromConfig();
            await server.StartCommand.ExecuteAsync(null);
        }
    }

    private static bool LooksLikeLlamaServerExecutable(string executablePath) =>
        !string.IsNullOrWhiteSpace(executablePath)
        && Path.GetFileName(executablePath.Trim()).Contains("llama-server", StringComparison.OrdinalIgnoreCase);

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
        if (e.PropertyName == nameof(ServerProcessViewModel.Status))
            OnPropertyChanged(nameof(AnyServerRunning));

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
