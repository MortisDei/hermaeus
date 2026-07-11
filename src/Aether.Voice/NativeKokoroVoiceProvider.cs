using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Voice;

/// <summary>
/// Native, in-process Kokoro text-to-speech: <see cref="KokoroPhonemizer"/> +
/// <see cref="KokoroTokenizer"/> feed <see cref="KokoroOnnxModel"/> directly,
/// with no Python subprocess and no HTTP round trip. This is the default
/// voice provider; the Python-based Kokoro provider (<c>KokoroVoiceProvider</c>
/// in Aether.Services) remains available as an advanced/fallback path. See
/// docs/review/archived/r1/07-roadmap.md item 5.
/// </summary>
public sealed class NativeKokoroVoiceProvider : ITtsService, IVoiceProvider, IDisposable
{
    private static readonly string[] SupportedVoices =
    [
        "af_heart", "af_alloy", "af_aoede", "af_bella", "af_jessica", "af_kore",
        "af_nicole", "af_nova", "af_river", "af_sarah", "af_sky",
        "am_adam", "am_echo", "am_eric", "am_fenrir", "am_liam", "am_michael", "am_onyx", "am_puck", "am_santa",
        "bf_alice", "bf_emma", "bf_isabella", "bf_lily",
        "bm_daniel", "bm_fable", "bm_george", "bm_lewis"
    ];

    private readonly ISettingsService _settings;
    private readonly KokoroOnnxModel _model;
    private readonly SemaphoreSlim _synthesisGate = new(1, 1);

    public VoiceProvider Id => VoiceProvider.KokoroNative;
    public string DisplayName => "Kokoro (native)";
    public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Local;
    public (int Major, int Minor)? RequiredPythonVersion => null;

    public NativeKokoroVoiceProvider(ISettingsService settings, AppLifecycleJournalService? journal = null)
    {
        _settings = settings;
        _model = new KokoroOnnxModel(() => ResolveAssetsDirectory(_settings.Settings), journal);
    }

    public static string ResolveAssetsDirectory(AppSettings settings)
    {
        var configured = settings.DataManagement.LocalAiAssetsRoot?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
            : Path.GetFullPath(configured);
        return Path.Combine(root, "Models", "voice", "kokoro-native");
    }

    public bool IsInstalled => _model.AssetsPresent(NormalizeVoice(_settings.Settings.Tts.Speaker));

    public VoiceProviderDetection Detect()
    {
        var assetsDir = ResolveAssetsDirectory(_settings.Settings);
        var modelPath = KokoroOnnxModel.ModelPath(assetsDir);
        if (!File.Exists(modelPath))
            return new VoiceProviderDetection(false, "Kokoro ONNX model not installed", "Run the native Kokoro install action to download and verify the model and voice assets.");

        return new VoiceProviderDetection(true, "Kokoro ONNX model detected", $"Model path: {modelPath}", modelPath);
    }

    public VoiceInstallPlan InstallPlan()
    {
        var assetsDir = ResolveAssetsDirectory(_settings.Settings);
        var steps = new List<VoiceInstallStep>
        {
            new(
                "Download Kokoro ONNX model and voices",
                assetsDir,
                "Downloads the quantized Kokoro ONNX model plus the built-in English voice style files, verifying each against a pinned SHA256 hash.",
                VoiceInstallRiskLevel.Medium,
                true,
                ["download", "onnx-community/Kokoro-82M-v1.0-ONNX"])
        };

        return new VoiceInstallPlan(
            "Kokoro (native) runs fully in-process once its ONNX model and voice assets are downloaded once.",
            steps,
            "Downloads a few hundred megabytes from Hugging Face on first install; inference afterward is fully local and offline.");
    }

