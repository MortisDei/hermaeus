namespace Aether.Core.Models;

/// <summary>
/// Where a memory applies. Chat-extracted facts are Global; agent workspace
/// notes are Workspace; Conversation is reserved for chat-local memory.
/// </summary>
public enum MemoryScope
{
    Global,
    Conversation,
    Workspace
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
    /// normalized workspace root for Workspace.
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
    /// IDs of related memories (for future relationship visualization).
    /// </summary>
    public List<string> RelatedMemoryIds { get; set; } = [];

    /// <summary>
    /// Whether this memory content is encrypted at rest.
    /// </summary>
    public bool IsEncrypted { get; set; }
}
