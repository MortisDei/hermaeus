using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed record RecommendationTransactionResult(
    string RecommendationId,
    bool Succeeded,
    string ResultCode,
    string Message);

/// <summary>
/// Owns explicit recommendation decisions for settings-backed managed servers.
/// It does not start, stop, or reconfigure a live process. Settings are written
/// atomically by ISettingsService, and recovery only observes the resulting
/// identity instead of replaying a patch.
/// </summary>
public sealed class RecommendationApplicationService
{
    private readonly IRecommendationStore _store;
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecommendationApplicationService(IRecommendationStore store, ISettingsService settings)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<RecommendationTransactionResult> ApplyAsync(
        string recommendationId,
        string actor = "owner",
        CancellationToken ct = default)
    {
        ValidateId(recommendationId, nameof(recommendationId));
        await _gate.WaitAsync(ct);
        try
        {
            var recommendation = await GetRequiredAsync(recommendationId, ct);
            if (recommendation.Status != RecommendationStatus.Current)
                return await RefuseAsync(recommendation, actor, RecommendationDecisionKind.Apply,
                    "not-current", "This recommendation is no longer current.", ct);
            if (recommendation.Eligibility != RecommendationEligibility.Actionable)
                return await RefuseAsync(recommendation, actor, RecommendationDecisionKind.Apply,
                    "not-actionable", "This recommendation does not have enough evidence for Apply.", ct);
            var patch = ParseManagedPatch(recommendation);
            var current = FindServer(patch.ServerId);
            if (current is null)
                return await RefuseAsync(recommendation, actor, RecommendationDecisionKind.Apply,
                    "stale-target", "The recommendation target no longer exists.", ct, RecommendationStatus.Superseded);

            var currentIdentity = ConfigurationIdentityFactory.Create(current).StableId;
            if (!string.Equals(currentIdentity, recommendation.CurrentConfigurationIdentity, StringComparison.Ordinal))
                return await RefuseAsync(recommendation, actor, RecommendationDecisionKind.Apply,
                    "stale-refused", "The target settings changed after this recommendation was derived. Review it again.", ct, RecommendationStatus.Superseded);

            var candidateSettings = _settings.Settings.Clone();
            var candidate = FindServer(candidateSettings, patch.ServerId)
                ?? throw new InvalidOperationException("The cloned recommendation target could not be found.");
            var preImagePatch = CreatePreImagePatch(current, patch);
            var previousRollbacks = await _store.QueryRollbacksAsync(recommendation.Id, ct);
            ManagedServerRecommendationPatch.Apply(candidate, patch);
            var postIdentity = ConfigurationIdentityFactory.Create(candidate).StableId;
            var rollback = new RecommendationRollbackRecord(
                NewId(), recommendation.Id, preImagePatch.CanonicalJson, preImagePatch.Sha256,
                postIdentity, DateTime.UtcNow, Consumed: false);
            await _store.AddRollbackAsync(rollback, ct);
            await _store.AddDecisionAsync(new RecommendationDecisionRecord(
                NewId(), recommendation.Id, RecommendationDecisionKind.Apply, actor,
                currentIdentity, "pending", DateTime.UtcNow), ct);
            try
            {
                await _settings.SaveAsync(candidateSettings);
            }
            catch
            {
                await _store.AddDecisionAsync(new RecommendationDecisionRecord(
                    NewId(), recommendation.Id, RecommendationDecisionKind.Apply, actor,
                    currentIdentity, "failed", DateTime.UtcNow), CancellationToken.None);
                throw;
            }

            await _store.AddDecisionAsync(new RecommendationDecisionRecord(
                NewId(), recommendation.Id, RecommendationDecisionKind.Apply, actor,
                currentIdentity, "applied", DateTime.UtcNow), ct);
            foreach (var previous in previousRollbacks.Where(value => !value.Consumed))
                await _store.ConsumeRollbackAsync(previous.Id, ct);
            await _store.SetStatusAsync(recommendation.Id, RecommendationStatus.Accepted, ct);
            return new(recommendation.Id, true, "applied", "The reviewed settings were saved. Any running server remains unchanged until explicitly restarted.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecommendationTransactionResult> DismissAsync(
        string recommendationId,
        string actor = "owner",
        CancellationToken ct = default)
    {
        ValidateId(recommendationId, nameof(recommendationId));
        var recommendation = await GetRequiredAsync(recommendationId, ct);
        if (recommendation.Status != RecommendationStatus.Current)
            return new(recommendation.Id, false, "not-current", "This recommendation is no longer current.");
        await _store.AddDecisionAsync(new RecommendationDecisionRecord(
            NewId(), recommendation.Id, RecommendationDecisionKind.Dismiss, actor, null, "dismissed", DateTime.UtcNow), ct);
        await _store.SetStatusAsync(recommendation.Id, RecommendationStatus.Dismissed, ct);
        return new(recommendation.Id, true, "dismissed", "The identical recommendation will remain dismissed.");
    }

    public async Task<RecommendationTransactionResult> UndoAsync(
        string recommendationId,
        string actor = "owner",
        CancellationToken ct = default)
    {
        ValidateId(recommendationId, nameof(recommendationId));
        await _gate.WaitAsync(ct);
        try
        {
            var recommendation = await GetRequiredAsync(recommendationId, ct);
            var rollback = (await _store.QueryRollbacksAsync(recommendation.Id, ct))
                .FirstOrDefault(value => !value.Consumed);
            if (rollback is null)
                return new(recommendation.Id, false, "no-rollback", "No unused rollback snapshot is available.");
            var patch = ManagedServerRecommendationPatch.Parse(rollback.PreImageJson);
            var current = FindServer(patch.ServerId);
            if (current is null)
                return await RefuseAsync(recommendation, actor, RecommendationDecisionKind.Undo,
                    "stale-undo-target", "The rollback target no longer exists.", ct, RecommendationStatus.Superseded);
            var currentIdentity = ConfigurationIdentityFactory.Create(current).StableId;
            if (!string.Equals(currentIdentity, rollback.PostApplyConfigurationIdentity, StringComparison.Ordinal))
                return await RefuseAsync(recommendation, actor, RecommendationDecisionKind.Undo,
                    "stale-undo-refused", "The target changed after Apply. Undo will not overwrite that later edit.", ct);

            var candidateSettings = _settings.Settings.Clone();
            var candidate = FindServer(candidateSettings, patch.ServerId)
                ?? throw new InvalidOperationException("The cloned rollback target could not be found.");
            var undoPreImagePatch = CreatePreImagePatch(current, patch);
            ManagedServerRecommendationPatch.Apply(candidate, patch);
            var postIdentity = ConfigurationIdentityFactory.Create(candidate).StableId;
            var undoRollback = new RecommendationRollbackRecord(
                NewId(), recommendation.Id, undoPreImagePatch.CanonicalJson,
                undoPreImagePatch.Sha256, postIdentity, DateTime.UtcNow, Consumed: false);
            await _store.AddRollbackAsync(undoRollback, ct);
            await _store.AddDecisionAsync(new RecommendationDecisionRecord(
                NewId(), recommendation.Id, RecommendationDecisionKind.Undo, actor,
                currentIdentity, "pending", DateTime.UtcNow), ct);
            try
            {
                await _settings.SaveAsync(candidateSettings);
            }
            catch
            {
                await _store.AddDecisionAsync(new RecommendationDecisionRecord(
                    NewId(), recommendation.Id, RecommendationDecisionKind.Undo, actor,
                    currentIdentity, "failed", DateTime.UtcNow), CancellationToken.None);
                throw;
            }

            await _store.AddDecisionAsync(new RecommendationDecisionRecord(
                NewId(), recommendation.Id, RecommendationDecisionKind.Undo, actor,
                currentIdentity, "undone", DateTime.UtcNow), ct);
            foreach (var item in await _store.QueryRollbacksAsync(recommendation.Id, ct))
                if (!item.Consumed)
                    await _store.ConsumeRollbackAsync(item.Id, ct);
            await _store.SetStatusAsync(recommendation.Id, RecommendationStatus.Current, ct);
            return new(recommendation.Id, true, "undone", "The saved settings were restored. Any running server remains unchanged.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ReconcileAsync(CancellationToken ct = default)
    {
        var decisions = await _store.QueryDecisionsAsync(ct: ct);
        var reconciled = 0;
        foreach (var decision in decisions
                     .GroupBy(value => value.RecommendationId, StringComparer.Ordinal)
                     .Select(group => group.First())
                     .Where(value => value.ResultCode == "pending"))
        {
            ct.ThrowIfCancellationRequested();
            var recommendation = await _store.GetAsync(decision.RecommendationId, ct);
            if (recommendation is null)
                continue;
            var rollback = (await _store.QueryRollbacksAsync(recommendation.Id, ct))
                .FirstOrDefault(value => !value.Consumed);
            var patch = rollback is null ? null : ManagedServerRecommendationPatch.Parse(rollback.PreImageJson);
            var current = patch is null ? null : FindServer(patch.ServerId);
            var currentIdentity = current is null ? string.Empty : ConfigurationIdentityFactory.Create(current).StableId;
            var matchesExpected = string.Equals(currentIdentity, decision.ExpectedCurrentConfigurationIdentity, StringComparison.Ordinal);
            var matchesPost = rollback is not null && string.Equals(currentIdentity, rollback.PostApplyConfigurationIdentity, StringComparison.Ordinal);
            var code = matchesPost
                ? decision.Decision == RecommendationDecisionKind.Undo ? "reconciled-undone" : "reconciled-applied"
                : matchesExpected ? "reconciled-not-applied" : "reconciled-stale";
            var status = matchesPost
                ? decision.Decision == RecommendationDecisionKind.Undo ? RecommendationStatus.Current : RecommendationStatus.Accepted
                : matchesExpected ? RecommendationStatus.Current : RecommendationStatus.Superseded;
            await _store.AddDecisionAsync(decision with { Id = NewId(), ResultCode = code, CreatedAtUtc = DateTime.UtcNow }, ct);
            if (matchesPost && decision.Decision == RecommendationDecisionKind.Undo && rollback is not null)
            {
                foreach (var item in await _store.QueryRollbacksAsync(recommendation.Id, ct))
                    if (!item.Consumed)
                        await _store.ConsumeRollbackAsync(item.Id, ct);
            }
            await _store.SetStatusAsync(recommendation.Id, status, ct);
            reconciled++;
        }
        return reconciled;
    }

    private static ParsedManagedServerPatch ParseManagedPatch(ConfigurationRecommendation recommendation)
    {
        if (!string.Equals(recommendation.ProposedPatch.TargetDomain, ManagedServerRecommendationPatch.TargetDomain, StringComparison.Ordinal))
            throw new InvalidOperationException("Only managed-server recommendation patches can be applied by this service.");
        var patch = ManagedServerRecommendationPatch.Parse(recommendation.ProposedPatch.CanonicalJson);
        if (!string.Equals(recommendation.TargetIdentity, patch.ServerId, StringComparison.Ordinal))
            throw new InvalidOperationException("The recommendation target does not match its patch target.");
        return patch;
    }

    private static RecommendationPatch CreatePreImagePatch(ServerConfig current, ParsedManagedServerPatch proposed)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in proposed.Changes.Keys)
            values[field] = ReadCurrentValue(current, field);
        return ManagedServerRecommendationPatch.CreateFromChanges(current.Id, values);
    }

    private static object? ReadCurrentValue(ServerConfig config, string field) => field switch
    {
        "contextSize" => config.ContextSize,
        "gpuPlacement" => config.TryGetGpuPlacement(out var placement, out _) ? placement : throw new InvalidOperationException("The current GPU placement is invalid."),
        "threads" => config.Threads,
        "promptThreads" => config.PromptThreads,
        "slots" => config.Slots,
        "kvCacheTypeK" => config.KvCacheTypeK,
        "kvCacheTypeV" => config.KvCacheTypeV,
        "flashAttention" => config.FlashAttention,
        "cpuMoeLayers" => config.CpuMoeLayers,
        "speculativeTypes" => (config.Speculative ?? new SpeculativeDecodingConfig()).Types,
        "draftGpuLayers" => (config.Speculative ?? new SpeculativeDecodingConfig()).DraftGpuLayers,
        "speculativeNMax" => (config.Speculative ?? new SpeculativeDecodingConfig()).NMax,
        "speculativeNMin" => (config.Speculative ?? new SpeculativeDecodingConfig()).NMin,
        "speculativePMin" => (config.Speculative ?? new SpeculativeDecodingConfig()).PMin,
        _ => throw new InvalidOperationException($"Managed-server patch field '{field}' is not supported.")
    };

    private async Task<RecommendationTransactionResult> RefuseAsync(
        ConfigurationRecommendation recommendation,
        string actor,
        RecommendationDecisionKind decision,
        string code,
        string message,
        CancellationToken ct,
        RecommendationStatus? status = null)
    {
        await _store.AddDecisionAsync(new RecommendationDecisionRecord(
            NewId(), recommendation.Id, decision, actor,
            recommendation.CurrentConfigurationIdentity, code, DateTime.UtcNow), ct);
        if (status is { } value)
            await _store.SetStatusAsync(recommendation.Id, value, ct);
        return new(recommendation.Id, false, code, message);
    }

    private async Task<ConfigurationRecommendation> GetRequiredAsync(string id, CancellationToken ct) =>
        await _store.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Recommendation '{id}' does not exist.");

    private ServerConfig? FindServer(string id) => FindServer(_settings.Settings, id);

    private static ServerConfig? FindServer(AppSettings settings, string id) =>
        settings.ManagedServers.FirstOrDefault(server => string.Equals(server.Id, id, StringComparison.Ordinal));

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static void ValidateId(string id, string field)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsWhiteSpace)
            || id.Contains('/') || id.Contains('\\'))
            throw new ArgumentException("Recommendation ids must be path-free opaque values.", field);
    }
}
