using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed record RecommendationRuleDefinition(string RuleId, RecommendationKind Kind, int DerivationVersion);

/// <summary>
/// Fixed registry for recommendation rules. Rules are application code, not
/// model-provided text or persisted executable behavior.
/// </summary>
public sealed class RecommendationRuleRegistry
{
    private readonly IReadOnlyDictionary<string, RecommendationRuleDefinition> _rules;

    public RecommendationRuleRegistry()
    {
        var definitions = new[]
        {
            new RecommendationRuleDefinition("runtime-retune-after-identity-drift", RecommendationKind.RuntimeConfiguration, 1),
            new RecommendationRuleDefinition("compatible-proven-launch", RecommendationKind.RuntimeConfiguration, 1),
            new RecommendationRuleDefinition("review-lab-winner", RecommendationKind.RuntimeConfiguration, 1),
            new RecommendationRuleDefinition("review-context-kv-placement", RecommendationKind.WorkloadPlacement, 1),
            new RecommendationRuleDefinition("review-default-model", RecommendationKind.DefaultModel, 1),
            new RecommendationRuleDefinition("retest-incompatible-evidence", RecommendationKind.Retest, 1),
            new RecommendationRuleDefinition("review-resource-conflict", RecommendationKind.ResourceConflict, 1)
        };
        _rules = definitions.ToDictionary(value => value.RuleId, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<RecommendationRuleDefinition> Rules => _rules.Values.ToArray();

    public bool TryGet(string ruleId, out RecommendationRuleDefinition? rule) =>
        _rules.TryGetValue(ruleId, out rule);
}

/// <summary>Derives and persists one deterministic, path-free recommendation.</summary>
public sealed class RecommendationDerivationService
{
    private readonly IRecommendationStore _store;
    private readonly RecommendationRuleRegistry _registry;

    public RecommendationDerivationService(IRecommendationStore store, RecommendationRuleRegistry registry)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<ConfigurationRecommendation> DeriveAsync(
        RecommendationProposal proposal,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!_registry.TryGet(proposal.RuleId, out var rule) || rule is null)
            throw new InvalidOperationException($"Recommendation rule '{proposal.RuleId}' is not registered.");
        if (rule.Kind != proposal.Kind || rule.DerivationVersion != proposal.DerivationVersion)
            throw new InvalidOperationException("The recommendation rule and proposal version do not agree.");

        var evidence = RecommendationEvidenceRules.Assess(proposal.Evidence, proposal.EvaluatedAtUtc);
        var eligibility = RecommendationEligibilityRules.Evaluate(
            proposal.TargetIdentityComplete,
            proposal.TargetExists,
            proposal.RequiredEvidenceRevoked,
            proposal.Contradicted,
            proposal.RequiredEvidenceExpired || evidence.RequiredEvidenceExpired,
            proposal.MinimumFactsComplete && evidence.RequiredFactsComplete,
            proposal.Actionable);
        var recommendation = ConfigurationRecommendation.Create(
            proposal.Kind,
            proposal.TargetIdentity,
            proposal.CurrentConfigurationIdentity,
            proposal.ProposedPatch,
            proposal.Evidence,
            proposal.Conditions,
            proposal.Tradeoffs,
            eligibility,
            proposal.RuleId,
            proposal.DerivationVersion,
            proposal.ReasonCode,
            proposal.EvaluatedAtUtc,
            proposal.ExpiresAtUtc);
        return await _store.AddOrGetAsync(recommendation, ct);
    }
}
