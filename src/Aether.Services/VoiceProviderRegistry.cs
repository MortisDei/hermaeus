using System.Collections.Frozen;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class VoiceProviderRegistry : IVoiceProviderRegistry
{
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<VoiceProvider, ITtsService> _providers;
    private readonly Dictionary<VoiceProvider, VoiceProviderInfo> _providerInfos;
    private VoiceProvider _activeProvider;

    public VoiceProviderRegistry(
        ISettingsService settingsService,
        XttsV2VoiceProvider xttsV2,
        KokoroVoiceProvider kokoro,
        F5TtsVoiceProvider f5Tts)
    {
        _settingsService = settingsService;
        _providers = new()
        {
            { VoiceProvider.XttsV2, xttsV2 },
            { VoiceProvider.Kokoro, kokoro },
            { VoiceProvider.F5Tts, f5Tts }
        };

        _providerInfos = new()
        {
            {
                VoiceProvider.Kokoro,
                new VoiceProviderInfo(
                    VoiceProvider.Kokoro,
                    "Kokoro",
                    "Fast local TTS readback. Lightweight, recommended default.",
                    VoiceProviderCategory.Recommended,
                    kokoro.IsInstalled)
            },
            {
                VoiceProvider.F5Tts,
                new VoiceProviderInfo(
                    VoiceProvider.F5Tts,
                    "F5-TTS",
                    "Modern voice cloning backend. Higher quality, heavier install. Noncommercial pretrained model license.",
                    VoiceProviderCategory.Advanced,
                    f5Tts.IsInstalled)
            },
            {
                VoiceProvider.XttsV2,
                new VoiceProviderInfo(
                    VoiceProvider.XttsV2,
                    "XTTS v2",
                    "Legacy Coqui voice cloning backend. Best compatibility with existing workflows, requires Python 3.11.",
                    VoiceProviderCategory.Legacy,
                    xttsV2.IsInstalled)
            }
        };

        _activeProvider = ParseProviderFromSettings(settingsService.Settings.VoiceProvider);
    }

    public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders()
    {
        return _providerInfos.Values
            .OrderBy(p => (int)p.Category)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public VoiceProvider GetActiveProvider() => _activeProvider;

    public async Task SetActiveProviderAsync(VoiceProvider provider)
    {
        if (!_providers.ContainsKey(provider))
            throw new ArgumentException($"Unknown voice provider: {provider}");

        _activeProvider = provider;
        _settingsService.Settings.VoiceProvider = provider.ToString();
        await _settingsService.SaveAsync();
    }

    public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider)
    {
        // TODO: Store and retrieve provider-specific configs
        return null;
    }

    public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config)
    {
        // TODO: Persist provider-specific configs
        return Task.CompletedTask;
    }

    public ITtsService GetActiveTtsService()
    {
        if (!_providers.TryGetValue(_activeProvider, out var service))
            throw new InvalidOperationException($"No TTS service available for provider: {_activeProvider}");

        return service;
    }

    private static VoiceProvider ParseProviderFromSettings(string providerName)
    {
        return providerName switch
        {
            "Kokoro" => VoiceProvider.Kokoro,
            "F5Tts" or "F5-TTS" => VoiceProvider.F5Tts,
            "XttsV2" or "XTTS" or "XTTS v2" => VoiceProvider.XttsV2,
            _ => VoiceProvider.Kokoro // Default to Kokoro
        };
    }
}
