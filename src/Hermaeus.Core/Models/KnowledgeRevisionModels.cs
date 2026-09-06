namespace Hermaeus.Core.Models;

public enum KnowledgeRevisionStatus
{
    Current,
    Superseded,
    Disputed,
    Archived
}

public enum KnowledgeTemporalOrigin
{
    Unknown,
    UserProvided,
    SourceEvidence,
    DeterministicRule,
    ModelInference
}

public enum KnowledgeTimeQueryMode
{
    Current,
    AsOf,
    History
}

/// <summary>
/// A decision attached to a revision or review action. The content is bounded
/// by the persistence implementation before it is written to disk.
/// </summary>
public sealed record KnowledgeRevisionDecision(
    string Kind,
    string Actor,
    string Reason,
    DateTime RecordedAtUtc,
    string? DecisionId = null);

/// <summary>
/// Immutable content and lineage data for one assertion revision. Presentation
/// preferences remain on the current Memory projection and do not create a
/// content revision.
/// </summary>
public sealed record KnowledgeAssertionRevision(
    string AssertionId,
    string RevisionId,
    string? PreviousRevisionId,
    string Content,
    MemoryScope Scope,
    string ScopeId,
    string Category,
    DateTime RecordedAtUtc,
    DateTime? EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    KnowledgeTemporalOrigin TemporalOrigin,
    IReadOnlyList<SourceReference> SourceReferences,
    KnowledgeRevisionStatus Status,
    KnowledgeRevisionDecision? Decision);

/// <summary>
/// Input for an explicit content-bearing assertion command. The Memory carries
/// the current presentation and scope fields; content is copied into a new
/// immutable revision by the store.
/// </summary>
public sealed record KnowledgeRevisionDraft(
    Memory Memory,
    DateTime? EffectiveFromUtc = null,
    DateTime? EffectiveToUtc = null,
    KnowledgeTemporalOrigin TemporalOrigin = KnowledgeTemporalOrigin.Unknown,
    IReadOnlyList<SourceReference>? SourceReferences = null,
    KnowledgeRevisionDecision? Decision = null);

/// <summary>
/// Presentation-only mutation. Content and temporal fields are intentionally
/// absent, so pinning, tags, scope binding, and lifecycle state cannot silently
/// rewrite a fact.
/// </summary>
public sealed record KnowledgePresentationMutation(
    string Title,
    MemoryScope Scope,
    string ScopeId,
    string Category,
    IReadOnlyList<string> Tags,
    double ImportanceScore,
    bool IsPinned,
    bool IsArchived,
    int FrequencyCount,
    DateTime? LastMergeTime,
    DateTime? ExpirationDate,
    IReadOnlyList<string> RelatedMemoryIds,
    IReadOnlyList<KnowledgeRelationship> Relationships,
    bool IsEncrypted,
    string? SourceConversationId)
{
    public static KnowledgePresentationMutation FromMemory(Memory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return new(
            memory.Title,
            memory.Scope,
            memory.ScopeId,
            memory.Category,
            memory.Tags.ToArray(),
            memory.ImportanceScore,
            memory.IsPinned,
            memory.IsArchived,
            memory.FrequencyCount,
            memory.LastMergeTime,
            memory.ExpirationDate,
            memory.RelatedMemoryIds.ToArray(),
            memory.Relationships.ToArray(),
            memory.IsEncrypted,
            memory.SourceConversationId);
    }
}

public sealed record KnowledgeTimeQuery(
    KnowledgeTimeQueryMode Mode,
    DateTime? AsOfUtc = null,
    bool IncludeDisputed = false,
    MemoryScope? Scope = null,
    string? ScopeId = null,
    int Limit = 100);

public sealed class KnowledgeRevisionConflictException : InvalidOperationException
{
    public KnowledgeRevisionConflictException(string assertionId, string? expectedRevisionId, string? actualRevisionId)
        : base($"Knowledge assertion '{assertionId}' changed concurrently. Expected revision '{expectedRevisionId ?? "<none>"}', actual revision '{actualRevisionId ?? "<none>"}'.")
    {
        AssertionId = assertionId;
        ExpectedRevisionId = expectedRevisionId;
        ActualRevisionId = actualRevisionId;
    }

    public string AssertionId { get; }
    public string? ExpectedRevisionId { get; }
    public string? ActualRevisionId { get; }
}

public enum KnowledgeContradictionProposalStatus
{
    Pending,
    Accepted,
    Rejected
}

public enum KnowledgeContradictionDisposition
{
    Coexist,
    Revise,
    SupersedeFromTime,
    MarkDisputed,
    NoRelationship
}

/// <summary>
/// A review-only link between two exact revisions that appear incompatible.
/// A proposal never changes either revision's status or effective interval.
/// </summary>
public sealed record KnowledgeContradictionProposal(
    string ProposalId,
    string LeftAssertionId,
    string LeftRevisionId,
    string RightAssertionId,
    string RightRevisionId,
    string Explanation,
    KnowledgeTemporalOrigin Origin,
    string SourceComparison,
    string EffectiveTimeComparison,
    KnowledgeContradictionDisposition ProposedDisposition,
    string MissingEvidence,
    KnowledgeContradictionProposalStatus Status,
    DateTime CreatedAtUtc,
    KnowledgeRevisionDecision? Decision);

public sealed record KnowledgeContradictionProposalDraft(
    string LeftAssertionId,
    string LeftRevisionId,
    string RightAssertionId,
    string RightRevisionId,
    string Explanation,
    string SourceComparison,
    string EffectiveTimeComparison,
    KnowledgeContradictionDisposition ProposedDisposition,
    string MissingEvidence,
    KnowledgeTemporalOrigin Origin = KnowledgeTemporalOrigin.ModelInference);
