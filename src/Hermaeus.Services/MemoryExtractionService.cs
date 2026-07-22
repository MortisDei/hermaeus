using System.Text.Json;
using System.Text.RegularExpressions;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

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

    // [MEMORY_UPDATE: <id> | <new content>] and [MEMORY_FORGET: <id>] let the
    // model correct or retire a memory it was shown this turn (see
    // ConversationMemoryService.ApplyInjectedMemoryMarkersAsync, which only
    // honors ids that were actually injected into the current prompt).
    private static readonly Regex MemoryUpdateMarkerRegex = new(
        @"\[MEMORY_UPDATE:\s*([^\|\]]+?)\s*\|\s*(.+?)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500));
    private static readonly Regex MemoryForgetMarkerRegex = new(
        @"\[MEMORY_FORGET:\s*([^\]]+?)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500));

    // Keywords to help categorize memories
    private static readonly string[] PreferenceKeywords = ["prefer", "like", "enjoy", "want", "prefer doing"];
    private static readonly string[] BehaviourKeywords = ["learn", "learned", "understand", "realize", "noticed", "remember"];
    private static readonly string[] InterestKeywords = ["interested", "curious", "fascinated", "explore"];

    public Task<List<Memory>> ExtractMemoriesAsync(string modelOutput, string? sourceConversationId = null)
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

        return Task.FromResult(memories);
    }

    /// <summary>
    /// Parses a structured JSON extraction response
    /// (<c>{"memories": [{"content", "category", "importance", "tags": [...]}]}</c>)
    /// instead of the [MEMORY: ...] marker-with-heuristics path. Used by the
    /// auto-summary flow, which already spends one LLM call on extraction
    /// and gets model-supplied category/importance/tags for free by asking
    /// for JSON instead of prose. Tolerant of markdown code fences and
    /// leading/trailing prose around the JSON object; returns an empty list
    /// (never throws) if nothing parseable is found, so callers can fall
    /// back to <see cref="ExtractMemoriesAsync"/>.
    /// </summary>
    public Task<List<Memory>> ExtractStructuredMemoriesAsync(string modelOutput, string? sourceConversationId = null)
    {
        var memories = new List<Memory>();
        var json = ExtractJsonObject(modelOutput);
        if (json is null)
            return Task.FromResult(memories);

        StructuredExtractionResult? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<StructuredExtractionResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return Task.FromResult(memories);
        }

        foreach (var item in parsed?.Memories ?? [])
        {
            var content = item.Content?.Trim() ?? string.Empty;
            if (content.Length == 0) continue;

            var category = item.Category?.Trim().ToLowerInvariant() switch
            {
                "preferences" or "learned_behaviors" or "interests" or "facts" => item.Category!.Trim().ToLowerInvariant(),
                _ => "facts"
            };
            var importance = item.Importance is >= 0 and <= 1 ? item.Importance!.Value : 0.5;
            var tags = (item.Tags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

            memories.Add(new Memory
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
                Tags = tags
            });
        }

        return Task.FromResult(memories);
    }

    /// <summary>Brace-matching JSON object extraction, tolerant of markdown fences and surrounding prose (mirrors AgentService's JSON protocol parsing).</summary>
    private static string? ExtractJsonObject(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        var start = trimmed.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (escaped) { escaped = false; continue; }
            if (ch == '\\') { escaped = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    var candidate = trimmed[start..(i + 1)];
                    try
                    {
                        using var doc = JsonDocument.Parse(candidate);
                        return candidate;
                    }
                    catch (JsonException)
                    {
                        return null;
                    }
                }
            }
        }

        return null;
    }

    private sealed class StructuredExtractionResult
    {
        public List<StructuredMemoryItem>? Memories { get; set; }
    }

    private sealed class StructuredMemoryItem
    {
        public string? Content { get; set; }
        public string? Category { get; set; }
        public double? Importance { get; set; }
        public List<string>? Tags { get; set; }
    }

    public string CleanMemoryMarkers(string modelOutput)
    {
        // Remove all [MEMORY: ...] / [MEMORY_UPDATE: ...] / [MEMORY_FORGET: ...] blocks.
        var cleaned = MemoryMarkerRegex.Replace(modelOutput, string.Empty);
        cleaned = MemoryUpdateMarkerRegex.Replace(cleaned, string.Empty);
        cleaned = MemoryForgetMarkerRegex.Replace(cleaned, string.Empty);
        return cleaned.Trim();
    }

    /// <summary>Parses [MEMORY_UPDATE: id | new content] markers from model output.</summary>
    public IReadOnlyList<(string Id, string NewContent)> ExtractUpdateMarkers(string modelOutput)
    {
        var results = new List<(string, string)>();
        foreach (Match match in MemoryUpdateMarkerRegex.Matches(modelOutput))
        {
            if (match.Groups.Count < 3) continue;
            var id = match.Groups[1].Value.Trim();
            var content = match.Groups[2].Value.Trim();
            if (id.Length > 0 && content.Length > 0)
                results.Add((id, content));
        }

        return results;
    }

    /// <summary>Parses [MEMORY_FORGET: id] markers from model output.</summary>
    public IReadOnlyList<string> ExtractForgetMarkers(string modelOutput)
    {
        var results = new List<string>();
        foreach (Match match in MemoryForgetMarkerRegex.Matches(modelOutput))
        {
            if (match.Groups.Count < 2) continue;
            var id = match.Groups[1].Value.Trim();
            if (id.Length > 0)
                results.Add(id);
        }

        return results;
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
