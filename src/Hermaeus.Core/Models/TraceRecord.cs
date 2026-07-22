namespace Hermaeus.Core.Models;

/// <summary>Which subsystem produced a trace. Viewers filter on this; the schema is shared.</summary>
public enum TraceKind
{
    Chat,
    Rag,
    Agent,
    LocalApi
}

/// <summary>
/// One record of "what happened in a send/run" across chat, RAG, and agent.
/// The envelope is common; kind-specific detail lives in <see cref="DetailJson"/>.
/// </summary>
public sealed record TraceRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public TraceKind Kind { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Conversation id (chat), dataset id (RAG), task id (agent), or the
    /// calling app's self-reported name from <c>X-Hermaeus-Client</c> (local API).
    /// </summary>
    public string SourceId { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Short operation label, e.g. "send", "rag-query", "agent-step".</summary>
    public string Operation { get; init; } = string.Empty;

    public long FirstTokenMs { get; init; }
    public long TotalLatencyMs { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
    public string Error { get; init; } = string.Empty;

    /// <summary>Kind-specific payload as JSON (RagQueryTrace, chat context summary, agent step).</summary>
    public string DetailJson { get; init; } = "{}";
}

/// <summary>
/// Durable per-model daily usage rollup (never pruned, unlike
/// <see cref="TraceRecord"/> rows), aggregated over a requested window.
/// See r6 02-usage-history-recommendations.md.
/// </summary>
public sealed record ModelUsageRow(TraceKind Kind, string ModelId, long CallCount, long TotalTokens);
