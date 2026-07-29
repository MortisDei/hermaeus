using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Voice;

/// <summary>
/// Native, in-process speech recognition: <see cref="Wav2Vec2OnnxModel"/> runs
/// directly, no subprocess and no HTTP round trip - mirrors
/// <see cref="NativeKokoroVoiceProvider"/>'s posture on the TTS side. This is the
/// default (and only local) speech recognition provider; see
/// docs/review/05-voice-input.md 5.1.
/// </summary>
public sealed class NativeSpeechRecognitionProvider : ISpeechRecognitionService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly Wav2Vec2OnnxModel _model;

    public string ProviderName => "Speech recognition (native)";
    public bool IsAvailable => _model.AssetsPresent();

    public NativeSpeechRecognitionProvider(ISettingsService settings)
    {
        _settings = settings;
        _model = new Wav2Vec2OnnxModel(() => ResolveAssetsDirectory(_settings.Settings));
    }

    public static string ResolveAssetsDirectory(AppSettings settings)
    {
        var configured = settings.DataManagement.LocalAiAssetsRoot?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus")
            : Path.GetFullPath(configured);
        return Path.Combine(root, "Models", "voice", "wav2vec2-stt");
    }

    public bool IsInstalled => _model.AssetsPresent();

    public VoiceProviderDetection Detect()
    {
        var assetsDir = ResolveAssetsDirectory(_settings.Settings);
        var modelPath = Wav2Vec2OnnxModel.ModelPath(assetsDir);
        if (!File.Exists(modelPath))
            return new VoiceProviderDetection(false, "Speech recognition model not installed", "Run the install action in Services > Voice to download and verify the model.");

        return new VoiceProviderDetection(true, "Speech recognition model detected", $"Model path: {modelPath}", modelPath);
    }

    public VoiceInstallPlan InstallPlan()
    {
        var assetsDir = ResolveAssetsDirectory(_settings.Settings);
        var steps = new List<VoiceInstallStep>
        {
            new(
                "Download the speech recognition ONNX model",
                assetsDir,
                "Downloads a pinned wav2vec2 CTC model (English, ~360 MB) from the official facebook/wav2vec2-base-960h repository, verifying it against a pinned SHA256 hash.",
                VoiceInstallRiskLevel.Medium,
                true,
                ["download", "facebook/wav2vec2-base-960h"])
        };

        return new VoiceInstallPlan(
            "Speech recognition runs fully in-process once its ONNX model is downloaded once.",
            steps,
            "Downloads roughly 360 MB from Hugging Face on first install; inference afterward is fully local and offline.");
    }

    public async Task<bool> InstallAssetsAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            await _model.InstallAssetsAsync(progress, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default)
    {
        if (!_model.AssetsPresent())
            return new VoiceHealth(VoiceHealthStatus.Warning, "Speech recognition model not installed", "Run the install action in Services > Voice to download the ONNX model.");

        var loaded = await _model.EnsureLoadedAsync(ct);
        return loaded
            ? new VoiceHealth(VoiceHealthStatus.Healthy, "Speech recognition model loaded", "ONNX session initialized from local assets.")
            : new VoiceHealth(VoiceHealthStatus.Unhealthy, "Speech recognition model failed to load", "Installed assets failed SHA256 verification or failed to load; reinstall.");
    }

    public async Task<SpeechTranscript> TranscribeAsync(Stream wavPcm16Mono16k, SpeechTranscribeOptions options, CancellationToken ct = default)
    {
        if (!await _model.EnsureLoadedAsync(ct))
            return new SpeechTranscript(string.Empty, 0, "en", false,
                "Speech recognition model is not installed. Run the install action in Services > Voice first.");

        WavFile.WavAudio audio;
        try
        {
            audio = WavFile.Read(wavPcm16Mono16k);
        }
        catch (Exception ex)
        {
            return new SpeechTranscript(string.Empty, 0, "en", false, ex.Message);
        }

        if (audio.Channels != 1 || audio.SampleRate != Wav2Vec2OnnxModel.SampleRate)
            return new SpeechTranscript(string.Empty, 0, "en", false,
                $"Expected 16kHz mono PCM16 audio, got {audio.SampleRate}Hz/{audio.Channels}ch.");

        var durationMs = audio.SampleRate == 0 ? 0 : (int)(audio.Samples.Length * 1000L / audio.SampleRate);

        string text;
        try
        {
            text = await Task.Run(() => _model.Transcribe(audio.Samples), ct);
        }
        catch (Exception ex)
        {
            return new SpeechTranscript(string.Empty, durationMs, "en", false, ex.Message);
        }

        var lowConfidence = string.IsNullOrWhiteSpace(text);
        return new SpeechTranscript(text, durationMs, "en", lowConfidence, lowConfidence ? "No speech detected." : null);
    }

    public void Dispose() => _model.Dispose();
}
