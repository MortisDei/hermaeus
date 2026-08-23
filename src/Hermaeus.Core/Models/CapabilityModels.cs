namespace Hermaeus.Core.Models;

public enum CapabilityState
{
    Available,
    Unavailable,
    Unknown
}

public sealed record CapabilityEvidence(
    CapabilityState State,
    string EvidenceCode,
    string Detail);

/// <summary>How a runtime speculative mechanism obtains proposed tokens.</summary>
public enum SpeculativeDrafterKind
{
    Self,
    EmbeddedMtp,
    External,
    Unknown
}

/// <summary>
/// A mechanism advertised by the selected llama-server. Configurable means
/// Hermaeus has complete, bounded launch and validation semantics for it; it
/// does not mean every advertised upstream mechanism is safe to expose.
/// </summary>
public sealed record RuntimeSpeculativeCapability(
    string Type,
    SpeculativeDrafterKind DrafterKind,
    bool Configurable);

/// <summary>Runtime-only capability surface retained with a model snapshot.</summary>
public sealed record RuntimeCapabilitySurface(
    IReadOnlyList<RuntimeSpeculativeCapability> Speculative,
    CapabilityEvidence PromptThreads,
    CapabilityEvidence BackendSampling,
    CapabilityEvidence PerformanceInstrumentation);

public sealed record LocalModelCapabilities(
    string ModelPath,
    CapabilityEvidence EmbeddedMtp,
    CapabilityEvidence ReasoningOutput,
    CapabilityEvidence ReasoningPreservation,
    CapabilityEvidence Vision,
    DateTime ProbedAtUtc,
    RuntimeCapabilitySurface? RuntimeSurface = null);
