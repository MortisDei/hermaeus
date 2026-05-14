using Aether.Core.Models;
using Aether.Core.Services;

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

    private readonly ISettingsService _settings;

    public VoiceProvider Id => VoiceProvider.Kokoro;
    public string DisplayName => "Kokoro";
    public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Local;
    public (int Major, int Minor) RequiredPythonVersion => (3, 12);

    public KokoroVoiceProvider(ISettingsService settings)
    {
        _settings = settings;
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

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default)
    {
        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        if (!VoiceProviderProcessRunner.IsExecutableAvailable(python))
            return new VoiceHealth(VoiceHealthStatus.Unhealthy, "Python missing", "Configure a Python 3.12 interpreter or venv.");

        var script = "import importlib\nimportlib.import_module('kokoro')\nimportlib.import_module('soundfile')\n";
        var result = await VoiceProviderProcessRunner.RunPythonScriptAsync(python, script, [], ct);
        return result.Success
            ? new VoiceHealth(VoiceHealthStatus.Healthy, "Kokoro is ready", "Python packages import successfully.")
            : new VoiceHealth(VoiceHealthStatus.Unhealthy, "Kokoro import failed", result.Log);
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
        if (!_settings.Settings.Tts.Enabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("No text supplied for synthesis.");

        var voice = NormalizeVoice(speaker);
        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        var output = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Path.GetTempPath(), $"aether-kokoro-{Guid.NewGuid():N}.wav")
            : outputPath;

        var script = """
import argparse
from pathlib import Path

import numpy as np
import soundfile as sf
from kokoro import KPipeline

parser = argparse.ArgumentParser(description=\"Aether Kokoro voice renderer\")
parser.add_argument(\"--text\", required=True)
parser.add_argument(\"--voice\", default=\"af_heart\")
parser.add_argument(\"--output\", required=True)
parser.add_argument(\"--speed\", type=float, default=1.0)
args = parser.parse_args()

voice = args.voice.strip() or \"af_heart\"
lang = voice[0].lower() if voice and voice[0].isalpha() else \"a\"
pipeline = KPipeline(lang_code=lang)

with sf.SoundFile(args.output, \"w\", samplerate=24000, channels=1, subtype=\"PCM_16\") as wav_file:
    for result in pipeline(args.text, voice=voice, speed=args.speed, split_pattern=r\"\\n+\"):
        audio = result.audio
        if audio is None:
            continue
        if hasattr(audio, \"detach\"):
            audio = audio.detach().cpu().numpy()
        else:
            audio = np.asarray(audio)
        wav_file.write(audio)
""";

        var run = await VoiceProviderProcessRunner.RunPythonScriptAsync(
            python,
            script,
            ["--text", text, "--voice", voice, "--output", output],
            ct);

        if (!run.Success)
            throw new InvalidOperationException($"Kokoro synthesis failed.\n{run.Log}");

        return output;
    }

    private static string NormalizeVoice(string? speaker)
    {
        var voice = speaker?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(voice) ? "af_heart" : voice;
    }
}
