using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IConversationStore
{
    Task InitializeAsync();
    Task<List<Conversation>> GetAllAsync(bool includeArchived = true, CancellationToken ct = default);
    Task<Conversation?> GetByIdAsync(string id, CancellationToken ct = default);
    Task SaveAsync(Conversation conversation, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<List<Conversation>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// r27 05-small-open-items.md 5.1: the sidebar's read. Returns titles,
    /// folders, tags and flags without deserialising a single message.
    /// <see cref="GetByIdAsync"/> is untouched: opening a conversation genuinely
    /// needs its messages.
    /// The default implementation projects from the full read so an existing
    /// store keeps working; the SQLite store overrides it with a real column
    /// projection, which is the point.
    /// </summary>
    async Task<List<ConversationSummary>> GetSummariesAsync(bool includeArchived = true, CancellationToken ct = default)
        => [.. (await GetAllAsync(includeArchived, ct)).Select(ConversationSummary.From)];

    /// <summary>
    /// The search counterpart. FTS keeps matching message text; this changes
    /// what is returned, not what is searched.
    /// </summary>
    async Task<List<ConversationSummary>> SearchSummariesAsync(string query, CancellationToken ct = default)
        => [.. (await SearchAsync(query, ct)).Select(ConversationSummary.From)];
}
