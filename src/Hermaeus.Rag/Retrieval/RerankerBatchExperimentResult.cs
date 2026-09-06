using Hermaeus.Core.Models;

namespace Hermaeus.Rag.Retrieval;

/// <summary>Bounded evidence from the explicit ONNX reranker batch experiment.</summary>
public sealed record RerankerBatchExperimentResult(
    CapabilityState State,
    string EvidenceCode,
    string Detail,
    int PairCount,
    int BatchSize,
    int MaxLength,
    bool? DynamicBatchGraph,
    bool? ScoreOrderEquivalent,
    float? MaximumAbsoluteScoreDifference,
    TimeSpan? SequentialDuration,
    TimeSpan? BatchedDuration,
    long? BatchedAllocatedBytes,
    long? MaximumTensorWorkingSetBytes,
    bool? BenefitObserved)
{
    public bool IsEquivalent => ScoreOrderEquivalent == true
        && MaximumAbsoluteScoreDifference is <= OnnxCrossEncoderReranker.ScoreEquivalenceTolerance;

    public static RerankerBatchExperimentResult Unknown(string evidenceCode, string detail) => new(
        CapabilityState.Unknown,
        evidenceCode,
        detail,
        0,
        0,
        0,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    public static RerankerBatchExperimentResult Unavailable(string evidenceCode, string detail) => new(
        CapabilityState.Unavailable,
        evidenceCode,
        detail,
        0,
        0,
        0,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);
}
