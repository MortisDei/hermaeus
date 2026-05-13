using System.Collections.ObjectModel;
using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IRuntimeProfileService _runtimeProfiles;
    private readonly IVoiceProviderRegistry _voiceProviders;
    private readonly IDoctorService _doctor;
    private readonly IToastService _toasts;

    [ObservableProperty] private int _stepIndex;
    [ObservableProperty] private string _dataRootDirectory = string.Empty;
    [ObservableProperty] private string _localAiAssetsRoot = string.Empty;
    [ObservableProperty] private string _modelFolder = string.Empty;
    [ObservableProperty] private string _selectedRuntimeId = string.Empty;
    [ObservableProperty] private VoiceProviderInfo? _selectedVoiceProvider;
    [ObservableProperty] private RuntimeProfileViewModel? _selectedRuntime;
    [ObservableProperty] private string _doctorSummary = "Run Doctor to verify the setup.";
    [ObservableProperty] private bool _doctorRan;
    [ObservableProperty] private bool _isDoctorRunning;

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

    public Action? RequestDataRootPicker { get; set; }
    public Action? RequestLocalAiAssetsRootPicker { get; set; }
    public Action? RequestModelFolderPicker { get; set; }
    public event Action? WizardCompleted;

    public SetupWizardViewModel(
        ISettingsService settings,
        IRuntimeProfileService runtimeProfiles,
        IVoiceProviderRegistry voiceProviders,
        IDoctorService doctor,
        IToastService toasts)
    {
        _settings = settings;
        _runtimeProfiles = runtimeProfiles;
        _voiceProviders = voiceProviders;
        _doctor = doctor;
        _toasts = toasts;
        LoadFromSettings();
    }

    public void LoadFromSettings()
    {
        var s = _settings.Settings;
        DataRootDirectory = s.DataManagement.DataRootDirectory;
        LocalAiAssetsRoot = s.DataManagement.LocalAiAssetsRoot;
        ModelFolder = s.ManagedServers.FirstOrDefault()?.ModelPath ?? string.Empty;

        RuntimeOptions.Clear();
        foreach (var profile in _runtimeProfiles.Profiles)
            RuntimeOptions.Add(new RuntimeProfileViewModel(profile));
        SelectedRuntimeId = RuntimeOptions.FirstOrDefault(p => p.Enabled)?.Id
            ?? RuntimeOptions.FirstOrDefault()?.Id
            ?? string.Empty;

        VoiceOptions.Clear();
        foreach (var provider in _voiceProviders.GetAvailableProviders())
            VoiceOptions.Add(provider);
        SelectedVoiceProvider = VoiceOptions.FirstOrDefault(p => p.Name == s.Tts.VoiceProvider)
            ?? VoiceOptions.FirstOrDefault();
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
