namespace Hermaeus.Core.Models;

/// <summary>Where a chat provider runs, which drives privacy posture.</summary>
public enum ProviderKind
{
    /// <summary>A server Hermaeus starts and stops itself (llama-server).</summary>
    ManagedLocal,
    /// <summary>A local API Hermaeus talks to but does not manage (Ollama).</summary>
    LocalApi,
    /// <summary>A remote API; prompts leave the machine.</summary>
    RemoteApi
}

[Flags]
public enum ProviderCapabilities
{
    None           = 0,
    Streaming      = 1 << 0,
    UsageReporting = 1 << 1,
    ModelPull      = 1 << 2,
    ModelDelete    = 1 << 3
}

/// <summary>
/// Declares what a provider is and can do, so routing, UI affordances, and
/// the privacy audit read one source instead of matching on tag strings.
/// </summary>
public sealed record ProviderDescriptor(
    string Tag,
    string DisplayName,
    ProviderKind Kind,
    ProviderCapabilities Capabilities)
{
    public bool Supports(ProviderCapabilities capability) => (Capabilities & capability) == capability;
    public bool IsRemote => Kind == ProviderKind.RemoteApi;
}