    public async Task<bool> InstallAssetsAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            await _model.InstallAssetsAsync(SupportedVoices, progress, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default)
    {
        var voice = NormalizeVoice(_settings.Settings.Tts.Speaker);
        if (!_model.AssetsPresent(voice))
            return new VoiceHealth(VoiceHealthStatus.Warning, "Kokoro native assets not installed", "Run the install action in Settings to download the ONNX model and voice files.");

        var loaded = await _model.EnsureLoadedAsync(voice, ct);
        return loaded
            ? new VoiceHealth(VoiceHealthStatus.Healthy, "Kokoro native model loaded", "ONNX session initialized from local assets.")
            : new VoiceHealth(VoiceHealthStatus.Unhealthy, "Kokoro native model failed to load", "Installed assets failed SHA256 verification or failed to load; reinstall.");
    }

    public Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VoiceDefinition>>(SupportedVoices.Select(v => new VoiceDefinition(v, v)).ToList());

    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(SupportedVoices);

    public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default) =>
        throw new NotSupportedException("Kokoro (native) uses built-in voices and does not import voice samples.");

    public async Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default)
    {
        try
        {
            var outputPath = await RenderToFileAsync(request.Text, request.Voice, request.OutputPath, ct);
            if (request.PlayAudio)
                await KokoroAudioPlayback.PlayAsync(outputPath, ct);
            return new VoiceSynthesisResult(true, "Kokoro native synthesis complete.", outputPath);
        }
        catch (Exception ex)
        {
            return new VoiceSynthesisResult(false, ex.Message);
        }
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (!_settings.Settings.Tts.Enabled)
            throw new InvalidOperationException("TTS is disabled in settings.");
        if (string.IsNullOrWhiteSpace(text))
            return;

        var output = await RenderToFileAsync(text, _settings.Settings.Tts.Speaker, null, ct);
        try
        {
            await KokoroAudioPlayback.PlayAsync(output, ct);
        }
        finally
        {
            try { File.Delete(output); }
            catch { }
        }
    }

    public async Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
    {
        var output = await RenderToFileAsync(text, speaker, null, ct);
        try
        {
            await KokoroAudioPlayback.PlayAsync(output, ct);
        }
        finally
        {
            try { File.Delete(output); }
            catch { }
        }
    }

    private async Task<string> RenderToFileAsync(string text, string? speaker, string? outputPath, CancellationToken ct)
    {
        await _synthesisGate.WaitAsync(ct);
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("No text supplied for synthesis.");

            var voice = NormalizeVoice(speaker);
            if (!await _model.EnsureLoadedAsync(voice, ct))
                throw new InvalidOperationException("Kokoro native model is not installed. Run the install action in Settings first.");

            var speed = Math.Clamp(_settings.Settings.Tts.Speed, 0.5, 2.0);
            var output = string.IsNullOrWhiteSpace(outputPath)
                ? Path.Combine(Path.GetTempPath(), $"aether-kokoro-native-{Guid.NewGuid():N}.wav")
                : outputPath;

            // Phonemize/tokenize/inference runs single-threaded ONNX inference
            // (see KokoroOnnxModel's conservative SessionOptions) and can take
            // several seconds for a paragraph; offload it so a caller on the
            // UI thread (SpeakAsync is invoked from ViewModel commands) does
            // not freeze for the duration (docs/review/01-code-audit.md P2-7).
            await Task.Run(() =>
            {
                var phonemes = KokoroPhonemizer.ToPhonemes(text);
                var chunks = KokoroTokenizer.Encode(phonemes);
                if (chunks.Count == 0)
                    throw new InvalidOperationException("Input text produced no phonemes to synthesize.");

                var samples = new List<float>();
                foreach (var chunk in chunks)
                {
                    ct.ThrowIfCancellationRequested();
                    samples.AddRange(_model.Synthesize(chunk, voice, speed));
                }

                WavFile.Write(output, samples.ToArray(), KokoroOnnxModel.SampleRate);
            }, ct);

            return output;
        }
        finally
        {
            _synthesisGate.Release();
        }
    }

    private static string NormalizeVoice(string? speaker)
    {
        var voice = speaker?.Trim() ?? string.Empty;
        return SupportedVoices.Contains(voice, StringComparer.OrdinalIgnoreCase) ? voice : "af_heart";
    }

    public void Dispose() => _model.Dispose();
}
