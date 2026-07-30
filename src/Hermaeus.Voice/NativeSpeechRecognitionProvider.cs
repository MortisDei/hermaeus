using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Voice;

/// <summary>
/// Native, in-process speech recognition: <see cref="WhisperOnnxModel"/> runs
/// directly, no subprocess and no HTTP round trip, mirroring
/// <see cref="NativeKokoroVoiceProvider"/>'s posture on the TTS side. This is
/// the default (and only local) speech recognition provider.
///
/// r25 doc 03 replaced r24's wav2vec2 CTC model here. The in-process
/// architecture was the right call and is unchanged; the model was not. Its
/// vocabulary held 26 uppercase letters and an apostrophe, with no lowercase and
/// no punctuation anywhere in it, so every transcript arrived as
/// HELLO CAN YOU CHECK THE BUILD and no post-processing could put back what was
/// never produced. Whisper emits punctuation, casing and a detected language
/// because it was trained on transcripts that have them.
/// </summary>
public sealed class NativeSpeechRecognitionProvider : ISpeechRecognitionService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly WhisperOnnxModel _model;

    public string ProviderName => "Speech recognition (native)";
    public bool IsAvailable => _model.AssetsPresent();

    public NativeSpeechRecognitionProvider(ISettingsService settings)
    {
        _settings = settings;
        _model = new WhisperOnnxModel(() => ResolveAssetsDirectory(_settings.Settings));
    }

    public static string ResolveAssetsDirectory(AppSettings settings) =>
        Path.Combine(ResolveVoiceModelsRoot(settings), "whisper-stt");

    /// <summary>
    /// r25 doc 03 3.6: where r24's wav2vec2 assets live, so Doctor can report a
    /// superseded install and offer to remove it. Never deleted automatically:
    /// the user chose to download those hundreds of megabytes and they are theirs.
    /// </summary>
    public static string ResolveSupersededAssetsDirectory(AppSettings settings) =>
        Path.Combine(ResolveVoiceModelsRoot(settings), "wav2vec2-stt");

    private static string ResolveVoiceModelsRoot(AppSettings settings)
    {
        var configured = settings.DataManagement.LocalAiAssetsRoot?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus")
            : Path.GetFullPath(configured);
        return Path.Combine(root, "Models", "voice");
    }

    public bool IsInstalled => _model.AssetsPresent();

    public VoiceProviderDetection Detect()
    {
        var assetsDir = ResolveAssetsDirectory(_settings.Settings);
        var missing = WhisperOnnxModel.AllAssets
            .Count(a => !File.Exists(WhisperOnnxModel.PathFor(assetsDir, a)));

        if (missing > 0)
            return new VoiceProviderDetection(
                false,
                "Speech recognition model not installed",
                $"Missing {missing} of {WhisperOnnxModel.AllAssets.Length} model files. " +
                "Run the install action in Services > Voice to download and verify them.");

        return new VoiceProviderDetection(
            true, "Speech recognition model detected", $"Model path: {assetsDir}", assetsDir);
    }

    public VoiceInstallPlan InstallPlan()
    {
        var assetsDir = ResolveAssetsDirectory(_settings.Settings);
        var megabytes = WhisperOnnxModel.TotalDownloadBytes / 1024 / 1024;

        var steps = new List<VoiceInstallStep>
        {
            new(
                "Download the Whisper speech recognition model",
                assetsDir,
                $"Downloads a pinned Whisper base model ({megabytes} MB across " +
                $"{WhisperOnnxModel.AllAssets.Length} files: an encoder, a decoder, and its tokenizer " +
                "and generation config) from the onnx-community/whisper-base repository, " +
                "verifying every file against a pinned SHA256 hash.",
                VoiceInstallRiskLevel.Medium,
                true,
                ["download", "onnx-community/whisper-base"])
        };

        return new VoiceInstallPlan(
            "Speech recognition runs fully in-process once its model is downloaded once. " +
            "Transcripts include punctuation and casing, and the language is detected rather than assumed.",
            steps,
            $"Downloads roughly {megabytes} MB from Hugging Face on first install; " +
            "inference afterward is fully local and offline.");
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
            return new VoiceHealth(VoiceHealthStatus.Warning, "Speech recognition model not installed",
                "Run the install action in Services > Voice to download the Whisper model.");

        var loaded = await _model.EnsureLoadedAsync(ct);
        return loaded
            ? new VoiceHealth(VoiceHealthStatus.Healthy, "Speech recognition model loaded",
                "Whisper encoder and decoder sessions initialized from local assets.")
            : new VoiceHealth(VoiceHealthStatus.Unhealthy, "Speech recognition model failed to load",
                "Installed assets failed SHA256 verification or failed to load; reinstall.");
    }

    public async Task<SpeechTranscript> TranscribeAsync(
        Stream wavPcm16Mono16k, SpeechTranscribeOptions options, CancellationToken ct = default)
    {
        if (!await _model.EnsureLoadedAsync(ct))
            return new SpeechTranscript(string.Empty, 0, string.Empty, false,
                "Speech recognition model is not installed. Run the install action in Services > Voice first.");

        WavFile.WavAudio audio;
        try
        {
            audio = WavFile.Read(wavPcm16Mono16k);
        }
        catch (Exception ex)
        {
            return new SpeechTranscript(string.Empty, 0, string.Empty, false, ex.Message);
        }

        if (audio.Channels != 1 || audio.SampleRate != WhisperOnnxModel.SampleRate)
            return new SpeechTranscript(string.Empty, 0, string.Empty, false,
                $"Expected 16kHz mono PCM16 audio, got {audio.SampleRate}Hz/{audio.Channels}ch.");

        var durationMs = audio.SampleRate == 0 ? 0 : (int)(audio.Samples.Length * 1000L / audio.SampleRate);

        WhisperTranscription result;
        try
        {
            var language = ResolveForcedLanguage(options.LanguageHint);
            result = await Task.Run(
                () => _model.Transcribe(audio.Samples, language, options.Progress, ct), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SpeechTranscript(string.Empty, durationMs, string.Empty, false, ex.Message);
        }

        var empty = string.IsNullOrWhiteSpace(result.Text);

        // r25 doc 03 3.3: low confidence now means something. Before r25 it only
        // meant "the text came back empty", which is why r24's hands-free mode
        // could not actually refuse to auto-send a hallucinated turn.
        var lowConfidence = empty || result.LooksLikeLoop;
        var error = empty
            ? "No speech detected."
            : result.LooksLikeLoop
                ? "The transcript looks like a repetition loop rather than speech."
                : null;

        return new SpeechTranscript(result.Text, durationMs, result.Language, lowConfidence, error);
    }

    /// <summary>Empty or "auto" means let the model detect it, which is the default.</summary>
    private string? ResolveForcedLanguage(string? hint)
    {
        var configured = string.IsNullOrWhiteSpace(hint) ? _settings.Settings.Stt.Language : hint;
        configured = configured?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            || string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : configured;
    }

    public void Dispose() => _model.Dispose();
}
