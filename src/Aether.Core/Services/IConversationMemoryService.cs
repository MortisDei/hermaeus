namespace Aether.Core.Services;

/// <summary>
/// Builds and persists durable memories from important conversations.
/// </summary>
public interface IConversationMemoryService
{
    /// <summary>
    /// Analyse a conversation and persist high-value memories when it is important enough.
    /// Safe to call repeatedly; implementations should deduplicate and rate-limit work.
    /// </summary>
    Task RunAutoSummaryAsync(string conversationId, CancellationToken ct = default);
}
