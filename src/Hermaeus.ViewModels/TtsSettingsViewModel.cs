using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

/// <summary>One row of the per-channel voice settings editor (Settings > Voice).</summary>
public partial class VoiceChannelSettingViewModel : ObservableObject
{
    /// <summary>r24: display sentinel for "use the global voice" in the channel voice picker;
    /// never itself stored - <see cref="VoiceDisplay"/> maps it to/from an empty <see cref="VoiceId"/>.</summary>
    public const string DefaultVoiceLabel = "(Default voice)";

    public VoiceChannel Channel { get; }
    public string DisplayName { get; }
    /// <summary>
    /// Per-row catalogue owned by this channel. Each editable ComboBox gets its
    /// own collection instance so a provider refresh cannot share stale items
    /// or selection state between rows.
    /// </summary>
    public UiBoundCollection<string> VoiceOptions { get; }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _voiceId = string.Empty;

    /// <summary>Set by the owning <see cref="TtsSettingsViewModel"/> whenever the active
    /// voice provider changes; drives <see cref="ShowsRemoteNotice"/> (r6 03-platform-cleanup.md 3.4).</summary>
    [ObservableProperty] private bool _remoteProviderActive;

    /// <summary>True when this channel is enabled and the active provider sends spoken text off-machine.</summary>
    public bool ShowsRemoteNotice => Enabled && RemoteProviderActive;

    /// <summary>r24: the channel voice picker's bound text - a provider voice id, or
    /// <see cref="DefaultVoiceLabel"/> for "use the global voice".</summary>
    public string VoiceDisplay
    {
        get => string.IsNullOrEmpty(VoiceId) ? DefaultVoiceLabel : VoiceId;
        set
        {
            if (string.Equals(value, DefaultVoiceLabel, StringComparison.Ordinal))
            {
                VoiceId = string.Empty;
                return;
            }

            // An empty edit is not a deliberate channel reset. The explicit
            // default sentinel remains the way to select the global voice,
            // while transient empty edits preserve the last real choice.
            if (string.IsNullOrWhiteSpace(value))
                return;

            VoiceId = value.Trim();
        }
    }

    public VoiceChannelSettingViewModel(
        VoiceChannel channel,
        string displayName,
        UiBoundCollection<string>? voiceOptions = null)
    {
        Channel = channel;
        DisplayName = displayName;
        VoiceOptions = voiceOptions ?? [];
    }

    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(ShowsRemoteNotice));
    partial void OnRemoteProviderActiveChanged(bool value) => OnPropertyChanged(nameof(ShowsRemoteNotice));
    partial void OnVoiceIdChanged(string value) => OnPropertyChanged(nameof(VoiceDisplay));
}

public partial class AudioFeedbackToggleViewModel : ObservableObject
{
    public AudioFeedbackEventKind Kind { get; }
    public string DisplayName { get; }
    [ObservableProperty] private bool _enabled;

    public AudioFeedbackToggleViewModel(AudioFeedbackEventKind kind)
    {
        Kind = kind;
        DisplayName = kind.ToString();
    }
}

