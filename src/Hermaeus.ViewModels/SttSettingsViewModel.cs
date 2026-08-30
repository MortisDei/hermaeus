using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.ViewModels;

/// <summary>
/// Services > Voice speech-recognition section (r24 doc 05 5.1/5.3/5.7):
/// provider/device/model selection (process-and-model config, so Services not
/// Settings, per the r22 split precedent), the native model install action, and
/// "Transcribe audio file..." - the one STT path this round guarantees is
/// verifiable without a live microphone in the building.
/// </summary>
public partial class SttSettingsViewModel : ViewModelBase
{
    /// <summary>
    /// r25 doc 03: stated as a duration, because that is what the user is holding.
    ///
    /// The old cap was 200 MB of bytes, about 1.7 hours of 16 kHz mono PCM16, fed
    /// to wav2vec2 as ONE full-self-attention tensor. That was not a slow
    /// transcription, it was an out-of-memory kill of the whole application,
    /// reachable by picking an ordinary podcast file in a picker the app itself
    /// offered. Whisper decodes fixed 30-second windows, so memory no longer grows
    /// with length at all and this cap is about respecting the user's time rather
    /// than about survival.
    /// </summary>
    private const int MaxAudioMinutes = 90;

    private const long MaxFileBytes =
        (long)MaxAudioMinutes * 60 * 16000 * 2;

    private readonly ISettingsService _settings;
    private readonly ISpeechRecognitionProviderRegistry _providers;
    private readonly IToastService _toasts;
    private readonly IAudioCapture? _audioCapture;
    private readonly IDoctorService? _doctor;
    private bool _loading;

    [ObservableProperty] private bool _sttEnabled;
    [ObservableProperty] private string _selectedProvider = "OnnxNative";
    [ObservableProperty] private string _remoteModel = "whisper-1";
    [ObservableProperty] private string _selectedDeviceId = string.Empty;
    [ObservableProperty] private bool _isTranscribingFile;
    [ObservableProperty] private string _transcribedText = string.Empty;
    [ObservableProperty] private string _transcribeStatus = string.Empty;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _installProgress = string.Empty;

    public string[] Providers { get; } = ["OnnxNative", "OpenAi"];
    public UiBoundCollection<AudioInputDevice> Devices { get; } = [];
    public bool IsNativeProvider => SelectedProvider == "OnnxNative";
    public bool MicrophoneAvailable => _audioCapture?.IsAvailable ?? false;
    public string MicrophoneStatus => MicrophoneAvailable
        ? "Microphone available."
        : _audioCapture?.UnavailableReason ?? "No microphone checked.";

    /// <summary>
    /// r25 follow-up: whether the local model is actually on disk. The card had no
    /// such state at all, so "Install model" sat there unconditionally and was
    /// still sitting there after a successful install.
    /// </summary>
    public bool IsModelInstalled
    {
        get
        {
            try
            {
                return _providers.GetActiveService().IsAvailable;
            }
            catch
            {
                return false;
            }
        }
    }

    public string ModelStatus => IsModelInstalled
        ? "Speech recognition model installed."
        : "Speech recognition model is not installed.";

    /// <summary>
    /// r25 follow-up: installing is a Doctor action. Doctor is where this app's
    /// "something is missing, fix it" actions already live, and it already carries
    /// a speech-recognition check with its own install fix, so Services offering a
    /// second, independent install button meant two entry points where only one
    /// reported progress or completion. Services now reports state and hands off.
    /// </summary>
    public Action<string>? RequestNavigate { get; set; }

    [RelayCommand]
    private void OpenDoctorToInstall() => RequestNavigate?.Invoke("doctor");

    /// <summary>Re-reads model presence. Called on load and after Doctor reports an install.</summary>
    public void RefreshModelStatus()
    {
        OnPropertyChanged(nameof(IsModelInstalled));
        OnPropertyChanged(nameof(ModelStatus));
    }

    /// <summary>Wired by the View's code-behind to a native file picker filtered to .wav.</summary>
    public Func<Task<string?>>? RequestAudioFilePicker { get; set; }
    public Func<string, Task<bool>>? RequestCopyToClipboard { get; set; }

    public SttSettingsViewModel(
        ISettingsService settings,
        ISpeechRecognitionProviderRegistry providers,
        IToastService toasts,
        IAudioCapture? audioCapture = null,
        IDoctorService? doctor = null)
    {
        _settings = settings;
        _providers = providers;
        _toasts = toasts;
        _audioCapture = audioCapture;
        _doctor = doctor;

        Load();
        RefreshDevices();
    }

