using System.Collections.ObjectModel;
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

    public ObservableCollection<string> Steps { get; } =
    [
        "Data roots",
        "Chat backend",
        "Model folder",
        "Voice",
        "Doctor",
        "Finish"
    ];

    public ObservableCollection<RuntimeProfileViewModel> RuntimeOptions { get; } = [];
    public ObservableCollection<VoiceProviderInfo> VoiceOptions { get; } = [];
    public ObservableCollection<string> VoiceOnboardingSteps { get; } = [];

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
        await ApplyStepAsync(StepIndex);
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
        await ApplyStepAsync(StepIndex);
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

    private async Task ApplyStepAsync(int step)
    {
        switch (step)
        {
            case 0:
                _settings.Settings.DataManagement.DataRootDirectory = DataRootDirectory.Trim();
                _settings.Settings.DataManagement.LocalAiAssetsRoot = LocalAiAssetsRoot.Trim();
                await _settings.SaveAsync();
                break;
            case 1:
                // Guard against no runtime options being present
                if (RuntimeOptions.Count == 0) return;
                foreach (var profile in RuntimeOptions)
                    profile.Enabled = profile.Id == SelectedRuntimeId;
                foreach (var profile in RuntimeOptions)
                    await _runtimeProfiles.SaveAsync(profile.ToProfile());
                break;
            case 2:
                var server = _settings.Settings.ManagedServers.FirstOrDefault();
                if (server is not null)
                {
                    server.ModelPath = ModelFolder.Trim();
                    await _settings.SaveAsync();
                }
                break;
            case 3:
                if (SelectedVoiceProvider is not null)
                    await _voiceProviders.SetActiveProviderAsync(SelectedVoiceProvider.Id);
                break;
            default:
                break;
        }
    }

}
