namespace Hermaeus.Core.Services;

public interface ITtsService
{
    Task SpeakAsync(string text, CancellationToken ct = default);
    Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default);
    Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default);
}
