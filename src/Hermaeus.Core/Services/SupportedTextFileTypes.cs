namespace Hermaeus.Core.Services;

/// <summary>
/// Common text and source file types that Hermaeus may read as bounded text.
/// This is an extension policy, not a content detector. Callers still apply
/// their own size, binary, path, and extraction rules.
/// </summary>
public static class SupportedTextFileTypes
{
    public static IReadOnlySet<string> Extensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".adoc", ".axaml", ".c", ".cfg", ".conf", ".config", ".cpp", ".cs", ".csproj",
        ".css", ".csv", ".fs", ".go", ".gradle",
        ".h", ".hpp", ".htm", ".html", ".ini", ".java", ".js", ".json", ".json5", ".jsonl",
        ".jsx", ".kt", ".kts", ".log", ".lua", ".markdown", ".md", ".mdown", ".mdx", ".mkdn",
        ".php", ".ps1", ".props", ".properties", ".py", ".pyproject", ".razor", ".rb", ".rs",
        ".rst", ".scss", ".sh", ".sln", ".sql", ".svg", ".swift", ".targets", ".tex", ".toml",
        ".ts", ".tsv", ".tsx", ".txt", ".vb", ".xaml", ".xml", ".yaml", ".yml"
    };

    public static IReadOnlySet<string> FileNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".dockerignore", ".gitattributes", ".gitignore", "CMakeLists.txt", "Dockerfile", "Makefile",
        "README", "README.md", "LICENSE", "NOTICE"
    };

    public static bool IsSupported(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fileName = Path.GetFileName(path);
        return FileNames.Contains(fileName) || Extensions.Contains(Path.GetExtension(fileName));
    }

    public static IReadOnlyList<string> PickerPatterns =>
        Extensions.OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .Select(extension => $"*{extension}")
            .Concat(FileNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
}
