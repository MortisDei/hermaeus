using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.ViewModels;

public enum MicButtonState { Unavailable, Ready, Recording, Transcribing }

/// <summary>
/// r24 doc 05 5.4: shared dictation control state machine, one instance per
/// usage site (chat input, agent goal/reply box, RAG query box, ...). Press to
/// start, press again to stop; never auto-sends - the transcript is handed to
/// the host via <see cref="TranscriptReady"/> for the host to insert at its own
/// text box's cursor, since this control does not own that text box.
/// </summary>
public sealed partial class MicButtonViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioCapture? _capture;
    private readonly ISpeechRecognitionProviderRegistry? _sttProviders;
    private readonly ISettingsService _settings;
    private ICaptureSession? _session;
    private System.Threading.Timer? _maxUtteranceTimer;

    [ObservableProperty] private MicButtonState _state = MicButtonState.Unavailable;
    [ObservableProperty] private float _level;

    /// <summary>Fired with the transcript once recording stops and transcription
    /// completes successfully. Never fired for an empty/low-confidence result -
    /// a hands-free mode or dictation flow sending the room's silence as text
    /// would be worse than not firing at all.</summary>
    public event Action<string>? TranscriptReady;

    public string TooltipText => State switch
    {
        MicButtonState.Unavailable => _capture is null || _sttProviders is null
            ? "Speech recognition is not configured."
            : !_settings.Settings.Stt.Enabled
                ? "Speech recognition is off. Enable it in Services > Voice."
                : !_capture.IsAvailable
                    ? _capture.UnavailableReason ?? "No microphone available."
                    // r25 follow-up: pressing a live mic only to fail at transcription
                    // is a worse answer than saying up front that there is nothing to
                    // transcribe with.
                    : "Speech recognition model is not installed. Install it from Hermaeus Doctor.",
        MicButtonState.Ready => "Start dictation",
        MicButtonState.Recording => "Stop dictation",
        MicButtonState.Transcribing => "Transcribing...",
        _ => string.Empty
    };

    public MicButtonViewModel(IAudioCapture? capture, ISpeechRecognitionProviderRegistry? sttProviders, ISettingsService settings)
    {
        _capture = capture;
        _sttProviders = sttProviders;
        _settings = settings;

        // r25 follow-up: without this the control's state is whatever it was at
        // app startup, forever. Speech recognition is off by default, so enabling
        // it (or installing the model) left every mic button in the app disabled
        // until the next restart, with no indication why. The doc comment on
        // Refresh already said "call after Settings > Voice changes"; nothing
        // ever did.
        _settingsChanged = (_, _) => RunOnUi(Refresh);
        _settings.SettingsChanged += _settingsChanged;

        Refresh();
    }

    private readonly EventHandler? _settingsChanged;

    /// <summary>Re-evaluates availability; call after Settings > Voice changes. A no-op
    /// while a recording/transcription is in flight so a settings change mid-dictation
    /// cannot yank the control out from under an active session.</summary>
    public void Refresh()
    {
        if (State is MicButtonState.Recording or MicButtonState.Transcribing)
            return;

        RefreshCore();
    }

    /// <summary>The actual recompute, unguarded - used by <see cref="Refresh"/> for
    /// external callers and directly by the end of a recording/transcribe cycle, which
    /// must always resolve back to Ready/Unavailable regardless of the state it is
    /// leaving (Refresh's own guard would otherwise see State still at Transcribing and
    /// refuse to move it, leaving the control stuck).</summary>
    private void RefreshCore()
    {
        var available = _settings.Settings.Stt.Enabled
            && _capture is { IsAvailable: true }
            && _sttProviders is not null
            && ProviderIsUsable();
        State = available ? MicButtonState.Ready : MicButtonState.Unavailable;
        OnPropertyChanged(nameof(TooltipText));
    }

    /// <summary>
    /// r25 follow-up: an enabled mic with no installed model is not "ready", it is
    /// a button that records and then fails. Treated as unavailable, with a
    /// tooltip that says which of the two things is missing.
    /// </summary>
    private bool ProviderIsUsable()
    {
        try
        {
            return _sttProviders?.GetActiveService().IsAvailable == true;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (State == MicButtonState.Recording)
        {
            await StopAndTranscribeAsync();
            return;
        }

        if (State != MicButtonState.Ready || _capture is null)
            return;

        try
        {
            var deviceId = _settings.Settings.Stt.InputDeviceId;
            _session = await _capture.StartAsync(string.IsNullOrWhiteSpace(deviceId) ? null : deviceId);
            _session.PeakLevelChanged += OnPeakLevelChanged;
            State = MicButtonState.Recording;

            var maxSeconds = Math.Max(1, _settings.Settings.Stt.MaxUtteranceSeconds);
            _maxUtteranceTimer = new System.Threading.Timer(_ => RunOnUi(() => _ = StopAndTranscribeAsync()), null, TimeSpan.FromSeconds(maxSeconds), Timeout.InfiniteTimeSpan);
        }
        catch (Exception)
        {
            State = MicButtonState.Unavailable;
        }
        OnPropertyChanged(nameof(TooltipText));
    }

    private void OnPeakLevelChanged(float level) => RunOnUi(() => Level = level);

    private async Task StopAndTranscribeAsync()
    {
        _maxUtteranceTimer?.Dispose();
        _maxUtteranceTimer = null;

        var session = _session;
        _session = null;
        Level = 0;

        if (session is null)
        {
            RefreshCore();
            return;
        }

        session.PeakLevelChanged -= OnPeakLevelChanged;
        string? path;
        try
        {
            path = session.Stop();
        }
        finally
        {
            session.Dispose();
        }

        if (string.IsNullOrEmpty(path))
        {
            RefreshCore();
            return;
        }

        State = MicButtonState.Transcribing;
        OnPropertyChanged(nameof(TooltipText));
        try
        {
            if (_sttProviders is not null)
            {
                await using var stream = File.OpenRead(path);
                var service = _sttProviders.GetActiveService();
                var result = await service.TranscribeAsync(stream, new SpeechTranscribeOptions());
                if (!result.IsLowConfidence && !string.IsNullOrWhiteSpace(result.Text))
                    TranscriptReady?.Invoke(result.Text);
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* transient audio is transient */ }
            RefreshCore();
        }
    }

    public void Dispose()
    {
        if (_settingsChanged is not null)
            _settings.SettingsChanged -= _settingsChanged;
        _maxUtteranceTimer?.Dispose();
        _session?.Dispose();
    }
}
