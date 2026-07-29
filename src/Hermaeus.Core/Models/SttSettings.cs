namespace Hermaeus.Core.Models;

/// <summary>
/// Speech-to-text configuration (r24 doc 05). Off by default; a Hermaeus with no
/// STT backend installed behaves exactly as before this existed. Field placement
/// follows the r22 Settings/Services split precedent: process/model/device fields
/// belong on the Services page, preference-only fields on the Settings page - see
/// each field's own doc comment.
/// </summary>
public class SttSettings
{
    // ── Services > Voice: process/model/device ─────────────────────────

    /// <summary>Master switch; every mic affordance is hidden or disabled while off.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Active provider: "OnnxNative" (in-process ONNX, local, default) or "OpenAi" (remote).</summary>
    public string Provider { get; set; } = "OnnxNative";

    /// <summary>Selected input device id from <see cref="Services.IAudioCapture.EnumerateDevices"/>. Empty means the platform default.</summary>
    public string InputDeviceId { get; set; } = string.Empty;

    /// <summary>Remote transcription model name. Base URL and API key reuse
    /// <see cref="LlmSettings.OpenAiBaseUrl"/>/<see cref="LlmSettings.OpenAiApiKey"/> -
    /// matching how OpenAI voice (TTS) already resolves its key through
    /// ISecretStore rather than asking for the same credential twice.</summary>
    public string RemoteModel { get; set; } = "whisper-1";

    // ── Settings > Voice: preferences only ──────────────────────────────

    /// <summary>Push-to-talk hotkey, in-app only on Linux (no system-wide hotkey
    /// support there; see <see cref="UiSettings.EnableLocalHotkeys"/>).</summary>
    public string PushToTalkKey { get; set; } = string.Empty;

    /// <summary>Dictation inserts the transcript at the cursor for editing; it never sends by itself.</summary>
    public bool InsertAtCursor { get; set; } = true;

    /// <summary>Hands-free conversation mode (5.5), Chat only. Off by default.</summary>
    public bool HandsFreeEnabled { get; set; } = false;

    /// <summary>Silence duration, in ms below the amplitude threshold, that ends an utterance.</summary>
    public int SilenceThresholdMs { get; set; } = 1200;

    /// <summary>Minimum utterance length in ms so a breath does not end the turn.</summary>
    public int MinUtteranceMs { get; set; } = 400;

    /// <summary>Hard maximum capture length in seconds, enforced regardless of silence detection.</summary>
    public int MaxUtteranceSeconds { get; set; } = 60;
}
