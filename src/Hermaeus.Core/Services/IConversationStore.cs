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
}
