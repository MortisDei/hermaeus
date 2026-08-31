using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// The sole public write boundary for memory assertion content and lineage.
/// Implementations must compare expected current revision ids inside the same
/// transaction as the write.
/// </summary>
public interface IKnowledgeRevisionStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<KnowledgeAssertionRevision> CreateAssertionAsync(
        KnowledgeRevisionDraft draft,
        CancellationToken ct = default);

    Task<KnowledgeAssertionRevision> ReviseAssertionAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        KnowledgeRevisionDraft draft,
        CancellationToken ct = default);

    Task<KnowledgeAssertionRevision> CorrectAssertionAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        KnowledgeRevisionDraft draft,
        CancellationToken ct = default);

    Task<KnowledgeAssertionRevision> SetDisputeAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        bool disputed,
        KnowledgeRevisionDecision decision,
        CancellationToken ct = default);

    Task<KnowledgeAssertionRevision> MutatePresentationAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        KnowledgePresentationMutation mutation,
        CancellationToken ct = default);

    Task<KnowledgeAssertionRevision> RestoreRevisionAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        string revisionId,
        KnowledgeRevisionDecision decision,
        CancellationToken ct = default);

    Task HardDeleteAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        CancellationToken ct = default);

    Task<KnowledgeAssertionRevision?> GetCurrentRevisionAsync(
        string assertionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeAssertionRevision>> QueryAsync(
        KnowledgeTimeQuery query,
        CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeAssertionRevision>> GetHistoryAsync(
        string assertionId,
        CancellationToken ct = default);

    Task<KnowledgeContradictionProposal> CreateContradictionProposalAsync(
        KnowledgeContradictionProposalDraft draft,
        CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeContradictionProposal>> GetContradictionProposalsAsync(
        string? assertionId = null,
        bool includeReviewed = false,
        CancellationToken ct = default);

    Task RejectContradictionProposalAsync(
        string proposalId,
        KnowledgeRevisionDecision decision,
        CancellationToken ct = default);
}
