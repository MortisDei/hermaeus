using System;
using System.Linq;

namespace Hermaeus.Core.Models;

public enum VoiceProvider
{
    Kokoro,
    F5Tts,
    XttsV2,
    OpenAi,
    KokoroNative
}

/// <summary>
/// Stable parsing and persistence identity for voice providers. The settings
/// file stores enum ids; display names remain accepted for compatibility with
/// older Settings-page saves.
/// </summary>
public static class VoiceProviderIdentity
{
    public static bool TryParse(string? value, out VoiceProvider provider)
    {
        provider = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (Enum.TryParse(trimmed, ignoreCase: true, out provider)
            && Enum.IsDefined(provider))
            return true;

        if (trimmed.Equals("Kokoro (Python)", StringComparison.OrdinalIgnoreCase))
            provider = VoiceProvider.Kokoro;
        else if (trimmed.Equals("Kokoro (native)", StringComparison.OrdinalIgnoreCase))
            provider = VoiceProvider.KokoroNative;
        else if (trimmed.Equals("F5-TTS", StringComparison.OrdinalIgnoreCase))
            provider = VoiceProvider.F5Tts;
        else if (trimmed.Equals("XTTS", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("XTTS v2", StringComparison.OrdinalIgnoreCase))
            provider = VoiceProvider.XttsV2;
        else if (trimmed.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            provider = VoiceProvider.OpenAi;
        else
            return false;

        return true;
    }

    public static string CanonicalId(VoiceProvider provider) => provider.ToString();
}

public enum VoiceProviderCategory
{
    Recommended,
    Advanced,
    Legacy
}

[Flags]
public enum VoiceCapability
{
    None = 0,
    TextToSpeech = 1 << 0,
    VoiceCloning = 1 << 1,
    Local = 1 << 2,
    Remote = 1 << 3,
    RequiresApiKey = 1 << 4,
    Experimental = 1 << 5,
    Legacy = 1 << 6
}

public enum VoiceHealthStatus
{
    Healthy,
    Warning,
    Unhealthy
}

public enum VoiceInstallRiskLevel
{
    Low,
    Medium,
    High
}

public sealed record VoiceProviderInfo(
    VoiceProvider Id,
    string Name,
    string Description,
    VoiceProviderCategory Category,
    bool IsInstalled,
    VoiceCapability Capabilities,
    string? InstallationPath = null)
{
    public string CategoryLabel => Category switch
    {
        VoiceProviderCategory.Recommended => "Recommended",
        VoiceProviderCategory.Advanced => "Advanced",
        VoiceProviderCategory.Legacy => "Legacy",
        _ => Category.ToString()
    };

    public string CapabilityLabel => Capabilities == VoiceCapability.None
        ? ""
        : string.Join(", ", Enum.GetValues<VoiceCapability>()
            .Where(c => c != VoiceCapability.None && Capabilities.HasFlag(c))
            .Select(c => c switch
            {
                VoiceCapability.TextToSpeech => "TTS",
                VoiceCapability.VoiceCloning => "Cloning",
                VoiceCapability.Local => "Local",
                VoiceCapability.Remote => "Remote",
                VoiceCapability.RequiresApiKey => "API key",
                VoiceCapability.Experimental => "Experimental",
                VoiceCapability.Legacy => "Legacy",
                _ => c.ToString()
            }));
}

public sealed record VoiceProviderConfig(
    string ProviderId,
    Dictionary<string, string>? Settings = null)
{
    public VoiceProviderConfig(string ProviderId) : this(ProviderId, new Dictionary<string, string>()) { }
}

public sealed record VoiceProviderDetection(
    bool IsAvailable,
    string Summary,
    string Detail,
    string? InstallationPath = null);

public sealed record VoiceInstallStep(
    string Title,
    string TargetPath,
    string Detail,
    VoiceInstallRiskLevel Risk,
    bool RequiresNetwork,
    IReadOnlyList<string> CommandPreview);

public sealed record VoiceInstallPlan(
    string Summary,
    IReadOnlyList<VoiceInstallStep> Steps,
    string RiskNotes);

public sealed record VoiceHealth(
    VoiceHealthStatus Status,
    string Summary,
    string Detail);

public sealed record VoiceDefinition(
    string Id,
    string Name,
    string? Description = null,
    bool RequiresSample = false);

public sealed record VoiceSynthesisRequest(
    string Text,
    string? Voice = null,
    string? VoiceSamplePath = null,
    string? OutputPath = null,
    string Format = "wav",
    bool PlayAudio = true);

public sealed record VoiceSynthesisResult(
    bool Success,
    string Message,
    string? OutputPath = null,
    byte[]? Audio = null);