public partial class TtsSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ITtsService _tts;
    private readonly IVoiceProviderRegistry _voiceProviderRegistry;
    private readonly IToastService _toasts;
    private readonly XttsProcessManager _xttsProcess;
    private readonly KokoroProcessManager _kokoroProcess;
    private readonly ISettingsService _settings;
    private readonly IVoiceOrchestrator? _voice;
    private bool _externalServiceRunning;
    private bool _isReloading;
    private VoiceProvider? _lastHealthProvider;
    private VoiceHealthStatus? _lastHealthStatus;
    private long _voiceRefreshGeneration;
    private CancellationTokenSource? _voiceRefreshCancellation;

    public UiBoundCollection<VoiceChannelSettingViewModel> VoiceChannels { get; } = [];
    public UiBoundCollection<AudioFeedbackToggleViewModel> AudioFeedbackEvents { get; } = [];

    /// <summary>r24: the channel voice picker's catalogue - the default-voice sentinel
    /// followed by the active provider's own voices. Each channel receives a
    /// separate snapshot from this parent catalogue.</summary>
    public UiBoundCollection<string> ChannelVoiceOptions { get; } = [VoiceChannelSettingViewModel.DefaultVoiceLabel];

    [ObservableProperty] private bool _autoSpeakChatReplies;
    [ObservableProperty] private bool _streamingChatSpeech;
    [ObservableProperty] private bool _audioFeedbackEnabled = true;
    [ObservableProperty] private int _audioFeedbackVolume = 50;
    [ObservableProperty] private bool _audioFeedbackMuted;
    [ObservableProperty] private bool _suppressAudioFeedbackWhileTts = true;

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
    [ObservableProperty] private string _ttsPreviewText = "Hermaeus voice preview is ready.";
    [ObservableProperty] private string _ttsCloneDisplayName = string.Empty;
    [ObservableProperty] private string _ttsStatus = "Stopped";
    [ObservableProperty] private string _selectedVoiceProvider = "Kokoro (native)";
    [ObservableProperty] private bool _isRefreshingVoices;

    public Func<Task>? RequestTtsVoiceSamplePicker { get; set; }
    public Action? RequestTtsPythonPicker { get; set; }
    public Action? RequestTtsScriptPicker { get; set; }
    public Action? RequestTtsModelDirectoryPicker { get; set; }
    public Action? RequestTtsOutputPicker { get; set; }
    public Action? RequestTtsVoiceDirectoryPicker { get; set; }
    public Action<string>? RequestNavigate { get; set; }

    public string[] TtsDevices { get; } = ["cpu", "auto", "cuda", "rocm", "mps"];
    public UiBoundCollection<string> TtsVoices { get; } = ["default"];
    public UiBoundCollection<VoiceProviderInfo> VoiceProviders { get; } = [];

    /// <summary>
    /// The settings editor displays provider names, but persistence must use
    /// the stable enum id so Kokoro (Python) cannot be reloaded as native
    /// Kokoro on the next startup.
    /// </summary>
    public VoiceProvider SelectedVoiceProviderId
    {
        get
        {
            var selected = VoiceProviders.FirstOrDefault(p =>
                p.Name.Equals(SelectedVoiceProvider, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                return selected.Id;

            return VoiceProviderIdentity.TryParse(SelectedVoiceProvider, out var parsed)
                ? parsed
                : VoiceProvider.KokoroNative;
        }
    }

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

    /// <summary>True when the active provider sends spoken text to a remote endpoint (r6 3.4).</summary>
    public bool IsSelectedProviderRemote
    {
        get
        {
            var provider = VoiceProviders.FirstOrDefault(p => p.Name.Equals(SelectedVoiceProvider, StringComparison.OrdinalIgnoreCase));
            return provider is not null && provider.Capabilities.HasFlag(VoiceCapability.Remote);
        }
    }

    /// <summary>
    /// Native Kokoro's actionable failure is owned by Doctor, not by the
    /// Services settings editor. This remains false until a health probe has
    /// observed a non-healthy result for the currently selected provider.
    /// </summary>
    public bool CanOpenDoctor => IsKokoroNativeProvider
        && _lastHealthProvider == VoiceProvider.KokoroNative
        && _lastHealthStatus is not null
        && _lastHealthStatus != VoiceHealthStatus.Healthy;

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
        _settings = settings;
        _voice = voiceOrchestrator;
        foreach (var kind in Enum.GetValues<AudioFeedbackEventKind>())
            AudioFeedbackEvents.Add(new AudioFeedbackToggleViewModel(kind));
        _xttsProcess.StatusChanged += OnXttsStatusChanged;
        _kokoroProcess.StatusChanged += OnXttsStatusChanged;
        TtsVoices.CollectionChanged += (_, _) =>
        {
            RefreshChannelVoiceOptions();
            OnPropertyChanged(nameof(ChannelVoiceDiscoveryStatus));
        };
        RefreshChannelVoiceOptions();
    }

    /// <summary>r24: recomputes the channel voice picker's suggestion list from the
    /// current <see cref="TtsVoices"/> (the active provider's own voices).</summary>
    private void RefreshChannelVoiceOptions()
    {
        ChannelVoiceOptions.Clear();
        ChannelVoiceOptions.Add(VoiceChannelSettingViewModel.DefaultVoiceLabel);
        foreach (var voice in TtsVoices)
            ChannelVoiceOptions.Add(voice);
        foreach (var channel in VoiceChannels)
        {
            channel.VoiceOptions.Clear();
            foreach (var voice in ChannelVoiceOptions)
                channel.VoiceOptions.Add(voice);
        }
        OnPropertyChanged(nameof(ChannelVoiceOptionsAreProviderSupplied));
        OnPropertyChanged(nameof(ChannelVoiceDiscoveryStatus));
    }

    /// <summary>
    /// r29 doc 01 1.2: false while the picker holds nothing but the sentinel and
    /// the placeholder "default" <see cref="TtsVoices"/> starts with, which is
    /// the common state because <see cref="RefreshTtsVoicesAsync"/> is fired and
    /// forgotten at construction and leaves the initial list in place when the
    /// voice service is not running. The picker looks populated and is not; the
    /// view uses this to say so and name the fix.
    /// </summary>
    public bool ChannelVoiceOptionsAreProviderSupplied =>
        ChannelVoiceOptions.Any(o =>
            o != VoiceChannelSettingViewModel.DefaultVoiceLabel &&
            !string.Equals(o, "default", StringComparison.OrdinalIgnoreCase));

    public string ChannelVoiceDiscoveryStatus => IsRefreshingVoices
        ? $"Loading voices from {SelectedVoiceProvider}..."
        : ChannelVoiceOptionsAreProviderSupplied
            ? $"{ChannelVoiceOptions.Count(o => o != VoiceChannelSettingViewModel.DefaultVoiceLabel && !string.Equals(o, "default", StringComparison.OrdinalIgnoreCase))} named voice(s) reported by {SelectedVoiceProvider}."
            : $"{SelectedVoiceProvider} has not reported named voices yet. Retrying is safe; you can also enter a verified voice id.";

    private static readonly VoiceChannel[] AllChannels =
    [
        VoiceChannel.Chat, VoiceChannel.Agent, VoiceChannel.Doctor,
        VoiceChannel.Benchmark, VoiceChannel.Notification, VoiceChannel.System
    ];

    /// <summary>
    /// Writes the channel voice editor state back onto <paramref name="tts"/>.
    /// Called from <c>SettingsViewModel.ApplyTtsTo</c> alongside the rest of
    /// the TTS field mapping. Never touches <see cref="TtsSettings.Profiles"/>
    /// (r24: legacy, read-only from this UI).
    /// </summary>
    public void ApplyVoiceOrchestrationTo(TtsSettings tts)
    {
        tts.AutoSpeakChatReplies = AutoSpeakChatReplies;
        tts.StreamingChatSpeech = StreamingChatSpeech;
        tts.AudioFeedback.Enabled = AudioFeedbackEnabled;
        tts.AudioFeedback.Volume = Math.Clamp(AudioFeedbackVolume, 0, 100);
        tts.AudioFeedback.Muted = AudioFeedbackMuted;
        tts.AudioFeedback.SuppressWhileTtsSpeaking = SuppressAudioFeedbackWhileTts;
        tts.AudioFeedback.EventEnabled = AudioFeedbackEvents.ToDictionary(item => item.Kind.ToString(), item => item.Enabled);
        tts.Channels = VoiceChannels.ToDictionary(
            c => c.Channel.ToString(),
            c => new VoiceChannelConfig { Enabled = c.Enabled, VoiceId = c.VoiceId.Trim() });
    }

    /// <summary>r24: a channel's voice id, preferring the direct value and falling back to
    /// resolving a legacy ProfileName against the (no-longer-editable) Profiles list once,
    /// so an existing per-channel choice made before profiles were removed is not lost.</summary>
    private static string ResolveChannelVoiceId(TtsSettings tts, VoiceChannelConfig? config)
    {
        if (config is null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(config.VoiceId)) return config.VoiceId;
        if (string.IsNullOrWhiteSpace(config.ProfileName)) return string.Empty;

        var profile = tts.Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, config.ProfileName, StringComparison.OrdinalIgnoreCase));
        return profile?.VoiceId ?? string.Empty;
    }

    private void ReloadVoiceOrchestration(TtsSettings tts)
    {
        VoiceChannels.Clear();
        foreach (var channel in AllChannels)
        {
            var hasConfig = tts.Channels.TryGetValue(channel.ToString(), out var config);
            var enabled = hasConfig ? config!.Enabled : channel == VoiceChannel.Chat;
            var voiceId = ResolveChannelVoiceId(tts, config);
            var channelViewModel = new VoiceChannelSettingViewModel(channel, channel.ToString())
            { Enabled = enabled, VoiceId = voiceId };
            foreach (var option in ChannelVoiceOptions)
                channelViewModel.VoiceOptions.Add(option);
            VoiceChannels.Add(channelViewModel);
        }

        AutoSpeakChatReplies = tts.AutoSpeakChatReplies;
        StreamingChatSpeech = tts.StreamingChatSpeech;
        AudioFeedbackEnabled = tts.AudioFeedback.Enabled;
        AudioFeedbackVolume = Math.Clamp(tts.AudioFeedback.Volume, 0, 100);
        AudioFeedbackMuted = tts.AudioFeedback.Muted;
        SuppressAudioFeedbackWhileTts = tts.AudioFeedback.SuppressWhileTtsSpeaking;
        foreach (var item in AudioFeedbackEvents)
            item.Enabled = tts.AudioFeedback.IsEnabled(item.Kind);
        OnPropertyChanged(nameof(IsVoiceMuted));

        var remote = IsSelectedProviderRemote;
        foreach (var channel in VoiceChannels)
            channel.RemoteProviderActive = remote;
    }

    private void OnXttsStatusChanged() => RunOnUi(ApplyXttsStatus);

    private void ApplyXttsStatus()
    {
        if (!(IsKokoroNativeProvider && _lastHealthProvider == VoiceProvider.KokoroNative && _lastHealthStatus is not null))
        {
            TtsStatus = IsXttsV2Provider
                ? _xttsProcess.StatusLabel
                : IsKokoroProvider
                    ? _kokoroProcess.StatusLabel
                    : "Ready";
        }
        OnPropertyChanged(nameof(IsTtsRunning));
        OnPropertyChanged(nameof(IsServerManagedProvider));
        OnPropertyChanged(nameof(CanOpenDoctor));
        StartTtsCommand.NotifyCanExecuteChanged();
        StopTtsCommand.NotifyCanExecuteChanged();
    }

    partial void OnTtsStatusChanged(string value) => OnPropertyChanged(nameof(CanOpenDoctor));

    public void Dispose()
    {
        SupersedeVoiceRefresh();
        _xttsProcess.StatusChanged -= OnXttsStatusChanged;
        _kokoroProcess.StatusChanged -= OnXttsStatusChanged;
    }

    public void ReloadFrom(AppSettings settings)
    {
        _isReloading = true;
        TtsEnabled = settings.Tts.Enabled;
        TtsServiceUrl = settings.Tts.ServiceUrl;
        // r12 01-settings-lifecycle.md 1.6: Tts.PythonPath is never stored as
        // a secret reference (paths are not secrets), so the IsReference
        // guard here was dead and, if it ever did trip, would have blanked
        // the box on reload while ApplyTtsTo wrote it back unconditionally
        // on the next save - a reload/apply asymmetry that could wipe a
        // value. Reload it the same way as every other Tts path field.
        TtsPythonPath = settings.Tts.PythonPath;
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
        var providerName = SelectedVoiceProvider;
        var generation = Interlocked.Increment(ref _voiceRefreshGeneration);
        var cancellation = new CancellationTokenSource();
        var prior = Interlocked.Exchange(ref _voiceRefreshCancellation, cancellation);
        CancelVoiceRefresh(prior);
        IsRefreshingVoices = true;
        try
        {
            var voices = await _tts.GetVoicesAsync(cancellation.Token);
            if (!OwnsVoiceRefresh(providerName, generation, cancellation))
                return;

            TtsVoices.Clear();
            foreach (var voice in voices)
                TtsVoices.Add(voice);

            if (string.IsNullOrWhiteSpace(TtsSpeaker) && TtsVoices.Count > 0)
                TtsSpeaker = TtsVoices[0];
            else if (!string.IsNullOrWhiteSpace(TtsSpeaker) && !TtsVoices.Contains(TtsSpeaker))
                TtsVoices.Add(TtsSpeaker);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer provider selection or explicit retry owns the catalogue now.
        }
        catch (Exception ex)
        {
            if (OwnsVoiceRefresh(providerName, generation, cancellation))
                _toasts.Show("Voice list unavailable", ex.Message, ToastKind.Warning);
        }
        finally
        {
            if (OwnsVoiceRefresh(providerName, generation, cancellation))
            {
                IsRefreshingVoices = false;
                Interlocked.CompareExchange(ref _voiceRefreshCancellation, null, cancellation);
                OnPropertyChanged(nameof(ChannelVoiceDiscoveryStatus));
            }

            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void OpenDoctor() => RequestNavigate?.Invoke("doctor");

    private bool OwnsVoiceRefresh(string providerName, long generation, CancellationTokenSource cancellation) =>
        generation == Volatile.Read(ref _voiceRefreshGeneration)
        && ReferenceEquals(cancellation, Volatile.Read(ref _voiceRefreshCancellation))
        && string.Equals(providerName, SelectedVoiceProvider, StringComparison.Ordinal);

    private void SupersedeVoiceRefresh()
    {
        Interlocked.Increment(ref _voiceRefreshGeneration);
        CancelVoiceRefresh(Interlocked.Exchange(ref _voiceRefreshCancellation, null));
        IsRefreshingVoices = false;
        OnPropertyChanged(nameof(ChannelVoiceDiscoveryStatus));
    }

    private static void CancelVoiceRefresh(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return;

        cancellation.Cancel();
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
        VoiceProvider active;
        try
        {
            active = _voiceProviderRegistry.GetActiveProvider();
            _lastHealthProvider = active;
            var provider = _voiceProviderRegistry.GetVoiceProvider(active);
            var health = await provider.HealthCheckAsync(ct);
            _lastHealthStatus = health.Status;
            _externalServiceRunning = health.Status == VoiceHealthStatus.Healthy;
            TtsStatus = health.Summary;
        }
        catch (Exception ex)
        {
            active = _voiceProviderRegistry.GetActiveProvider();
            _lastHealthProvider = active;
            _lastHealthStatus = VoiceHealthStatus.Unhealthy;
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
        SupersedeVoiceRefresh();
        _lastHealthProvider = null;
        _lastHealthStatus = null;
        OnPropertyChanged(nameof(ChannelVoiceDiscoveryStatus));
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
        OnPropertyChanged(nameof(IsSelectedProviderRemote));

        var remote = IsSelectedProviderRemote;
        foreach (var channel in VoiceChannels)
            channel.RemoteProviderActive = remote;
    }

    private string NormalizeProviderName(string providerName)
    {
        var match = VoiceProviders.FirstOrDefault(p =>
            p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase)
            || p.Id.ToString().Equals(providerName, StringComparison.OrdinalIgnoreCase));
        return match?.Name ?? "Kokoro (native)";
    }
}
