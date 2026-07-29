using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Voice;

namespace Hermaeus.Services;

/// <summary>r24 doc 05 5.1: registry mirroring <see cref="VoiceProviderRegistry"/>,
/// selecting between the local (in-process ONNX) and remote (OpenAI-compatible)
/// speech recognition providers.</summary>
public sealed class SpeechRecognitionProviderRegistry : ISpeechRecognitionProviderRegistry
{
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<SttProvider, ISpeechRecognitionService> _providers;

    public SpeechRecognitionProviderRegistry(
        ISettingsService settingsService,
        NativeSpeechRecognitionProvider onnxNative,
        OpenAiSpeechRecognitionProvider openAi)
    {
        _settingsService = settingsService;
        _providers = new()
        {
            { SttProvider.OnnxNative, onnxNative },
            { SttProvider.OpenAi, openAi }
        };
    }

    public SttProvider GetActiveProvider() =>
        Enum.TryParse<SttProvider>(_settingsService.Settings.Stt.Provider, out var provider) ? provider : SttProvider.OnnxNative;

    public ISpeechRecognitionService GetActiveService() => _providers[GetActiveProvider()];

    public async Task SetActiveProviderAsync(SttProvider provider)
    {
        if (!_providers.ContainsKey(provider))
            throw new ArgumentException($"Unknown speech recognition provider: {provider}");

        _settingsService.Settings.Stt.Provider = provider.ToString();
        await _settingsService.SaveAsync();
    }
}
