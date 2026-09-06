namespace Hermaeus.Core.Models;

public sealed record AdaptiveLaunchExperienceContext(
    string RequestedConsumerId,
    string SnapshotId,
    string HardwareIdentity,
    string PlanJson,
    string ConfigurationIdentity);

public sealed record AdaptiveLaunchExperienceAction(
    string EffectiveLaunchJson,
    string OutcomeJson,
    string ObservationJson);

/// <summary>Path-free outcome data used to select a compatible prior launch.</summary>
public sealed record AdaptiveLaunchOutcome(
    string CandidateId,
    string FailureKind,
    string Status,
    bool EffectiveAuditable,
    DateTime RecordedAtUtc);

/// <summary>Bounded effective-launch projection retained in an experience row.</summary>
public sealed record AdaptiveLaunchEffectiveProjection(
    string CandidateId,
    string RuntimeStableId,
    string ParserVersion,
    bool IsAuditable,
    IReadOnlyList<AdaptiveFieldObservation> Fields);

public sealed class AdaptiveLaunchExperienceCodec : EmpiricalExperienceCodec<AdaptiveLaunchExperienceContext, AdaptiveLaunchExperienceAction>
{
    public override string Domain => EmpiricalExperienceDomains.AdaptiveLaunch;
}
