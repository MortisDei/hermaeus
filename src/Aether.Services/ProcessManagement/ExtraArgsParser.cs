namespace Aether.Services.ProcessManagement;

public static class ExtraArgsParser
{
    public static IEnumerable<string> Split(string extraArgs)
    {
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var escaping = false;

        foreach (var ch in extraArgs.Trim())
        {
            if (escaping)
            {
                current.Append(ch);
                escaping = false;
                continue;
            }

            if (ch == '\\')
            {
                escaping = true;
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

        if (escaping)
            current.Append('\\');

        if (current.Length > 0)
            yield return current.ToString();
    }
}
