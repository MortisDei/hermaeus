using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// F5-TTS voice provider - modern voice cloning backend.
/// Advanced provider with higher quality but heavier install.
/// Pretrained models use CC-BY-NC license (noncommercial).
/// </summary>
public sealed class F5TtsVoiceProvider : ITtsService, IVoiceProvider
{
    private readonly ISettingsService _settings;

    public VoiceProvider Id => VoiceProvider.F5Tts;
    public string DisplayName => "F5-TTS";
    public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.VoiceCloning | VoiceCapability.Local | VoiceCapability.Experimental;

    public F5TtsVoiceProvider(ISettingsService settings)
    {
        _settings = settings;
    }

    public bool IsInstalled => VoiceProviderProcessRunner.IsExecutableAvailable(VoiceProviderProcessRunner.ResolvePythonPath(_settings));

    public VoiceProviderDetection Detect()
    {
        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        if (!VoiceProviderProcessRunner.IsExecutableAvailable(python))
            return new VoiceProviderDetection(false, "Python not found", "Install Python 3.11 or point Aether at a venv python.");

        var voiceDir = _settings.Settings.Tts.VoiceDirectory.Trim();
        var hasVoice = Directory.Exists(voiceDir) && Directory.EnumerateFiles(voiceDir).Any();
        var summary = hasVoice ? "Voice samples detected" : "Voice samples missing";
        var detail = hasVoice
            ? $"Voice directory: {voiceDir}"
            : "Import a voice sample before cloning.";
        return new VoiceProviderDetection(hasVoice, summary, detail, voiceDir);
    }

    public VoiceInstallPlan InstallPlan()
    {
        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        var steps = new List<VoiceInstallStep>
        {
            new(
                "Install F5-TTS packages",
                python,
                "Installs f5-tts and soundfile for voice cloning.",
                VoiceInstallRiskLevel.High,
                true,
                [python, "-m", "pip", "install", "f5-tts", "soundfile"]) 
        };

        return new VoiceInstallPlan(
            "F5-TTS requires Python 3.11 and cloning dependencies.",
            steps,
            "Packages download from PyPI and can be large.");
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default)
    {
        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        if (!VoiceProviderProcessRunner.IsExecutableAvailable(python))
            return new VoiceHealth(VoiceHealthStatus.Unhealthy, "Python missing", "Configure a Python 3.11 interpreter or venv.");

        var script = "import importlib\nimportlib.import_module('f5_tts')\nimportlib.import_module('soundfile')\n";
        var result = await VoiceProviderProcessRunner.RunPythonScriptAsync(python, script, [], ct);
        if (!result.Success)
            return new VoiceHealth(VoiceHealthStatus.Unhealthy, "F5-TTS import failed", result.Log);

        var voices = await GetVoicesAsync(ct);
        return voices.Count == 0
            ? new VoiceHealth(VoiceHealthStatus.Warning, "No voice samples", "Import a voice sample to use F5-TTS.")
            : new VoiceHealth(VoiceHealthStatus.Healthy, "F5-TTS is ready", "Python packages import successfully.");
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
            var outputPath = await RenderToFileAsync(request.Text, request.VoiceSamplePath ?? request.Voice, request.OutputPath, ct);
            if (request.PlayAudio)
                await VoiceProviderProcessRunner.PlayWavFileAsync(outputPath, ct);
            return new VoiceSynthesisResult(true, "F5-TTS synthesis complete.", outputPath);
        }
        catch (Exception ex)
        {
            return new VoiceSynthesisResult(false, ex.Message);
        }
    }

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
        var voiceDir = _settings.Settings.Tts.VoiceDirectory.Trim();
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

        var targetDir = _settings.Settings.Tts.VoiceDirectory.Trim();
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

        var python = VoiceProviderProcessRunner.ResolvePythonPath(_settings);
        var referenceFile = ResolveReferenceAudioFile(speaker);
        if (referenceFile is null)
            throw new InvalidOperationException("F5-TTS needs a voice sample. Import one first or choose an existing sample file.");

        var output = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Path.GetTempPath(), $"aether-f5tts-{Guid.NewGuid():N}.wav")
            : outputPath;

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
            ["--text", text, "--ref-audio", referenceFile, "--output", output, "--device", _settings.Settings.Tts.Device.Trim() ?? "cpu", "--model", "F5TTS_v1_Base"],
            ct);

        if (!run.Success)
            throw new InvalidOperationException($"F5-TTS synthesis failed.\n{run.Log}");

        return output;
    }

    private string? ResolveReferenceAudioFile(string? speaker)
    {
        var explicitSpeaker = speaker?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitSpeaker) && File.Exists(explicitSpeaker))
            return Path.GetFullPath(explicitSpeaker);

        var resolved = VoiceProviderProcessRunner.ResolveSpeakerFile(_settings);
        if (resolved is not null)
            return resolved;

        var voiceDir = _settings.Settings.Tts.VoiceDirectory.Trim();
        if (!Directory.Exists(voiceDir))
            return null;

        return Directory.EnumerateFiles(voiceDir, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase));
    }
}
