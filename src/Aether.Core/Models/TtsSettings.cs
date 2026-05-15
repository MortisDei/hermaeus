namespace Aether.Core.Models;

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
    /// Active voice provider name (e.g., "Kokoro", "XTTS", "F5-TTS", "OpenAI").
    /// </summary>
    public string VoiceProvider { get; set; } = "Kokoro";

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
    /// Device to use for TTS inference ("cpu", "cuda", or "rocm").
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
}
