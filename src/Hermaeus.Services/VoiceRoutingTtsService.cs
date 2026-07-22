using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class VoiceRoutingTtsService : ITtsService
{
    private readonly IVoiceProviderRegistry _registry;

    public VoiceRoutingTtsService(IVoiceProviderRegistry registry)
    {
        _registry = registry;
    }

    public Task SpeakAsync(string text, CancellationToken ct = default) =>
        _registry.GetActiveTtsService().SpeakAsync(text, ct);

    public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default) =>
        _registry.GetActiveTtsService().PreviewVoiceAsync(speaker, text, ct);

    public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default) =>
        _registry.GetActiveTtsService().ImportVoiceSampleAsync(sourcePath, displayName, ct);

    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default) =>
        _registry.GetActiveTtsService().GetVoicesAsync(ct);
}
