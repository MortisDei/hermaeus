using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// F5-TTS voice provider - modern voice cloning backend.
/// Advanced provider with higher quality but heavier install.
/// Pretrained models use CC-BY-NC license (noncommercial).
/// </summary>
public sealed class F5TtsVoiceProvider : ITtsService
{
    private readonly ISettingsService _settings;

    public F5TtsVoiceProvider(ISettingsService settings)
    {
        _settings = settings;
    }

    public bool IsInstalled => !string.IsNullOrWhiteSpace(VoiceProviderProcessRunner.ResolvePythonPath(_settings));

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        await RenderAndPlayAsync(text, null, ct);
    }

    public async Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
    {
        await RenderAndPlayAsync(text, speaker, ct);
    }

    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default)
    {
        var voiceDir = _settings.Settings.TtsVoiceDirectory.Trim();
        if (!Directory.Exists(voiceDir))
            return Task.FromResult<IReadOnlyList<string>>([]);

        IReadOnlyList<string> voices = Directory.EnumerateFiles(voiceDir, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        return Task.FromResult(voices);
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
        var safeName = string.Join("_", displayName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        var targetPath = Path.Combine(targetDir, $"{(string.IsNullOrWhiteSpace(safeName) ? "voice" : safeName)}{ext}");

        await using var source = File.OpenRead(sourcePath);
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, ct);
        return targetPath;
    }

    private async Task RenderAndPlayAsync(string text, string? speaker, CancellationToken ct)
    {
        if (!_settings.Settings.TtsEnabled)
            throw new InvalidOperationException("TTS is disabled in settings.");

        if (string.IsNullOrWhiteSpace(text))
            return;

        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        var referenceFile = ResolveReferenceAudioFile(speaker);
        if (referenceFile is null)
            throw new InvalidOperationException("F5-TTS needs a voice sample. Import one first or choose an existing sample file.");

        var output = Path.Combine(Path.GetTempPath(), $"aether-f5tts-{Guid.NewGuid():N}.wav");
        try
        {
            var script = """
import argparse
from pathlib import Path

import soundfile as sf
from f5_tts.api import F5TTS

parser = argparse.ArgumentParser(description="Aether F5-TTS renderer")
parser.add_argument("--text", required=True)
parser.add_argument("--ref-audio", required=True)
parser.add_argument("--ref-text", default="")
parser.add_argument("--output", required=True)
parser.add_argument("--device", default="cpu")
parser.add_argument("--model", default="F5TTS_v1_Base")
parser.add_argument("--remove-silence", action="store_true")
args = parser.parse_args()

tts = F5TTS(model=args.model, device=args.device)
ref_text = args.ref_text.strip() if args.ref_text else tts.transcribe(args.ref_audio)
if not ref_text:
    raise RuntimeError("Could not determine reference text for F5-TTS voice cloning.")

wav, sr, _ = tts.infer(
    ref_file=args.ref_audio,
    ref_text=ref_text,
    gen_text=args.text,
    file_wave=args.output,
    remove_silence=args.remove_silence,
)

if not Path(args.output).exists():
    sf.write(args.output, wav, sr)
""";

            var run = await VoiceProviderProcessRunner.RunPythonScriptAsync(
                python,
                script,
                ["--text", text, "--ref-audio", referenceFile, "--output", output, "--device", _settings.Settings.TtsDevice.Trim() ?? "cpu", "--model", "F5TTS_v1_Base"],
                ct);

            if (!run.Success)
                throw new InvalidOperationException($"F5-TTS synthesis failed.\n{run.Log}");

            await VoiceProviderProcessRunner.PlayWavFileAsync(output, ct);
        }
        finally
        {
            try { File.Delete(output); }
            catch { }
        }
    }

    private string? ResolveReferenceAudioFile(string? speaker)
    {
        var explicitSpeaker = speaker?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitSpeaker) && File.Exists(explicitSpeaker))
            return Path.GetFullPath(explicitSpeaker);

        var resolved = VoiceProviderProcessRunner.ResolveSpeakerFile(_settings);
        if (resolved is not null)
            return resolved;

        var voiceDir = _settings.Settings.TtsVoiceDirectory.Trim();
        if (!Directory.Exists(voiceDir))
            return null;

        return Directory.EnumerateFiles(voiceDir, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase));
    }
}
