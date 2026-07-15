using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// Service for storing, retrieving, and managing persistent chat memories.
/// </summary>
public interface IMemoryStore
{
    /// <summary>
    /// Initialize the memory store (create tables if needed).
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all memories, optionally filtered by archived state.
    /// </summary>
    Task<List<Memory>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default);

    /// <summary>
    /// Get a specific memory by ID.
    /// </summary>
    Task<Memory?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Get memories by category (facts, preferences, learned_behaviors, interests).
    /// </summary>
    Task<List<Memory>> GetByCategoryAsync(string category, CancellationToken ct = default);

    /// <summary>
    /// Get memories for a scope. A null scopeId returns every row in the scope;
    /// otherwise rows are filtered to the exact scope key (conversation id or
    /// workspace root).
    /// </summary>
    Task<List<Memory>> GetByScopeAsync(MemoryScope scope, string? scopeId = null, bool includeArchived = false, CancellationToken ct = default);

    /// <summary>
    /// Save or update a memory.
    /// </summary>
    Task SaveAsync(Memory memory, CancellationToken ct = default);

    /// <summary>
    /// Delete a memory permanently.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Search memories by query (full-text search across content, tags, category).
    /// </summary>
    Task<List<Memory>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Get memories by importance score threshold (e.g., importance >= 0.7).
    /// </summary>
    Task<List<Memory>> GetByImportanceAsync(double minScore, CancellationToken ct = default);

    /// <summary>
    /// Get recent memories (last N by UpdatedAt).
    /// </summary>
    Task<List<Memory>> GetRecentAsync(int limit = 10, CancellationToken ct = default);

    /// <summary>
    /// Get recent memories for a single source conversation without scanning unrelated recent rows.
    /// </summary>
    Task<List<Memory>> GetRecentByConversationAsync(string conversationId, int limit = 10, CancellationToken ct = default);

    /// <summary>
    /// Get the exact count of stored (non-archived by default) memories for a single conversation.
    /// </summary>
    Task<int> GetCountByConversationAsync(string conversationId, bool includeArchived = false, CancellationToken ct = default);

    /// <summary>
    /// Get exact counts for multiple conversations in a single efficient query. Returns a mapping of conversationId -> count.
    /// </summary>
    Task<Dictionary<string,int>> GetCountsByConversationAsync(IEnumerable<string> conversationIds, bool includeArchived = false, CancellationToken ct = default);

    /// <summary>
    /// Records that these memories were actually injected into a prompt (not
    /// just retrieved by a search): bumps RecallCount and sets LastRecalledAt
    /// to now. Call after injection selection, not after every search.
    /// </summary>
    Task MarkRecalledAsync(IEnumerable<string> ids, CancellationToken ct = default);

    /// <summary>
    /// Archives (never hard-deletes) non-pinned memories whose effective
    /// importance (<see cref="MemoryLifecycle.ComputeEffectiveImportance"/>)
    /// has decayed below <paramref name="importanceFloor"/> and that have
    /// gone unrecalled for at least <paramref name="unrecalledForDays"/>.
    /// Returns how many were archived.
    /// </summary>
    Task<int> ArchiveStaleMemoriesAsync(double importanceFloor = 0.05, int unrecalledForDays = 180, CancellationToken ct = default);

    /// <summary>
    /// Embeds rows that have no vector yet, off the chat send path (r9
    /// 01-send-path-latency.md 1.2). Call once shortly after startup (after
    /// the embedding model warm-up) and after memory writes; a no-op when no
    /// embedding service is configured.
    /// </summary>
    Task RunEmbeddingBackfillAsync(CancellationToken ct = default);
}
