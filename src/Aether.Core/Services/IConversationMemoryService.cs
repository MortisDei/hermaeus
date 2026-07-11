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

    /// <summary>
    /// Applies any [MEMORY_UPDATE: id | content] / [MEMORY_FORGET: id]
    /// markers in a model response, but only for ids in
    /// <paramref name="injectedMemoryIds"/> - the memories that were
    /// actually shown to the model this turn. A marker referencing any
    /// other id is ignored and logged, never applied. Returns the response
    /// text with every marker (valid or not) stripped, since none of them
    /// are meant to reach the user.
    /// </summary>
    Task<string> ApplyInjectedMemoryMarkersAsync(string responseText, IReadOnlyList<string> injectedMemoryIds, CancellationToken ct = default);
}
