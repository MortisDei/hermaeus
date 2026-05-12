using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// Kokoro voice provider - fast local TTS readback.
/// Recommended default voice engine.
/// </summary>
public sealed class KokoroVoiceProvider : ITtsService
{
    public bool IsInstalled => false; // TODO: Check if Kokoro is installed

    public Task SpeakAsync(string text, CancellationToken ct = default)
    {
        throw new NotImplementedException("Kokoro provider not yet implemented.");
    }

    public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
    {
        throw new NotImplementedException("Kokoro provider not yet implemented.");
    }

    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("Kokoro provider not yet implemented.");
    }

    public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default)
    {
        throw new NotImplementedException("Kokoro provider does not support voice samples.");
    }
}
