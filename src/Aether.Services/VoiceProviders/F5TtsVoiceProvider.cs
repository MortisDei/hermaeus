using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// F5-TTS voice provider - modern voice cloning backend.
/// Advanced provider with higher quality but heavier install.
/// Pretrained models use CC-BY-NC license (noncommercial).
/// </summary>
public sealed class F5TtsVoiceProvider : ITtsService
{
    public bool IsInstalled => false; // TODO: Check if F5-TTS is installed

    public Task SpeakAsync(string text, CancellationToken ct = default)
    {
        throw new NotImplementedException("F5-TTS provider not yet implemented.");
    }

    public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
    {
        throw new NotImplementedException("F5-TTS provider not yet implemented.");
    }

    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("F5-TTS provider not yet implemented.");
    }

    public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default)
    {
        throw new NotImplementedException("F5-TTS provider voice import not yet implemented.");
    }
}
