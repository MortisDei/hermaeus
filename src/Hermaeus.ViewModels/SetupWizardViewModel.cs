using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly RuntimeProfileService _runtimeProfiles;
    private readonly IVoiceProviderRegistry _voiceProviders;
    private readonly IDoctorService _doctor;
    private readonly IToastService _toasts;
    private readonly ISystemInfoService _systemInfo;
    private readonly ModelDownloadService _modelDownloads;
    private readonly ModelManifestStore _manifest;
    private readonly LlamaServerSetupService _llamaSetup;

    [ObservableProperty] private int _stepIndex;
    [ObservableProperty] private string _dataRootDirectory = string.Empty;
    [ObservableProperty] private string _localAiAssetsRoot = string.Empty;
    [ObservableProperty] private string _modelFolder = string.Empty;
    [ObservableProperty] private string _selectedRuntimeId = string.Empty;
    [ObservableProperty] private VoiceProviderInfo? _selectedVoiceProvider;
    [ObservableProperty] private RuntimeProfileViewModel? _selectedRuntime;
    [ObservableProperty] private string _doctorSummary = "Run Doctor to verify the setup.";
    [ObservableProperty] private string _voiceOnboardingSummary = string.Empty;
    [ObservableProperty] private string _voiceOnboardingRiskNotes = string.Empty;
    [ObservableProperty] private bool _doctorRan;
    [ObservableProperty] private bool _isDoctorRunning;

    // ── Guided starter model download (docs/review 02-onboarding-and-usability.md 2.1) ──
    [ObservableProperty] private bool _useStarterModelDownload;
    /// <summary>The VRAM-based default. Kept so the wizard can say which entry it suggested.</summary>
    [ObservableProperty] private StarterModelEntry? _recommendedStarterModel;
    /// <summary>
    /// What will actually be downloaded. Starts at <see cref="RecommendedStarterModel"/>
    /// and the user can pick any entry in <see cref="StarterModels"/>: the sizes,
    /// families, quantisations, and licences are all visible before download.
    /// </summary>
    [ObservableProperty] private StarterModelEntry? _selectedStarterModel;
    [ObservableProperty] private string _recommendedStarterModelFitLabel = string.Empty;
    [ObservableProperty] private string _recommendedStarterModelFitReason = string.Empty;

    /// <summary>Every starter model on offer, for the wizard's picker.</summary>
    public IReadOnlyList<StarterModelEntry> StarterModels { get; } = StarterModelCatalog.All;

    /// <summary>True when the chosen entry is the one recommended for this machine.</summary>
    public bool SelectedStarterModelIsRecommended =>
        SelectedStarterModel is not null && SelectedStarterModel.Id == RecommendedStarterModel?.Id;

    /// <summary>
    /// The recommendation the selection is currently tracking. Used to tell
    /// "the user has not chosen anything yet" from "the user chose this".
    /// </summary>
    private string _followedRecommendationId = string.Empty;

    /// <summary>
    /// The hardware probe finishes after the wizard is constructed, so the
    /// recommendation can arrive (or change) with a selection already in place.
    /// Follow it while the selection is still whatever was last recommended;
    /// leave it alone the moment the user has picked something else.
    /// </summary>
    partial void OnRecommendedStarterModelChanged(StarterModelEntry? value)
    {
        if (value is null)
            return;

        if (SelectedStarterModel is null || SelectedStarterModel.Id == _followedRecommendationId)
        {
            _followedRecommendationId = value.Id;
            SelectedStarterModel = value;
        }

        OnPropertyChanged(nameof(SelectedStarterModelIsRecommended));
    }

    partial void OnSelectedStarterModelChanged(StarterModelEntry? value)
    {
        if (!IsDownloadingStarterModel && !string.IsNullOrEmpty(_starterModelDownloadId)
            && !string.Equals(_starterModelDownloadId, value?.Id, StringComparison.Ordinal))
        {
            StarterModelDownloadCompleted = false;
            StarterModelDownloadPercent = 0;
            StarterModelDownloadStatus = string.Empty;
            StarterModelDownloadError = string.Empty;
        }
        OnPropertyChanged(nameof(SelectedStarterModelIsRecommended));
        // The fit badge describes the model that will be downloaded, so it has
        // to follow the selection rather than stay on the recommendation.
        _ = RefreshStarterModelFitAsync();
    }
    [ObservableProperty] private bool _isDownloadingStarterModel;
    [ObservableProperty] private double _starterModelDownloadPercent;
    [ObservableProperty] private string _starterModelDownloadStatus = string.Empty;
    [ObservableProperty] private string _starterModelDownloadError = string.Empty;
    [ObservableProperty] private bool _starterModelDownloadCompleted;
    private string _starterModelDownloadId = string.Empty;

    // ── Voice install from the wizard (docs/review 02-onboarding-and-usability.md 2.2) ──
    [ObservableProperty] private bool _isInstallingVoice;
    [ObservableProperty] private string _voiceInstallProgress = string.Empty;
    [ObservableProperty] private string _voiceInstallError = string.Empty;
    [ObservableProperty] private bool _voiceInstallCompleted;

    [ObservableProperty] private bool _isInstallingManagedLlama;
    [ObservableProperty] private bool _managedLlamaReady;
    [ObservableProperty] private string _managedLlamaInstallProgress = string.Empty;
    [ObservableProperty] private string _managedLlamaInstallError = string.Empty;

    public bool CanInstallSelectedVoiceProvider =>
        SelectedVoiceProvider?.Id == VoiceProvider.KokoroNative && !VoiceInstallCompleted;
    public bool SelectedRuntimeUsesManagedLlama => SelectedRuntime is { IsLlamaCpp: true, StartManagedLlamaServer: true };
    public bool CanInstallManagedLlama => SelectedRuntimeUsesManagedLlama && !ManagedLlamaReady;
    public string ManagedLlamaReadinessSummary => ManagedLlamaReady
        ? "Managed llama.cpp is installed and linked to Services."
        : "Install managed llama.cpp here before Doctor validates the runtime.";

    /// <summary>Shown on the Finish step; see docs/review 02-onboarding-and-usability.md item 2.2.</summary>
    public string VoiceReadinessSummary
    {
        get
        {
            if (VoiceInstallCompleted)
                return "Voice is ready.";
            if (!CanInstallSelectedVoiceProvider)
                return "Voice provider selected. Finish any further setup it needs in Settings > Voice.";
            return "Voice is not installed yet. You can finish this later in Settings > Voice.";
        }
    }

    private bool _syncingRuntimeSelection;

    public UiBoundCollection<string> Steps { get; } =
    [
        "Data roots",
        "Chat backend",
        "Model folder",
        "Voice",
        "Doctor",
        "Finish"
    ];

    public UiBoundCollection<RuntimeProfileViewModel> RuntimeOptions { get; } = [];
    public UiBoundCollection<VoiceProviderInfo> VoiceOptions { get; } = [];
    public UiBoundCollection<string> VoiceOnboardingSteps { get; } = [];

    public string CurrentStepTitle => StepIndex >= 0 && StepIndex < Steps.Count ? Steps[StepIndex] : string.Empty;
    public string GuidanceText => StepIndex switch
    {
        0 => "Moss: Choose where Hermaeus keeps its data and local AI assets. The defaults are safe, and you can change them later.",
        1 => "Moss: Pick the runtime that will serve chat models. Setup only enables what you select.",
        2 => "Moss: Use an existing GGUF or download a verified starter model. You can skip this and add a model later.",
        3 => "Moss: Voice is optional. Kokoro can be installed here, and other providers can be configured later.",
        4 => "Moss: Doctor checks the setup without changing it. If a check fails, inspect Logs and use Resume setup in the header to return here.",
        _ => "Moss: Finish to apply this setup. Anything skipped remains available from Settings, Services, Models, or Doctor."
    };
    public bool IsLastStep => StepIndex >= Steps.Count - 1;
    public bool IsNotLastStep => !IsLastStep;
    public bool IsStep0 => StepIndex == 0;
    public bool IsStep1 => StepIndex == 1;
    public bool IsStep2 => StepIndex == 2;
    public bool IsStep3 => StepIndex == 3;
    public bool IsStep4 => StepIndex == 4;
    public bool IsStep5 => StepIndex == 5;
    public bool HasVoiceOnboardingSummary => !string.IsNullOrWhiteSpace(VoiceOnboardingSummary);

    public Action? RequestDataRootPicker { get; set; }
    public Action? RequestLocalAiAssetsRootPicker { get; set; }
    public Action? RequestModelFolderPicker { get; set; }
    public event Action? WizardCompleted;

    public SetupWizardViewModel(
        ISettingsService settings,
        RuntimeProfileService runtimeProfiles,
        IVoiceProviderRegistry voiceProviders,
        IDoctorService doctor,
        IToastService toasts,
        ISystemInfoService systemInfo,
        ModelDownloadService? modelDownloads = null,
        ModelManifestStore? manifest = null,
        LlamaServerSetupService? llamaSetup = null)
    {
        _settings = settings;
        _runtimeProfiles = runtimeProfiles;
        _voiceProviders = voiceProviders;
        _doctor = doctor;
        _toasts = toasts;
        _systemInfo = systemInfo;
        _modelDownloads = modelDownloads ?? new ModelDownloadService();
        _manifest = manifest ?? new ModelManifestStore(settings);
        _llamaSetup = llamaSetup ?? new LlamaServerSetupService(_modelDownloads);
        LoadFromSettings(resetStep: true);
    }

    public void LoadFromSettings(bool resetStep = false)
    {
        if (resetStep)
            StepIndex = 0;
        var s = _settings.Settings;
        DataRootDirectory = s.DataManagement.DataRootDirectory;
        LocalAiAssetsRoot = s.DataManagement.LocalAiAssetsRoot;
        ModelFolder = s.ManagedServers.FirstOrDefault()?.ModelPath ?? string.Empty;
        UseStarterModelDownload = string.IsNullOrWhiteSpace(ModelFolder) || !ModelFolder.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase);

        RuntimeOptions.Clear();
        foreach (var profile in _runtimeProfiles.Profiles)
            RuntimeOptions.Add(new RuntimeProfileViewModel(profile));
        SelectedRuntimeId = RuntimeOptions.FirstOrDefault(p => p.Enabled)?.Id
            ?? RuntimeOptions.FirstOrDefault()?.Id
            ?? string.Empty;

        VoiceOptions.Clear();
        foreach (var provider in _voiceProviders.GetAvailableProviders())
            VoiceOptions.Add(provider);
        var activeProviderId = _voiceProviders.GetActiveProvider();
        SelectedVoiceProvider = VoiceOptions.FirstOrDefault(p => p.Id == activeProviderId)
            ?? VoiceOptions.FirstOrDefault();
        UpdateVoiceOnboarding(SelectedVoiceProvider);
        RefreshVoiceInstallationState();
        RefreshManagedLlamaState();

        _ = RefreshRecommendedStarterModelAsync();
    }

    private async Task RefreshRecommendedStarterModelAsync()
    {
        try
        {
            var snapshot = await _systemInfo.CaptureAsync();
            RecommendedStarterModel = StarterModelCatalog.Recommend(snapshot);
        }
        catch
        {
            RecommendedStarterModel = StarterModelCatalog.Small;
        }

        // OnRecommendedStarterModelChanged has already moved the selection if
        // the user has not chosen for themselves.
        await RefreshStarterModelFitAsync();
    }

    private async Task RefreshStarterModelFitAsync()
    {
        var entry = SelectedStarterModel ?? RecommendedStarterModel;
        if (entry is null)
            return;

        try
        {
            var hardware = await _systemInfo.GetHardwareProfileAsync();
            var fit = ModelFitEstimator.Estimate(entry.SizeBytes, hardware);
            RecommendedStarterModelFitLabel = ModelFitEstimator.Label(fit.Tier);
            RecommendedStarterModelFitReason = fit.Reason;
        }
        catch
        {
            RecommendedStarterModelFitLabel = string.Empty;
            RecommendedStarterModelFitReason = string.Empty;
        }
    }

    [RelayCommand]
    private void BrowseDataRoot() => RequestDataRootPicker?.Invoke();

    [RelayCommand]
    private void BrowseLocalAiAssetsRoot() => RequestLocalAiAssetsRootPicker?.Invoke();

    [RelayCommand]
    private void BrowseModelFolder() => RequestModelFolderPicker?.Invoke();

    [RelayCommand]
    private async Task NextAsync()
    {
        if (StepIndex >= Steps.Count - 1) return;
        if (!await ApplyStepAsync(StepIndex)) return;
        StepIndex++;
    }

    [RelayCommand]
    private void Back()
    {
        if (StepIndex <= 0) return;
        StepIndex--;
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        _settings.Settings.SetupWizardCompleted = true;
        await _settings.SaveAsync();
        WizardCompleted?.Invoke();
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (!await ApplyStepAsync(StepIndex)) return;
        _settings.Settings.SetupWizardCompleted = true;
        await _settings.SaveAsync();
        WizardCompleted?.Invoke();
    }

    [RelayCommand]
    private async Task RunDoctorAsync()
    {
        if (IsDoctorRunning) return;
        IsDoctorRunning = true;
        try
        {
            var report = await _doctor.ScanAsync();
            DoctorRan = true;
            DoctorSummary = report.Summary;
        }
        catch (Exception ex)
        {
            DoctorSummary = ex.Message;
            _toasts.Show("Doctor failed", ex.Message, ToastKind.Error, 6000);
        }
        finally
        {
            IsDoctorRunning = false;
        }
    }

    [RelayCommand]
    private async Task DownloadStarterModelAsync()
    {
        if (IsDownloadingStarterModel) return;
        var entry = SelectedStarterModel ?? RecommendedStarterModel ?? StarterModelCatalog.Small;

        IsDownloadingStarterModel = true;
        StarterModelDownloadCompleted = false;
        StarterModelDownloadError = string.Empty;
        StarterModelDownloadPercent = 0;
        try
        {
            var root = ResolveStarterModelRoot();
            if (!ModelPathSafety.TryResolveFileUnderRoot(root, Path.Combine(root, "Models", "chat", entry.FileName), out var destination, out var pathError))
            {
                StarterModelDownloadError = pathError;
                StarterModelDownloadStatus = "Choose a different AI root.";
                return;
            }

            var folder = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(folder);

            if (File.Exists(destination))
            {
                StarterModelDownloadStatus = "Checking the existing file...";
                if (!await _modelDownloads.VerifyHashAsync(destination, entry.Sha256))
                {
                    StarterModelDownloadError = $"A different file already exists at {destination}. It was not changed.";
                    StarterModelDownloadStatus = "Existing file conflicts with the selected starter model.";
                    return;
                }

                await AdoptStarterModelAsync(entry, destination);
                StarterModelDownloadPercent = 100;
                StarterModelDownloadStatus = $"{entry.DisplayName} was already downloaded and is ready.";
                return;
            }

            StarterModelDownloadStatus = $"Downloading {entry.DisplayName}...";
            var progress = new Progress<DownloadProgress>(p => StarterModelDownloadPercent = p.PercentComplete);
            var result = await _modelDownloads.DownloadAsync(entry.DownloadUrl, destination, progress);
            if (!result.Success)
            {
                StarterModelDownloadError = result.Message;
                return;
            }

            StarterModelDownloadStatus = "Verifying download...";
            if (!await _modelDownloads.VerifyHashAsync(destination, entry.Sha256))
            {
                try { File.Delete(destination); } catch { }
                StarterModelDownloadError = "The downloaded file failed hash verification and was removed. Please try again.";
                StarterModelDownloadStatus = "Hash verification failed.";
                return;
            }

            await AdoptStarterModelAsync(entry, destination);
            StarterModelDownloadPercent = 100;
            StarterModelDownloadCompleted = true;
            StarterModelDownloadStatus = $"{entry.DisplayName} is ready.";
        }
        catch (Exception ex)
        {
            StarterModelDownloadError = ex.Message;
            StarterModelDownloadStatus = "Starter model adoption failed.";
        }
        finally
        {
            IsDownloadingStarterModel = false;
        }
    }

    private async Task AdoptStarterModelAsync(StarterModelEntry entry, string destination)
    {
        var info = new FileInfo(destination);
        if (!info.Exists)
            throw new InvalidOperationException($"The downloaded model is missing at {destination}.");

        await _manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = Path.GetFullPath(destination),
            RepoId = GetRepoId(entry.DownloadUrl),
            RepoFile = entry.FileName,
            Sha256 = entry.Sha256,
            SizeBytes = info.Length,
            Source = "starter"
        });

        ModelFolder = Path.GetFullPath(destination);
        _starterModelDownloadId = entry.Id;
        StarterModelDownloadCompleted = true;
        StarterModelDownloadError = string.Empty;
    }

    private string ResolveStarterModelRoot()
    {
        var configured = _settings.Settings.DataManagement.LocalAiAssetsRoot?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus")
            : Path.GetFullPath(configured);
        return root;
    }

    private static string GetRepoId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return string.Empty;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? $"{segments[0]}/{segments[1]}" : string.Empty;
    }

    [RelayCommand]
    private async Task InstallVoiceAsync()
    {
        if (IsInstallingVoice || !CanInstallSelectedVoiceProvider) return;

        IsInstallingVoice = true;
        VoiceInstallCompleted = false;
        VoiceInstallError = string.Empty;
        try
        {
            var progress = new Progress<string>(s => VoiceInstallProgress = s);
            var ok = await _doctor.InstallNativeKokoroAssetsAsync(progress, CancellationToken.None);
            if (ok)
            {
                VoiceInstallCompleted = true;
                RefreshVoiceInstallationState();
                VoiceInstallProgress = "Kokoro (native) voice installed.";
            }
            else
            {
                VoiceInstallError = "Voice install failed. See diagnostics for details.";
            }
        }
        catch (Exception ex)
        {
            VoiceInstallError = ex.Message;
        }
        finally
        {
            IsInstallingVoice = false;
        }
    }

    partial void OnSelectedRuntimeChanged(RuntimeProfileViewModel? value)
    {
        if (_syncingRuntimeSelection)
            return;

        try
        {
            _syncingRuntimeSelection = true;
            if (value is not null && SelectedRuntimeId != value.Id)
                SelectedRuntimeId = value.Id;
        }
        finally
        {
            _syncingRuntimeSelection = false;
        }
        RefreshManagedLlamaState();
        OnPropertyChanged(nameof(SelectedRuntimeUsesManagedLlama));
        OnPropertyChanged(nameof(CanInstallManagedLlama));
        OnPropertyChanged(nameof(ManagedLlamaReadinessSummary));
    }

    partial void OnSelectedRuntimeIdChanged(string value)
    {
        if (_syncingRuntimeSelection)
            return;

        try
        {
            _syncingRuntimeSelection = true;
            if (!string.IsNullOrEmpty(value) && SelectedRuntime?.Id != value)
                SelectedRuntime = RuntimeOptions.FirstOrDefault(p => p.Id == value);
        }
        finally
        {
            _syncingRuntimeSelection = false;
        }
    }

    partial void OnSelectedVoiceProviderChanged(VoiceProviderInfo? value)
    {
        UpdateVoiceOnboarding(value);
        VoiceInstallCompleted = false;
        RefreshVoiceInstallationState();
        OnPropertyChanged(nameof(CanInstallSelectedVoiceProvider));
        OnPropertyChanged(nameof(VoiceReadinessSummary));
        VoiceInstallError = string.Empty;
    }

    partial void OnVoiceInstallCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(VoiceReadinessSummary));
        OnPropertyChanged(nameof(CanInstallSelectedVoiceProvider));
    }

    partial void OnManagedLlamaReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstallManagedLlama));
        OnPropertyChanged(nameof(ManagedLlamaReadinessSummary));
    }

    partial void OnStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStepTitle));
        OnPropertyChanged(nameof(GuidanceText));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(IsNotLastStep));
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(IsStep5));
        if (value == 3)
            RefreshVoiceInstallationState();
        if (value is 1 or 4)
            RefreshManagedLlamaState();
    }

    [RelayCommand]
    private async Task InstallManagedLlamaAsync()
    {
        if (IsInstallingManagedLlama || !CanInstallManagedLlama)
            return;

        IsInstallingManagedLlama = true;
        ManagedLlamaInstallError = string.Empty;
        try
        {
            var progress = new Progress<string>(message => ManagedLlamaInstallProgress = message);
            if (!await _doctor.InstallLlamaServerUpdateAsync(progress, CancellationToken.None))
            {
                ManagedLlamaInstallError = "llama.cpp installation failed. See Doctor diagnostics for details.";
                return;
            }

            RefreshManagedLlamaState();
            ManagedLlamaInstallProgress = "Managed llama.cpp is ready.";
        }
        catch (Exception ex)
        {
            ManagedLlamaInstallError = ex.Message;
        }
        finally
        {
            IsInstallingManagedLlama = false;
        }
    }

    private void RefreshVoiceInstallationState()
    {
        if (SelectedVoiceProvider is null)
            return;

        var available = _voiceProviders.GetAvailableProviders()
            .FirstOrDefault(provider => provider.Id == SelectedVoiceProvider.Id);
        var installed = available?.IsInstalled == true;
        try
        {
            var provider = _voiceProviders.GetVoiceProvider(SelectedVoiceProvider.Id);
            if (provider.Id == SelectedVoiceProvider.Id)
                installed |= provider.IsInstalled;
        }
        catch
        {
        }

        if (available is not null || installed)
            VoiceInstallCompleted = installed;
    }

    private void RefreshManagedLlamaState()
    {
        var server = _settings.Settings.ManagedServers.FirstOrDefault(s => !s.EmbeddingsMode)
            ?? _settings.Settings.ManagedServers.FirstOrDefault();
        var configured = server?.ExecutablePath?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configured) && _llamaSetup.IsInstalled(configured))
        {
            ManagedLlamaReady = true;
            return;
        }

        var root = LocalAiAssetsRoot.Trim();
        if (string.IsNullOrWhiteSpace(root))
            root = SettingsService.ResolveDataRoot(_settings.Settings);
        var installRoot = _llamaSetup.GetDefaultInstallPath(root);
        ManagedLlamaReady = LlamaServerSetupService.ResolveInstalledExecutable(installRoot) is not null;
    }

    private void UpdateVoiceOnboarding(VoiceProviderInfo? provider)
    {
        VoiceOnboardingSteps.Clear();

        if (provider is null)
        {
            VoiceOnboardingSummary = string.Empty;
            VoiceOnboardingRiskNotes = string.Empty;
            OnPropertyChanged(nameof(HasVoiceOnboardingSummary));
            return;
        }

        var plan = _voiceProviders.GetVoiceProvider(provider.Id).InstallPlan();
        VoiceOnboardingSummary = plan.Summary;
        VoiceOnboardingRiskNotes = plan.RiskNotes;
        OnPropertyChanged(nameof(HasVoiceOnboardingSummary));
        foreach (var step in plan.Steps)
            VoiceOnboardingSteps.Add($"{step.Title}: {step.Detail}");
    }

    /// <summary>Returns false to refuse advancing the step (r12 01-settings-lifecycle.md 1.1).</summary>
    private async Task<bool> ApplyStepAsync(int step)
    {
        switch (step)
        {
            case 0:
                return await ApplyDataRootStepAsync();
            case 1:
                // Guard against no runtime options being present
                if (RuntimeOptions.Count == 0) return true;
                foreach (var profile in RuntimeOptions)
                    profile.Enabled = profile.Id == SelectedRuntimeId;
                foreach (var profile in RuntimeOptions)
                    await _runtimeProfiles.SaveAsync(profile.ToProfile());
                return true;
            case 2:
                if (UseStarterModelDownload && (!StarterModelDownloadCompleted || string.IsNullOrWhiteSpace(ModelFolder) || !File.Exists(ModelFolder)))
                {
                    StarterModelDownloadError = $"Download a starter model before continuing. Expected file: {ModelFolder}";
                    _toasts.Show("Model is not ready", StarterModelDownloadError, ToastKind.Error, 7000);
                    return false;
                }
                var server = _settings.Settings.ManagedServers.FirstOrDefault();
                if (server is null)
                {
                    _toasts.Show("No managed server configured", "Add a managed server in Services before setting a model folder.", ToastKind.Warning, 6000);
                    return true;
                }
                server.ModelPath = ModelFolder.Trim();
                if (!File.Exists(server.ModelPath))
                {
                    StarterModelDownloadError = $"The selected model is missing at {server.ModelPath}. Retry the download or choose another model.";
                    return false;
                }
                await _settings.SaveAsync();
                return true;
            case 3:
                if (SelectedVoiceProvider is not null)
                    await _voiceProviders.SetActiveProviderAsync(SelectedVoiceProvider.Id);
                return true;
            default:
                return true;
        }
    }

    /// <summary>
    /// r12 01-settings-lifecycle.md 1.1: re-running the wizard (Settings'
    /// "re-run setup" link) and changing the data root used to call a plain
    /// <c>SaveAsync()</c>, which never migrates - conversations, memories,
    /// RAG, and traces stayed behind in the old root and silently
    /// "disappeared". This mirrors <see cref="SettingsViewModel.SaveAsync"/>:
    /// preview conflicts with the same message the Settings page shows,
    /// build a candidate copy so a failed save cannot leave a half-applied
    /// edit in the live settings, and surface the same migrated-files toast.
    /// </summary>
    private async Task<bool> ApplyDataRootStepAsync()
    {
        var previousDataRoot = _settings.Settings.DataManagement.DataRootDirectory;
        var nextDataRoot = DataRootDirectory.Trim();

        var plan = _settings.PreviewDataRootMigration(previousDataRoot, nextDataRoot);
        if (plan.Conflicts.Count > 0)
        {
            _toasts.Show("Data root not changed",
                $"Move blocked: {plan.Conflicts.Count} existing database file(s) in target.",
                ToastKind.Error, 7000);
            return false;
        }

        var candidate = _settings.Settings.Clone();
        candidate.DataManagement.DataRootDirectory = nextDataRoot;
        candidate.DataManagement.LocalAiAssetsRoot = LocalAiAssetsRoot.Trim();

        try
        {
            var result = await _settings.SaveAsync(candidate, previousDataRoot);
            if (result.DataMigrated)
            {
                var message = $"Moved {result.FilesMoved} database file(s) to {result.CurrentDataRoot}. Backup: {result.BackupDirectory}";
                _toasts.Show("Hermaeus data moved", message, ToastKind.Success, 7000);
            }
        }
        catch (Exception ex)
        {
            _toasts.Show("Data root not changed", ex.Message, ToastKind.Error, 7000);
            return false;
        }

        return true;
    }
}
