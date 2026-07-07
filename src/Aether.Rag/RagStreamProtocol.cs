using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Rag.Models;

namespace Aether.Rag;

/// <summary>
/// Fields carried by a "__RAG_TRACE__...__END_TRACE__" sentinel block.
/// Optional fields are null (not defaulted) when the JSON payload omits
/// them, so a caller updating existing UI state knows to leave that field
/// alone rather than overwrite it with an empty value.
/// </summary>
public sealed record RagTraceUpdate(
    string Id,
    long RetrievalLatencyMs,
    long TotalLatencyMs,
    float GroundingScore,
    string? ExpandedQuery,
    string? QueryVariants,
    string? PlannerNotes,
    string? ContextPackingSummary,
    bool? Refused,
    string? RefusalReason);

/// <summary>
/// Parses the "__RAG_SOURCES__...__END_SOURCES__" and
/// "__RAG_TRACE__...__END_TRACE__" sentinel blocks that <see cref="RagQueryService"/>
/// interleaves into its answer token stream. Both <c>RagViewModel</c> (UI) and
/// <see cref="Eval.RagEvalService"/> (scoring) used to parse this same wire
/// format independently; this is the one shared implementation.
/// </summary>
public static class RagStreamProtocol
{
    private static readonly Regex SourcesPattern = new(@"__RAG_SOURCES__(.+)__END_SOURCES__", RegexOptions.Compiled);
    private static readonly Regex TracePattern = new(@"__RAG_TRACE__(.+)__END_TRACE__", RegexOptions.Compiled);

    public static List<RagTraceChunk> ParseSources(string header)
    {
        var json = SourcesPattern.Match(header).Groups[1].Value;
        var list = JsonSerializer.Deserialize<List<JsonElement>>(json) ?? [];
        return list.Select(el => new RagTraceChunk
        {
            Rank = el.GetProperty("rank").GetInt32(),
            Title = el.GetProperty("title").GetString() ?? string.Empty,
            File = el.GetProperty("file").GetString() ?? string.Empty,
            Path = el.TryGetProperty("path", out var path) ? path.GetString() ?? string.Empty : string.Empty,
            Score = el.GetProperty("score").GetSingle(),
            Content = el.TryGetProperty("content", out var content) ? content.GetString() ?? string.Empty : string.Empty
        }).ToList();
    }

    public static RagTraceUpdate ParseTrace(string token)
    {
        var json = TracePattern.Match(token).Groups[1].Value;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new RagTraceUpdate(
            root.GetProperty("Id").GetString() ?? string.Empty,
            root.GetProperty("RetrievalLatencyMs").GetInt64(),
            root.GetProperty("TotalLatencyMs").GetInt64(),
            root.GetProperty("GroundingScore").GetSingle(),
            root.TryGetProperty("ExpandedQuery", out var expandedQuery) ? expandedQuery.GetString() : null,
            root.TryGetProperty("QueryVariants", out var variants)
                ? string.Join("\n", variants.EnumerateArray().Select(v => v.GetString()).Where(v => !string.IsNullOrWhiteSpace(v)))
                : null,
            root.TryGetProperty("PlannerNotes", out var plannerNotes) ? plannerNotes.GetString() : null,
            root.TryGetProperty("ContextPackingSummary", out var packingSummary) ? packingSummary.GetString() : null,
            root.TryGetProperty("Refused", out var refused) ? refused.GetBoolean() : null,
            root.TryGetProperty("RefusalReason", out var refusalReason) ? refusalReason.GetString() : null);
    }
}
