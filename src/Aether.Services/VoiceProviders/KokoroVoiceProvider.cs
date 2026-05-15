using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services.ProcessManagement;
using System.Net.Http.Json;

namespace Aether.Services;

/// <summary>
/// Kokoro voice provider - fast local readback.
/// Recommended default voice engine.
/// </summary>
public sealed class KokoroVoiceProvider : ITtsService, IVoiceProvider
{
    private static readonly string[] DefaultVoices =
    [
        "af_heart",
        "af_bella",
        "af_sky",
        "af_nicole",
        "am_michael",
        "am_danny",
        "bf_isabella",
        "bm_george"
    ];

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly ISettingsService _settings;
    private readonly KokoroProcessManager _processManager;
    private readonly SemaphoreSlim _synthesisGate = new(1, 1);

    public VoiceProvider Id => VoiceProvider.Kokoro;
    public string DisplayName => "Kokoro";
    public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Local;
    public (int Major, int Minor) RequiredPythonVersion => (3, 12);

    public KokoroVoiceProvider(ISettingsService settings, KokoroProcessManager? processManager = null)
    {
        _settings = settings;
        _processManager = processManager ?? new KokoroProcessManager();
    }

    public bool IsInstalled => VoiceProviderProcessRunner.IsExecutableAvailable(VoiceProviderProcessRunner.ResolvePythonPath(_settings));

    public VoiceProviderDetection Detect()
    {
        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        if (!VoiceProviderProcessRunner.IsExecutableAvailable(python))
        {
            return new VoiceProviderDetection(false, "Python not found", "Install Python 3.12 or point Aether at a Python 3.12 venv.");
        }

        return new VoiceProviderDetection(true, "Python detected", $"Python path: {python}");
    }

    public VoiceInstallPlan InstallPlan()
    {
        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        var steps = new List<VoiceInstallStep>
        {
            new(
                "Install Kokoro packages",
                python,
                "Installs kokoro, soundfile, and numpy for local synthesis.",
                VoiceInstallRiskLevel.Medium,
                true,
                [python, "-m", "pip", "install", "kokoro", "soundfile", "numpy"]) 
        };

        return new VoiceInstallPlan(
            "Kokoro requires Python 3.12 and a small set of packages.",
            steps,
            "Packages download from PyPI and run local inference.");
    }

    public Task StartAsync(CancellationToken ct = default) => _processManager.StartAsync(_settings.Settings, ct);

    public Task StopAsync(CancellationToken ct = default)
    {
        _processManager.Stop();
        return Task.CompletedTask;
    }

    public async Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default)
    {
        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        if (!VoiceProviderProcessRunner.IsExecutableAvailable(python))
            return new VoiceHealth(VoiceHealthStatus.Unhealthy, "Python missing", "Configure a Python 3.12 interpreter or venv.");

        var baseUrl = _settings.Settings.Tts.ServiceUrl.TrimEnd('/');
        try
        {
            using var response = await _http.GetAsync($"{baseUrl}/health", ct);
            if (response.IsSuccessStatusCode)
                return new VoiceHealth(VoiceHealthStatus.Healthy, "Kokoro service is running", $"{baseUrl}/health responded OK.");
        }
        catch (Exception ex)
        {
            return new VoiceHealth(VoiceHealthStatus.Warning, "Kokoro service is not running", ex.Message);
        }

        return new VoiceHealth(VoiceHealthStatus.Warning, "Kokoro service is not running", "Start the Kokoro service or synthesize once to launch it.");
    }

    public async Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default)
    {
        var voices = await GetVoicesAsync(ct);
        return voices.Select(v => new VoiceDefinition(v, v)).ToList();
    }

    public async Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default)
    {
        try
        {
            var outputPath = await RenderToFileAsync(request.Text, request.Voice, request.OutputPath, ct);
            if (request.PlayAudio)
                await VoiceProviderProcessRunner.PlayWavFileAsync(outputPath, ct);
            return new VoiceSynthesisResult(true, "Kokoro synthesis complete.", outputPath);
        }
        catch (Exception ex)
        {
            return new VoiceSynthesisResult(false, ex.Message);
        }
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        await RenderAndPlayAsync(text, _settings.Settings.Tts.Speaker, ct);
    }

    public async Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
    {
        await RenderAndPlayAsync(text, speaker, ct);
    }

    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> voices = DefaultVoices;
        return Task.FromResult(voices);
    }

    public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default)
    {
        throw new NotSupportedException("Kokoro uses built-in voices and does not import voice samples.");
    }

    private async Task RenderAndPlayAsync(string text, string? speaker, CancellationToken ct)
    {
        if (!_settings.Settings.Tts.Enabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            return;

        var output = string.Empty;
        try
        {
            output = await RenderToFileAsync(text, speaker, null, ct);
            await VoiceProviderProcessRunner.PlayWavFileAsync(output, ct);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(output))
            {
                try { File.Delete(output); }
                catch { }
            }
        }
    }

    private async Task<string> RenderToFileAsync(string text, string? speaker, string? outputPath, CancellationToken ct)
    {
        await _synthesisGate.WaitAsync(ct);
        try
        {
        if (!_settings.Settings.Tts.Enabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("No text supplied for synthesis.");

        var voice = NormalizeVoice(speaker);
        var output = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Path.GetTempPath(), $"aether-kokoro-{Guid.NewGuid():N}.wav")
            : outputPath;

        await EnsureServiceRunningAsync(ct);
        var baseUrl = _settings.Settings.Tts.ServiceUrl.TrimEnd('/');
        var payload = new
        {
            input = text,
            speaker_wav = voice,
            speed = Math.Clamp(_settings.Settings.Tts.Speed, 0.5, 2.0)
        };

        using var response = await _http.PostAsJsonAsync($"{baseUrl}/v1/audio/speech", payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
                ? $"Kokoro synthesis failed with {(int)response.StatusCode}."
                : $"Kokoro synthesis failed: {body}");
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(output);
        await source.CopyToAsync(file, ct);

        return output;
        }
        finally
        {
            _synthesisGate.Release();
        }
    }

    private async Task EnsureServiceRunningAsync(CancellationToken ct)
    {
        if (_processManager.IsRunning)
            return;

        await _processManager.StartAsync(_settings.Settings, ct);
    }

    private static string NormalizeVoice(string? speaker)
    {
        var voice = speaker?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(voice) ? "af_heart" : voice;
    }
}
