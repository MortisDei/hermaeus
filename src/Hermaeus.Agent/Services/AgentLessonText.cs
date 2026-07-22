namespace Hermaeus.Agent.Services;

/// <summary>
/// Deterministic goal/lesson text tokenization shared by task-terminal
/// lesson signatures (<see cref="AgentService"/>) and lesson relevance
/// scoring (<see cref="AgentContextBuilder"/>), so both use the exact same
/// notion of "shared terms" (docs/review/02-lessons-v2.md L4, L5). No LLM
/// involved anywhere in this file.
/// </summary>
internal static class AgentLessonText
{
    private static readonly char[] TokenSeparators =
        [' ', '\t', '\r', '\n', '.', ',', ':', ';', '/', '\\', '-', '_', '(', ')', '[', ']', '{', '}', '"', '\'', '!', '?', '@', '#', '$', '%', '^', '&', '*', '+', '=', '<', '>', '|', '~', '`'];

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "your",
        "have", "has", "are", "was", "were", "will", "shall", "should", "could",
        "would", "not", "but", "all", "any", "can", "its", "who", "what",
        "when", "where", "how", "why", "then", "than", "them", "they", "you"
    };

    /// <summary>Lowercased, alphanumeric-run tokens with stopwords and sub-3-char tokens dropped, distinct, original order preserved.</summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return text
            .ToLowerInvariant()
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !Stopwords.Contains(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Deterministic 16-hex-char fingerprint of a goal's first 8 sorted
    /// tokens. Reworded goals fingerprint differently; that false-negative
    /// is accepted in exchange for never false-matching two unrelated
    /// goals.
    /// </summary>
    public static string Fingerprint(string goal)
    {
        var tokens = Tokenize(goal).OrderBy(t => t, StringComparer.Ordinal).Take(8);
        var joined = string.Join('|', tokens);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
