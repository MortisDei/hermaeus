using Hermaeus.Rag.Models;

namespace Hermaeus.Rag;

public enum RagStreamEventKind
{
    Token,
    Sources,
    Trace
}

/// <summary>
/// Fields carried by a completed (or refused) RAG query's trace summary.
/// Optional fields are null when the underlying trace omitted them, so a
/// caller updating existing UI state knows to leave that field alone rather
/// than overwrite it with an empty value.
/// </summary>
public sealed record RagTraceSummary(
    string Id,
    long RetrievalLatencyMs,
    long TotalLatencyMs,
    float GroundingScore,
    string Mode,
    string? ExpandedQuery = null,
    string? QueryVariants = null,
    string? PlannerNotes = null,
    string? ContextPackingSummary = null,
    bool? Refused = null,
    string? RefusalReason = null);

/// <summary>
/// Typed replacement for the "__RAG_SOURCES__...__END_SOURCES__" and
/// "__RAG_TRACE__...__END_TRACE__" sentinel strings <see cref="RagQueryService"/>
/// used to interleave into its plain-string answer stream
/// (docs/review/03-next-level-roadmap.md Phase 1). Every consumer
/// (`RagViewModel`, `RagEvalService`, `Hermaeus.LocalApi`) used to detect and
/// strip that wire format independently; now they switch on <see cref="Kind"/>.
/// </summary>
public sealed record RagStreamEvent(RagStreamEventKind Kind, string Text = "", IReadOnlyList<RagTraceChunk>? Sources = null, RagTraceSummary? Trace = null)
{
    public static RagStreamEvent ForToken(string text) => new(RagStreamEventKind.Token, Text: text);
    public static RagStreamEvent ForSources(IReadOnlyList<RagTraceChunk> sources) => new(RagStreamEventKind.Sources, Sources: sources);
    public static RagStreamEvent ForTrace(RagTraceSummary trace) => new(RagStreamEventKind.Trace, Trace: trace);
}
