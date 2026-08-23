namespace Hermaeus.Core.Models;

/// <summary>
/// Where a memory applies. Chat-extracted facts are Global; agent workspace
/// notes are Workspace; Conversation is reserved for chat-local memory.
/// </summary>
public enum MemoryScope
{
    Global,
    Conversation,
    Workspace,

    /// <summary>r24 doc 01: keyed by project id. Appended last; the scope is
    /// persisted by name (see <c>MemoryStore</c>), not ordinal, but new values
    /// still append rather than insert to keep that guarantee obvious.</summary>
    Project
}

/// <summary>
/// Represents a stored memory fact, preference, learned behaviour, or interest
/// extracted from conversations or generated through auto-summary.
/// </summary>
public class Memory
{
    /// <summary>
    /// Unique identifier for this memory.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Scope this memory applies to. Defaults to Global (visible everywhere).
    /// </summary>
    public MemoryScope Scope { get; set; } = MemoryScope.Global;

    /// <summary>
    /// Scope key: empty for Global, conversation id for Conversation,
    /// normalized workspace root for Workspace, project id for Project.
    /// </summary>
    public string ScopeId { get; set; } = string.Empty;

    /// <summary>
    /// Optional short title (used by workspace notes; empty for extracted facts).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Category of memory: facts, preferences, learned_behaviors, or interests.
    /// </summary>
    public string Category { get; set; } = "facts";

    /// <summary>
    /// The memory content (text).
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when this memory was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when this memory was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional reference to the source conversation ID where this memory originated.
    /// </summary>
    public string? SourceConversationId { get; set; }

    /// <summary>
    /// Structured provenance for this memory (docs/review/03-next-level-roadmap.md
    /// Phase 1, "provenance everywhere"). New memories get one populated at
    /// extraction time; memories saved before this field existed backfill one
    /// from <see cref="SourceConversationId"/> at read time instead of a data
    /// rewrite (see <c>MemoryStore.Map</c>).
    /// </summary>
    public SourceReference? Source { get; set; }

    /// <summary>
    /// Importance score from 0-1, used for ranking which memories to inject into context.
    /// </summary>
    public double ImportanceScore { get; set; } = 0.5;

    /// <summary>
    /// Tags for organizing and filtering memories.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Whether this memory is pinned (user-marked as important).
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Whether this memory is archived (hidden but not deleted).
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Counter tracking how many duplicate memories have been merged into this one.
    /// </summary>
    public int FrequencyCount { get; set; } = 1;

    /// <summary>
    /// Timestamp of the last merge operation that combined duplicate memories.
    /// </summary>
    public DateTime? LastMergeTime { get; set; }

    /// <summary>
    /// Optional expiration date for lifecycle management (auto-archive or delete).
    /// </summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>
    /// Legacy IDs of related memories. Kept for existing callers and rows;
    /// <see cref="Relationships"/> is the typed replacement.
    /// </summary>
    public List<string> RelatedMemoryIds { get; set; } = [];

    /// <summary>
    /// Typed, evidence-backed links to existing Hermaeus entities. This is
    /// intentionally a bounded relationship list, not a knowledge graph.
    /// </summary>
    public List<KnowledgeRelationship> Relationships { get; set; } = [];

    /// <summary>
    /// Query-only reason this memory was added through a direct relationship.
    /// Never persisted, and never traversed beyond the single recorded hop.
    /// </summary>
    public RelationshipRetrieval? RetrievedViaRelationship { get; set; }

    /// <summary>
    /// The source shown in a chat context receipt. A relationship-expanded
    /// memory keeps its actual source while making the direct relationship
    /// that admitted it visible to the user.
    /// </summary>
    public SourceReference ToContextSource()
    {
        var source = Source ?? new SourceReference(
            ProvenanceKind.Memory,
            string.IsNullOrWhiteSpace(Title) ? "Stored memory" : Title,
            Locator: Id,
            Snippet: Content,
            Timestamp: UpdatedAt);

        if (RetrievedViaRelationship is null)
            return source;

        var relationship = RetrievedViaRelationship;
        var targetTitle = string.IsNullOrWhiteSpace(Title) ? source.Title : Title;
        return new SourceReference(
            ProvenanceKind.Memory,
            $"{targetTitle} (via {KnowledgeRelationshipSemantics.DisplayName(relationship.Kind)} relationship from {relationship.SourceMemoryTitle})",
            Locator: Id,
            Snippet: relationship.Evidence?.Snippet ?? source.Snippet,
            Score: source.Score,
            Timestamp: source.Timestamp,
            EvidenceOrigin: relationship.Evidence?.EvidenceOrigin ?? source.EvidenceOrigin);
    }

    /// <summary>
    /// Whether this memory content is encrypted at rest.
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// How many times this memory has actually been selected for injection
    /// into a prompt (not just retrieved by a search). Used to decay
    /// effective importance for memories nobody has used in a long time.
    /// </summary>
    public int RecallCount { get; set; }

    /// <summary>
    /// When this memory was last selected for injection, if ever.
    /// </summary>
    public DateTime? LastRecalledAt { get; set; }

    /// <summary>
    /// Query-time relevance score from <see cref="Hermaeus.Core.Services.IMemoryStore.SearchAsync"/>
    /// (hybrid FTS+embedding when an embedding model is configured, a plain
    /// rank-based score otherwise). Not persisted; only meaningful on
    /// objects returned from a search.
    /// </summary>
    public double? RelevanceScore { get; set; }
}
