namespace Aether.Core.Models;

public enum VoiceProvider
{
    Kokoro,
    F5Tts,
    XttsV2
}

public enum VoiceProviderCategory
{
    Recommended,
    Advanced,
    Legacy
}

public sealed record VoiceProviderInfo(
    VoiceProvider Id,
    string Name,
    string Description,
    VoiceProviderCategory Category,
    bool IsInstalled,
    string? InstallationPath = null)
{
    public string CategoryLabel => Category switch
    {
        VoiceProviderCategory.Recommended => "Recommended",
        VoiceProviderCategory.Advanced => "Advanced",
        VoiceProviderCategory.Legacy => "Legacy",
        _ => Category.ToString()
    };
}

public sealed record VoiceProviderConfig(
    string ProviderId,
    Dictionary<string, string>? Settings = null)
{
    public VoiceProviderConfig(string ProviderId) : this(ProviderId, new Dictionary<string, string>()) { }
}
