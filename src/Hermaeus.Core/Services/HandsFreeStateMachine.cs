namespace Hermaeus.Core.Services;

public enum HandsFreeState { Idle, Listening, Transcribing, Sending, Speaking }

/// <summary>
/// r24 doc 05 5.5: hands-free conversation mode's explicit state machine -
/// Idle -> Listening -> Transcribing -> Sending -> Speaking -> Listening, with a
/// hard Stop from any state that returns to Idle immediately. Pure and driven
/// entirely by the caller feeding it level samples and outcomes, so it is
/// testable with a synthetic level source and needs no real audio capture,
/// microphone, or ONNX session - see docs/review/05-voice-input.md 5.5's own
/// testing note. Playback and capture are mutually exclusive: this type is never
/// in both Listening and Speaking at once, because State is a single field.
/// </summary>
public sealed class HandsFreeStateMachine
{
    private readonly float _amplitudeThreshold;
    private readonly TimeSpan _silenceThreshold;
    private readonly TimeSpan _minUtterance;
    private readonly TimeSpan _maxUtterance;

    private DateTime _listeningStartedUtc;
    private DateTime? _silenceStartedUtc;

    public HandsFreeState State { get; private set; } = HandsFreeState.Idle;

    /// <summary>Set when Listening ends because the endpointer decided the utterance is
    /// done (silence or max-length), for the caller to know whether to actually stop
    /// capturing and hand the WAV off for transcription.</summary>
    public event Action? UtteranceEndpointed;

    public HandsFreeStateMachine(
        float amplitudeThreshold = 0.02f,
        int silenceThresholdMs = 1200,
        int minUtteranceMs = 400,
        int maxUtteranceSeconds = 60)
    {
        _amplitudeThreshold = amplitudeThreshold;
        _silenceThreshold = TimeSpan.FromMilliseconds(silenceThresholdMs);
        _minUtterance = TimeSpan.FromMilliseconds(minUtteranceMs);
        _maxUtterance = TimeSpan.FromSeconds(maxUtteranceSeconds);
    }

    /// <summary>Idle -> Listening. No-op from any other state - only an explicit Stop can
    /// interrupt a run already in progress.</summary>
    public void Start(DateTime nowUtc)
    {
        if (State != HandsFreeState.Idle) return;
        BeginListening(nowUtc);
    }

    /// <summary>Hard stop from any state, returning to Idle immediately - the doc's own
    /// requirement that a hard Stop always works regardless of what is in flight.</summary>
    public void Stop()
    {
        State = HandsFreeState.Idle;
        _silenceStartedUtc = null;
    }

    /// <summary>Feeds one amplitude sample (0..1) from the active capture while Listening.
    /// A no-op in every other state. Ends the utterance (raising
    /// <see cref="UtteranceEndpointed"/> and moving to Transcribing) once amplitude has
    /// stayed below the threshold for silenceThresholdMs, but only after minUtteranceMs
    /// has elapsed (so a breath before speaking does not end the turn), or unconditionally
    /// once maxUtteranceSeconds is reached (so a noisy room does not record forever).</summary>
    public void OnLevel(float amplitude, DateTime nowUtc)
    {
        if (State != HandsFreeState.Listening) return;

        var elapsed = nowUtc - _listeningStartedUtc;
        if (elapsed >= _maxUtterance)
        {
            EndpointUtterance();
            return;
        }

        if (amplitude < _amplitudeThreshold)
        {
            _silenceStartedUtc ??= nowUtc;
            if (elapsed >= _minUtterance && nowUtc - _silenceStartedUtc.Value >= _silenceThreshold)
                EndpointUtterance();
        }
        else
        {
            _silenceStartedUtc = null;
        }
    }

    private void EndpointUtterance()
    {
        State = HandsFreeState.Transcribing;
        _silenceStartedUtc = null;
        UtteranceEndpointed?.Invoke();
    }

    /// <summary>Transcribing -> Sending, or back to Listening if the transcript is empty
    /// or low-confidence. Never auto-sends the room's background noise as a question -
    /// the doc's own explicit requirement.</summary>
    public void OnTranscriptReady(string transcript, bool isLowConfidence, DateTime nowUtc)
    {
        if (State != HandsFreeState.Transcribing) return;

        if (isLowConfidence || string.IsNullOrWhiteSpace(transcript))
        {
            BeginListening(nowUtc);
            return;
        }

        State = HandsFreeState.Sending;
    }

    /// <summary>Sending -> Speaking (a reply will be spoken) or directly back to
    /// Listening (muted / no voice channel active for this reply).</summary>
    public void OnReplyReady(bool willSpeak, DateTime nowUtc)
    {
        if (State != HandsFreeState.Sending) return;
        if (willSpeak)
            State = HandsFreeState.Speaking;
        else
            BeginListening(nowUtc);
    }

    /// <summary>Speaking -> Listening once playback finishes.</summary>
    public void OnSpeakingComplete(DateTime nowUtc)
    {
        if (State != HandsFreeState.Speaking) return;
        BeginListening(nowUtc);
    }

    private void BeginListening(DateTime nowUtc)
    {
        State = HandsFreeState.Listening;
        _listeningStartedUtc = nowUtc;
        _silenceStartedUtc = null;
    }
}
