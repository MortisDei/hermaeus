using System.Globalization;
using System.Runtime.InteropServices;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

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
    private readonly IActivityRecorder?    _activity;
    private readonly LocalModelCapabilityService? _capabilityService;
    private readonly IResourceCoordinator? _resourceCoordinator;
    private readonly AdaptiveInferenceExperienceService? _adaptiveExperience;
    private readonly RecommendationDerivationService? _recommendationDerivation;
    private LocalModelCapabilities? _localCapabilities;
    private ServerStatus _lastRecordedStatus = ServerStatus.Stopped;
    private ServerConfig                   _config;
    private OrphanServerInfo? _orphanInfo;
    private string? _lastModelPathForDefaults;
    private string? _modelPathForMmproj;

    [ObservableProperty] private string       _name;
    [ObservableProperty] private string       _executablePath;
    [ObservableProperty] private string       _modelPath;
    /// <summary>r19 5.3: optional vision projector (--mmproj); empty means text-only.</summary>
    [ObservableProperty] private string       _mmprojPath = string.Empty;
    /// <summary>Retains the configured projector path while controlling whether this server uses it.</summary>
    [ObservableProperty] private bool         _useProjector = true;
    [ObservableProperty] private int          _port;
    [ObservableProperty] private int          _contextSize;
    [ObservableProperty] private int          _gpuLayers;
    [ObservableProperty] private string       _gpuPlacementSelection = "CPU";
    [ObservableProperty] private int          _threads;
    [ObservableProperty] private int          _promptThreads;
    [ObservableProperty] private int          _slots;
    [ObservableProperty] private bool         _embeddingsMode;
    [ObservableProperty] private bool         _autoStart;
    [ObservableProperty] private bool         _preserveReasoning;
    [ObservableProperty] private string       _extraArgs = string.Empty;
    [ObservableProperty] private AdaptiveInferenceMode _adaptiveMode = AdaptiveInferenceMode.Fixed;
    [ObservableProperty] private int          _adaptiveMinimumContext;
    [ObservableProperty] private long         _adaptiveMinimumGpuHeadroomBytes = ResourceHeadroomPolicy.DefaultDeviceStabilityBytes;

    /// <summary>
    /// User-facing MiB projection of the persisted byte headroom value. The
    /// settings contract stays in bytes so admission math does not lose
    /// precision, while the editor avoids asking users to enter raw bytes.
    /// </summary>
    public double AdaptiveMinimumGpuHeadroomMiB
    {
        get => AdaptiveMinimumGpuHeadroomBytes / (1024d * 1024d);
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                value = 0;

            var bytes = value >= long.MaxValue / (1024d * 1024d)
                ? long.MaxValue
                : (long)Math.Round(value * 1024d * 1024d, MidpointRounding.AwayFromZero);
            AdaptiveMinimumGpuHeadroomBytes = bytes;
        }
    }
    [ObservableProperty] private bool         _adaptiveAllowGpuLayerReduction;
    [ObservableProperty] private bool         _adaptiveAllowContextReduction;
    [ObservableProperty] private bool         _adaptiveAllowKvPrecisionChange;
    [ObservableProperty] private bool         _adaptiveAllowCpuMoePlacement;
    [ObservableProperty] private bool         _adaptiveAllowMultiDevicePlacement;
    [ObservableProperty] private bool         _adaptivePreserveAcceleratedBackend = true;
    [ObservableProperty] private int          _adaptivePreferredEvidenceAgeDays = 7;
    public static IReadOnlyList<AdaptiveInferenceMode> AdaptiveModeOptions { get; } =
        [AdaptiveInferenceMode.Fixed, AdaptiveInferenceMode.Advise, AdaptiveInferenceMode.AdaptAtLaunch];

    // r18 04-llama-server-engine-options.md 4.1: first-class engine options, editable-form
    // fields on the server editor next to Context Size/GPU Layers/Threads/Slots.
    [ObservableProperty] private string       _kvCacheTypeK = "f16";
    [ObservableProperty] private string       _kvCacheTypeV = "f16";
    public string KvCacheType
    {
        get => KvCacheTypeK;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "f16" : value.Trim();
            KvCacheTypeK = normalized;
            KvCacheTypeV = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NeedsFlashAttentionForQuantizedV));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            ApplyContextFitNote();
        }
    }
    [ObservableProperty] private string       _flashAttention = "auto";
    [ObservableProperty] private bool         _contextShift;
    [ObservableProperty] private bool         _memoryLock;
    [ObservableProperty] private bool         _noMemoryMap;

    /// <summary>
    /// Mixture-of-Experts CPU offload, as text so the box can be left empty for
    /// "off". Empty or 0 means off; a positive N is --n-cpu-moe N; "all" is
    /// --cpu-moe. See ServerConfig.CpuMoeLayers for why this is not the same
    /// knob as GPU layers.
    /// </summary>
    [ObservableProperty] private string       _cpuMoeLayersText = string.Empty;
    // ── r27 03-drafting-and-proof.md 3.1: speculative decoding, as one section ──
    // The r18 4.4 bool owned a flag that is a list. A second bool beside it
    // would have given two knobs that both own --spec-type and can contradict
    // each other, which is how this area acquires a bug that only shows up in
    // one configuration.

    /// <summary>
    /// The underlying comma-separated `--spec-type` list. Still the stored
    /// shape, because the flag genuinely is a list and the two techniques
    /// compose; the checkboxes below are a view onto it, and each one only ever
    /// adds or removes its own token, so an exotic list set by hand in
    /// settings.json survives being toggled.
    /// </summary>
    [ObservableProperty] private string       _speculativeTypes = string.Empty;

    /// <summary>r27 follow-up: zero extra VRAM, drafts from the prompt and history itself (`ngram-mod`).</summary>
    public bool UseNgramDecoding
    {
        get => HasType(NgramType);
        set => SetType(NgramType, value);
    }

    /// <summary>r27 follow-up: drafts from the MTP head beside the model (`draft-mtp`).</summary>
    public bool UseDraftModelDecoding
    {
        get => HasType(DraftType);
        set => SetType(DraftType, value);
    }

    private const string NgramType = "ngram-mod";
    private const string DraftType = "draft-mtp";
    private readonly HashSet<string> _runtimeSpeculativeTypes = new(StringComparer.OrdinalIgnoreCase);
    private int _runtimeCapabilityGeneration;
    [ObservableProperty] private bool _runtimeCapabilitiesKnown;
    [ObservableProperty] private string _runtimeCapabilityStatus = "Checking selected llama-server capabilities.";

    public bool SupportsNgramDecoding => _runtimeSpeculativeTypes.Contains(NgramType);
    public bool SupportsDraftModelDecoding => _runtimeSpeculativeTypes.Contains(DraftType);
    public bool SupportsPromptThreads { get; private set; }
    public bool CanEditNgramDecoding => CanEdit && (SupportsNgramDecoding || UseNgramDecoding);
    public bool CanEditDraftModelDecoding => CanEdit && (SupportsDraftModelDecoding || UseDraftModelDecoding);
    public bool HasPromptThreadsControl => SupportsPromptThreads || PromptThreads > 0;
    public static IReadOnlyList<string> GpuPlacementOptions { get; } = ["CPU", "Auto", "All", "Exact"];
    public bool IsExactGpuPlacement => string.Equals(GpuPlacementSelection, "Exact", StringComparison.Ordinal);

    private bool HasType(string type) =>
        ParseTypes(SpeculativeTypes).Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));

    private void SetType(string type, bool enabled)
    {
        var types = ParseTypes(SpeculativeTypes);
        var present = types.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
        if (present == enabled)
            return;

        if (enabled)
            types.Add(type);
        else
            types.RemoveAll(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));

        SpeculativeTypes = string.Join(",", types);
        OnPropertyChanged(nameof(UseNgramDecoding));
        OnPropertyChanged(nameof(UseDraftModelDecoding));
    }
    [ObservableProperty] private string       _draftModelPath = string.Empty;
    [ObservableProperty] private string       _draftGpuLayersText = string.Empty;
    [ObservableProperty] private string       _speculativeNMaxText = string.Empty;
    [ObservableProperty] private string       _speculativeNMinText = string.Empty;
    [ObservableProperty] private string       _speculativePMinText = string.Empty;

    /// <summary>r27 3.4: the combined target-plus-draft VRAM estimate, information rather than a block.</summary>
    [ObservableProperty] private string       _draftFitNote = string.Empty;
    public bool HasDraftFitNote => !string.IsNullOrEmpty(DraftFitNote);
    partial void OnDraftFitNoteChanged(string value) => OnPropertyChanged(nameof(HasDraftFitNote));
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
    [ObservableProperty] private string       _gpuFitBreakdown = string.Empty;
    [ObservableProperty] private string       _admissionReceipt = string.Empty;
    public bool HasAdmissionReceipt => !string.IsNullOrWhiteSpace(AdmissionReceipt);
    public bool HasGpuFitBreakdown => !string.IsNullOrWhiteSpace(GpuFitBreakdown);
    partial void OnGpuFitBreakdownChanged(string value) => OnPropertyChanged(nameof(HasGpuFitBreakdown));

    /// <summary>Names where the current Context Size value came from, empty when the user set it directly.</summary>
    [ObservableProperty] private string       _contextSourceLabel = string.Empty;
    public bool HasContextSourceLabel => !string.IsNullOrEmpty(ContextSourceLabel);
    partial void OnContextSourceLabelChanged(string value) => OnPropertyChanged(nameof(HasContextSourceLabel));

    public string Id => _config.Id;
    public bool ReasoningPreserveAvailable => _modelProfiles?.Get(ModelPath)?.DefaultPreserveReasoning == true;
    public string ReasoningPreserveStatus => ReasoningPreserveAvailable
        ? "Template support is confirmed for this model."
        : "Waiting for a successful model capability probe.";
    public bool IsRunning  => Status == ServerStatus.Running;
    public ManagedRuntimeProcessIdentity? CurrentProcessIdentity => _mgr.CurrentProcessIdentity;
    public bool IsStopped  => Status is ServerStatus.Stopped or ServerStatus.Error;
    public bool IsStarting => Status == ServerStatus.Starting;
    public bool IsError    => Status == ServerStatus.Error;

    /// <summary>
    /// r27 01 1.3: when this server entered <see cref="ServerStatus.Starting"/>,
    /// so Chat can say how long it has been waiting. Null whenever the server is
    /// not starting. Settable so tests can drive an elapsed time without a clock.
    /// </summary>
    public DateTime? StartingSinceUtc { get; set; }
    public bool CanEdit => IsStopped && !IsAutoTuning;
    public bool HasUnsavedChanges =>
        _config.Name != Name ||
        _config.ExecutablePath != ExecutablePath ||
        _config.ModelPath != ModelPath ||
        _config.MmprojPath != MmprojPath ||
        _config.UseProjector != UseProjector ||
        _config.Port != Port ||
        _config.ContextSize != ContextSize ||
        PlacementCanonical(_config) != CurrentPlacementCanonical() ||
        _config.Threads != Threads ||
        _config.PromptThreads != PromptThreads ||
        _config.Slots != Slots ||
        _config.EmbeddingsMode != EmbeddingsMode ||
        _config.AutoStart != AutoStart ||
        _config.PreserveReasoning != PreserveReasoning ||
        _config.ExtraArgs != ExtraArgs ||
        EffectiveKvCacheType(_config) != KvCacheType ||
        _config.FlashAttention != FlashAttention ||
        _config.ContextShift != ContextShift ||
        _config.MemoryLock != MemoryLock ||
        _config.NoMemoryMap != NoMemoryMap ||
        _config.CpuMoeLayers != ParseCpuMoeLayers(CpuMoeLayersText) ||
        _config.AdaptiveEnvelope?.CanonicalValue != BuildAdaptiveEnvelope().CanonicalValue ||
        !SpeculativeMatchesConfig();

    /// <summary>
    /// Human-readable configured GPU placement for the Services card. Effective
    /// placement remains a runtime observation and is not guessed here.
    /// </summary>
    public string EffectiveOffloadLabel => GpuPlacementSelection switch
    {
        "CPU" => "0 (CPU)",
        "Auto" => "automatic placement",
        "All" => "all layers",
        "Exact" => $"{GpuLayers} layers (exact)",
        _ => "Unknown placement"
    };

    /// <summary>
    /// The URL RAG/chat actually reach this server at (Settings > RAG used to
    /// duplicate this as an editable "Embed URL" text field; it was always
    /// overwritten by this server's Port on save, so the port here is the
    /// single source of truth - this label just surfaces the result).
    /// </summary>
    public string EmbedUrlLabel => $"http://localhost:{Port}";
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
    private string? _autoSelectedMmprojPath;
    private string? _autoSelectedDraftModelPath;
    private bool _settingAutoMmproj;
    private bool _settingAutoDraftModel;

    public UiBoundCollection<string> DetectedModelPaths { get; } = [];
    public UiBoundCollection<string> DetectedMmprojPaths { get; } = [];

    public bool HasMissingModel => !string.IsNullOrWhiteSpace(ModelPath) && !File.Exists(ModelPath);
    public string ModelPathHint => HasMissingModel
        ? $"Configured model is missing: {ModelPath}. Browse for it or choose a verified model; Hermaeus will not substitute another file."
        : string.Empty;

    public bool HasMissingMmproj => !string.IsNullOrWhiteSpace(MmprojPath) && !File.Exists(MmprojPath);
    public string MmprojHint => HasMissingMmproj
        ? $"Configured projector is missing: {MmprojPath}. It is {(UseProjector ? "enabled" : "disabled")} for this server. Browse for it or clear it; the saved path and provenance stay visible until you repair or explicitly clear it."
        : string.Empty;

    [RelayCommand]
    private void ClearMmproj()
    {
        MmprojPath = string.Empty;
        _autoSelectedMmprojPath = null;
    }

    /// <summary>
    /// Local `mtp-*.gguf` files found beside the selected model are listed as
    /// candidates, exactly as <see cref="DetectedMmprojPaths"/> lists projector
    /// candidates. Their filenames do not establish compatibility.
    /// </summary>
    public UiBoundCollection<string> DetectedDraftModelPaths { get; } = [];

    /// <summary>Folder a model-path file picker should open in: the embeddings subfolder for the embeddings server, otherwise the models folder.</summary>
    public string SuggestedModelBrowseDirectory
    {
        get
        {
            var root = _settings.Settings.DataManagement.LocalAiAssetsRoot;
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;
            return EmbeddingsMode
                ? Hermaeus.Services.LocalAiAssetLocator.GetPreferredEmbeddingsDirectory(root)
                : Hermaeus.Services.LocalAiAssetLocator.Detect(root).ModelsDirectory;
        }
    }

    /// <summary>
    /// An unrelated settings save from anywhere else in the app (the Settings
    /// tab's SaveAsync(AppSettings, ...) overload swaps ISettingsService.Settings
    /// wholesale) otherwise leaves this row's _config pointing at the pre-swap
    /// object: still readable, but no longer part of the live settings tree. The
    /// next Start/Save on this row would silently mutate that orphaned object
    /// via SyncToConfig() and then serialize the *live* tree, discarding the
    /// edit with no error. Re-pointing _config at the fresh same-id instance
    /// (called from Rebuild for every already-known server) fixes that; bound
    /// display properties are left untouched so an in-progress unsaved edit in
    /// the form is never clobbered.
    /// </summary>
    public void RebindConfig(ServerConfig cfg) => _config = cfg;

    public void RefreshDetectedModels()
    {
        var root = _settings.Settings.DataManagement.LocalAiAssetsRoot;
        var found = EmbeddingsMode
            ? Hermaeus.Services.LocalAiAssetLocator.FindEmbeddingModels(root)
            : Hermaeus.Services.LocalAiAssetLocator.FindGgufModels(root);
        var current = ModelPath;
        DetectedModelPaths.Clear();
        foreach (var path in found)
            DetectedModelPaths.Add(path);
        // r19 2.5: a model path browsed (or previously saved) from outside
        // the scanned assets root never appears in `found`; without the
        // manual free-text fallback box this round removed, the ComboBox
        // would otherwise render blank for it after every rescan.
        if (!string.IsNullOrWhiteSpace(current) && !DetectedModelPaths.Any(path => ModelPathSafety.AreSameLocalPath(path, current)))
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

    /// <summary>Rescans for projector candidates beside the selected model. Candidates are
    /// displayed for explicit user selection only; a filename is not compatibility evidence
    /// and the sole candidate is never silently selected.</summary>
    private void RefreshDetectedMmprojPaths(string modelPath)
    {
        var modelChanged = !ModelPathSafety.AreSameLocalPath(_modelPathForMmproj, modelPath);
        var current = modelChanged ? string.Empty : MmprojPath;
        if (modelChanged && !string.IsNullOrWhiteSpace(MmprojPath))
        {
            _settingAutoMmproj = true;
            try { MmprojPath = string.Empty; }
            finally { _settingAutoMmproj = false; }
        }

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

        var hasCurrentCandidate = !string.IsNullOrWhiteSpace(current)
            && DetectedMmprojPaths.Any(path => ModelPathSafety.AreSameLocalPath(path, current));
        if (!string.IsNullOrWhiteSpace(current) && !hasCurrentCandidate)
            DetectedMmprojPaths.Insert(0, current);

        SetAutoMmprojPath(current, string.Empty);
        _modelPathForMmproj = string.IsNullOrWhiteSpace(modelPath) ? null : modelPath;
        OnPropertyChanged(nameof(HasMissingMmproj));
        OnPropertyChanged(nameof(MmprojHint));
    }

    /// <summary>
    /// Rescans for draft-head candidates whenever the model changes. Candidates
    /// are displayed for explicit user selection only. Filename and directory
    /// conventions are not compatibility evidence, so a sole candidate is not
    /// silently selected.
    /// unsloth ships the head in an `MTP/` subdirectory beside the model, so
    /// both that and the model's own directory are scanned.
    /// Populating this path does NOT enable speculative decoding. No flag is
    /// emitted until <see cref="UseDraftModelDecoding"/> is ticked, which is
    /// what keeps this discovery rather than the auto-selection r27 doc 03
    /// declined: nothing about the runtime configuration changes on its own.
    /// </summary>
    private void RefreshDetectedDraftModelPaths(string modelPath)
    {
        var current = DraftModelPath;
        DetectedDraftModelPaths.Clear();

        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            var dir = Path.GetDirectoryName(modelPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                foreach (var file in EnumerateDraftHeads(dir))
                    DetectedDraftModelPaths.Add(file);

                var mtpDir = Path.Combine(dir, "MTP");
                if (Directory.Exists(mtpDir))
                {
                    foreach (var file in EnumerateDraftHeads(mtpDir))
                        DetectedDraftModelPaths.Add(file);
                }
            }
        }

        var hasCurrentCandidate = !string.IsNullOrWhiteSpace(current)
            && DetectedDraftModelPaths.Any(path => ModelPathSafety.AreSameLocalPath(path, current));
        // A missing persisted path is not a candidate. Keeping it in the
        // dropdown made a deleted draft look like the only useful choice and
        // encouraged a launch with a dead companion. Preserve the path in the
        // editor so it can be explained and cleared, but list only files that
        // exist now.

        SetAutoDraftModelPath(current, string.Empty);

        OnPropertyChanged(nameof(HasDetectedDraftModel));
        OnPropertyChanged(nameof(DraftModelHint));
    }

    private void SetAutoMmprojPath(string path, string soleCandidate)
    {
        _settingAutoMmproj = true;
        try
        {
            if (!ModelPathSafety.AreSameLocalPath(MmprojPath, path))
                MmprojPath = path;
            _autoSelectedMmprojPath = ModelPathSafety.AreSameLocalPath(path, soleCandidate) ? path : null;
        }
        finally { _settingAutoMmproj = false; }
    }

    private void SetAutoDraftModelPath(string path, string soleCandidate)
    {
        _settingAutoDraftModel = true;
        try
        {
            if (!ModelPathSafety.AreSameLocalPath(DraftModelPath, path))
                DraftModelPath = path;
            _autoSelectedDraftModelPath = ModelPathSafety.AreSameLocalPath(path, soleCandidate) ? path : null;
        }
        finally { _settingAutoDraftModel = false; }
    }

    private static IEnumerable<string> EnumerateDraftHeads(string directory)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "mtp-*.gguf").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
            yield return file;
    }

    /// <summary>True when a draft head was found beside the model, so the draft checkbox is worth offering.</summary>
    public bool HasDetectedDraftModel => DetectedDraftModelPaths.Count > 0;

    public bool HasMissingDraftModel => !string.IsNullOrWhiteSpace(DraftModelPath) && !File.Exists(DraftModelPath);

    public bool HasDraftModelHint => HasMissingDraftModel || !HasDetectedDraftModel;

    /// <summary>Says why the draft checkbox is unavailable, rather than leaving it inert and unexplained.</summary>
    public string DraftModelHint => HasMissingDraftModel
        ? $"The saved draft-model path is stale: {DraftModelPath}. It is not a usable candidate. If this model has a trusted repository mapping, review its current known companions first; otherwise clear it or choose a manually verified companion. Hermaeus will not substitute another model."
        : HasDetectedDraftModel
            ? string.Empty
            : "No mtp-*.gguf draft head was found beside this model. Download one from the model's repository, or pick a file.";

    [RelayCommand]
    private void ClearDraftModel()
    {
        DraftModelPath = string.Empty;
        _autoSelectedDraftModelPath = null;
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
        ModelProfileService? modelProfiles = null,
        IActivityRecorder? activity = null,
        LocalModelCapabilityService? capabilityService = null,
        IResourceCoordinator? resourceCoordinator = null,
        AdaptiveInferenceExperienceService? adaptiveExperience = null,
        RecommendationDerivationService? recommendationDerivation = null)
    {
        _mgr = new ServerProcessManager(redactor, resourceCoordinator: resourceCoordinator);
        _config   = config;
        _settings = settings;
        _trust = trust;
        _toasts = toasts;
        _runtimeLogs = runtimeLogs;
        _orphanDetector = orphanDetector ?? new OrphanServerDetector();
        _hardwareProfile = hardwareProfile;
        _modelProfiles = modelProfiles;
        _activity = activity;
        _capabilityService = capabilityService;
        _resourceCoordinator = resourceCoordinator;
        _adaptiveExperience = adaptiveExperience;
        _recommendationDerivation = recommendationDerivation;

        _name           = config.Name;
        _executablePath = config.ExecutablePath;
        _modelPath      = config.ModelPath;
        _mmprojPath     = config.MmprojPath;
        _useProjector   = config.UseProjector;
        var adaptive = config.AdaptiveEnvelope ?? new AdaptiveInferenceEnvelope();
        _adaptiveMode = adaptive.Mode;
        _adaptiveMinimumContext = adaptive.MinimumContext;
        _adaptiveMinimumGpuHeadroomBytes = adaptive.MinimumGpuHeadroomBytes;
        _adaptiveAllowGpuLayerReduction = adaptive.AllowGpuLayerReduction;
        _adaptiveAllowContextReduction = adaptive.AllowContextReduction;
        _adaptiveAllowKvPrecisionChange = adaptive.AllowKvPrecisionChange;
        _adaptiveAllowCpuMoePlacement = adaptive.AllowCpuMoePlacement;
        _adaptiveAllowMultiDevicePlacement = adaptive.AllowMultiDevicePlacement;
        _adaptivePreserveAcceleratedBackend = adaptive.PreserveAcceleratedBackend;
        _adaptivePreferredEvidenceAgeDays = Math.Clamp((int)Math.Round(adaptive.PreferredEvidenceAge.TotalDays), 1, 30);
        _lastModelPathForDefaults = string.IsNullOrWhiteSpace(config.ModelPath) ? null : config.ModelPath;
        _modelPathForMmproj = string.IsNullOrWhiteSpace(config.ModelPath) ? null : config.ModelPath;
        _port           = config.Port;
        _contextSize    = config.ContextSize;
        _gpuLayers      = config.GpuLayers;
        _gpuPlacementSelection = PlacementSelection(config);
        _threads        = config.Threads;
        _promptThreads  = config.PromptThreads;
        _slots          = config.Slots;
        _embeddingsMode = config.EmbeddingsMode;
        _autoStart      = config.AutoStart;
        _preserveReasoning = config.PreserveReasoning;
        _extraArgs      = config.ExtraArgs;
        _kvCacheTypeK   = EffectiveKvCacheType(config);
        _kvCacheTypeV   = EffectiveKvCacheType(config);
        _flashAttention = config.FlashAttention;
        _contextShift   = config.ContextShift;
        _memoryLock     = config.MemoryLock;
        _noMemoryMap    = config.NoMemoryMap;
        _cpuMoeLayersText = FormatCpuMoeLayers(config.CpuMoeLayers);
        var speculative = config.Speculative ?? new SpeculativeDecodingConfig();
        _speculativeTypes     = string.Join(",", speculative.Types);
        _draftModelPath       = speculative.DraftModelPath;
        _draftGpuLayersText   = speculative.DraftGpuLayers?.ToString() ?? string.Empty;
        _speculativeNMaxText  = speculative.NMax?.ToString() ?? string.Empty;
        _speculativeNMinText  = speculative.NMin?.ToString() ?? string.Empty;
        _speculativePMinText  = speculative.PMin?.ToString("0.###") ?? string.Empty;

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
            RecordServerActivity(s);
        });

        _mgr.LogLine += line => RunOnUi(() =>
        {
            LogOutput = _mgr.GetLog();
            if (!string.IsNullOrWhiteSpace(line))
                _runtimeLogs.Add(MapLog(line));
        });

        RefreshDetectedModels();
        RefreshDetectedMmprojPaths(ModelPath);
        RefreshDetectedDraftModelPaths(ModelPath);
        ScheduleContextFitRefresh();
        _ = RefreshRuntimeCapabilitiesAsync(ExecutablePath);
    }

    /// <summary>doc 04 4.2: managed server start, stop, and crash all record through
    /// Activity, fire-and-forget, off any hot path. Only real transitions
    /// (not construction-time no-ops) are recorded.</summary>
    private void RecordServerActivity(ServerStatus s)
    {
        if (_activity is null || s == _lastRecordedStatus) return;
        var previous = _lastRecordedStatus;
        _lastRecordedStatus = s;

        switch (s)
        {
            case ServerStatus.Running:
                _ = _activity.RecordAsync("services.server-start", Id, ActivityOutcome.Succeeded, $"{Name} started");
                break;
            case ServerStatus.Error:
                _ = _activity.RecordAsync("services.server-crash", Id, ActivityOutcome.Failed, $"{Name} failed", ErrorMessage);
                break;
            case ServerStatus.Stopped when previous == ServerStatus.Running:
                _ = _activity.RecordAsync("services.server-stop", Id, ActivityOutcome.Succeeded, $"{Name} stopped");
                break;
        }
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
        GpuFitBreakdown = ComputeGpuFitBreakdown();
        var note = ComputeContextFitNote();
        ContextFitNote = note;
        HasContextFitWarning = !string.IsNullOrEmpty(note);
    }

    private string ComputeGpuFitBreakdown()
    {
        if (string.Equals(GpuPlacementSelection, "Auto", StringComparison.Ordinal))
            return "GPU fit: Unknown until the selected runtime reports effective placement.";

        if (_hardwareProfile is null || _ggufInfo is null || TryGetModelFileSizeBytes() is not long modelBytes)
            return string.Empty;

        var companions = new List<FitCompanionInput>();
        if (UseProjector)
            AddCompanion("Vision projector", MmprojPath, FitPlacement.Unknown);
        if (UseDraftModelDecoding)
            AddCompanion("Speculative draft model", DraftModelPath, FitPlacement.Unknown);

        var prediction = ModelFitPredictor.Predict(new ModelFitPredictionRequest(
            null,
            modelBytes,
            ContextSize,
            GpuLayers,
            Slots,
            KvCacheTypeK,
            KvCacheTypeV,
            CapabilityStateForKv(KvCacheTypeK),
            CapabilityStateForKv(KvCacheTypeV),
            KvCacheMath.HasSwaFull(ExtraArgs),
            ParseCpuMoeLayers(CpuMoeLayersText),
            _hardwareProfile,
            companions), _ggufInfo);
        return ModelFitPredictor.FormatBreakdown(prediction);

        void AddCompanion(string name, string path, FitPlacement placement)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var file = new FileInfo(path);
                if (file.Exists && file.Length > 0)
                {
                    companions.Add(new FitCompanionInput(
                        name, file.Length, placement,
                        EvidenceOrigin.DeterministicCalculation,
                        "Separate companion file allocation using its observed file size."));
                    return;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            companions.Add(new FitCompanionInput(
                name, 0, FitPlacement.Unknown,
                EvidenceOrigin.DeterministicCalculation,
                "The configured companion file size is unavailable."));
        }
    }

    private CapabilityState CapabilityStateForKv(string type) =>
        _localCapabilities?.Observations?.FirstOrDefault(observation =>
            string.Equals(observation.CapabilityId, $"runtime.kv.type.{type.ToLowerInvariant()}", StringComparison.Ordinal))?.State
        ?? CapabilityState.Unknown;

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

        if (string.Equals(GpuPlacementSelection, "Auto", StringComparison.Ordinal))
            return "GPU fit is Unknown until the selected runtime reports effective placement.";

        var fileSizeBytes = TryGetModelFileSizeBytes();
        var bpeK = KvCacheMath.ResolveBytesPerElement(KvCacheType, ExtraArgs, isKeyCache: true);
        var bpeV = KvCacheMath.ResolveBytesPerElement(KvCacheType, ExtraArgs, isKeyCache: false);
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
            var file = new FileInfo(ModelPath);
            return file.Exists && file.Length > 0 ? file.Length : null;
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
        await RunOnUiAsync(() =>
        {
            ApplyOrphanDetectionResult(info);
            return Task.CompletedTask;
        });
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
        SyncToConfig();
        await SaveConfigAsync();
        if (_resourceCoordinator is null)
        {
            ErrorMessage = "Resource admission is unavailable; the managed server was not started.";
            Status = ServerStatus.Error;
            NotifyStatusProps();
            return;
        }

        var config = BuildConfig();
        _resourceCoordinator.RegisterConsumer(ResourceAllocationFactory.ManagedServerConsumer(config));

        var envelope = config.AdaptiveEnvelope ?? new AdaptiveInferenceEnvelope();
        try
        {
            if (envelope.Mode == AdaptiveInferenceMode.Fixed)
            {
                await StartCandidateAsync(config, AdaptiveInferencePlanner.HeadroomPolicy(config), ct);
                return;
            }

            var runtime = await LocalModelCapabilityService.ProbeRuntimeAsync(config.ExecutablePath, ct);
            var model = File.Exists(config.ModelPath)
                ? await Task.Run(() => GgufMetadataReader.TryRead(config.ModelPath), ct)
                : null;
            var planningRequest = CreateAdmissionRequest(config, AdaptiveInferencePlanner.HeadroomPolicy(config));
            var planningSnapshot = await _resourceCoordinator.PlanAsync(planningRequest, ct);
            var adaptive = AdaptiveInferencePlanner.Build(config, planningSnapshot, runtime, model);
            var runtimeIdentity = await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(
                config.ExecutablePath, runtime.VersionOrHelpText, ct);
            var modelIdentity = RuntimeIdentityFactory.CreateModelIdentity(config.ModelPath, model);
            var configurationIdentity = ConfigurationIdentityFactory.Create(config).StableId;
            var preference = _adaptiveExperience is null
                ? null
                : await _adaptiveExperience.FindPreferredCandidateAsync(
                    planningSnapshot,
                    runtimeIdentity,
                    modelIdentity,
                    configurationIdentity,
                    envelope,
                    ct: ct);
            adaptive = AdaptiveInferencePlanner.PreferCandidate(adaptive, preference?.CandidateId);
            AdmissionReceipt = FormatAdaptivePlan(adaptive, planningSnapshot);
            if (preference is not null)
                AdmissionReceipt += $" Preferred recent compatible success: {preference.CandidateId}.";

            if (envelope.Mode == AdaptiveInferenceMode.Advise)
                return;

            var failures = new List<string>();
            foreach (var candidate in adaptive.Candidates)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var candidateConfig = candidate.Configuration;
                    var policy = AdaptiveInferencePlanner.HeadroomPolicy(candidateConfig);
                    var request = CreateAdmissionRequest(candidateConfig, policy, allowUnknown: false);
                    await using var lease = await _resourceCoordinator.AcquireAsync(request, ct);
                    AdmissionReceipt = $"Adaptive candidate {candidate.Ordinal + 1}: {candidate.Reason} {FormatAdmissionReceipt(lease.Plan)}";
                    if (BeforeStartAsync is not null)
                        await BeforeStartAsync(this);
                    await _mgr.StartAsync(candidateConfig, lease, ct);
                    var launch = _mgr.LastLaunchResult;
                    await TryRecordAdaptiveOutcomeAsync(
                        config,
                        lease.Plan,
                        runtimeIdentity,
                        modelIdentity,
                        configurationIdentity,
                        candidate,
                        launch,
                        ct);

                    if (_mgr.Status == ServerStatus.Running)
                    {
                        if (candidate.RequiresEffectiveObservation && _mgr.LastEffectiveLaunch?.IsAuditable != true)
                        {
                            failures.Add($"{candidate.CandidateId}: effective placement/context remained Unknown after health.");
                            await _mgr.StopAsync();
                            ErrorMessage = "Adaptive launch stopped because the selected runtime did not expose an auditable effective placement and context. No fallback was attempted.";
                            Status = ServerStatus.Error;
                            NotifyStatusProps();
                            return;
                        }
                        return;
                    }

                    failures.Add($"{candidate.CandidateId}: {launch.FailureKind}. {launch.ErrorMessage}");
                    if (launch.FailureKind != ServerLaunchFailureKind.ResourceExhaustion)
                        break;
                    await _mgr.StopAsync();
                }
                catch (ResourceAdmissionException ex)
                {
                    AdmissionReceipt = $"Adaptive candidate {candidate.Ordinal + 1}: {candidate.Reason} {FormatAdmissionReceipt(ex.Plan)}";
                    failures.Add($"{candidate.CandidateId}: admission refused because {ex.Plan.Feasibility}.");
                    if (ex.Plan.Feasibility != ResourcePlanFeasibility.DoesNotFit)
                        break;
                }
            }

            ErrorMessage = failures.Count == 0
                ? "No bounded adaptive launch candidate was available."
                : $"No bounded adaptive launch candidate started successfully.\n\n{string.Join("\n", failures)}";
            Status = ServerStatus.Error;
            NotifyStatusProps();
        }
        catch (ResourceAdmissionException ex)
        {
            AdmissionReceipt = FormatAdmissionReceipt(ex.Plan);
            ErrorMessage = ex.Message;
            Status = ServerStatus.Error;
            NotifyStatusProps();
        }
    }

    private ResourceAdmissionRequest CreateAdmissionRequest(
        ServerConfig config,
        ResourceHeadroomPolicy policy,
        bool allowUnknown = true) =>
        new(
            config.Id,
            ResourceAllocationFactory.ManagedServerProposal(config),
            policy,
            callerId: $"services.server.{config.Id}",
            allowUnknown: allowUnknown);

    private async Task StartCandidateAsync(ServerConfig config, ResourceHeadroomPolicy policy, CancellationToken ct)
    {
        var request = CreateAdmissionRequest(config, policy);
        await using var lease = await _resourceCoordinator!.AcquireAsync(request, ct);
        AdmissionReceipt = FormatAdmissionReceipt(lease.Plan);
        if (BeforeStartAsync is not null)
            await BeforeStartAsync(this);
        await _mgr.StartAsync(config, lease, ct);
    }

    private async Task TryRecordAdaptiveOutcomeAsync(
        ServerConfig configured,
        ResourceWorkloadPlan workload,
        RuntimeIdentityV2 runtime,
        ModelIdentityV2 model,
        string configurationIdentity,
        AdaptiveInferenceCandidate candidate,
        ServerLaunchResult result,
        CancellationToken ct)
    {
        if (_adaptiveExperience is null)
            return;

        try
        {
            await _adaptiveExperience.RecordAsync(
                workload,
                runtime,
                model,
                configurationIdentity,
                candidate.CandidateId,
                candidate.ChangedFields,
                result,
                ct);

            if (_recommendationDerivation is not null
                && candidate.ChangesConfiguration
                && result.FailureKind == ServerLaunchFailureKind.None
                && result.EffectiveLaunch?.IsAuditable == true)
            {
                var currentIdentity = ConfigurationIdentityFactory.Create(configured);
                var patch = ManagedServerRecommendationPatch.Create(configured.Id, configured, candidate.Configuration);
                var now = DateTime.UtcNow;
                var recommendation = await _recommendationDerivation.DeriveAsync(new RecommendationProposal(
                    RecommendationKind.RuntimeConfiguration,
                    configured.Id,
                    currentIdentity.StableId,
                    patch,
                    [new RecommendationEvidenceReference(
                        $"adaptive-effective-{candidate.CandidateId}",
                        "adaptive-effective-launch",
                        Required: true,
                        CapabilityState.Available,
                        now,
                        configured.AdaptiveEnvelope?.PreferredEvidenceAge)],
                    [new RecommendationCondition("candidate", candidate.CandidateId)],
                    [new RecommendationTradeoff("restart", "requires-explicit-restart")],
                    "compatible-proven-launch",
                    1,
                    "adaptive-effective-values",
                    now,
                    currentIdentity.Completeness == IdentityCompleteness.Complete,
                    TargetExists: true,
                    RequiredEvidenceRevoked: false,
                    Contradicted: false,
                    RequiredEvidenceExpired: false,
                    MinimumFactsComplete: currentIdentity.Completeness == IdentityCompleteness.Complete
                        && runtime.Completeness == IdentityCompleteness.Complete
                        && model.Completeness == IdentityCompleteness.Complete
                        && workload.HardwareIdentityComplete,
                    Actionable: true,
                    ExpiresAtUtc: now + (configured.AdaptiveEnvelope?.PreferredEvidenceAge ?? TimeSpan.FromDays(7))));
                AdmissionReceipt += $" Reviewable configuration recommendation: {recommendation.Id}.";
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            AdmissionReceipt += " Adaptive outcome persistence timed out.";
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            AdmissionReceipt += " Adaptive outcome persistence was unavailable.";
        }
    }

    private static string FormatAdaptivePlan(AdaptiveInferencePlan plan, ResourceWorkloadPlan workload)
    {
        var candidates = plan.Candidates.Count == 0
            ? "none"
            : string.Join("; ", plan.Candidates.Select(candidate =>
                $"{candidate.Ordinal + 1}. {candidate.CandidateId} ({string.Join(", ", candidate.ChangedFields.DefaultIfEmpty("unchanged"))})"));
        var unavailable = plan.UnavailableReasons.Count == 0
            ? string.Empty
            : $" Unavailable/Unknown: {string.Join(" ", plan.UnavailableReasons)}";
        return $"Adaptive {plan.Mode} plan from {workload.Feasibility}: {candidates}.{unavailable}";
    }

    private static string FormatAdmissionReceipt(ResourceWorkloadPlan plan)
    {
        var unknown = plan.UnknownComponents.Count == 0 ? "none" : $"{plan.UnknownComponents.Count} Unknown";
        var system = plan.SystemRemainingBytes.HasValue
            ? SystemOverviewViewModel.FormatBytes(plan.SystemRemainingBytes.Value)
            : "Unknown";
        var devices = plan.DeviceHeadroom.Count == 0
            ? "no device total"
            : string.Join(", ", plan.DeviceHeadroom.Select(device =>
                $"{device.DeviceId}: {(device.RemainingBytes.HasValue ? SystemOverviewViewModel.FormatBytes(device.RemainingBytes.Value) : "Unknown")} remaining"));
        return $"Workload fit: {plan.Feasibility}; system headroom {system}; {devices}; {unknown} component(s). Snapshot {plan.SnapshotId}.";
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
    private void BrowseDraftModel()
    {
        RequestFilePicker?.Invoke(nameof(DraftModelPath));
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

    /// <summary>
    /// r27 01 1.2: the auto-start predicate, exposed so
    /// <see cref="ServicesViewModel.SelectAutoStartTargets"/> can group by port
    /// without restating the condition in a second place.
    /// </summary>
    public bool WillAutoStart => AutoStart && !string.IsNullOrWhiteSpace(ModelPath);

    public async Task AutoStartIfConfiguredAsync()
    {
        if (WillAutoStart)
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
    /// Stops this managed runtime at an awaited process boundary. Exclusive
    /// workloads such as Lab must not begin loading a second model until the
    /// source process has actually released its model memory.
    /// </summary>
    public Task StopAndWaitAsync() => _mgr.StopAsync();

    /// <summary>Synchronizes the bound status after a programmatic start has
    /// completed. The manager is authoritative, while its UI event is queued
    /// through the dispatcher and can otherwise arrive after Lab checks it.</summary>
    public void RefreshStatusFromManager()
    {
        Status = _mgr.Status;
        ErrorMessage = _mgr.ErrorMessage;
        NotifyStatusProps();
    }

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

    /// <summary>Refreshes the displayed model path after another service has
    /// changed the live server configuration, before a programmatic restart.
    /// </summary>
    public void SyncModelPathFromConfig() => ModelPath = _config.ModelPath;

    public async Task SelectModelAndRestartAsync(string modelPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            return;

        var normalized = Path.GetFullPath(modelPath);
        var current = string.IsNullOrWhiteSpace(ModelPath) ? string.Empty : Path.GetFullPath(ModelPath);
        var modelChanged = !ModelPathSafety.AreSameLocalPath(current, normalized);

        if (Status is ServerStatus.Running or ServerStatus.Starting && modelChanged)
            StopIfRunning();

        if (modelChanged)
        {
            ModelPath = normalized;
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

    private Task PersistTuneProfileAsync(ServerTuneResult? result = null)
    {
        LlamaTuneProfileStore.Upsert(_settings.Settings, ModelPath, ContextSize, ExtraArgs, GpuLayers, Threads, result, BuildGpuPlacement());
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
        _config.UseProjector   = UseProjector;
        _config.Port           = Port;
        _config.ContextSize    = ContextSize;
        var placement = BuildGpuPlacement();
        _config.GpuPlacement  = placement;
        _config.GpuLayers      = placement.LegacyGpuLayers ?? 0;
        _config.Threads        = Threads;
        _config.PromptThreads  = PromptThreads;
        _config.Slots          = Slots;
        _config.EmbeddingsMode = EmbeddingsMode;
        _config.AutoStart      = AutoStart;
        _config.PreserveReasoning = PreserveReasoning;
        _config.ReasoningPreserveSupported = ReasoningPreserveAvailable;
        _config.ExtraArgs      = ExtraArgs;
        _config.KvCacheType    = KvCacheType;
        _config.KvCacheTypeK   = KvCacheType;
        _config.KvCacheTypeV   = KvCacheType;
        _config.FlashAttention = FlashAttention;
        _config.ContextShift   = ContextShift;
        _config.MemoryLock     = MemoryLock;
        _config.NoMemoryMap    = NoMemoryMap;
        _config.CpuMoeLayers   = ParseCpuMoeLayers(CpuMoeLayersText);
        _config.Speculative    = BuildSpeculative();
        _config.AdaptiveEnvelope = BuildAdaptiveEnvelope();
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(EffectiveOffloadLabel));
        OnPropertyChanged(nameof(ExtraArgsTrustWarning));

        // Managed servers are always local processes, so their configured port is the
        // single source of truth. Without this, changing the port here left
        // Llm.LlamaCppBaseUrl / Rag.EmbeddingBaseUrl (what chat/benchmark/RAG actually
        // connect to) pointing at the old port, silently breaking model listing,
        // generation, and embedding health checks.
        if (EmbeddingsMode)
        {
            _settings.Settings.Rag.EmbeddingBaseUrl = $"http://localhost:{Port}";

            // Settings > RAG used to have its own "Embed model" picker that pushed a
            // name down to this card's ModelPath; that duplicated this card, so this
            // card is now the only place the model is chosen and pushes the name the
            // other direction instead - RagViewModel's dataset/reindex tracking reads
            // Rag.EmbeddingModel by name, not by file path.
            if (!string.IsNullOrWhiteSpace(ModelPath))
                _settings.Settings.Rag.EmbeddingModel = Path.GetFileNameWithoutExtension(ModelPath);
        }
        else
            SyncChatBaseUrlToPort();
    }

    private AdaptiveInferenceEnvelope BuildAdaptiveEnvelope() => new()
    {
        Mode = AdaptiveMode,
        MinimumContext = AdaptiveMinimumContext,
        MinimumGpuHeadroomBytes = AdaptiveMinimumGpuHeadroomBytes,
        AllowGpuLayerReduction = AdaptiveAllowGpuLayerReduction,
        AllowContextReduction = AdaptiveAllowContextReduction,
        AllowKvPrecisionChange = AdaptiveAllowKvPrecisionChange,
        AllowCpuMoePlacement = AdaptiveAllowCpuMoePlacement,
        AllowMultiDevicePlacement = AdaptiveAllowMultiDevicePlacement,
        PreserveAcceleratedBackend = AdaptivePreserveAcceleratedBackend,
        PreferredEvidenceAge = TimeSpan.FromDays(Math.Clamp(AdaptivePreferredEvidenceAgeDays, 1, 30))
    };

    private void SyncChatBaseUrlToPort()
    {
        var url = $"http://localhost:{Port}";
        _settings.Settings.Llm.LlamaCppBaseUrl = url;

        var linked = _settings.Settings.RuntimeProfiles
            .FirstOrDefault(p => p.Kind == RuntimeKind.LlamaCpp && p.LinkedServerId == _config.Id);
        if (linked is not null)
            linked.BaseUrl = url;
    }

    private GpuPlacementIntent BuildGpuPlacement() => GpuPlacementSelection switch
    {
        "CPU" => GpuPlacementIntent.Cpu(),
        "Auto" => GpuPlacementIntent.Auto(),
        "All" => GpuPlacementIntent.All(),
        "Exact" => GpuPlacementIntent.Exact(GpuLayers),
        _ => GpuPlacementIntent.Cpu()
    };

    private string CurrentPlacementCanonical() => BuildGpuPlacement().CanonicalValue;

    private static string PlacementCanonical(ServerConfig config) =>
        config.TryGetGpuPlacement(out var placement, out _)
            ? placement!.CanonicalValue
            : "invalid";

    private static string PlacementSelection(ServerConfig config)
    {
        if (!config.TryGetGpuPlacement(out var placement, out _))
            return "CPU";

        return placement!.Kind switch
        {
            GpuPlacementKind.Cpu => "CPU",
            GpuPlacementKind.Auto => "Auto",
            GpuPlacementKind.All => "All",
            GpuPlacementKind.Exact => "Exact",
            _ => "CPU"
        };
    }

    /// <summary>
    /// The editor's current state as a <see cref="ServerConfig"/>, without
    /// touching the saved one. Public because it is the honest way to ask
    /// "what would this server launch with right now".
    /// </summary>
    public ServerConfig BuildConfig() => new()
    {
        Id             = Id,
        Name           = Name,
        ExecutablePath = ExecutablePath,
        ModelPath      = ModelPath,
        MmprojPath     = MmprojPath,
        UseProjector   = UseProjector,
        Port           = Port,
        ContextSize    = ContextSize,
        GpuLayers      = GpuLayers,
        GpuPlacement   = BuildGpuPlacement(),
        Threads        = Threads,
        PromptThreads  = PromptThreads,
        Slots          = Slots,
        EmbeddingsMode = EmbeddingsMode,
        AutoStart      = AutoStart,
        PreserveReasoning = PreserveReasoning,
        ReasoningPreserveSupported = ReasoningPreserveAvailable,
        ExtraArgs      = ExtraArgs,
        KvCacheType    = KvCacheType,
        KvCacheTypeK   = KvCacheType,
        KvCacheTypeV   = KvCacheType,
        FlashAttention = FlashAttention,
        ContextShift   = ContextShift,
        MemoryLock     = MemoryLock,
        NoMemoryMap    = NoMemoryMap,
        CpuMoeLayers   = ParseCpuMoeLayers(CpuMoeLayersText),
        Speculative    = BuildSpeculative(),
        AdaptiveEnvelope = BuildAdaptiveEnvelope()
    };

    /// <summary>
    /// r27 3.1: the edited section, parsed back into config shape. Text boxes
    /// rather than numeric spinners because every one of these is optional:
    /// blank means "leave the server's own default alone" and emits no flag.
    /// </summary>
    private SpeculativeDecodingConfig BuildSpeculative() => new()
    {
        Types = ParseTypes(SpeculativeTypes),
        DraftModelPath = DraftModelPath?.Trim() ?? string.Empty,
        DraftGpuLayers = ParseOptionalInt(DraftGpuLayersText),
        NMax = ParseOptionalInt(SpeculativeNMaxText),
        NMin = ParseOptionalInt(SpeculativeNMinText),
        PMin = ParseOptionalDouble(SpeculativePMinText)
    };

    public static List<string> ParseTypes(string? text) =>
        (text ?? string.Empty)
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int? ParseOptionalInt(string? text) =>
        int.TryParse((text ?? string.Empty).Trim(), out var value) ? value : null;

    private static double? ParseOptionalDouble(string? text) =>
        double.TryParse((text ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private bool SpeculativeMatchesConfig()
    {
        var saved = _config.Speculative ?? new SpeculativeDecodingConfig();
        var edited = BuildSpeculative();
        return saved.Types.SequenceEqual(edited.Types, StringComparer.OrdinalIgnoreCase)
            && string.Equals(saved.DraftModelPath, edited.DraftModelPath, StringComparison.Ordinal)
            && saved.DraftGpuLayers == edited.DraftGpuLayers
            && saved.NMax == edited.NMax
            && saved.NMin == edited.NMin
            && saved.PMin == edited.PMin;
    }

    private void NotifyStatusProps()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditNgramDecoding));
        OnPropertyChanged(nameof(CanEditDraftModelDecoding));
        OnPropertyChanged(nameof(StatusLabel));
    }

    partial void OnStatusChanged(ServerStatus value)
    {
        // r27 01 1.3: stamped on the transition into Starting and cleared on the
        // way out, so a server that has been starting for two minutes can say so.
        StartingSinceUtc = value == ServerStatus.Starting ? DateTime.UtcNow : null;
        NotifyStatusProps();
    }
    partial void OnIsAutoTuningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        AutoTuneCommand.NotifyCanExecuteChanged();
    }
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnExecutablePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        _ = RefreshRuntimeCapabilitiesAsync(value);
    }
    partial void OnModelPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasMissingModel));
        OnPropertyChanged(nameof(ModelPathHint));
        OnPropertyChanged(nameof(ReasoningPreserveAvailable));
        OnPropertyChanged(nameof(ReasoningPreserveStatus));
        ScheduleContextFitRefresh();
        RepairDetectedModelPathsIfBrowsedOutsideRoot(value);
        ApplyModelDefaultsIfPathActuallyChanged(value);
        RefreshDetectedMmprojPaths(value);
        RefreshDetectedDraftModelPaths(value);
    }
    partial void OnMmprojPathChanged(string value)
    {
        if (!_settingAutoMmproj)
            _autoSelectedMmprojPath = null;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasMissingMmproj));
        OnPropertyChanged(nameof(MmprojHint));
    }
    partial void OnUseProjectorChanged(bool value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(MmprojHint));
        ApplyContextFitNote();
    }
    partial void OnPreserveReasoningChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));

    /// <summary>
    /// r19 2.5: the ComboBox's SelectedItem binding can only display a value
    /// present in its ItemsSource. A path browsed (or previously saved) from
    /// outside the detected-models scan would otherwise render blank once
    /// the free-text fallback TextBox was removed.
    /// </summary>
    private void RepairDetectedModelPathsIfBrowsedOutsideRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!DetectedModelPaths.Any(path => ModelPathSafety.AreSameLocalPath(path, value)))
            DetectedModelPaths.Insert(0, value);
    }

    /// <summary>
    /// r32 Batch 1: applies the model-card context default only when the
    /// selected model actually changes to a different
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
        if (ModelPathSafety.AreSameLocalPath(value, _lastModelPathForDefaults))
            return;
        _lastModelPathForDefaults = value;

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
    partial void OnPortChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(EmbedUrlLabel));
    }
    partial void OnContextSizeChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ApplyContextFitNote();
    }
    partial void OnGpuLayersChanged(int value)
    {
        if (value == -1)
            GpuPlacementSelection = "All";
        else if (value == 0 && string.Equals(GpuPlacementSelection, "Exact", StringComparison.Ordinal))
            GpuPlacementSelection = "CPU";
        else if (value > 0)
            GpuPlacementSelection = "Exact";
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(EffectiveOffloadLabel));
        OnPropertyChanged(nameof(IsExactGpuPlacement));
        ApplyContextFitNote();
    }
    partial void OnGpuPlacementSelectionChanged(string value)
    {
        switch (value)
        {
            case "CPU":
            case "Auto":
                GpuLayers = 0;
                break;
            case "All":
                GpuLayers = -1;
                break;
            case "Exact":
                if (GpuLayers <= 0)
                    GpuLayers = 1;
                break;
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(EffectiveOffloadLabel));
        OnPropertyChanged(nameof(IsExactGpuPlacement));
        ApplyContextFitNote();
    }
    partial void OnThreadsChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnPromptThreadsChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasPromptThreadsControl));
    }
    partial void OnSlotsChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasMissingMmproj));
        OnPropertyChanged(nameof(MmprojHint));
        ApplyContextFitNote();
    }
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
        OnPropertyChanged(nameof(KvCacheType));
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
        ApplyContextFitNote();
    }
    partial void OnContextShiftChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnMemoryLockChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnNoMemoryMapChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnCpuMoeLayersTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ApplyContextFitNote();
    }
    partial void OnAdaptiveModeChanged(AdaptiveInferenceMode value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAdaptiveMinimumContextChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAdaptiveMinimumGpuHeadroomBytesChanged(long value)
    {
        OnPropertyChanged(nameof(AdaptiveMinimumGpuHeadroomMiB));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }
    partial void OnAdaptiveAllowGpuLayerReductionChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAdaptiveAllowContextReductionChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAdaptiveAllowKvPrecisionChangeChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAdaptiveAllowCpuMoePlacementChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAdaptiveAllowMultiDevicePlacementChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAdaptivePreserveAcceleratedBackendChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnAdaptivePreferredEvidenceAgeDaysChanged(int value) => OnPropertyChanged(nameof(HasUnsavedChanges));

    /// <summary>
    /// Empty/0 off, "all" (or any negative) all layers, otherwise N. Public
    /// rather than internal only because Hermaeus.ViewModels does not grant
    /// InternalsVisibleTo and these are pure functions worth testing directly.
    /// </summary>
    public static int ParseCpuMoeLayers(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return 0;
        if (string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
            return -1;
        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return 0;
        return parsed < 0 ? -1 : parsed;
    }

    public static string FormatCpuMoeLayers(int layers) => layers switch
    {
        0 => string.Empty,
        < 0 => "all",
        _ => layers.ToString(CultureInfo.InvariantCulture)
    };
    partial void OnSpeculativeTypesChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(UseNgramDecoding));
        OnPropertyChanged(nameof(UseDraftModelDecoding));
        ApplyDraftFitNote();
    }
    partial void OnDraftModelPathChanged(string value)
    {
        if (!_settingAutoDraftModel)
            _autoSelectedDraftModelPath = null;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasDetectedDraftModel));
        OnPropertyChanged(nameof(HasMissingDraftModel));
        OnPropertyChanged(nameof(DraftModelHint));
        OnPropertyChanged(nameof(HasDraftModelHint));
        ApplyDraftFitNote();
    }
    partial void OnDraftGpuLayersTextChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnSpeculativeNMaxTextChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnSpeculativeNMinTextChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));
    partial void OnSpeculativePMinTextChanged(string value) => OnPropertyChanged(nameof(HasUnsavedChanges));

    private async Task RefreshRuntimeCapabilitiesAsync(string executablePath)
    {
        var generation = ++_runtimeCapabilityGeneration;
        var facts = await LocalModelCapabilityService.ProbeRuntimeAsync(executablePath);
        IReadOnlyList<CapabilityDrift> drift = [];
        LocalModelCapabilities? capabilities = null;
        if (_capabilityService is not null && File.Exists(ModelPath))
        {
            var probe = await _capabilityService.ProbeWithDriftAsync(ModelPath, executablePath);
            drift = probe.Drift;
            capabilities = probe.Capabilities;
        }
        RunOnUi(() =>
        {
            if (generation != _runtimeCapabilityGeneration)
                return;

            _runtimeSpeculativeTypes.Clear();
            _localCapabilities = capabilities;
            foreach (var type in facts.SpeculativeTypes)
                _runtimeSpeculativeTypes.Add(type);
            SupportsPromptThreads = facts.SupportsPromptThreads;
            RuntimeCapabilitiesKnown = facts.HelpProbeSucceeded;
            RuntimeCapabilityStatus = facts.HelpProbeSucceeded
                ? facts.SpeculativeTypes.Count == 0
                    ? "This llama-server advertises no speculative types."
                    : $"Runtime speculative types: {string.Join(", ", facts.SpeculativeTypes)}."
                : "Could not read selected llama-server help. Runtime-only options stay unavailable.";
            OnPropertyChanged(nameof(SupportsNgramDecoding));
            OnPropertyChanged(nameof(SupportsDraftModelDecoding));
            OnPropertyChanged(nameof(SupportsPromptThreads));
            OnPropertyChanged(nameof(CanEditNgramDecoding));
            OnPropertyChanged(nameof(CanEditDraftModelDecoding));
            OnPropertyChanged(nameof(HasPromptThreadsControl));
            ApplyContextFitNote();
            ShowCapabilityDrift(drift);
        });
    }

    private void ShowCapabilityDrift(IReadOnlyList<CapabilityDrift> drift)
    {
        if (drift.Count == 0)
            return;

        var affected = drift.Where(change => change.AffectsConfiguredCapability).ToArray();
        if (affected.Length > 0)
        {
            _toasts.ShowDetails("Moss: runtime capability changed", string.Join(" ", affected.Select(change => change.Detail)) + " Check Services before starting.", ToastKind.Warning, 8000);
            return;
        }

        _toasts.ShowDetails("Moss: llama.cpp learned something", string.Join(" ", drift.Select(change => change.Detail)), ToastKind.Info, 7000);
    }

    /// <summary>
    /// r27 03-drafting-and-proof.md 3.4: a draft model is a second allocation.
    /// The combined estimate is shown before the server starts, as information
    /// and never a block: the user may have reasons, and llama.cpp spills to
    /// system memory rather than failing.
    /// Also carries 3.3's refusal and warning, so an incompatible pair is named
    /// here rather than discovered by pressing Start.
    /// </summary>
    private void ApplyDraftFitNote()
    {
        var config = BuildConfig();
        if (config.Speculative is not { RequiresDraftModel: true })
        {
            DraftFitNote = string.Empty;
            return;
        }

        var validation = SpeculativeDecodingValidator.Validate(config);
        if (validation.HasMessage)
        {
            DraftFitNote = validation.Message;
            return;
        }

        var draftPath = config.Speculative.DraftModelPath;
        if (draftPath.Length == 0 || !File.Exists(draftPath))
        {
            DraftFitNote = string.Empty;
            return;
        }

        var draftBytes = new FileInfo(draftPath).Length;
        var targetBytes = TryGetModelFileSizeBytes();
        var vram = _hardwareProfile?.MaxGpuVramBytes ?? 0;
        if (!targetBytes.HasValue || draftBytes <= 0)
        {
            DraftFitNote = !targetBytes.HasValue
                ? "Target model size is Unknown; the combined draft fit cannot be estimated."
                : "Draft model size is Unknown; the combined draft fit cannot be estimated.";
            return;
        }

        var combined = targetBytes.Value + draftBytes;

        DraftFitNote = vram > 0
            ? $"Target plus draft is roughly {FormatGb(combined)} of weights against {FormatGb(vram)} of VRAM."
            : $"Target plus draft is roughly {FormatGb(combined)} of weights.";
    }

    /// <summary>
    /// llama.cpp may leave Flash Attention disabled when auto is selected for a quantized
    /// V cache. Inform-only - never auto-changes either field; the user can launch with
    /// exactly what they chose regardless.
    /// </summary>
    public bool NeedsFlashAttentionForQuantizedV =>
        KvCacheMath.RequiresRuntimeAdvertisement(KvCacheTypeV)
        && !string.Equals(FlashAttention, "on", StringComparison.OrdinalIgnoreCase);

    private static string EffectiveKvCacheType(ServerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.KvCacheType)
            && !string.Equals(config.KvCacheType, "f16", StringComparison.OrdinalIgnoreCase))
            return config.KvCacheType;
        if (!string.IsNullOrWhiteSpace(config.KvCacheTypeK)
            && !string.Equals(config.KvCacheTypeK, "f16", StringComparison.OrdinalIgnoreCase))
            return config.KvCacheTypeK;
        return "f16";
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
        var level = RuntimeLogClassifier.ClassifyLevel(line);
        if (level == RuntimeLogLevel.Info &&
            (lowered.Contains("slot", StringComparison.Ordinal) || lowered.Contains("kv cache", StringComparison.Ordinal)))
            level = RuntimeLogLevel.Debug;

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
    /// <summary>r27 01 1.5: records elapsed-to-healthy per server, since auto-start is no longer inside the startup total.</summary>
    private readonly IStartupTimingService? _startupTiming;
    private readonly TrustService _trust;
    private readonly IRuntimeLogService _runtimeLogs;
    private readonly LocalModelCapabilityService? _capabilityService;
    private readonly OrphanServerDetector _orphanDetector;
    private readonly ISystemInfoService? _systemInfo;
    private readonly IActivityRecorder? _activity;
    private readonly ModelProfileService _modelProfiles;
    private readonly IResourceCoordinator? _resourceCoordinator;
    private readonly AdaptiveInferenceExperienceService? _adaptiveExperience;
    private readonly RecommendationDerivationService? _recommendationDerivation;
    private readonly IRecommendationStore? _recommendationStore;
    private readonly RecommendationApplicationService? _recommendationApplication;
    private HardwareProfile? _hardwareProfile;

    /// <summary>Shared (DI singleton) with <see cref="SettingsViewModel.Tts"/> - voice
    /// provider/process management now lives here; per-channel routing and profiles
    /// stay in Settings, reading the same live instance.</summary>
    public TtsSettingsViewModel Tts { get; }

    /// <summary>r24 doc 05 5.7: speech recognition process/model/device config.</summary>
    public SttSettingsViewModel? Stt { get; }

    /// <summary>
    /// r29 doc 01 1.1: set by the DI root to the single settings save flow
    /// (<see cref="SettingsViewModel.SaveAsync"/>). The Voice and STT cards on
    /// this page mutate DI singletons shared with Settings, and only that flow
    /// persists them. Null in tests that do not exercise saving.
    /// </summary>
    public Func<Task>? SaveAllSettings { get; set; }

    /// <summary>Transient "Saved" confirmation, mirroring the Settings page.</summary>
    [ObservableProperty] private bool _isSaved;

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (SaveAllSettings is null)
            return;
        await SaveAllSettings();
        IsSaved = true;
        _ = ResetIsSavedAfterDelayAsync();
    }

    private async Task ResetIsSavedAfterDelayAsync()
    {
        await Task.Delay(2000);
        IsSaved = false;
    }

    public UiBoundCollection<ServerProcessViewModel> Servers { get; } = [];
    public UiBoundCollection<RuntimeProfileViewModel> RuntimeProfiles { get; } = [];
    public UiBoundCollection<RecommendationReviewViewModel> Recommendations { get; } = [];
    public bool HasRecommendations => Recommendations.Count > 0;
    public Action<string>? RequestNavigate { get; set; }

    /// <summary>
    /// Loads current and accepted managed-server recommendations after startup
    /// has selected the settings data root. Other recommendation kinds remain
    /// owned by their source page and are not shown as Services actions.
    /// </summary>
    public async Task RefreshRecommendationsAsync(CancellationToken ct = default)
    {
        if (_recommendationStore is null || _recommendationApplication is null)
            return;
        try
        {
            var rows = await _recommendationStore.QueryAsync(new RecommendationQuery { Limit = 32 }, ct);
            var servers = _settings.Settings.ManagedServers.ToDictionary(server => server.Id, StringComparer.Ordinal);
            var cards = rows
                .Where(row => row.Status is RecommendationStatus.Current or RecommendationStatus.Accepted
                    && string.Equals(row.ProposedPatch.TargetDomain, ManagedServerRecommendationPatch.TargetDomain, StringComparison.Ordinal))
                .Select(row => new RecommendationReviewViewModel(
                    row,
                    _recommendationApplication,
                    servers.GetValueOrDefault(row.TargetIdentity),
                    () => RefreshRecommendationsAsync(),
                    RequestNavigate))
                .ToList();
            RunOnUi(() =>
            {
                Recommendations.Clear();
                foreach (var card in cards)
                    Recommendations.Add(card);
                OnPropertyChanged(nameof(HasRecommendations));
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // A store timeout is not a reason to make the Services page fail.
        }
    }

    public void RefreshAllDetectedModels()
    {
        foreach (var server in Servers)
            server.RefreshDetectedModels();
    }

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

    /// <summary>
    /// The non-embedding managed server is the owner of Chat launch settings.
    /// Chat can expose a compact projection of this row without creating a
    /// second settings owner or a second launch path.
    /// </summary>
    public ServerProcessViewModel? ChatServer => Servers.FirstOrDefault(server => !server.EmbeddingsMode);

    /// <summary>
    /// Identifies a loopback llama.cpp endpoint that belongs to a managed
    /// server currently known to be stopped. Remote endpoints and servers in
    /// Starting remain probeable because this view model cannot establish that
    /// they are intentionally unavailable.
    /// </summary>
    public bool IsManagedServerStopped(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
            return false;

        return Servers.Any(server =>
            !server.EmbeddingsMode &&
            server.Port == endpoint.Port &&
            server.Status == ServerStatus.Stopped);
    }

    /// <summary>
    /// Identifies a loopback llama.cpp endpoint whose unavailable model list is
    /// expected because the managed server is stopped or is still in its
    /// health-wait startup phase. Error is deliberately excluded so a genuine
    /// startup failure remains visible.
    /// </summary>
    public bool IsManagedServerExpectedUnavailable(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
            return false;

        return Servers.Any(server =>
            !server.EmbeddingsMode &&
            server.Port == endpoint.Port &&
            server.Status is ServerStatus.Stopped or ServerStatus.Starting);
    }

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
        TtsSettingsViewModel tts,
        OrphanServerDetector? orphanDetector = null,
        ISystemInfoService? systemInfo = null,
        ModelProfileService? modelProfiles = null,
        IActivityRecorder? activity = null,
        SttSettingsViewModel? stt = null,
        IStartupTimingService? startupTiming = null,
        LocalModelCapabilityService? capabilityService = null,
        IResourceCoordinator? resourceCoordinator = null,
        AdaptiveInferenceExperienceService? adaptiveExperience = null,
        RecommendationDerivationService? recommendationDerivation = null,
        IRecommendationStore? recommendationStore = null,
        RecommendationApplicationService? recommendationApplication = null)
    {
        _startupTiming = startupTiming;
        _settings = settings;
        _runtimeProfiles = runtimeProfiles;
        _toasts = toasts;
        _redactor = redactor;
        _trust = trust;
        _runtimeLogs = runtimeLogs;
        Tts = tts;
        Stt = stt;
        _orphanDetector = orphanDetector ?? new OrphanServerDetector();
        _systemInfo = systemInfo;
        _activity = activity;
        _capabilityService = capabilityService;
        _resourceCoordinator = resourceCoordinator;
        _adaptiveExperience = adaptiveExperience;
        _recommendationDerivation = recommendationDerivation;
        _recommendationStore = recommendationStore;
        _recommendationApplication = recommendationApplication;
        _modelProfiles = modelProfiles ?? new ModelProfileService(settings);
        Rebuild();
        _settings.SettingsChanged += (_, _) =>
        {
            RunOnUi(Rebuild);
            _ = RefreshRecommendationsAsync();
        };
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

    /// <summary>
    /// Builds live Chat telemetry from the managed server row that owns the
    /// serving process. Process identity comes from the child process itself,
    /// never from a port lookup. Incomplete configuration or hardware facts
    /// remain incomplete rather than being guessed.
    /// </summary>
    public async Task<RuntimeTelemetryRequest?> CreateManagedTelemetryRequestAsync(
        string modelId,
        CancellationToken ct = default)
    {
        var server = Servers.FirstOrDefault(item =>
            item.IsRunning
            && !item.EmbeddingsMode
            && (string.IsNullOrWhiteSpace(modelId)
                || string.Equals(item.ModelPath, modelId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(item.ModelPath), modelId, StringComparison.OrdinalIgnoreCase)));
        if (server is null || server.CurrentProcessIdentity is not { } process)
            return null;

        var config = server.BuildConfig();
        var runtime = await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(config.ExecutablePath, null, ct);
        var gguf = File.Exists(config.ModelPath)
            ? await Task.Run(() => GgufMetadataReader.TryRead(config.ModelPath), ct)
            : null;
        var model = RuntimeIdentityFactory.CreateModelIdentity(config.ModelPath, gguf);
        var hardware = new HardwareIdentityV2(
            Environment.OSVersion.Platform.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            string.IsNullOrWhiteSpace(_hardwareProfile?.GpuName) ? string.Empty : "unknown",
            _hardwareProfile?.GpuName ?? string.Empty,
            _hardwareProfile is { MaxGpuVramBytes: > 0 } profile ? profile.MaxGpuVramBytes : null,
            _hardwareProfile is { TotalRamBytes: > 0 } ram ? ram.TotalRamBytes : null,
            string.Empty,
            "single",
            IdentityCompleteness.Incomplete);
        var speculative = config.Speculative ?? new SpeculativeDecodingConfig();
        var companionIdentity = string.IsNullOrWhiteSpace(speculative.DraftModelPath)
            ? string.Empty
            : RuntimeIdentityFactory.CreateModelIdentity(speculative.DraftModelPath, null).StableId;
        var configuration = ConfigurationIdentityFactory.Create(config, companionIdentity);
        var fingerprint = new EmpiricalProfileFingerprintV2(runtime, model, hardware, configuration);
        return new RuntimeTelemetryRequest(
            $"chat-{server.Id}", process.ProcessId, process.StartedAtUtc,
            runtime, fingerprint, IncludeDeviceTotals: true);
    }

    /// <summary>Re-checks every non-Running server's port for a leftover process (r9 02-server-lifecycle.md 2.3). Startup and Services-view-refresh entry point.</summary>
    [RelayCommand]
    public async Task RefreshOrphanDetectionAsync()
    {
        foreach (var server in Servers.ToList())
            await server.RefreshOrphanStatusAsync();
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "services.refresh-orphan-detection", Title: "Check for leftover server processes", Area: "Services",
            Description: "Re-check every stopped managed server's port for a leftover process.",
            Keywords: ["services", "orphan", "port", "refresh"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => RefreshOrphanDetectionCommand.ExecuteAsync(null)));
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
        Hermaeus.Services.SettingsService.NormalizeManagedServers(_settings.Settings.ManagedServers);
        var configs = _settings.Settings.ManagedServers;

        // Ensure we always have the two default slots
        while (configs.Count < 2)
            configs.Add(new ServerConfig
            {
                Name = configs.Count == 0 ? "Chat" : "Embeddings",
                Port = configs.Count == 0 ? 8080 : 8081,
                EmbeddingsMode = configs.Count == 1
            });

        ResolveInstalledManagedExecutables(configs);

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
                current.RebindConfig(cfg);
                current.RefreshDetectedModels();
            }
            else
            {
                var vm = new ServerProcessViewModel(cfg, _settings, _redactor, _trust, _toasts, _runtimeLogs, _orphanDetector, _hardwareProfile, _modelProfiles, _activity, _capabilityService, _resourceCoordinator, _adaptiveExperience, _recommendationDerivation)
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
        OnPropertyChanged(nameof(ChatServer));

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

    private void ResolveInstalledManagedExecutables(IEnumerable<ServerConfig> configs)
    {
        var assetsRoot = _settings.Settings.DataManagement.LocalAiAssetsRoot?.Trim();
        if (string.IsNullOrWhiteSpace(assetsRoot))
            return;

        string installPath;
        try
        {
            installPath = Path.Combine(Path.GetFullPath(assetsRoot), "llama-server");
        }
        catch (ArgumentException)
        {
            return;
        }

        var resolved = LlamaServerSetupService.ResolveInstalledExecutable(installPath);
        if (string.IsNullOrWhiteSpace(resolved))
            return;

        foreach (var config in configs)
        {
            var configured = config.ExecutablePath?.Trim() ?? string.Empty;
            if (IsDefaultLlamaServerPath(configured) || IsMissingPathUnderInstall(configured, installPath))
                config.ExecutablePath = resolved;
        }
    }

    private static bool IsDefaultLlamaServerPath(string path)
    {
        var fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(fileName, "llama-server", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "llama-server.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingPathUnderInstall(string path, string installPath)
    {
        if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(fullPath).StartsWith("llama-server", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

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

    /// <summary>
    /// Stops only managed embedding servers that are actually running. Doctor
    /// uses this around a verified embedding-model migration so the process is
    /// restarted with the new path and cannot continue serving the old vector
    /// dimensions.
    /// </summary>
    public async Task<IReadOnlyList<string>> StopRunningEmbeddingServersForModelChangeAsync()
    {
        var stopped = Servers
            .Where(server => server.EmbeddingsMode && server.IsRunning)
            .Select(server => server.Id)
            .ToList();

        foreach (var id in stopped)
        {
            var server = Servers.FirstOrDefault(candidate => candidate.Id == id);
            if (server is not null)
                await server.StopAndWaitAsync();
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
            server.SyncModelPathFromConfig();
            await server.StartCommand.ExecuteAsync(null);
            server.RefreshStatusFromManager();
        }
    }

    /// <summary>
    /// Re-syncs every server row's displayed <c>ExecutablePath</c> from its
    /// underlying config, without starting or stopping anything. An llama.cpp
    /// update rewrites <c>ExecutablePath</c> on every <see cref="ServerConfig"/>
    /// unconditionally, including ones that were not running (and so are never
    /// named in <see cref="RestartServersAsync"/>'s server-id list); those rows
    /// would otherwise keep showing the pre-update path until the next app
    /// restart rebuilds them from settings.
    /// </summary>
    public void SyncAllExecutablePathsFromConfig()
    {
        foreach (var server in Servers)
            server.SyncExecutablePathFromConfig();
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

    /// <summary>
    /// r27 01-startup-that-never-waits.md 1.2: every configured server starts at
    /// once. Sequentially, each one awaited a full model load behind
    /// WaitForHealthAsync's five-minute deadline before the next was even
    /// launched, and two servers on separate ports and separate processes have
    /// no reason to wait for each other.
    /// </summary>
    public Task AutoStartAllAsync() =>
        Task.WhenAll(SelectAutoStartTargets(Servers).Select(TimedAutoStartAsync));

    private async Task TimedAutoStartAsync(ServerProcessViewModel server)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await server.AutoStartIfConfiguredAsync();
        _startupTiming?.RecordServerStart(new StartupServerStart(server.Name, sw.ElapsedMilliseconds, server.IsRunning));
    }

    /// <summary>
    /// The servers <see cref="AutoStartAllAsync"/> actually launches: at most one
    /// per port. StartAsync's port preflight and StopSamePortPeersBeforeStartAsync
    /// both assume they are looking at settled state, so two concurrent starts on
    /// one port could both pass the preflight. The rest keep today's behaviour of
    /// being stopped or refused. Pure and static so this is testable without a
    /// process.
    /// </summary>
    /// <summary>
    /// r27 01 1.3: the managed non-embedding server Chat is currently waiting on,
    /// or null. Embedding servers are excluded because listing chat models never
    /// depended on one.
    /// </summary>
    public ChatWarmingServer? GetWarmingChatServer()
    {
        var starting = Servers.FirstOrDefault(s => !s.EmbeddingsMode && s.IsStarting);
        if (starting is null)
            return null;

        var since = starting.StartingSinceUtc ?? DateTime.UtcNow;
        return new ChatWarmingServer(starting.Name, DateTime.UtcNow - since);
    }

    public static IReadOnlyList<ServerProcessViewModel> SelectAutoStartTargets(IEnumerable<ServerProcessViewModel> servers) =>
        servers
            .Where(s => s.WillAutoStart)
            .GroupBy(s => s.Port)
            .Select(g => g.First())
            .ToList();

    public async Task<IReadOnlyList<string>> StopRunningNonEmbeddingServersAsync()
    {
        var suspended = Servers
            .Where(s => s.IsRunning && !s.EmbeddingsMode)
            .Select(s => s.Id)
            .ToList();

        foreach (var serverId in suspended)
        {
            var server = Servers.FirstOrDefault(s => s.Id == serverId);
            if (server is not null)
                await server.StopAndWaitAsync();
        }

        return await Task.FromResult(suspended);
    }

    public async Task<IReadOnlyList<string>> SuspendRunningServersAsync(IEnumerable<string> serverIds)
    {
        var requested = serverIds.ToHashSet(StringComparer.Ordinal);
        var suspended = Servers.Where(server => requested.Contains(server.Id) && server.IsRunning)
            .Select(server => server.Id).ToArray();
        foreach (var id in suspended)
            await Servers.First(server => server.Id == id).StopAndWaitAsync();
        return suspended;
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

    public async Task StopAllAsync()
    {
        foreach (var srv in Servers)
            await srv.StopAndWaitAsync();
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
    public string KindLabel => Hermaeus.Services.CompositeLlmService.DescriptorFor(Kind).DisplayName;

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