    private void Load()
    {
        _loading = true;
        try
        {
            var stt = _settings.Settings.Stt;
            SttEnabled = stt.Enabled;
            SelectedProvider = stt.Provider;
            RemoteModel = stt.RemoteModel;
            SelectedDeviceId = stt.InputDeviceId;
        }
        finally
        {
            _loading = false;
        }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        Devices.Clear();
        if (_audioCapture is null) return;
        foreach (var device in _audioCapture.EnumerateDevices())
            Devices.Add(device);
        OnPropertyChanged(nameof(MicrophoneAvailable));
        OnPropertyChanged(nameof(MicrophoneStatus));
        RefreshModelStatus();
    }

    partial void OnSttEnabledChanged(bool value) => SaveIfNotLoading();
    partial void OnSelectedProviderChanged(string value)
    {
        OnPropertyChanged(nameof(IsNativeProvider));
        SaveIfNotLoading();
    }
    partial void OnRemoteModelChanged(string value) => SaveIfNotLoading();
    partial void OnSelectedDeviceIdChanged(string value) => SaveIfNotLoading();

    private void SaveIfNotLoading()
    {
        if (_loading) return;

        var stt = _settings.Settings.Stt;
        stt.Enabled = SttEnabled;
        stt.Provider = SelectedProvider;
        stt.RemoteModel = RemoteModel;
        stt.InputDeviceId = SelectedDeviceId;
        _ = _settings.SaveAsync();
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_doctor is null || IsInstalling) return;

        IsInstalling = true;
        InstallProgress = string.Empty;
        try
        {
            var progress = new Progress<string>(s => RunOnUi(() => InstallProgress = s));
            var ok = await _doctor.InstallSpeechRecognitionAssetsAsync(progress);
            _toasts.Show(
                ok ? "Speech recognition installed" : "Install failed",
                ok ? "The speech recognition model was downloaded and verified." : "Could not install the speech recognition model; see logs.",
                ok ? ToastKind.Success : ToastKind.Warning);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>r24 doc 05 5.3: exercises the whole pipeline except capture, on a
    /// user-picked file - the one path this round guarantees is verifiable without
    /// a live microphone. Validates the picked path defensively even though it came
    /// from a native picker: normalized, no symlink, .wav only, size-capped.</summary>
    [RelayCommand]
    private async Task TranscribeFileAsync()
    {
        if (RequestAudioFilePicker is null || IsTranscribingFile) return;

        var path = await RequestAudioFilePicker();
        if (string.IsNullOrWhiteSpace(path)) return;

        IsTranscribingFile = true;
        TranscribedText = string.Empty;
        TranscribeStatus = string.Empty;
        try
        {
            string fullPath;
            FileInfo info;
            try
            {
                fullPath = Path.GetFullPath(path);
                info = new FileInfo(fullPath);
            }
            catch (Exception ex)
            {
                TranscribeStatus = $"Invalid file path: {ex.Message}";
                return;
            }

            if (!info.Exists)
            {
                TranscribeStatus = "File not found.";
                return;
            }
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                TranscribeStatus = "Symlinked files are not accepted.";
                return;
            }
            if (!string.Equals(info.Extension, ".wav", StringComparison.OrdinalIgnoreCase))
            {
                TranscribeStatus = "Only .wav files are accepted.";
                return;
            }
            if (info.Length > MaxFileBytes)
            {
                TranscribeStatus =
                    $"That recording is longer than the {MaxAudioMinutes}-minute limit for a single transcription.";
                return;
            }

            await using var stream = File.OpenRead(fullPath);
            var service = _providers.GetActiveService();
            var progress = new Progress<string>(message => RunOnUi(() => TranscribeStatus = message));
            var result = await service.TranscribeAsync(
                stream, new SpeechTranscribeOptions(Progress: progress));

            if (result.Error is not null)
            {
                TranscribeStatus = result.Error;
                return;
            }

            TranscribedText = result.Text;
            TranscribeStatus = result.IsLowConfidence
                ? "No speech detected."
                : $"Transcribed {result.DurationMs} ms of audio.";
        }
        finally
        {
            IsTranscribingFile = false;
        }
    }

    [RelayCommand]
    private async Task CopyTranscriptAsync()
    {
        if (RequestCopyToClipboard is null || string.IsNullOrEmpty(TranscribedText)) return;
        var copied = false;
        try { copied = await RequestCopyToClipboard(TranscribedText); }
        catch { }
        _toasts.Show(copied ? "Transcript copied" : "Could not copy transcript",
            copied ? "The transcript was copied to the clipboard." : "The clipboard was unavailable.",
            copied ? ToastKind.Success : ToastKind.Warning, 3000);
    }
}
