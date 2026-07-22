using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IVoiceProviderRegistry
{
    /// Get information about all available voice providers
    IReadOnlyList<VoiceProviderInfo> GetAvailableProviders();

    /// Get the currently active voice provider
    VoiceProvider GetActiveProvider();

    /// Get the active provider instance
    IVoiceProvider GetActiveVoiceProvider();

    /// Get a provider instance by id
    IVoiceProvider GetVoiceProvider(VoiceProvider provider);

    /// Set the active voice provider
    Task SetActiveProviderAsync(VoiceProvider provider);

    /// Get provider-specific configuration
    VoiceProviderConfig? GetProviderConfig(VoiceProvider provider);

    /// Update provider-specific configuration
    Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config);

    /// Get the TTS service for the currently active provider
    ITtsService GetActiveTtsService();
}
