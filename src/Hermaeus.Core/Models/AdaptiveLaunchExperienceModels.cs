namespace Hermaeus.Core.Models;

public sealed record AdaptiveLaunchExperienceContext(
    string RequestedConsumerId,
    string SnapshotId,
    string HardwareIdentity,
    string PlanJson);

public sealed record AdaptiveLaunchExperienceAction(
    string EffectiveLaunchJson,
    string OutcomeJson,
    string ObservationJson);

public sealed class AdaptiveLaunchExperienceCodec : EmpiricalExperienceCodec<AdaptiveLaunchExperienceContext, AdaptiveLaunchExperienceAction>
{
    public override string Domain => EmpiricalExperienceDomains.AdaptiveLaunch;
}
