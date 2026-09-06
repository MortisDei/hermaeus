using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IRecommendationStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<ConfigurationRecommendation> AddOrGetAsync(ConfigurationRecommendation recommendation, CancellationToken ct = default);
    Task<ConfigurationRecommendation?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<ConfigurationRecommendation>> QueryAsync(RecommendationQuery query, CancellationToken ct = default);
    Task SetStatusAsync(string recommendationId, RecommendationStatus status, CancellationToken ct = default);
    Task<RecommendationDecisionRecord> AddDecisionAsync(RecommendationDecisionRecord decision, CancellationToken ct = default);
    Task<RecommendationRollbackRecord> AddRollbackAsync(RecommendationRollbackRecord rollback, CancellationToken ct = default);
    Task ConsumeRollbackAsync(string rollbackId, CancellationToken ct = default);
    Task<IReadOnlyList<RecommendationDecisionRecord>> QueryDecisionsAsync(string? recommendationId = null, CancellationToken ct = default);
    Task<IReadOnlyList<RecommendationRollbackRecord>> QueryRollbacksAsync(string? recommendationId = null, CancellationToken ct = default);
}
