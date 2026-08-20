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

public sealed record LocalModelCapabilities(
    string ModelPath,
    CapabilityEvidence EmbeddedMtp,
    CapabilityEvidence ReasoningOutput,
    CapabilityEvidence ReasoningPreservation,
    CapabilityEvidence Vision,
    DateTime ProbedAtUtc);
