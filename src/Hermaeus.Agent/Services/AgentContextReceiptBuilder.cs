using Hermaeus.Agent.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Agent.Services;

/// <summary>One populated section of a step's context pack: what was injected and how much it cost.</summary>
public sealed record AgentContextReceiptSection(string SectionLabel, int ItemCount, int EstimatedTokens, IReadOnlyList<string> ItemIdentifiers);

/// <summary>
/// Builds the "why were these files selected" receipt for one agent step
/// (r6 01-first-five-minutes.md 1.5): per section, how many items were
/// injected, their combined token estimate, and their identifiers. A pure
/// function over data <see cref="AgentContextBuilder"/> already assembled;
/// adds no new persistence. Sections that contributed nothing are omitted
/// rather than shown empty.
/// </summary>
public static class AgentContextReceiptBuilder
{
    public static IReadOnlyList<AgentContextReceiptSection> Build(AgentContextPack pack)
    {
        var sections = new List<AgentContextReceiptSection>();
        AddSection(sections, "Memory", pack.RetrievedMemory.Where(i => i.Source == "workspace-memory"));
        AddSection(sections, "RAG", pack.RetrievedMemory.Where(i => i.Source == "rag"));
        AddSection(sections, "Workspace files", pack.RetrievedFiles);
        AddSection(sections, "Project instructions", pack.ProjectInstructions);
        AddSection(sections, "Project State", pack.ProjectState);
        AddSection(sections, "Transcript replay", pack.TranscriptHistory);
        AddStringSection(sections, "Transcript diagnostics", pack.TranscriptDiagnostics);
        AddSection(sections, "Lessons", pack.Lessons);
        AddSection(sections, "Sub-tasks", pack.SubTaskReports);
        return sections;
    }

    private static void AddSection(List<AgentContextReceiptSection> sections, string label, IEnumerable<AgentRetrievedItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        sections.Add(new AgentContextReceiptSection(
            label,
            list.Count,
            list.Sum(i => ContextPackBuilder.EstimateTokens(i.Content)),
            list.Select(i => i.Title).ToList()));
    }

    private static void AddStringSection(List<AgentContextReceiptSection> sections, string label, IReadOnlyList<string> entries)
    {
        if (entries.Count == 0) return;

        sections.Add(new AgentContextReceiptSection(
            label,
            entries.Count,
            entries.Sum(ContextPackBuilder.EstimateTokens),
            entries));
    }
}
