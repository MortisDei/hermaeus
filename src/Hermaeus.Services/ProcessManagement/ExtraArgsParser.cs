namespace Hermaeus.Services.ProcessManagement;

public static class ExtraArgsParser
{
    /// <summary>
    /// Splits a shell-like extra-args string into tokens, honoring double
    /// quotes for tokens containing whitespace. A backslash only escapes an
    /// immediately following quote or backslash (r11 1.4: quoting rules,
    /// documented in the Services settings tooltip) - every other backslash,
    /// including the common case of a bare or quoted Windows path like
    /// <c>C:\models\proj.gguf</c>, is a literal character. Previously every
    /// backslash consumed the next character unconditionally, silently
    /// corrupting Windows paths (<c>--mmproj C:\models\proj.gguf</c> became
    /// <c>--mmproj C:modelsproj.gguf</c>) before they reached llama-server.
    /// </summary>
    public static IEnumerable<string> Split(string extraArgs)
    {
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var trimmed = extraArgs.Trim();

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];

            if (ch == '\\' && i + 1 < trimmed.Length && (trimmed[i + 1] == '"' || trimmed[i + 1] == '\\'))
            {
                current.Append(trimmed[i + 1]);
                i++;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length == 0) continue;
                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }
}
