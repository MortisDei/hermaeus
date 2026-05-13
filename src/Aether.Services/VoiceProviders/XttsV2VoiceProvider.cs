using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services.ProcessManagement;

namespace Aether.Services;

/// <summary>
/// Legacy XTTS v2 voice cloning backend. Requires Python 3.11.
/// </summary>
public sealed class XttsV2VoiceProvider : ITtsService, IVoiceProvider, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly ISettingsService _settings;
    private readonly XttsProcessManager _processManager;

    public VoiceProvider Id => VoiceProvider.XttsV2;
    public string DisplayName => "XTTS v2";
    public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.VoiceCloning | VoiceCapability.Local | VoiceCapability.Legacy;

    public bool IsInstalled => File.Exists(_settings.Settings.Tts.ScriptPath);

    public XttsV2VoiceProvider(ISettingsService settings, XttsProcessManager processManager)
    {
        _settings = settings;
        _processManager = processManager;
    }

    public VoiceProviderDetection Detect()
    {
        var script = _settings.Settings.Tts.ScriptPath.Trim();
        if (string.IsNullOrWhiteSpace(script) || !File.Exists(script))
            return new VoiceProviderDetection(false, "XTTS script missing", "Set the XTTS API script path in Settings.");

        var python = _settings.Settings.Tts.PythonPath.Trim();
        if (!string.IsNullOrWhiteSpace(python) && !File.Exists(python))
            return new VoiceProviderDetection(false, "XTTS Python missing", "Set a valid XTTS venv python path.");

        return new VoiceProviderDetection(true, "XTTS assets detected", script, script);
    }

    public VoiceInstallPlan InstallPlan()
    {
        var python = string.IsNullOrWhiteSpace(_settings.Settings.Tts.PythonPath)
            ? VoiceProviderProcessRunner.ResolvePythonPath(_settings)
            : _settings.Settings.Tts.PythonPath.Trim();
        var steps = new List<VoiceInstallStep>
        {
            new(
                "Install XTTS packages",
                python,
                "Installs TTS, fastapi, uvicorn, and soundfile for the XTTS server.",
                VoiceInstallRiskLevel.High,
                true,
                [python, "-m", "pip", "install", "TTS", "fastapi", "uvicorn", "soundfile"]) 
        };

        return new VoiceInstallPlan(
            "XTTS requires Python 3.11, packages, and an API script.",
            steps,
            "Packages download from PyPI and run a local API server.");
    }

    public Task StartAsync(CancellationToken ct = default) => _processManager.StartAsync(_settings.Settings, ct);

    public Task StopAsync(CancellationToken ct = default)
    {
        _processManager.Stop();
        return Task.CompletedTask;
    }

    public async Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default)
    {
        if (!IsInstalled)
            return new VoiceHealth(VoiceHealthStatus.Unhealthy, "XTTS script missing", "Set the XTTS API script path.");

        var baseUrl = _settings.Settings.Tts.ServiceUrl.TrimEnd('/');
        try
        {
            using var resp = await _http.GetAsync($"{baseUrl}/health", ct);
            if (resp.IsSuccessStatusCode)
                return new VoiceHealth(VoiceHealthStatus.Healthy, "XTTS server healthy", $"{baseUrl}/health responded OK.");
        }
        catch (Exception ex)
        {
            return new VoiceHealth(VoiceHealthStatus.Warning, "XTTS server not reachable", ex.Message);
        }

        return new VoiceHealth(VoiceHealthStatus.Warning, "XTTS server not reachable", "Start the XTTS server and re-check.");
    }

    public async Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default)
    {
        var voices = await GetVoicesAsync(ct);
        return voices.Select(v => new VoiceDefinition(v, v, RequiresSample: true)).ToList();
    }

    public async Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default)
    {
        try
        {
            var outputPath = await RenderToFileAsync(request.Text, request.Voice ?? string.Empty, request.OutputPath, ct);
            if (request.PlayAudio)
                await PlayAsync(await File.ReadAllBytesAsync(outputPath, ct), ct);
            return new VoiceSynthesisResult(true, "XTTS synthesis complete.", outputPath);
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

        var baseUrl = _settings.Settings.Tts.ServiceUrl.TrimEnd('/');

        var speaker = _settings.Settings.Tts.Speaker.Trim();
        if (speaker.Equals("default", StringComparison.OrdinalIgnoreCase))
            speaker = string.Empty;

        var outputPath = await RenderToFileAsync(text, speaker, null, ct);
        var wav = await File.ReadAllBytesAsync(outputPath, ct);
        if (wav.Length == 0)
            throw new InvalidOperationException("XTTS v2 returned an empty audio response.");

        await PlayAsync(wav, ct);
        try { File.Delete(outputPath); }
        catch { }
    }

    public async Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
    {
        if (!_settings.Settings.Tts.Enabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            return;

        var baseUrl = _settings.Settings.Tts.ServiceUrl.TrimEnd('/');

        var outputPath = await RenderToFileAsync(text, speaker, null, ct);
        var wav = await File.ReadAllBytesAsync(outputPath, ct);
        if (wav.Length == 0)
            throw new InvalidOperationException("XTTS v2 returned an empty audio response.");

        await PlayAsync(wav, ct);
        try { File.Delete(outputPath); }
        catch { }
    }

    public async Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default)
    {
        if (!_settings.Settings.Tts.Enabled)
            return [];

        var baseUrl = _settings.Settings.Tts.ServiceUrl.TrimEnd('/');
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

        var targetDir = _settings.Settings.Tts.VoiceDirectory.Trim();
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

    private async Task<string> RenderToFileAsync(string text, string speaker, string? outputPath, CancellationToken ct)
    {
        var baseUrl = _settings.Settings.Tts.ServiceUrl.TrimEnd('/');
        using var response = await PostSpeechAsync(baseUrl, text, speaker, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var detail = TryReadJsonError(body);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"XTTS v2 returned {(int)response.StatusCode} {response.ReasonPhrase}."
                : $"XTTS v2 returned {(int)response.StatusCode}: {detail}");
        }

        var path = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Path.GetTempPath(), $"aether-xtts-{Guid.NewGuid():N}.wav")
            : outputPath;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(path);
        await source.CopyToAsync(file, ct);
        return path;
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
