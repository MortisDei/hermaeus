using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IProjectStore
{
    Task InitializeAsync();
    Task<List<Project>> GetAllAsync(bool includeArchived = true, CancellationToken ct = default);
    Task<Project?> GetByIdAsync(string id, CancellationToken ct = default);
    Task SaveAsync(Project project, CancellationToken ct = default);

    /// <summary>Removes the project row only. Never touches conversations, tasks,
    /// datasets or memories that reference it; callers clear those bindings first.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public interface IProjectStateStore
{
    Task<ProjectState> GetStateAsync(string projectId, CancellationToken ct = default);
    Task<ProjectState> SaveStateAsync(ProjectState state, long expectedRevision, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectStateProposal>> GetProposalsAsync(string projectId, bool includeReviewed = false, CancellationToken ct = default);
    Task<ProjectStateProposal> CreateProposalAsync(ProjectStateProposal proposal, CancellationToken ct = default);
    Task<ProjectState> AcceptProposalAsync(string proposalId, ProjectState? editedState = null, CancellationToken ct = default);
    Task RejectProposalAsync(string proposalId, string reason, CancellationToken ct = default);
}
