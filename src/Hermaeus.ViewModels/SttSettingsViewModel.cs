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
    private const long MaxFileBytes = 200L * 1024 * 1024;

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

    /// <summary>Wired by the View's code-behind to a native file picker filtered to .wav.</summary>
    public Func<Task<string?>>? RequestAudioFilePicker { get; set; }
    public Func<string, Task>? RequestCopyToClipboard { get; set; }

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
                TranscribeStatus = $"File is too large (max {MaxFileBytes / 1024 / 1024} MB).";
                return;
            }

            await using var stream = File.OpenRead(fullPath);
            var service = _providers.GetActiveService();
            var result = await service.TranscribeAsync(stream, new SpeechTranscribeOptions());

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
        await RequestCopyToClipboard(TranscribedText);
    }
}
