using System.Net.Http.Headers;
using System.Text.Json;
using Hermaeus.Core.Services;

namespace Hermaeus.Voice;

/// <summary>
/// r24 doc 05 5.1: remote speech recognition via an OpenAI-compatible
/// <c>/v1/audio/transcriptions</c> endpoint. Off by default, never the default
/// provider - see docs/review/05-voice-input.md. Reuses
/// <see cref="Core.Models.LlmSettings.OpenAiBaseUrl"/>/<c>OpenAiApiKey</c> exactly
/// as <c>OpenAiVoiceProvider</c> (TTS) already does, rather than asking for the
/// same credential a second time; only the transcription model name is STT-specific.
/// </summary>
public sealed class OpenAiSpeechRecognitionProvider : ISpeechRecognitionService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

    public string ProviderName => "Speech recognition (remote, OpenAI-compatible)";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_settings.Settings.Llm.OpenAiBaseUrl);

    public OpenAiSpeechRecognitionProvider(ISettingsService settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    public async Task<Core.Models.SpeechTranscript> TranscribeAsync(Stream wavPcm16Mono16k, Core.Models.SpeechTranscribeOptions options, CancellationToken ct = default)
    {
        var baseUrl = _settings.Settings.Llm.OpenAiBaseUrl.TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(_settings.Settings.Stt.RemoteModel) ? "whisper-1" : _settings.Settings.Stt.RemoteModel;
        var apiKey = (await _secrets.ResolveAsync(_settings.Settings.Llm.OpenAiApiKey, ct)).Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return new Core.Models.SpeechTranscript(string.Empty, 0, "en", false, "No OpenAI API key configured.");

        using var content = new MultipartFormDataContent();
        using var audioContent = new StreamContent(wavPcm16Mono16k);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");
        content.Add(new StringContent(model), "model");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/audio/transcriptions") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new Core.Models.SpeechTranscript(string.Empty, 0, "en", false, $"Remote transcription failed: {(int)response.StatusCode} {response.StatusCode}");

            var text = ParseTranscriptText(body);
            return new Core.Models.SpeechTranscript(text, 0, "en", string.IsNullOrWhiteSpace(text));
        }
        catch (Exception ex)
        {
            return new Core.Models.SpeechTranscript(string.Empty, 0, "en", false, ex.Message);
        }
    }

    /// <summary>Pure JSON parse, independently testable against canned response bodies
    /// including the empty and error-shaped cases (Testing list in doc 05).</summary>
    internal static string ParseTranscriptText(string jsonBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
