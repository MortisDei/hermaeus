namespace Aether.Services.ProcessManagement;

internal static class ExtraArgsParser
{
    public static IEnumerable<string> Split(string extraArgs)
    {
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in extraArgs.Trim())
        {
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
