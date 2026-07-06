namespace Aether.Core.Services;

/// <summary>
/// One candidate piece of model context with provenance.
/// <see cref="Tokens"/> may be a real token count (RAG chunks) or left null to estimate.
/// </summary>
public sealed record ContextPart(
    string Kind,
    string Title,
    string Content,
    string? GroupKey = null,
    int? Tokens = null,
    object? Data = null)
{
    public int EffectiveTokens => Tokens ?? ContextPackBuilder.EstimateTokens(Content);
    public bool Truncated { get; init; }
}

/// <summary>The parts that survived budget selection, with packing notes.</summary>
public sealed record PackedContext(
    IReadOnlyList<ContextPart> Parts,
    int TokensUsed,
    string Summary);

/// <summary>
/// Shared budget-aware context packer. Chat, RAG, and the agent all select
/// "what the model sees" through this one component instead of each owning
/// its own packing loop.
/// </summary>
public static class ContextPackBuilder
{
    /// <summary>Rough token estimate (~4 chars per token), shared by all consumers.</summary>
    public static int EstimateTokens(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));

    /// <summary>
    /// Select parts in the given priority order until the token budget, part count,
    /// or per-group cap is hit. The last part may be truncated to fit; if nothing
    /// fits, the first candidate is truncated as a fallback so consumers never end
    /// up with an empty pack when candidates exist.
    /// </summary>
    public static PackedContext Pack(
        IReadOnlyList<ContextPart> candidates,
        int tokenBudget,
        int maxParts = int.MaxValue,
        int maxPerGroup = int.MaxValue)
    {
        var budget = Math.Max(tokenBudget, 128);
        var partLimit = Math.Max(maxParts, 1);
        var groupLimit = Math.Max(maxPerGroup, 1);

        var selected = new List<ContextPart>();
        var usedTokens = 0;
        var groupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();

        foreach (var part in candidates)
        {
            if (selected.Count >= partLimit)
            {
                notes.Add($"part limit {partLimit} reached");
                break;
            }

            var groupKey = part.GroupKey ?? string.Empty;
            groupCounts.TryGetValue(groupKey, out var groupCount);
            if (groupKey.Length > 0 && groupCount >= groupLimit)
                continue;

            var tokens = Math.Max(part.EffectiveTokens, 1);
            var remaining = budget - usedTokens;
            if (remaining <= 0)
            {
                notes.Add($"budget {budget} tokens exhausted");
                break;
            }

            if (tokens > remaining)
            {
                var truncated = TruncateContent(part.Content, remaining * 4);
                if (string.IsNullOrWhiteSpace(truncated))
                    continue;

                selected.Add(part with { Content = truncated, Tokens = remaining, Truncated = true });
                usedTokens = budget;
                notes.Add($"truncated {part.Title} to fit budget");
                break;
            }

            selected.Add(part);
            usedTokens += tokens;
            groupCounts[groupKey] = groupCount + 1;
        }

        if (selected.Count == 0 && candidates.Count > 0)
        {
            var fallback = candidates[0];
            var snippet = TruncateContent(fallback.Content, Math.Max(budget * 4, 512));
            selected.Add(fallback with { Content = snippet, Truncated = true });
            usedTokens = Math.Min(budget, Math.Max(fallback.EffectiveTokens, 1));
            notes.Add("fallback part used because budget selection was empty");
        }

        return new PackedContext(selected, usedTokens, string.Join("; ", notes));
    }

    private static string TruncateContent(string content, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(content) || maxChars <= 0)
            return string.Empty;

        return content.Length <= maxChars
            ? content.Trim()
            : content[..maxChars].TrimEnd() + "...";
    }
}
