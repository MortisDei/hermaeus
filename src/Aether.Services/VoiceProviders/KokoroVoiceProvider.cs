using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// Kokoro voice provider - fast local readback.
/// Recommended default voice engine.
/// </summary>
public sealed class KokoroVoiceProvider : ITtsService
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

    public KokoroVoiceProvider(ISettingsService settings)
    {
        _settings = settings;
    }

    public bool IsInstalled => !string.IsNullOrWhiteSpace(VoiceProviderProcessRunner.ResolvePythonPath(_settings));

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        await RenderAndPlayAsync(text, _settings.Settings.TtsSpeaker, ct);
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
        if (!_settings.Settings.TtsEnabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            return;

        var voice = NormalizeVoice(speaker);
        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        var output = Path.Combine(Path.GetTempPath(), $"aether-kokoro-{Guid.NewGuid():N}.wav");

        try
        {
            var script = """
import argparse
from pathlib import Path

import numpy as np
import soundfile as sf
from kokoro import KPipeline

parser = argparse.ArgumentParser(description="Aether Kokoro voice renderer")
parser.add_argument("--text", required=True)
parser.add_argument("--voice", default="af_heart")
parser.add_argument("--output", required=True)
parser.add_argument("--speed", type=float, default=1.0)
args = parser.parse_args()

voice = args.voice.strip() or "af_heart"
lang = voice[0].lower() if voice and voice[0].isalpha() else "a"
pipeline = KPipeline(lang_code=lang)

with sf.SoundFile(args.output, "w", samplerate=24000, channels=1, subtype="PCM_16") as wav_file:
    for result in pipeline(args.text, voice=voice, speed=args.speed, split_pattern=r"\\n+"):
        audio = result.audio
        if audio is None:
            continue
        if hasattr(audio, "detach"):
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

            await VoiceProviderProcessRunner.PlayWavFileAsync(output, ct);
        }
        finally
        {
            try { File.Delete(output); }
            catch { }
        }
    }

    private static string NormalizeVoice(string? speaker)
    {
        var voice = speaker?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(voice) ? "af_heart" : voice;
    }
}
