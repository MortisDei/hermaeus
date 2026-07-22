using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>
/// Injects memories into chat context and generates memory instructions for models.
/// </summary>
public sealed class MemoryInjectionService
{
    public string BuildMemoryContext(List<Memory> memories)
    {
        if (memories.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("\n---\n## Stored Memories\n");

        // Group by category
        var byCategory = memories.GroupBy(m => m.Category).OrderBy(g => g.Key);

        foreach (var group in byCategory)
        {
            sb.AppendLine($"### {FormatCategoryName(group.Key)}");
            foreach (var memory in group.OrderByDescending(m => m.IsPinned).ThenByDescending(m => m.ImportanceScore))
            {
                // The id lets the model reference this exact memory in a
                // [MEMORY_UPDATE: id | ...] or [MEMORY_FORGET: id] marker;
                // only ids printed here are ever honored (see
                // ConversationMemoryService.ApplyInjectedMemoryMarkersAsync).
                var idTag = $"[id:{memory.Id}]";
                sb.AppendLine(memory.IsPinned
                    ? $"- ⭐ {idTag} {memory.Content}"
                    : $"- {idTag} {memory.Content}");
            }
            sb.AppendLine();
        }

        sb.AppendLine(
            "If any memory above (by its [id:...]) is stale or wrong, correct or " +
            "retire it: [MEMORY_UPDATE: <id> | <corrected content>] or " +
            "[MEMORY_FORGET: <id>]. Only ids shown above are honored.");

        return sb.ToString();
    }

    public string GetMemoryInstructionPrompt()
    {
        return @"
## Memory Instructions

You can store memories about the user, lessons learned, or important context for future conversations.

To save a memory, wrap it in the following format anywhere in your response:
[MEMORY: <memory content here>]

Examples:
[MEMORY: User prefers Australian English spelling (favour, behaviour, etc.)]
[MEMORY: User values performance optimization over quick solutions]
[MEMORY: Learned: User wants todos auto-continued without prompting]

Guidelines for memories:
- Only save important, long-lived information
- Avoid saving temporary data or ephemeral facts
- Be concise but specific (10-100 words typically)
- One memory per bracket pair

You can include multiple [MEMORY: ...] blocks if needed.

If a memory shown to you above (marked with an [id:...] tag) is stale or
wrong, correct or retire it instead of just ignoring it:
[MEMORY_UPDATE: <id> | <corrected content>]
[MEMORY_FORGET: <id>]

Only use ids that were actually shown to you in the Stored Memories section
above; an id you made up or recall from earlier in the conversation will be
ignored.
";
    }

    public Task<List<Memory>> SelectMemoriesForInjectionAsync(List<Memory> memories, int tokenBudget = 500)
    {
        if (memories.Count == 0)
            return Task.FromResult(new List<Memory>());

        // Rough estimation: ~4 chars per token
        var charBudget = tokenBudget * 4;
        var selected = new List<Memory>();
        var usedChars = 0;

        // Prioritize: pinned first, then how relevant this memory actually
        // was to the query (the RelevanceScore IMemoryStore.SearchAsync
        // computes - hybrid FTS+embedding when available, rank-based
        // otherwise) blended with the memory's own importance; recency is
        // only the final tiebreaker, not the primary driver it used to be.
        var sorted = memories
            .OrderByDescending(m => m.IsPinned)
            .ThenByDescending(EffectiveScore)
            .ThenByDescending(m => m.UpdatedAt)
            .ToList();

        foreach (var memory in sorted)
        {
            // Rough size: category + content + formatting
            var memorySize = memory.Category.Length + memory.Content.Length + 50;

            if (usedChars + memorySize <= charBudget)
            {
                selected.Add(memory);
                usedChars += memorySize;
            }

            if (usedChars >= charBudget)
                break;
        }

        return Task.FromResult(selected);
    }

    /// <summary>
    /// Blends search relevance with the memory's DECAYED importance (r16
    /// 02-memory-integrity.md 2.5) - using raw ImportanceScore here let a
    /// memory one day from stale-archival still outrank a fresh one at
    /// injection time, since the archiver already applies
    /// <see cref="MemoryLifecycle.ComputeEffectiveImportance"/> but this
    /// selection step did not. Falls back to effective importance alone for
    /// memories not retrieved via search. Pinned rows are unaffected (decay
    /// exempts them) and still sort first via the IsPinned ordering above.
    /// </summary>
    private static double EffectiveScore(Memory memory) =>
        memory.RelevanceScore is { } relevance
            ? (0.7 * relevance) + (0.3 * MemoryLifecycle.ComputeEffectiveImportance(memory))
            : MemoryLifecycle.ComputeEffectiveImportance(memory);

    private static string FormatCategoryName(string category) =>
        category switch
        {
            "facts" => "📌 Facts",
            "preferences" => "❤️ Preferences",
            "learned_behaviors" => "🧠 Learned Behaviors",
            "interests" => "✨ Interests",
            _ => category
        };
}
