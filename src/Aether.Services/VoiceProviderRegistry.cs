using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class VoiceProviderRegistry : IVoiceProviderRegistry
{
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<VoiceProvider, IVoiceProvider> _providers;
    private VoiceProvider _activeProvider;

    public VoiceProviderRegistry(
        ISettingsService settingsService,
        XttsV2VoiceProvider xttsV2,
        KokoroVoiceProvider kokoro,
        F5TtsVoiceProvider f5Tts,
        OpenAiVoiceProvider openAi)
    {
        _settingsService = settingsService;
        _providers = new()
        {
            { VoiceProvider.XttsV2, xttsV2 },
            { VoiceProvider.Kokoro, kokoro },
            { VoiceProvider.F5Tts, f5Tts },
            { VoiceProvider.OpenAi, openAi }
        };

        _activeProvider = ParseProviderFromSettings(settingsService.Settings.Tts.VoiceProvider);
    }

    public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders()
    {
        return new List<VoiceProviderInfo>
        {
            new VoiceProviderInfo(
                VoiceProvider.Kokoro,
                "Kokoro",
                "Fast local readback. Tiny, Apache-licensed, and low-drama.",
                VoiceProviderCategory.Recommended,
                ((KokoroVoiceProvider)_providers[VoiceProvider.Kokoro]).IsInstalled,
                _providers[VoiceProvider.Kokoro].Capabilities),
            new VoiceProviderInfo(
                VoiceProvider.F5Tts,
                "F5-TTS",
                "Advanced cloning and experimental high-quality voices. Pretrained models are CC-BY-NC.",
                VoiceProviderCategory.Advanced,
                ((F5TtsVoiceProvider)_providers[VoiceProvider.F5Tts]).IsInstalled,
                _providers[VoiceProvider.F5Tts].Capabilities),
            new VoiceProviderInfo(
                VoiceProvider.XttsV2,
                "XTTS v2",
                "Legacy Coqui-compatible voice cloning backend. Best compatibility with existing workflows, requires Python 3.11.",
                VoiceProviderCategory.Legacy,
                ((XttsV2VoiceProvider)_providers[VoiceProvider.XttsV2]).IsInstalled,
                _providers[VoiceProvider.XttsV2].Capabilities),
            new VoiceProviderInfo(
                VoiceProvider.OpenAi,
                "OpenAI",
                "Remote voice synthesis via OpenAI API.",
                VoiceProviderCategory.Advanced,
                ((OpenAiVoiceProvider)_providers[VoiceProvider.OpenAi]).IsInstalled,
                _providers[VoiceProvider.OpenAi].Capabilities)
        }
            .OrderBy(p => (int)p.Category)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public VoiceProvider GetActiveProvider() => _activeProvider;

    public IVoiceProvider GetActiveVoiceProvider() => GetVoiceProvider(_activeProvider);

    public IVoiceProvider GetVoiceProvider(VoiceProvider provider)
    {
        if (!_providers.TryGetValue(provider, out var svc))
            throw new ArgumentException($"Unknown voice provider: {provider}");
        return svc;
    }

    public async Task SetActiveProviderAsync(VoiceProvider provider)
    {
        if (!_providers.ContainsKey(provider))
            throw new ArgumentException($"Unknown voice provider: {provider}");

        _activeProvider = provider;
        _settingsService.Settings.Tts.VoiceProvider = provider.ToString();
        await _settingsService.SaveAsync();
    }

    public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider)
    {
        var key = provider.ToString();
        return _settingsService.Settings.VoiceProviderConfigs.TryGetValue(key, out var config)
            ? config
            : new VoiceProviderConfig(key);
    }

    public async Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config)
    {
        _settingsService.Settings.VoiceProviderConfigs[provider.ToString()] = config;
        await _settingsService.SaveAsync();
    }

    public ITtsService GetActiveTtsService()
    {
        if (!_providers.TryGetValue(_activeProvider, out var service))
            throw new InvalidOperationException($"No TTS service available for provider: {_activeProvider}");

        return (ITtsService)service;
    }

    private static VoiceProvider ParseProviderFromSettings(string providerName)
    {
        return providerName switch
        {
            "Kokoro" => VoiceProvider.Kokoro,
            "F5Tts" or "F5-TTS" => VoiceProvider.F5Tts,
            "XttsV2" or "XTTS" or "XTTS v2" => VoiceProvider.XttsV2,
            "OpenAi" or "OpenAI" => VoiceProvider.OpenAi,
            _ => VoiceProvider.Kokoro // Default to Kokoro
        };
    }
}
