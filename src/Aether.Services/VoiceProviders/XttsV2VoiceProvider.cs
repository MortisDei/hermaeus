using System.Diagnostics;
using System.Text.Json;
using System.Net.Http.Json;
using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// Legacy XTTS v2 voice cloning backend. Requires Python 3.11.
/// </summary>
public sealed class XttsV2VoiceProvider : ITtsService, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly ISettingsService _settings;

    public bool IsInstalled => File.Exists(_settings.Settings.TtsScriptPath);

    public XttsV2VoiceProvider(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (!_settings.Settings.TtsEnabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            return;

        var baseUrl = _settings.Settings.TtsServiceUrl.TrimEnd('/');

        var speaker = _settings.Settings.TtsSpeaker.Trim();
        if (speaker.Equals("default", StringComparison.OrdinalIgnoreCase))
            speaker = string.Empty;

        using var response = await PostSpeechAsync(baseUrl, text, speaker, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var detail = TryReadJsonError(body);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"XTTS v2 returned {(int)response.StatusCode} {response.ReasonPhrase}."
                : $"XTTS v2 returned {(int)response.StatusCode}: {detail}");
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var audio = new MemoryStream();
        await source.CopyToAsync(audio, ct);
        var wav = audio.ToArray();

        if (wav.Length == 0)
            throw new InvalidOperationException("XTTS v2 returned an empty audio response.");

        await PlayAsync(wav, ct);
    }

    public async Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
    {
        if (!_settings.Settings.TtsEnabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            return;

        var baseUrl = _settings.Settings.TtsServiceUrl.TrimEnd('/');

        using var response = await PostSpeechAsync(baseUrl, text, speaker, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var detail = TryReadJsonError(body);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"XTTS v2 returned {(int)response.StatusCode} {response.ReasonPhrase}."
                : $"XTTS v2 returned {(int)response.StatusCode}: {detail}");
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var audio = new MemoryStream();
        await source.CopyToAsync(audio, ct);
        var wav = audio.ToArray();

        if (wav.Length == 0)
            throw new InvalidOperationException("XTTS v2 returned an empty audio response.");

        await PlayAsync(wav, ct);
    }

    public async Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default)
    {
        if (!_settings.Settings.TtsEnabled)
            return [];

        var baseUrl = _settings.Settings.TtsServiceUrl.TrimEnd('/');
        try
        {
            var response = await _http.GetAsync($"{baseUrl}/voices", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (json.TryGetProperty("voices", out var voicesElement))
                {
                    var voices = new List<string>();
                    foreach (var voice in voicesElement.EnumerateArray())
                    {
                        voices.Add(Path.GetFileNameWithoutExtension(voice.GetString() ?? string.Empty));
                    }
                    return voices;
                }
            }
        }
        catch { }

        return [];
    }

    public async Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Voice sample not found at {sourcePath}");

        var targetDir = _settings.Settings.TtsVoiceDirectory.Trim();
        if (string.IsNullOrWhiteSpace(targetDir))
            throw new InvalidOperationException("TTS voice directory is not set.");

        Directory.CreateDirectory(targetDir);
        var ext = Path.GetExtension(sourcePath);
        var targetPath = Path.Combine(targetDir, $"{displayName}{ext}");

        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }

    private async Task<HttpResponseMessage> PostSpeechAsync(string baseUrl, string text, string speaker, CancellationToken ct)
    {
        var payload = new { input = text, speaker_wav = speaker };
        return await _http.PostAsJsonAsync($"{baseUrl}/v1/audio/speech", payload, cancellationToken: ct);
    }

    private static void AppendLine(string? line, List<string> log)
    {
        if (!string.IsNullOrWhiteSpace(line))
            log.Add(line);
    }

    private static string? TryReadJsonError(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task PlayAsync(byte[] wavData, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"aether-tts-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(tempFile, wavData);

            var psi = new ProcessStartInfo
            {
                FileName = "ffplay",
                ArgumentList = { "-nodisp", "-autoexit", tempFile },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!process.Start())
                throw new InvalidOperationException("ffplay not available; cannot play audio.");

            await process.WaitForExitAsync(ct);
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch { }
        }
    }

    public void Dispose()
    {
        _http?.Dispose();
    }
}
