using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IEmpiricalExperienceStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<EmpiricalExperience> AddAsync(EmpiricalExperienceDraft draft, CancellationToken ct = default);
    Task<IReadOnlyList<EmpiricalExperience>> AddBatchAsync(IReadOnlyList<EmpiricalExperienceDraft> drafts, CancellationToken ct = default);
    Task<EmpiricalExperience?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<EmpiricalExperience>> QueryAsync(EmpiricalExperienceQuery query, CancellationToken ct = default);
    Task<EmpiricalExperience> CorrectAsync(string priorId, EmpiricalExperienceDraft replacement, CancellationToken ct = default);
    Task RemoveAsync(string id, CancellationToken ct = default);
    Task<string> ExportAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default);
}
