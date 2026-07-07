using System.Text;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

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
                if (memory.IsPinned)
                    sb.AppendLine($"- ⭐ {memory.Content}");
                else
                    sb.AppendLine($"- {memory.Content}");
            }
            sb.AppendLine();
        }

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
";
    }

    public async Task<List<Memory>> SelectMemoriesForInjectionAsync(List<Memory> memories, int tokenBudget = 500)
    {
        return await Task.Run(() =>
        {
            if (memories.Count == 0)
                return [];

            // Rough estimation: ~4 chars per token
            var charBudget = tokenBudget * 4;
            var selected = new List<Memory>();
            var usedChars = 0;

            // Prioritize: pinned first, then by importance score, then by recency
            var sorted = memories
                .OrderByDescending(m => m.IsPinned)
                .ThenByDescending(m => m.ImportanceScore)
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

            return selected;
        });
    }

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
