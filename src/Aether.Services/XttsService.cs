using System.Diagnostics;
using System.Net.Http.Json;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class XttsService : ITtsService, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly ISettingsService _settings;

    public XttsService(ISettingsService settings)
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

        var payload = new Dictionary<string, object?>
        {
            ["input"] = text,
            ["language"] = "en"
        };

        if (!string.IsNullOrWhiteSpace(_settings.Settings.TtsSpeaker))
            payload["speaker_wav"] = _settings.Settings.TtsSpeaker;

        using var response = await _http.PostAsJsonAsync($"{baseUrl}/v1/audio/speech", payload, ct);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var audio = new MemoryStream();
        await source.CopyToAsync(audio, ct);
        var wav = audio.ToArray();

        if (wav.Length == 0)
            throw new InvalidOperationException("TTS returned an empty audio response.");

        await PlayAsync(wav, ct);
    }

    private static async Task PlayAsync(byte[] wav, CancellationToken ct)
    {
        if (await TryPlayViaStdInAsync("paplay", [], wav, ct)) return;
        if (await TryPlayViaStdInAsync("pw-play", ["-"], wav, ct)) return;
        if (await TryPlayViaStdInAsync("aplay", ["-q", "-"], wav, ct)) return;
        if (await TryPlayViaStdInAsync("ffplay", ["-autoexit", "-nodisp", "-loglevel", "quiet", "-i", "pipe:0"], wav, ct)) return;

        throw new InvalidOperationException("Generated speech in memory, but could not find paplay, pw-play, aplay, or ffplay to play it.");
    }

    private static async Task<bool> TryPlayViaStdInAsync(
        string command,
        string[] args,
        byte[] wav,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        try
        {
            if (!process.Start())
                return false;
        }
        catch
        {
            return false;
        }

        try
        {
            await process.StandardInput.BaseStream.WriteAsync(wav, ct);
            await process.StandardInput.BaseStream.FlushAsync(ct);
            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
