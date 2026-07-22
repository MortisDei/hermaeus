using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class OpenAiVoiceProvider : ITtsService, IVoiceProvider, IDisposable
{
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public VoiceProvider Id => VoiceProvider.OpenAi;
    public string DisplayName => "OpenAI";
    public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Remote | VoiceCapability.RequiresApiKey;
    public (int Major, int Minor)? RequiredPythonVersion => null; // Remote API, no Python required

    public OpenAiVoiceProvider(ISettingsService settings, ISecretStore secrets, HttpClient? http = null)
    {
        _settings = settings;
        _secrets = secrets;
        _http = http ?? DefaultHttp;
        _ownsHttp = http is not null;
    }

    public bool IsInstalled => !string.IsNullOrWhiteSpace(_settings.Settings.Llm.OpenAiApiKey);

    public VoiceProviderDetection Detect()
    {
        if (string.IsNullOrWhiteSpace(_settings.Settings.Llm.OpenAiApiKey))
            return new VoiceProviderDetection(false, "API key missing", "Set your OpenAI API key in Settings.");

        return new VoiceProviderDetection(true, "API key configured", "Remote voice generation available.");
    }

    public VoiceInstallPlan InstallPlan()
    {
        return new VoiceInstallPlan(
            "OpenAI voice requires a valid API key and network access.",
            [],
            "Requests are sent to the configured OpenAI endpoint.");
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Settings.Llm.OpenAiApiKey))
            return Task.FromResult(new VoiceHealth(VoiceHealthStatus.Unhealthy, "API key missing", "Add an OpenAI API key in Settings."));

        return Task.FromResult(new VoiceHealth(VoiceHealthStatus.Healthy, "OpenAI voice configured", "API key present."));
    }

    public Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<VoiceDefinition> voices =
        [
            new VoiceDefinition("alloy", "Alloy"),
            new VoiceDefinition("echo", "Echo"),
            new VoiceDefinition("fable", "Fable"),
            new VoiceDefinition("nova", "Nova"),
            new VoiceDefinition("onyx", "Onyx"),
            new VoiceDefinition("shimmer", "Shimmer")
        ];
        return Task.FromResult(voices);
    }

    public async Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default)
    {
        string outputPath;
        try
        {
            outputPath = await RenderToFileAsync(request.Text, request.Voice, request.OutputPath, ct);
        }
        catch (Exception ex)
        {
            return new VoiceSynthesisResult(false, ex.Message);
        }

        var message = "OpenAI synthesis complete.";
        if (request.PlayAudio)
        {
            try
            {
                await VoiceProviderProcessRunner.PlayWavFileAsync(outputPath, ct);
            }
            catch (Exception ex)
            {
                // r18 01-finish-the-open-work.md 1.5: synthesis already succeeded by this point
                // (the file above was written); a playback-only failure (missing/broken OS audio
                // player, malformed audio) used to be reported as a synthesis failure that also
                // discarded OutputPath, so a caller had no way to know the file existed - and it
                // never reached the cleanup below either.
                message = $"OpenAI synthesis complete; playback failed: {ex.Message}";
            }
        }

        // r11 4.3: when the caller did not request a persisted OutputPath, this synthesized to
        // a %TEMP% file that must not outlive playback.
        if (request.OutputPath is null && request.PlayAudio && !string.IsNullOrWhiteSpace(outputPath))
        {
            try { File.Delete(outputPath); }
            catch { }
        }

        return new VoiceSynthesisResult(true, message, outputPath);
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (!_settings.Settings.Tts.Enabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            return;

        var outputPath = await RenderToFileAsync(text, _settings.Settings.Tts.Speaker, null, ct);
        await VoiceProviderProcessRunner.PlayWavFileAsync(outputPath, ct);
        try { File.Delete(outputPath); }
        catch { }
    }

    public async Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
    {
        if (!_settings.Settings.Tts.Enabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            return;

        var outputPath = await RenderToFileAsync(text, speaker, null, ct);
        await VoiceProviderProcessRunner.PlayWavFileAsync(outputPath, ct);
        try { File.Delete(outputPath); }
        catch { }
    }

    public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default)
    {
        throw new NotSupportedException("OpenAI voice does not import local voice samples.");
    }

    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> voices = ["alloy", "echo", "fable", "nova", "onyx", "shimmer"];
        return Task.FromResult(voices);
    }

    private async Task<string> RenderToFileAsync(string text, string? voice, string? outputPath, CancellationToken ct)
    {
        var key = (await _secrets.ResolveAsync(_settings.Settings.Llm.OpenAiApiKey, ct)).Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("OpenAI API key is missing.");

        var baseUrl = _settings.Settings.Llm.OpenAiBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/v1/audio/speech";
        var payload = new
        {
            model = "tts-1",
            input = text,
            voice = string.IsNullOrWhiteSpace(voice) ? "alloy" : voice,
            response_format = "wav"
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            var detail = TryReadJsonError(body);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"OpenAI returned {(int)resp.StatusCode} {resp.ReasonPhrase}."
                : $"OpenAI returned {(int)resp.StatusCode}: {detail}");
        }

        var path = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Path.GetTempPath(), $"hermaeus-openai-{Guid.NewGuid():N}.wav")
            : outputPath;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(path);
        await stream.CopyToAsync(file, ct);
        return path;
    }

    private static string? TryReadJsonError(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
                return message.GetString();
        }
        catch { }

        return null;
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
