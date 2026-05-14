using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class TtsSettingsViewModel : ObservableObject
{
    private readonly ITtsService _tts;
    private readonly IVoiceProviderRegistry _voiceProviderRegistry;
    private readonly IToastService _toasts;
    private readonly XttsProcessManager _xttsProcess;
    private readonly ISecretStore _secrets;
    private readonly ISettingsService _settings;
    private readonly SynchronizationContext? _sync;

    [ObservableProperty] private bool   _ttsEnabled = true;
    [ObservableProperty] private string _ttsServiceUrl = "http://127.0.0.1:8020";
    [ObservableProperty] private string _ttsSpeaker = string.Empty;
    [ObservableProperty] private string _ttsPythonPath = string.Empty;
    [ObservableProperty] private string _ttsScriptPath = string.Empty;
    [ObservableProperty] private string _ttsModelDirectory = string.Empty;
    [ObservableProperty] private string _ttsOutputDirectory = string.Empty;
    [ObservableProperty] private string _ttsVoiceDirectory = string.Empty;
    [ObservableProperty] private string _ttsDevice = "cpu";
    [ObservableProperty] private string _ttsModelVersion = "2.0.3";
    [ObservableProperty] private bool   _ttsPreload;
    [ObservableProperty] private string _ttsPreviewText = "Aether voice preview is ready.";
    [ObservableProperty] private string _ttsCloneDisplayName = string.Empty;
    [ObservableProperty] private string _ttsStatus = "Stopped";
    [ObservableProperty] private string _selectedVoiceProvider = "Kokoro";

    public string[] TtsDevices { get; } = ["cpu", "auto", "cuda"];
    public ObservableCollection<string> TtsVoices { get; } = ["default"];
    public ObservableCollection<VoiceProviderInfo> VoiceProviders { get; } = [];

    public bool IsTtsRunning => IsLegacyVoiceBackend && _xttsProcess.IsRunning;
    public bool IsLegacyVoiceBackend
    {
        get
        {
            var provider = VoiceProviders.FirstOrDefault(p => p.Name.Equals(SelectedVoiceProvider, StringComparison.OrdinalIgnoreCase));
            // Legacy XTTS backend requires local capability and TTS support
            return provider is not null
                && provider.Id == VoiceProvider.XttsV2
                && provider.Capabilities.HasFlag(VoiceCapability.Local)
                && provider.Capabilities.HasFlag(VoiceCapability.TextToSpeech);
        }
    }

    public bool IsKokoroProvider
    {
        get
        {
            var provider = VoiceProviders.FirstOrDefault(p => p.Name.Equals(SelectedVoiceProvider, StringComparison.OrdinalIgnoreCase));
            return provider is not null && provider.Id == VoiceProvider.Kokoro;
        }
    }

    public bool IsF5TtsProvider
    {
        get
        {
            var provider = VoiceProviders.FirstOrDefault(p => p.Name.Equals(SelectedVoiceProvider, StringComparison.OrdinalIgnoreCase));
            return provider is not null && provider.Id == VoiceProvider.F5Tts;
        }
    }

    public bool IsOpenAiProvider
    {
        get
        {
            var provider = VoiceProviders.FirstOrDefault(p => p.Name.Equals(SelectedVoiceProvider, StringComparison.OrdinalIgnoreCase));
            return provider is not null && provider.Id == VoiceProvider.OpenAi;
        }
    }

    public TtsSettingsViewModel(
        ITtsService tts,
        IVoiceProviderRegistry voiceProviderRegistry,
        IToastService toasts,
        XttsProcessManager xttsProcess,
        ISecretStore secrets,
        ISettingsService settings)
    {
        _tts = tts;
        _voiceProviderRegistry = voiceProviderRegistry;
        _toasts = toasts;
        _xttsProcess = xttsProcess;
        _secrets = secrets;
        _settings = settings;
        _sync = SynchronizationContext.Current;
        _xttsProcess.StatusChanged += OnXttsStatusChanged;
    }

    private void OnXttsStatusChanged()
    {
        if (_sync is not null)
            _sync.Post(_ => ApplyXttsStatus(), null);
        else
            ApplyXttsStatus();
    }

    private void ApplyXttsStatus()
    {
        TtsStatus = IsLegacyVoiceBackend ? _xttsProcess.StatusLabel : "Ready";
        OnPropertyChanged(nameof(IsTtsRunning));
        StartTtsCommand.NotifyCanExecuteChanged();
        StopTtsCommand.NotifyCanExecuteChanged();
    }

    public void ReloadFrom(AppSettings settings)
    {
        TtsEnabled = settings.Tts.Enabled;
        TtsServiceUrl = settings.Tts.ServiceUrl;
        TtsSpeaker = settings.Tts.Speaker;
        TtsPythonPath = _secrets.IsReference(settings.Tts.PythonPath) ? string.Empty : settings.Tts.PythonPath;
        TtsScriptPath = settings.Tts.ScriptPath;
        TtsModelDirectory = settings.Tts.ModelDirectory;
        TtsOutputDirectory = settings.Tts.OutputDirectory;
        TtsVoiceDirectory = settings.Tts.VoiceDirectory;
        TtsDevice = settings.Tts.Device;
        TtsModelVersion = settings.Tts.ModelVersion;
        TtsPreload = settings.Tts.Preload;
        TtsStatus = IsLegacyVoiceBackend ? _xttsProcess.StatusLabel : "Ready";
        VoiceProviders.Clear();
        foreach (var provider in _voiceProviderRegistry.GetAvailableProviders())
            VoiceProviders.Add(provider);
        SelectedVoiceProvider = settings.Tts.VoiceProvider;
        OnPropertyChanged(nameof(IsLegacyVoiceBackend));
    }

    [RelayCommand]
    private async Task SetActiveVoiceProviderAsync(VoiceProviderInfo? provider)
    {
        if (provider is null) return;

        // Enforce that providers exposed as selectable support TTS
        if (!provider.Capabilities.HasFlag(VoiceCapability.TextToSpeech))
        {
            _toasts.Show("Provider not supported", $"{provider.Name} does not support text-to-speech.", ToastKind.Warning, 5000);
            return;
        }

        try
        {
            await _voiceProviderRegistry.SetActiveProviderAsync(provider.Id);
            SelectedVoiceProvider = provider.Name;
            await RefreshTtsVoicesAsync();
            OnPropertyChanged(nameof(IsLegacyVoiceBackend));
            OnPropertyChanged(nameof(IsKokoroProvider));
            OnPropertyChanged(nameof(IsF5TtsProvider));
            OnPropertyChanged(nameof(IsOpenAiProvider));
            _toasts.Show("Voice provider changed", $"Now using {provider.Name}.", ToastKind.Success, 4000);
        }
        catch (Exception ex)
        {
            _toasts.Show("Provider change failed", ex.Message, ToastKind.Error, 6000);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartTts))]
    private async Task StartTtsAsync()
    {
        if (!IsLegacyVoiceBackend)
        {
            _toasts.Show("No voice server needed", "Kokoro and F5-TTS generate directly without a background server.", ToastKind.Info, 5000);
            return;
        }

        if (string.IsNullOrWhiteSpace(TtsScriptPath))
        {
            if (string.IsNullOrWhiteSpace(TtsScriptPath))
            {
                _toasts.Show("XTTS path needed", "Choose the XTTS API server script before starting XTTS.", ToastKind.Warning);
                return;
            }
        }

        try
        {
            await _xttsProcess.StartAsync(_settings.Settings);
            _toasts.Show("XTTS v2 started", $"Listening at {TtsServiceUrl}", ToastKind.Success);
            await RefreshTtsVoicesAsync();
        }
        catch (Exception ex)
        {
            _toasts.Show("XTTS v2 failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopTts))]
    private void StopTts()
    {
        if (!IsLegacyVoiceBackend)
        {
            _toasts.Show("No voice server running", "The current provider does not use a background XTTS server.", ToastKind.Info, 4000);
            return;
        }

        _xttsProcess.Stop();
        _toasts.Show("XTTS v2 stopped", "The local voice server was stopped.", ToastKind.Info);
    }

    [RelayCommand]
    private async Task RefreshTtsVoicesAsync()
    {
        try
        {
            var voices = await _tts.GetVoicesAsync();
            TtsVoices.Clear();
            foreach (var voice in voices)
                TtsVoices.Add(voice);

            if (!string.IsNullOrWhiteSpace(TtsSpeaker) && !TtsVoices.Contains(TtsSpeaker))
                TtsVoices.Add(TtsSpeaker);
        }
        catch (Exception ex)
        {
            _toasts.Show("Voice list unavailable", ex.Message, ToastKind.Warning);
        }
    }

    [RelayCommand]
    private async Task PreviewTtsVoiceAsync()
    {
        try
        {
            await _tts.PreviewVoiceAsync(TtsSpeaker, TtsPreviewText);
            _toasts.Show("Voice preview played", string.IsNullOrWhiteSpace(TtsSpeaker) ? "default" : TtsSpeaker, ToastKind.Success);
        }
        catch (Exception ex)
        {
            _toasts.Show("Voice preview failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    public async Task ImportTtsVoiceSampleAsync(string sourcePath)
    {
        try
        {
            var imported = await _tts.ImportVoiceSampleAsync(sourcePath, TtsCloneDisplayName);
            TtsSpeaker = imported;
            await RefreshTtsVoicesAsync();
            _toasts.Show("Voice imported", Path.GetFileName(imported), ToastKind.Success);
        }
        catch (Exception ex)
        {
            _toasts.Show("Voice import failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    private bool CanStartTts() => IsLegacyVoiceBackend && !IsTtsRunning;
    private bool CanStopTts() => IsLegacyVoiceBackend && IsTtsRunning;
}
