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
}
