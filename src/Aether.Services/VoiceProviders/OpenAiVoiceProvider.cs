using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class OpenAiVoiceProvider : ITtsService, IVoiceProvider, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly ISettingsService _settings;

    public VoiceProvider Id => VoiceProvider.OpenAi;
    public string DisplayName => "OpenAI";
    public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Remote | VoiceCapability.RequiresApiKey;
    public (int Major, int Minor) RequiredPythonVersion => (0, 0); // Remote API, no Python required

    public OpenAiVoiceProvider(ISettingsService settings)
    {
        _settings = settings;
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
        try
        {
            var outputPath = await RenderToFileAsync(request.Text, request.Voice, request.OutputPath, ct);
            if (request.PlayAudio)
                await VoiceProviderProcessRunner.PlayWavFileAsync(outputPath, ct);
            return new VoiceSynthesisResult(true, "OpenAI synthesis complete.", outputPath);
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
        var key = _settings.Settings.Llm.OpenAiApiKey.Trim();
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
            ? Path.Combine(Path.GetTempPath(), $"aether-openai-{Guid.NewGuid():N}.wav")
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

    public void Dispose() => _http.Dispose();
}
