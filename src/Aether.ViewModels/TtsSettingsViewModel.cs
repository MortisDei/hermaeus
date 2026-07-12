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

/// <summary>One row of the per-channel voice settings editor (Settings > Voice).</summary>
public partial class VoiceChannelSettingViewModel : ObservableObject
{
    public VoiceChannel Channel { get; }
    public string DisplayName { get; }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _profileName = string.Empty;

    public VoiceChannelSettingViewModel(VoiceChannel channel, string displayName)
    {
        Channel = channel;
        DisplayName = displayName;
    }
}

/// <summary>One editable named voice/speed combination (Settings > Voice > Profiles).</summary>
public partial class VoiceProfileEditViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _voiceId = string.Empty;
    [ObservableProperty] private double? _speed;
}

public partial class TtsSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ITtsService _tts;
    private readonly IVoiceProviderRegistry _voiceProviderRegistry;
    private readonly IToastService _toasts;
    private readonly XttsProcessManager _xttsProcess;
    private readonly KokoroProcessManager _kokoroProcess;
    private readonly ISecretStore _secrets;
    private readonly ISettingsService _settings;
    private readonly IVoiceOrchestrator? _voice;
    private readonly SynchronizationContext? _sync;
    private bool _externalServiceRunning;
    private bool _isReloading;

    public ObservableCollection<VoiceChannelSettingViewModel> VoiceChannels { get; } = [];
    public ObservableCollection<VoiceProfileEditViewModel> VoiceProfiles { get; } = [];

    [ObservableProperty] private bool _autoSpeakChatReplies;
    [ObservableProperty] private bool _streamingChatSpeech;

    public bool IsVoiceMuted
    {
        get => _voice?.IsMuted ?? false;
        set
        {
            if (_voice is null || _voice.IsMuted == value) return;
            _voice.IsMuted = value;
            OnPropertyChanged();
        }
    }

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
    [ObservableProperty] private double _ttsSpeed = 1.0;
    [ObservableProperty] private bool   _ttsPreload;
    [ObservableProperty] private string _ttsPreviewText = "Aether voice preview is ready.";
    [ObservableProperty] private string _ttsCloneDisplayName = string.Empty;
    [ObservableProperty] private string _ttsStatus = "Stopped";
    [ObservableProperty] private string _selectedVoiceProvider = "Kokoro (native)";

    public Func<Task>? RequestTtsVoiceSamplePicker { get; set; }
    public Action? RequestTtsPythonPicker { get; set; }
    public Action? RequestTtsScriptPicker { get; set; }
    public Action? RequestTtsModelDirectoryPicker { get; set; }
    public Action? RequestTtsOutputPicker { get; set; }
    public Action? RequestTtsVoiceDirectoryPicker { get; set; }

    public string[] TtsDevices { get; } = ["cpu", "auto", "cuda", "rocm", "mps"];
    public ObservableCollection<string> TtsVoices { get; } = ["default"];
    public ObservableCollection<VoiceProviderInfo> VoiceProviders { get; } = [];

    public bool IsTtsRunning => IsXttsV2Provider
        ? (_xttsProcess.IsRunning || _externalServiceRunning)
        : (IsKokoroProvider && (_kokoroProcess.IsRunning || _externalServiceRunning));

    public bool IsServerManagedProvider => IsXttsV2Provider || IsKokoroProvider;

    public bool IsXttsV2Provider
    {
        get
        {
            var provider = VoiceProviders.FirstOrDefault(p => p.Name.Equals(SelectedVoiceProvider, StringComparison.OrdinalIgnoreCase));
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

    public bool IsKokoroNativeProvider
    {
        get
        {
            var provider = VoiceProviders.FirstOrDefault(p => p.Name.Equals(SelectedVoiceProvider, StringComparison.OrdinalIgnoreCase));
            return provider is not null && provider.Id == VoiceProvider.KokoroNative;
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

    public bool ShowsXttsFields => IsXttsV2Provider;
    public bool ShowsVoiceSampleFields => IsXttsV2Provider || IsF5TtsProvider;
    public bool ShowsPythonFields => IsServerManagedProvider;

    public TtsSettingsViewModel(
        ITtsService tts,
        IVoiceProviderRegistry voiceProviderRegistry,
        IToastService toasts,
        XttsProcessManager xttsProcess,
        KokoroProcessManager kokoroProcess,
        ISecretStore secrets,
        ISettingsService settings,
        IVoiceOrchestrator? voiceOrchestrator = null)
    {
        _tts = tts;
        _voiceProviderRegistry = voiceProviderRegistry;
        _toasts = toasts;
        _xttsProcess = xttsProcess;
        _kokoroProcess = kokoroProcess;
        _secrets = secrets;
        _settings = settings;
        _voice = voiceOrchestrator;
        _sync = SynchronizationContext.Current;
        _xttsProcess.StatusChanged += OnXttsStatusChanged;
        _kokoroProcess.StatusChanged += OnXttsStatusChanged;
    }

    private static readonly VoiceChannel[] AllChannels =
    [
        VoiceChannel.Chat, VoiceChannel.Agent, VoiceChannel.Doctor,
        VoiceChannel.Benchmark, VoiceChannel.Notification, VoiceChannel.System
    ];

    [RelayCommand]
    private void AddVoiceProfile() => VoiceProfiles.Add(new VoiceProfileEditViewModel { Name = "New profile" });

    [RelayCommand]
    private void RemoveVoiceProfile(VoiceProfileEditViewModel? profile)
    {
        if (profile is not null) VoiceProfiles.Remove(profile);
    }

    /// <summary>
    /// Writes the channel/profile editor state back onto <paramref name="tts"/>.
    /// Called from <c>SettingsViewModel.ApplyTtsTo</c> alongside the rest of
    /// the TTS field mapping.
    /// </summary>
    public void ApplyVoiceOrchestrationTo(TtsSettings tts)
    {
        tts.AutoSpeakChatReplies = AutoSpeakChatReplies;
        tts.StreamingChatSpeech = StreamingChatSpeech;
        tts.Profiles = VoiceProfiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new VoiceProfile { Name = p.Name.Trim(), VoiceId = p.VoiceId.Trim(), Speed = p.Speed })
            .ToList();
        tts.Channels = VoiceChannels.ToDictionary(
            c => c.Channel.ToString(),
            c => new VoiceChannelConfig { Enabled = c.Enabled, ProfileName = c.ProfileName.Trim() });
    }

    private void ReloadVoiceOrchestration(TtsSettings tts)
    {
        VoiceProfiles.Clear();
        foreach (var profile in tts.Profiles)
            VoiceProfiles.Add(new VoiceProfileEditViewModel { Name = profile.Name, VoiceId = profile.VoiceId, Speed = profile.Speed });

        VoiceChannels.Clear();
        foreach (var channel in AllChannels)
        {
            var enabled = tts.Channels.TryGetValue(channel.ToString(), out var config)
                ? config.Enabled
                : channel == VoiceChannel.Chat;
            var profileName = tts.Channels.TryGetValue(channel.ToString(), out var existing) ? existing.ProfileName : string.Empty;
            VoiceChannels.Add(new VoiceChannelSettingViewModel(channel, channel.ToString()) { Enabled = enabled, ProfileName = profileName });
        }

        AutoSpeakChatReplies = tts.AutoSpeakChatReplies;
        StreamingChatSpeech = tts.StreamingChatSpeech;
        OnPropertyChanged(nameof(IsVoiceMuted));
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
        TtsStatus = IsXttsV2Provider
            ? _xttsProcess.StatusLabel
            : IsKokoroProvider
                ? _kokoroProcess.StatusLabel
                : "Ready";
        OnPropertyChanged(nameof(IsTtsRunning));
        OnPropertyChanged(nameof(IsServerManagedProvider));
        StartTtsCommand.NotifyCanExecuteChanged();
        StopTtsCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _xttsProcess.StatusChanged -= OnXttsStatusChanged;
        _kokoroProcess.StatusChanged -= OnXttsStatusChanged;
    }

    public void ReloadFrom(AppSettings settings)
    {
        _isReloading = true;
        TtsEnabled = settings.Tts.Enabled;
        TtsServiceUrl = settings.Tts.ServiceUrl;
        TtsPythonPath = _secrets.IsReference(settings.Tts.PythonPath) ? string.Empty : settings.Tts.PythonPath;
        TtsScriptPath = settings.Tts.ScriptPath;
        TtsModelDirectory = settings.Tts.ModelDirectory;
        TtsOutputDirectory = settings.Tts.OutputDirectory;
        TtsVoiceDirectory = settings.Tts.VoiceDirectory;
        TtsDevice = settings.Tts.Device;
        TtsModelVersion = settings.Tts.ModelVersion;
        TtsSpeed = settings.Tts.Speed;
        TtsPreload = settings.Tts.Preload;
        VoiceProviders.Clear();
        foreach (var provider in _voiceProviderRegistry.GetAvailableProviders())
            VoiceProviders.Add(provider);
        SelectedVoiceProvider = NormalizeProviderName(settings.Tts.VoiceProvider);
        TtsSpeaker = string.IsNullOrWhiteSpace(settings.Tts.Speaker) && (IsKokoroProvider || IsKokoroNativeProvider)
            ? "af_heart"
            : settings.Tts.Speaker;
        ReloadVoiceOrchestration(settings.Tts);
        _isReloading = false;
        NotifyProviderDependentProperties();
        ApplyXttsStatus();
        _ = RefreshTtsVoicesAsync();
    }

    [RelayCommand]
    private void BrowseTtsScript() => RequestTtsScriptPicker?.Invoke();

    [RelayCommand]
    private void BrowseTtsPython() => RequestTtsPythonPicker?.Invoke();

    [RelayCommand]
    private void BrowseTtsModelDirectory() => RequestTtsModelDirectoryPicker?.Invoke();

    [RelayCommand]
    private void BrowseTtsOutput() => RequestTtsOutputPicker?.Invoke();

    [RelayCommand]
    private void BrowseTtsVoiceDirectory() => RequestTtsVoiceDirectoryPicker?.Invoke();

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
            NotifyProviderDependentProperties();
            ApplyXttsStatus();
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
        if (!IsServerManagedProvider)
        {
            _toasts.Show("No voice service", "The current provider does not use a managed background service.", ToastKind.Info, 5000);
            return;
        }

        if (IsXttsV2Provider && string.IsNullOrWhiteSpace(TtsScriptPath))
        {
            _toasts.Show("XTTS path needed", "Choose the XTTS API server script before starting XTTS v2.", ToastKind.Warning);
            return;
        }

        try
        {
            var provider = _voiceProviderRegistry.GetVoiceProvider(IsXttsV2Provider ? VoiceProvider.XttsV2 : VoiceProvider.Kokoro);
            await provider.StartAsync();
            _toasts.Show(IsXttsV2Provider ? "XTTS v2 started" : "Kokoro service started", $"Listening at {TtsServiceUrl}", ToastKind.Success);
            await RefreshTtsVoicesAsync();
            ApplyXttsStatus();
        }
        catch (Exception ex)
        {
            _toasts.Show(IsXttsV2Provider ? "XTTS v2 failed" : "Kokoro service failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopTts))]
    private void StopTts()
    {
        if (!IsServerManagedProvider)
        {
            _toasts.Show("No voice service", "The current provider does not use a managed background service.", ToastKind.Info, 4000);
            return;
        }

        if (IsXttsV2Provider)
            _xttsProcess.Stop();
        else if (IsKokoroProvider)
            _kokoroProcess.Stop();

        _toasts.Show(IsXttsV2Provider ? "XTTS v2 stopped" : "Kokoro service stopped", "The local voice service was stopped.", ToastKind.Info);
        ApplyXttsStatus();
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

            if (string.IsNullOrWhiteSpace(TtsSpeaker) && TtsVoices.Count > 0)
                TtsSpeaker = TtsVoices[0];
            else if (!string.IsNullOrWhiteSpace(TtsSpeaker) && !TtsVoices.Contains(TtsSpeaker))
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
        if (string.IsNullOrWhiteSpace(TtsPreviewText))
        {
            _toasts.Show("Nothing to preview", "Enter some text before playing the voice preview.", ToastKind.Info, 5000);
            return;
        }

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

    [RelayCommand]
    private async Task ImportTtsVoiceSampleAsync()
    {
        if (RequestTtsVoiceSamplePicker is null)
        {
            _toasts.Show("Voice import unavailable", "Voice sample picker is not available in this view.", ToastKind.Warning, 5000);
            return;
        }

        await RequestTtsVoiceSamplePicker();
    }

    public async Task ProbeActiveProviderHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var active = _voiceProviderRegistry.GetActiveProvider();
            var provider = _voiceProviderRegistry.GetVoiceProvider(active);
            var health = await provider.HealthCheckAsync(ct);
            _externalServiceRunning = health.Status == VoiceHealthStatus.Healthy;
            TtsStatus = health.Summary;
        }
        catch (Exception ex)
        {
            _externalServiceRunning = false;
            TtsStatus = ex.Message;
        }
        ApplyXttsStatus();
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

    private bool CanStartTts() => IsServerManagedProvider && !IsTtsRunning;
    private bool CanStopTts() => IsServerManagedProvider && IsTtsRunning;

    partial void OnSelectedVoiceProviderChanged(string value)
    {
        NotifyProviderDependentProperties();
        ApplyXttsStatus();
        if (_isReloading)
            return;

        _ = ActivateSelectedProviderAsync(value);
    }

    private async Task ActivateSelectedProviderAsync(string providerName)
    {
        var provider = VoiceProviders.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
            return;

        try
        {
            await _voiceProviderRegistry.SetActiveProviderAsync(provider.Id);
            if ((provider.Id == VoiceProvider.Kokoro || provider.Id == VoiceProvider.KokoroNative) && string.IsNullOrWhiteSpace(TtsSpeaker))
                TtsSpeaker = "af_heart";
            await RefreshTtsVoicesAsync();
            NotifyProviderDependentProperties();
            ApplyXttsStatus();
        }
        catch (Exception ex)
        {
            _toasts.Show("Provider change failed", ex.Message, ToastKind.Error, 6000);
        }
    }

    private void NotifyProviderDependentProperties()
    {
        OnPropertyChanged(nameof(IsXttsV2Provider));
        OnPropertyChanged(nameof(IsKokoroProvider));
        OnPropertyChanged(nameof(IsKokoroNativeProvider));
        OnPropertyChanged(nameof(IsF5TtsProvider));
        OnPropertyChanged(nameof(IsOpenAiProvider));
        OnPropertyChanged(nameof(IsServerManagedProvider));
        OnPropertyChanged(nameof(ShowsXttsFields));
        OnPropertyChanged(nameof(ShowsVoiceSampleFields));
        OnPropertyChanged(nameof(ShowsPythonFields));
    }

    private string NormalizeProviderName(string providerName)
    {
        var match = VoiceProviders.FirstOrDefault(p =>
            p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase)
            || p.Id.ToString().Equals(providerName, StringComparison.OrdinalIgnoreCase));
        return match?.Name ?? "Kokoro (native)";
    }
}
