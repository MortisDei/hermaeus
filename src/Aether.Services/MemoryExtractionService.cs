using System.Text.RegularExpressions;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// Extracts and categorizes memories from model output.
/// </summary>
public sealed class MemoryExtractionService
{
    // Pattern to match [MEMORY: content] markers (case-insensitive)
    private static readonly Regex MemoryMarkerRegex = new(
        @"\[MEMORY:\s*(.+?)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500));

    // Keywords to help categorize memories
    private static readonly string[] PreferenceKeywords = ["prefer", "like", "enjoy", "want", "prefer doing"];
    private static readonly string[] BehaviourKeywords = ["learn", "learned", "understand", "realize", "noticed", "remember"];
    private static readonly string[] InterestKeywords = ["interested", "curious", "fascinated", "explore"];

    public async Task<List<Memory>> ExtractMemoriesAsync(string modelOutput, string? sourceConversationId = null)
    {
        return await Task.Run(() =>
        {
            var memories = new List<Memory>();
            var matches = MemoryMarkerRegex.Matches(modelOutput);

            foreach (Match match in matches)
            {
                if (match.Groups.Count < 2) continue;

                var content = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;

                var category = CategorizeMemory(content);
                var importance = CalculateImportance(content);

                var memory = new Memory
                {
                    Id = Guid.NewGuid().ToString(),
                    Category = category,
                    Content = content,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    SourceConversationId = sourceConversationId,
                    Source = sourceConversationId is null
                        ? null
                        : new SourceReference(ProvenanceKind.Memory, TitleFrom(content), Locator: sourceConversationId, Snippet: content, Timestamp: DateTime.UtcNow),
                    ImportanceScore = importance,
                    Tags = ExtractTags(content)
                };

                memories.Add(memory);
            }

            return memories;
        });
    }

    public string CleanMemoryMarkers(string modelOutput)
    {
        // Remove all [MEMORY: ...] blocks
        return MemoryMarkerRegex.Replace(modelOutput, string.Empty).Trim();
    }

    /// <summary>
    /// Categorize memory based on content keywords.
    /// </summary>
    private static string CategorizeMemory(string content)
    {
        var lower = content.ToLowerInvariant();

        if (PreferenceKeywords.Any(kw => lower.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            return "preferences";

        if (BehaviourKeywords.Any(kw => lower.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            return "learned_behaviors";

        if (InterestKeywords.Any(kw => lower.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            return "interests";

        return "facts";
    }

    /// <summary>
    /// Calculate importance score (0-1) based on content characteristics.
    /// </summary>
    private static double CalculateImportance(string content)
    {
        var score = 0.5; // Base score

        // Boost for longer, more specific memories
        if (content.Length > 100) score += 0.2;
        else if (content.Length < 20) score -= 0.1;

        // Boost for personal preferences/learned behaviors
        if (content.Contains("prefer", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("remember", StringComparison.OrdinalIgnoreCase))
            score += 0.15;

        return Math.Min(1.0, Math.Max(0.0, score));
    }

    /// <summary>
    /// Extract potential tags from memory content.
    /// </summary>
    private static List<string> ExtractTags(string content)
    {
        var tags = new List<string>();

        // Simple keyword-based tagging
        if (content.Contains("prefer", StringComparison.OrdinalIgnoreCase))
            tags.Add("preference");
        if (content.Contains("learn", StringComparison.OrdinalIgnoreCase))
            tags.Add("learning");
        if (content.Contains("bug", StringComparison.OrdinalIgnoreCase) || content.Contains("error", StringComparison.OrdinalIgnoreCase))
            tags.Add("issue");
        if (content.Contains("performance", StringComparison.OrdinalIgnoreCase) || content.Contains("optimization", StringComparison.OrdinalIgnoreCase))
            tags.Add("performance");

        return tags;
    }

    /// <summary>Short, scannable label for a memory's source reference chip.</summary>
    internal static string TitleFrom(string content)
    {
        var flat = content.Trim();
        return flat.Length > 48 ? flat[..45] + "..." : flat;
    }
}
