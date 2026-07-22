using System.Text.RegularExpressions;

namespace Hermaeus.Services;

/// <summary>
/// Pure helpers for the "Get models" HF browser (r13 03-hugging-face.md 3.4): which tree
/// entries are multi-part GGUF sets (hidden this round rather than half-supported), and where
/// a selected file would land using the doc 02 flat-folder convention.
/// </summary>
public static class HuggingFaceBrowserSupport
{
    private static readonly Regex MultiPartRegex = new(@"-\d{5}-of-\d{5}\.gguf$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsMultiPartGguf(string fileName) => MultiPartRegex.IsMatch(fileName);

    /// <summary>Destination is always &lt;ModelsDirectory&gt;\LLM\&lt;filename&gt; (the 2.6
    /// convention), keyed on the bare filename only - the repo's internal folder structure is
    /// discarded. Collides when a file already sits at that destination.</summary>
    public static (string DestinationPath, bool Collides) PlanDestination(string modelsDirectory, string repoFilePath)
    {
        var fileName = Path.GetFileName(repoFilePath);
        var destination = Path.Combine(modelsDirectory, LlmFolderName.Resolve(modelsDirectory), fileName);
        return (destination, File.Exists(destination));
    }
}
