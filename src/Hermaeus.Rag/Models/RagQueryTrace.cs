namespace Hermaeus.Rag.Models;

/// <summary>
/// r10 02-rag-quality.md 2.2: <see cref="SemanticPlaceholder"/> never had a
/// real semantic implementation; every scoring path already collapsed to
/// token overlap. The value is kept only because it may be present in
/// already-persisted trace JSON (<c>RagQueryTrace.DetailJson</c>); nothing
/// in the codebase currently deserializes that JSON back into this enum,
/// but if a future reader does, treat <see cref="SemanticPlaceholder"/> as
/// <see cref="TokenOverlap"/> rather than reviving mode-specific scoring.
/// </summary>
public enum RagGroundingMode
{
    TokenOverlap,
    SemanticPlaceholder
}

public sealed class RagQueryTrace
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OperationId { get; set; } = string.Empty;
    public string DatasetId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string ExpandedQuestion { get; set; } = string.Empty;
    public List<string> QueryVariants { get; set; } = [];
    public string PlannerNotes { get; set; } = string.Empty;
    public int ContextTokenBudget { get; set; }
    public string ContextPackingSummary { get; set; } = string.Empty;
    public bool Refused { get; set; }
    public string RefusalReason { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public long RetrievalLatencyMs { get; set; }
    public long TotalLatencyMs { get; set; }
    public float GroundingScore { get; set; }
    public RagGroundingMode GroundingMode { get; set; } = RagGroundingMode.TokenOverlap;
    public List<RagTraceChunk> RetrievedChunks { get; set; } = [];
    public List<RagTraceChunk> SelectedContext { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class RagTraceChunk
{
    public int Rank { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceRevisionId { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string GenerationId { get; set; } = string.Empty;
    public float Score { get; set; }
    public string Content { get; set; } = string.Empty;

    // Per-signal breakdown for "why did retrieval choose this chunk"
    // (r6 01-first-five-minutes.md 1.6). Null/zero means that signal did not
    // run for this query (e.g. reranker disabled) rather than "scored zero".
    public int OutOfCount { get; set; }
    public float? VectorScore { get; set; }
    public float? KeywordScore { get; set; }
    public float? RerankScore { get; set; }
    public string MatchedTerm { get; set; } = string.Empty;
    public int MatchedTermCount { get; set; }

    /// <summary>
    /// One deterministic sentence built from whichever components are
    /// present, e.g. "Ranked 2nd of 8: strong semantic match, term
    /// 'migration' matched 3 times, reranker confirmed this ranking."
    /// </summary>
    public string PlainLanguageSummary
    {
        get
        {
            var parts = new List<string>();
            if (VectorScore is { } v)
            {
                parts.Add(v >= 0.75f ? "strong semantic match"
                    : v >= 0.5f ? "moderate semantic match"
                    : "weak semantic match");
            }
            if (MatchedTermCount > 0)
                parts.Add($"term '{MatchedTerm}' matched {MatchedTermCount} time{(MatchedTermCount == 1 ? "" : "s")}");
            if (RerankScore.HasValue)
                parts.Add("reranker confirmed this ranking");

            var prefix = $"Ranked {Rank}{OrdinalSuffix(Rank)} of {OutOfCount}";
            return parts.Count == 0 ? $"{prefix}." : $"{prefix}: {string.Join(", ", parts)}.";
        }
    }

    private static string OrdinalSuffix(int n)
    {
        if (n % 100 is >= 11 and <= 13) return "th";
        return (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    }
}
