using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class RecommendationTests
{
    [Fact]
    public void Eligibility_uses_the_documented_precedence()
    {
        Assert.Equal(RecommendationEligibility.Stale,
            RecommendationEligibilityRules.Evaluate(false, true, true, true, true, true, true));
        Assert.Equal(RecommendationEligibility.InsufficientEvidence,
            RecommendationEligibilityRules.Evaluate(true, true, true, true, true, true, true));
        Assert.Equal(RecommendationEligibility.Contradicted,
            RecommendationEligibilityRules.Evaluate(true, true, false, true, true, true, true));
        Assert.Equal(RecommendationEligibility.Stale,
            RecommendationEligibilityRules.Evaluate(true, true, false, false, true, true, true));
        Assert.Equal(RecommendationEligibility.InsufficientEvidence,
            RecommendationEligibilityRules.Evaluate(true, true, false, false, false, false, true));
        Assert.Equal(RecommendationEligibility.ReviewOnly,
            RecommendationEligibilityRules.Evaluate(true, true, false, false, false, true, false));
        Assert.Equal(RecommendationEligibility.Actionable,
            RecommendationEligibilityRules.Evaluate(true, true, false, false, false, true, true));
    }

    [Fact]
    public void Patch_is_canonical_and_rejects_secret_or_path_fields()
    {
        var first = RecommendationPatch.Create("runtime", "{\"gpu_layers\": 12, \"context\": 4096}");
        var second = RecommendationPatch.Create("runtime", "{\"context\":4096,\"gpu_layers\":12}");

        Assert.Equal(first.CanonicalJson, second.CanonicalJson);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Throws<ArgumentException>(() => RecommendationPatch.Create("runtime", "{\"api_key\":\"secret\"}"));
        Assert.Throws<ArgumentException>(() => RecommendationPatch.Create("runtime", "{\"model_path\":\"/tmp/model.gguf\"}"));
        Assert.Throws<ArgumentException>(() => RecommendationPatch.Create("runtime", "{\"models\":[\"/tmp/model.gguf\"]}"));
    }

    [Fact]
    public void Rule_registry_is_fixed_and_versioned()
    {
        var registry = new RecommendationRuleRegistry();

        Assert.Equal(7, registry.Rules.Count);
        Assert.True(registry.TryGet("compatible-proven-launch", out var rule));
        Assert.Equal(RecommendationKind.RuntimeConfiguration, rule!.Kind);
        Assert.False(registry.TryGet("model-supplied-rule", out _));
    }

    [Fact]
    public async Task Derivation_persists_normalized_rows_and_deduplicates_identical_proposals()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new SqliteRecommendationStore(settings, new RedactionService());
        var service = new RecommendationDerivationService(store, new RecommendationRuleRegistry());
        var proposal = Proposal();

        var first = await service.DeriveAsync(proposal);
        var second = await service.DeriveAsync(proposal);
        var rows = await store.QueryAsync(new RecommendationQuery { Status = RecommendationStatus.Current });

        Assert.Equal(first.Id, second.Id);
        Assert.Single(rows);
        Assert.Equal(RecommendationEligibility.Actionable, first.Eligibility);
        Assert.Single(first.Evidence);
        Assert.Single(first.Conditions);
        Assert.Single(first.Tradeoffs);
    }

    [Fact]
    public async Task Dismissed_proposal_is_not_recreated_by_deduplication()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new SqliteRecommendationStore(settings, new RedactionService());
        var service = new RecommendationDerivationService(store, new RecommendationRuleRegistry());
        var recommendation = await service.DeriveAsync(Proposal());

        await store.SetStatusAsync(recommendation.Id, RecommendationStatus.Dismissed);
        var repeated = await service.DeriveAsync(Proposal());
        var current = await store.QueryAsync(new RecommendationQuery { Status = RecommendationStatus.Current });
        var dismissed = await store.QueryAsync(new RecommendationQuery { Status = RecommendationStatus.Dismissed });

        Assert.Equal(RecommendationStatus.Dismissed, repeated.Status);
        Assert.Empty(current);
        Assert.Single(dismissed);
    }

    [Fact]
    public async Task Derivation_keeps_unknown_or_expired_required_evidence_non_actionable()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new SqliteRecommendationStore(settings, new RedactionService());
        var service = new RecommendationDerivationService(store, new RecommendationRuleRegistry());
        var evaluated = DateTime.UtcNow;
        var unknown = Proposal() with
        {
            EvaluatedAtUtc = evaluated,
            Evidence = [new RecommendationEvidenceReference("unknown", "lab", true, CapabilityState.Unknown, evaluated)]
        };
        var expired = Proposal() with
        {
            TargetIdentity = "runtime-expired",
            EvaluatedAtUtc = evaluated,
            Evidence = [new RecommendationEvidenceReference("expired", "lab", true, CapabilityState.Available, evaluated.AddDays(-2), TimeSpan.FromHours(1))]
        };

        var unknownResult = await service.DeriveAsync(unknown);
        var expiredResult = await service.DeriveAsync(expired);

        Assert.Equal(RecommendationEligibility.InsufficientEvidence, unknownResult.Eligibility);
        Assert.Equal(RecommendationEligibility.Stale, expiredResult.Eligibility);
    }

    [Fact]
    public async Task Decisions_and_rollbacks_round_trip_as_separate_records()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new SqliteRecommendationStore(settings, new RedactionService());
        var service = new RecommendationDerivationService(store, new RecommendationRuleRegistry());
        var recommendation = await service.DeriveAsync(Proposal());

        await store.AddDecisionAsync(new RecommendationDecisionRecord(
            "decision-1", recommendation.Id, RecommendationDecisionKind.Apply, "user",
            recommendation.CurrentConfigurationIdentity, "pending", DateTime.UtcNow));
        await store.AddRollbackAsync(new RecommendationRollbackRecord(
            "rollback-1", recommendation.Id, "{\"context\":4096}", "wrong-hash",
            "post-config", DateTime.UtcNow, false));

        var decisions = await store.QueryDecisionsAsync(recommendation.Id);
        var rollbacks = await store.QueryRollbacksAsync(recommendation.Id);

        Assert.Single(decisions);
        Assert.Equal(RecommendationDecisionKind.Apply, decisions[0].Decision);
        Assert.Single(rollbacks);
        Assert.Equal(ExperienceJson.Hash("{\"context\":4096}"), rollbacks[0].PreImageHash);
    }

    private static SettingsService NewSettings(TempDir temp)
    {
        var settings = new SettingsService(temp.PathFor("settings.json"));
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return settings;
    }

    private static RecommendationProposal Proposal() => new(
        RecommendationKind.RuntimeConfiguration,
        "runtime-target",
        "config-before",
        RecommendationPatch.Create("runtime", "{\"context\":8192}"),
        [new RecommendationEvidenceReference("evidence-1", "lab", true, CapabilityState.Available, DateTime.UtcNow)],
        [new RecommendationCondition("why", "measured")],
        [new RecommendationTradeoff("context", "uses more memory")],
        "compatible-proven-launch",
        1,
        "compatible-success",
        DateTime.UtcNow,
        true,
        true,
        false,
        false,
        false,
        true,
        true,
        DateTime.UtcNow.AddDays(1));
}
