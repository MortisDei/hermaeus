namespace Hermaeus.Services.Recall;

/// <summary>Builds a bounded, query-term-centred excerpt for a Recall result row (doc 02 2.4).</summary>
internal static class RecallSnippet
{
    private const int MaxLength = 220;
    private const int Radius = 90;

    public static string Build(string body, string query)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var text = body.Length > 4000 ? body[..4000] : body;

        var terms = query
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2)
            .ToList();

        var index = -1;
        foreach (var term in terms)
        {
            index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) break;
        }

        if (index < 0)
            return text.Length <= MaxLength ? text : text[..MaxLength].TrimEnd() + "...";

        var start = Math.Max(0, index - Radius);
        var end = Math.Min(text.Length, index + Radius);
        var excerpt = text[start..end].Trim();
        if (start > 0) excerpt = "..." + excerpt;
        if (end < text.Length) excerpt += "...";
        return excerpt;
    }
}
