using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// One populated section of a chat turn's context receipt: what kind of
/// material was injected, how many items, roughly what it cost, and the
/// items themselves for the expanded view.
/// </summary>
public sealed record ChatContextReceiptSection(
    ProvenanceKind Kind,
    string Label,
    int ItemCount,
    int EstimatedTokens,
    IReadOnlyList<SourceReference> Items);

/// <summary>
/// Builds the "what went into this answer" receipt for one chat turn
/// (r25 doc 02), the chat-side counterpart to
/// <c>AgentContextReceiptBuilder</c>.
///
/// Before r25, chat rendered three inconsistent strips: memory pills
/// collapsed behind a count (r18 3.3), and everything else - RAG excerpts
/// and, after r24 2.6, Recall hits - as an always-visible pill strip
/// directly above that collapsed pill. Collapsing the memory pill
/// therefore hid nothing the user could see, because Recall hits are
/// memories as far as the person reading the screen is concerned. One
/// receipt over every <see cref="ProvenanceKind"/> is the fix; splitting on
/// one more kind would only have produced two counts that disagree.
///
/// Pure function over source references that already exist in memory after
/// the pre-stream phase. It performs no retrieval and adds nothing to the
/// send path.
/// </summary>
public static class ChatContextReceipt
{
    /// <summary>
    /// Fixed presentation order. Ordering by dictionary iteration is not
    /// ordering; the receipt must read the same way for every turn.
    /// </summary>
    private static readonly (ProvenanceKind Kind, string Label, string Singular, string Plural)[] Sections =
    [
        (ProvenanceKind.Memory,    "Memories",           "memory",            "memories"),
        (ProvenanceKind.Recall,    "Recall",             "recall hit",        "recall hits"),
        (ProvenanceKind.Rag,       "Knowledge excerpts", "knowledge excerpt", "knowledge excerpts"),
        (ProvenanceKind.Workspace, "Workspace files",    "workspace file",    "workspace files"),
        (ProvenanceKind.AgentTool, "Tool results",       "tool result",       "tool results"),
        (ProvenanceKind.ProjectState, "Project State",   "Project State item", "Project State items")
    ];

    public static IReadOnlyList<ChatContextReceiptSection> Build(IReadOnlyList<SourceReference>? sources)
    {
        var receipt = new List<ChatContextReceiptSection>();
        if (sources is null || sources.Count == 0)
            return receipt;

        foreach (var (kind, label, _, _) in Sections)
        {
            var items = sources.Where(s => s.Kind == kind).ToList();
            if (items.Count == 0)
                continue;

            receipt.Add(new ChatContextReceiptSection(
                kind,
                label,
                items.Count,
                items.Sum(EstimateCost),
                items));
        }

        // Defensive: a ProvenanceKind added later must still appear rather than
        // vanish silently from the one surface that claims to be complete.
        foreach (var kind in sources.Select(s => s.Kind).Distinct().OrderBy(k => (int)k))
        {
            if (Sections.Any(s => s.Kind == kind))
                continue;

            var items = sources.Where(s => s.Kind == kind).ToList();
            receipt.Add(new ChatContextReceiptSection(
                kind, kind.ToString(), items.Count, items.Sum(EstimateCost), items));
        }

        return receipt;
    }

    /// <summary>
    /// The collapsed one-liner, e.g. "Context: 3 memories, 2 recall hits".
    /// Empty when nothing was injected, so the view can hide the whole
    /// affordance rather than show an empty receipt on an ordinary turn.
    /// </summary>
    public static string Summarize(IReadOnlyList<ChatContextReceiptSection> sections)
    {
        if (sections.Count == 0)
            return string.Empty;

        var parts = sections.Select(section =>
        {
            var match = Sections.FirstOrDefault(s => s.Kind == section.Kind);
            var noun = match.Plural is null
                ? section.Label.ToLowerInvariant()
                : section.ItemCount == 1 ? match.Singular : match.Plural;
            return $"{section.ItemCount} {noun}";
        });

        return $"Context: {string.Join(", ", parts)}";
    }

    /// <summary>
    /// Matches how <c>ChatViewModel.BuildRecallInjectionAsync</c> budgets a hit
    /// (title plus snippet), rather than inventing a second estimator.
    /// </summary>
    private static int EstimateCost(SourceReference source) =>
        ContextPackBuilder.EstimateTokens(source.Title) + ContextPackBuilder.EstimateTokens(source.Snippet);
}
