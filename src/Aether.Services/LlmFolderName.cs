namespace Aether.Services;

/// <summary>
/// r19 2.4: the flattened local-model folder is named "llm" (previously
/// "LLM"). Windows is case-insensitive so an existing "LLM" folder keeps
/// working there regardless, but a case-sensitive filesystem (Linux) would
/// otherwise silently grow a second, empty "llm" folder beside an existing
/// "LLM" one. Both call sites that plan a destination under it probe for
/// either casing first and reuse whichever already exists.
/// </summary>
internal static class LlmFolderName
{
    public const string Preferred = "llm";

    public static string Resolve(string modelsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(modelsDirectory) && Directory.Exists(modelsDirectory))
        {
            foreach (var dir in Directory.EnumerateDirectories(modelsDirectory))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, Preferred, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
        }
        return Preferred;
    }
}
