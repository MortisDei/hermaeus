using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly RuntimeProfileService _runtimeProfiles;
    private readonly IVoiceProviderRegistry _voiceProviders;
    private readonly IDoctorService _doctor;
    private readonly IToastService _toasts;
    private readonly ISystemInfoService _systemInfo;
    private readonly ModelDownloadService _modelDownloads;

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
    [ObservableProperty] private StarterModelEntry? _recommendedStarterModel;
    [ObservableProperty] private bool _isDownloadingStarterModel;
    [ObservableProperty] private double _starterModelDownloadPercent;
    [ObservableProperty] private string _starterModelDownloadStatus = string.Empty;
    [ObservableProperty] private string _starterModelDownloadError = string.Empty;
    [ObservableProperty] private bool _starterModelDownloadCompleted;

    // ── Voice install from the wizard (docs/review 02-onboarding-and-usability.md 2.2) ──
    [ObservableProperty] private bool _isInstallingVoice;
    [ObservableProperty] private string _voiceInstallProgress = string.Empty;
    [ObservableProperty] private string _voiceInstallError = string.Empty;
    [ObservableProperty] private bool _voiceInstallCompleted;

    public bool CanInstallSelectedVoiceProvider => SelectedVoiceProvider?.Id == VoiceProvider.KokoroNative;

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
        ModelDownloadService? modelDownloads = null)
    {
        _settings = settings;
        _runtimeProfiles = runtimeProfiles;
        _voiceProviders = voiceProviders;
        _doctor = doctor;
        _toasts = toasts;
        _systemInfo = systemInfo;
        _modelDownloads = modelDownloads ?? new ModelDownloadService();
        LoadFromSettings();
    }

    public void LoadFromSettings()
    {
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
        var entry = RecommendedStarterModel ?? StarterModelCatalog.Small;

        IsDownloadingStarterModel = true;
        StarterModelDownloadCompleted = false;
        StarterModelDownloadError = string.Empty;
        StarterModelDownloadPercent = 0;
        try
        {
            var folder = ResolveStarterModelFolder();
            Directory.CreateDirectory(folder);
            var destination = Path.Combine(folder, entry.FileName);

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
                return;
            }

            ModelFolder = destination;
            StarterModelDownloadCompleted = true;
            StarterModelDownloadStatus = $"{entry.DisplayName} is ready.";
        }
        catch (Exception ex)
        {
            StarterModelDownloadError = ex.Message;
        }
        finally
        {
            IsDownloadingStarterModel = false;
        }
    }

    private string ResolveStarterModelFolder()
    {
        var configured = _settings.Settings.DataManagement.LocalAiAssetsRoot?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
            : Path.GetFullPath(configured);
        return Path.Combine(root, "Models", "chat");
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
        OnPropertyChanged(nameof(CanInstallSelectedVoiceProvider));
        OnPropertyChanged(nameof(VoiceReadinessSummary));
        VoiceInstallCompleted = false;
        VoiceInstallError = string.Empty;
    }

    partial void OnVoiceInstallCompletedChanged(bool value) => OnPropertyChanged(nameof(VoiceReadinessSummary));

    partial void OnStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStepTitle));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(IsNotLastStep));
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(IsStep5));
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
                var server = _settings.Settings.ManagedServers.FirstOrDefault();
                if (server is null)
                {
                    _toasts.Show("No managed server configured", "Add a managed server in Services before setting a model folder.", ToastKind.Warning, 6000);
                    return true;
                }
                server.ModelPath = ModelFolder.Trim();
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
                _toasts.Show("Aether data moved", message, ToastKind.Success, 7000);
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
