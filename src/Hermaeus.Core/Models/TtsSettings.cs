namespace Hermaeus.Core.Models;

/// <summary>
/// Text-to-Speech (TTS) configuration including voice provider settings,
/// service endpoints, and audio output preferences.
/// </summary>
public class TtsSettings
{
    /// <summary>
    /// Enable or disable TTS functionality.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// URL of the TTS service endpoint.
    /// </summary>
    public string ServiceUrl { get; set; } = "http://127.0.0.1:8020";

    /// <summary>
    /// Active voice provider name (e.g., "KokoroNative", "Kokoro", "XTTS", "F5-TTS", "OpenAI").
    /// </summary>
    public string VoiceProvider { get; set; } = "KokoroNative";

    /// <summary>
    /// Selected speaker or voice identity.
    /// </summary>
    public string Speaker { get; set; } = string.Empty;

    /// <summary>
    /// Path to the Python executable for local TTS backends.
    /// </summary>
    public string PythonPath { get; set; } = "";

    /// <summary>
    /// Path to the TTS backend script (e.g., XTTS API script).
    /// </summary>
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>
    /// Directory containing TTS model files.
    /// </summary>
    public string ModelDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Output directory for generated audio files.
    /// </summary>
    public string OutputDirectory { get; set; } = "";

    /// <summary>
    /// Device to use for TTS inference ("cpu", "cuda", "rocm", or "mps").
    /// </summary>
    public string Device { get; set; } = "cpu";

    /// <summary>
    /// TTS model version (e.g., "2.0.3" for XTTS).
    /// </summary>
    public string ModelVersion { get; set; } = "2.0.3";

    /// <summary>
    /// Playback speed multiplier for voice synthesis.
    /// </summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>
    /// Preload TTS models on startup.
    /// </summary>
    public bool Preload { get; set; } = false;

    /// <summary>
    /// Directory containing voice sample files for voice cloning.
    /// </summary>
    public string VoiceDirectory { get; set; } = "";

    /// <summary>
    /// Automatically speak each completed assistant chat reply through the
    /// voice orchestrator's Chat channel. Off by default; the manual
    /// per-message speak button works regardless of this setting.
    /// </summary>
    public bool AutoSpeakChatReplies { get; set; } = false;

    /// <summary>
    /// Experimental: speak chat replies sentence-by-sentence while the LLM
    /// is still generating, instead of waiting for the full reply. Only
    /// takes effect when <see cref="AutoSpeakChatReplies"/> is also on.
    /// </summary>
    public bool StreamingChatSpeech { get; set; } = false;

    /// <summary>
    /// Named voice/speed combinations that channels can reference instead of
    /// always using the global <see cref="Speaker"/>.
    /// </summary>
    public List<VoiceProfile> Profiles { get; set; } = [];

    /// <summary>
    /// Per-channel voice settings, keyed by <see cref="VoiceChannel"/> name.
    /// A channel missing from this dictionary is treated as disabled, except
    /// "Chat" which defaults to enabled to preserve the pre-r5 manual speak
    /// button behavior.
    /// </summary>
    public Dictionary<string, VoiceChannelConfig> Channels { get; set; } = [];
}

public sealed class VoiceProfile
{
    public string Name { get; set; } = "";
    public string VoiceId { get; set; } = "";
    public double? Speed { get; set; }
}

public sealed class VoiceChannelConfig
{
    public bool Enabled { get; set; }
    public string ProfileName { get; set; } = "";
}
