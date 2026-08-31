using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

public sealed record AdaptiveLaunchPreference(string CandidateId, DateTime RecordedAtUtc);

/// <summary>
/// Stores and reads bounded adaptive launch outcomes. Compatibility is
/// deliberately stricter than a matching model name: it requires complete
/// runtime, model, hardware, and path-free workload identities.
/// </summary>
public sealed class AdaptiveInferenceExperienceService
{
    private readonly IEmpiricalExperienceStore _store;
    private readonly AdaptiveLaunchExperienceCodec _codec = new();

    public AdaptiveInferenceExperienceService(IEmpiricalExperienceStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<AdaptiveLaunchPreference?> FindPreferredCandidateAsync(
        ResourceWorkloadPlan workload,
        RuntimeIdentityV2 runtime,
        ModelIdentityV2 model,
        string configurationIdentity,
        AdaptiveInferenceEnvelope envelope,
        DateTime? nowUtc = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationIdentity);
        ArgumentNullException.ThrowIfNull(envelope);
        if (runtime.Completeness != IdentityCompleteness.Complete
            || model.Completeness != IdentityCompleteness.Complete
            || !workload.HardwareIdentityComplete
            || string.IsNullOrWhiteSpace(workload.HardwareIdentityId))
            return null;

        var now = nowUtc ?? DateTime.UtcNow;
        var from = now - envelope.PreferredEvidenceAge;
        var rows = await _store.QueryAsync(new EmpiricalExperienceQuery
        {
            Domain = EmpiricalExperienceDomains.AdaptiveLaunch,
            RuntimeFingerprint = runtime.StableId,
            ModelFingerprint = model.StableId,
            CreatedFromUtc = from,
            CreatedToUtc = now,
            Status = EmpiricalExperienceStatus.Current,
            Limit = 200
        }, ct);
        var workloadKey = WorkloadKey(workload);
        foreach (var row in rows.OrderByDescending(value => value.CreatedAtUtc))
        {
            try
            {
                var context = _codec.DecodeContext(row.ContextJson);
                if (!string.Equals(context.HardwareIdentity, workload.HardwareIdentityId, StringComparison.Ordinal)
                    || !string.Equals(context.PlanJson, workloadKey, StringComparison.Ordinal)
                    || !string.Equals(context.ConfigurationIdentity, configurationIdentity, StringComparison.Ordinal))
                    continue;

                var action = _codec.DecodeAction(row.ActionJson);
                var outcome = ExperienceJson.Decode<AdaptiveLaunchOutcome>(action.OutcomeJson);
                if (outcome.EffectiveAuditable
                    && string.Equals(outcome.FailureKind, ServerLaunchFailureKind.None.ToString(), StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(outcome.CandidateId))
                    return new(outcome.CandidateId, row.CreatedAtUtc);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
            {
                // A malformed or legacy row is not compatibility evidence.
            }
        }

        return null;
    }

    public Task<EmpiricalExperience> RecordAsync(
        ResourceWorkloadPlan workload,
        RuntimeIdentityV2 runtime,
        ModelIdentityV2 model,
        string configurationIdentity,
        string candidateId,
        IReadOnlyList<string> changedFields,
        ServerLaunchResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationIdentity);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(changedFields);
        if (string.IsNullOrWhiteSpace(candidateId))
            throw new ArgumentException("An adaptive candidate id is required.", nameof(candidateId));

        var effective = result.EffectiveLaunch;
        var outcome = new AdaptiveLaunchOutcome(
            candidateId,
            result.FailureKind.ToString(),
            result.Status.ToString(),
            effective?.IsAuditable == true,
            DateTime.UtcNow);
        var projection = new AdaptiveLaunchEffectiveProjection(
            candidateId,
            effective?.RuntimeIdentity.StableId ?? runtime.StableId,
            effective?.ParserVersion ?? EffectiveLaunchObservationParser.ParserVersion,
            effective?.IsAuditable == true,
            effective?.Fields ?? []);
        var observation = new
        {
            candidateId,
            changedFields = changedFields.ToArray(),
            failureKind = result.FailureKind.ToString(),
            status = result.Status.ToString()
        };
        var action = new AdaptiveLaunchExperienceAction(
            ExperienceJson.Canonicalize(projection),
            ExperienceJson.Canonicalize(outcome),
            ExperienceJson.Canonicalize(observation));
        var evidenceIds = effective?.EvidenceIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToArray() ?? [];
        if (evidenceIds.Length == 0)
            evidenceIds = [$"adaptive-launch-{Guid.NewGuid():N}"];
        var provenanceIds = evidenceIds.Select(SafeEvidenceId).Distinct(StringComparer.Ordinal).ToArray();

        var context = new AdaptiveLaunchExperienceContext(
            workload.RequestedConsumerId,
            workload.SnapshotId,
            workload.HardwareIdentityId,
            WorkloadKey(workload),
            configurationIdentity);
        var draft = new EmpiricalExperienceDraft
        {
            Domain = EmpiricalExperienceDomains.AdaptiveLaunch,
            ContextJson = _codec.EncodeContext(context),
            ActionJson = _codec.EncodeAction(action),
            RuntimeFingerprint = runtime.Completeness == IdentityCompleteness.Complete ? runtime.StableId : null,
            ModelFingerprint = model.Completeness == IdentityCompleteness.Complete ? model.StableId : null,
            Outcome = NormalizedToolOutcome.Create(
                effective?.IsAuditable == true && result.FailureKind == ServerLaunchFailureKind.None
                    ? NormalizedOutcome.Succeeded
                    : result.FailureKind == ServerLaunchFailureKind.ResourceExhaustion
                        ? NormalizedOutcome.Failed
                        : NormalizedOutcome.Unknown,
                "adaptive-launch-outcome-v1",
                $"Candidate {candidateId}: {result.FailureKind}."),
            Provenance = provenanceIds.Select(id => new EmpiricalExperienceProvenance(
                id,
                new SourceReference(
                    ProvenanceKind.RuntimeObservation,
                    "Managed effective launch observation",
                    id,
                    EvidenceOrigin: EvidenceOrigin.DirectObservation))).ToArray()
        };
        return _store.AddAsync(draft, ct);
    }

    private static string SafeEvidenceId(string value)
    {
        var chars = value.Trim().Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or ':' ? character : '-').ToArray();
        var safe = new string(chars);
        return safe.Length <= 128 ? safe : safe[..128];
    }

    public static string WorkloadKey(ResourceWorkloadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var value = new
        {
            requestedConsumer = plan.RequestedConsumerId,
            existingConsumers = plan.ExistingConsumers.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            preservedReservations = plan.PreservedReservations
                .Select(reservation => new
                {
                    reservation.ConsumerId,
                    reservation.PriorityClass,
                    deviceBytes = reservation.DeviceBytes
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToArray(),
                    reservation.SystemBytes
                })
                .OrderBy(reservation => reservation.ConsumerId, StringComparer.Ordinal)
                .ToArray(),
            policy = new
            {
                plan.HeadroomPolicy.DeviceStabilityBytes,
                plan.HeadroomPolicy.SystemStabilityBytes,
                plan.HeadroomPolicy.InteractiveReservationBytes,
                plan.HeadroomPolicy.ForegroundReservationBytes,
                plan.HeadroomPolicy.InProcessReservationBytes,
                plan.HeadroomPolicy.UnknownDeviceReservationBytes,
                reservationLifetime = plan.HeadroomPolicy.ReservationLifetime
            }
        };
        return ExperienceJson.Canonicalize(value);
    }
}
