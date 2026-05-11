using System.Diagnostics;
using System.Text.Json;
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
            ["text"] = text,
            ["input"] = text,
            ["language"] = "en"
        };

        var speaker = _settings.Settings.TtsSpeaker.Trim();
        if (!string.IsNullOrWhiteSpace(speaker) && !speaker.Equals("default", StringComparison.OrdinalIgnoreCase))
            payload["speaker_wav"] = speaker;

        var endpoint = !string.IsNullOrWhiteSpace(speaker) && !LooksLikeVoicePath(speaker)
            ? "tts_to_audio"
            : "v1/audio/speech";

        using var response = await _http.PostAsJsonAsync($"{baseUrl}/{endpoint}", payload, ct);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var audio = new MemoryStream();
        await source.CopyToAsync(audio, ct);
        var wav = audio.ToArray();

        if (wav.Length == 0)
            throw new InvalidOperationException("TTS returned an empty audio response.");

        await PlayAsync(wav, ct);
    }

    public async Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
    {
        var previous = _settings.Settings.TtsSpeaker;
        try
        {
            _settings.Settings.TtsSpeaker = speaker;
            await SpeakAsync(string.IsNullOrWhiteSpace(text)
                ? "Aether voice preview is ready."
                : text, ct);
        }
        finally
        {
            _settings.Settings.TtsSpeaker = previous;
        }
    }

    public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Voice sample file was not found.", sourcePath);

        var ext = Path.GetExtension(sourcePath);
        if (!new[] { ".wav", ".mp3", ".flac" }.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("XTTS voice samples must be .wav, .mp3, or .flac files.");

        var root = ResolveVoiceDirectory();
        Directory.CreateDirectory(root);
        var safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : displayName);
        var target = Path.Combine(root, $"{safeName}{ext.ToLowerInvariant()}");
        var i = 2;
        while (File.Exists(target))
            target = Path.Combine(root, $"{safeName}-{i++}{ext.ToLowerInvariant()}");

        File.Copy(sourcePath, target);
        return Task.FromResult(target);
    }

    public async Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default)
    {
        var baseUrl = _settings.Settings.TtsServiceUrl.TrimEnd('/');
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "default" };

        foreach (var endpoint in new[] { "studio_speakers", "speakers_list", "speakers", "speaker_ids", "api/speakers", "api/speaker_ids", "api/speakers_list" })
        {
            try
            {
                using var resp = await _http.GetAsync($"{baseUrl}/{endpoint}", ct);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                foreach (var voice in ParseVoiceNames(json))
                    all.Add(voice);
            }
            catch { }
        }

        try
        {
            using var resp = await _http.GetAsync($"{baseUrl}/voices", ct);
            if (resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                foreach (var voice in text
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(ParseMaryVoiceName)
                    .Where(v => !string.IsNullOrWhiteSpace(v)))
                {
                    all.Add(voice);
                }
            }
        }
        catch { }

        foreach (var local in Directory.Exists(ResolveVoiceDirectory())
                     ? Directory.EnumerateFiles(ResolveVoiceDirectory())
                     : [])
        {
            if (LooksLikeVoicePath(local))
                all.Add(local);
        }

        return all.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string ResolveVoiceDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_settings.Settings.TtsVoiceDirectory))
            return Path.GetFullPath(_settings.Settings.TtsVoiceDirectory.Trim());

        var dataRoot = SettingsService.ResolveDataRoot(_settings.Settings);
        return Path.Combine(dataRoot, "xtts-voices");
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

    private static List<string> ParseVoiceNames(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
            return json.EnumerateArray()
                .Select(x => x.GetString() ?? string.Empty)
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (json.ValueKind != JsonValueKind.Object)
            return [];

        foreach (var key in new[] { "speakers", "speaker_ids", "speaker_names", "voices" })
        {
            if (!json.TryGetProperty(key, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Array)
                return value.EnumerateArray()
                    .Select(x => x.GetString() ?? string.Empty)
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (value.ValueKind == JsonValueKind.Object)
                return value.EnumerateObject()
                    .Select(p => p.Name)
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        return json.EnumerateObject()
            .Select(p => p.Name)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool LooksLikeVoicePath(string value) =>
        value.Contains('/') ||
        value.Contains('\\') ||
        value.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".flac", StringComparison.OrdinalIgnoreCase);

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim('-', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? "voice" : cleaned;
    }

    private static string ParseMaryVoiceName(string line)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length <= 3)
            return tokens.FirstOrDefault() ?? string.Empty;

        return string.Join(" ", tokens.Take(tokens.Length - 2));
    }

    public void Dispose() => _http.Dispose();
}
