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

    /// <summary>
    /// The full per-turn memory marker pipeline (r16 02-memory-integrity.md
    /// 2.2): applies [MEMORY_UPDATE: id | ...] / [MEMORY_FORGET: id] markers
    /// for ids in <paramref name="injectedMemoryIds"/> (same rule as
    /// <see cref="ApplyInjectedMemoryMarkersAsync"/> - a no-op when nothing
    /// was injected this turn), extracts and dedupe-saves up to
    /// <paramref name="maxNewMemories"/> new <c>[MEMORY: ...]</c> blocks
    /// through the same merge path auto-summary uses, and strips every
    /// marker (valid, invalid, or unmatched) from the returned text - so raw
    /// marker syntax never reaches the persisted transcript, whether or not
    /// anything was injected or extracted this turn.
    /// </summary>
    Task<string> ApplyMemoryMarkersAsync(string responseText, IReadOnlyList<string> injectedMemoryIds, string? conversationId, int maxNewMemories = 3, CancellationToken ct = default);
}
