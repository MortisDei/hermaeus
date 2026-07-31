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

    /// <summary>
    /// r27 04-models-arrive-complete.md 4.2: the destination is
    /// <c>&lt;models&gt;/llm/&lt;repo folder&gt;/&lt;filename&gt;</c>.
    /// It used to discard the repository entirely and write to
    /// <c>&lt;models&gt;/llm/&lt;filename&gt;</c>, which is why the owner's seven files named
    /// <c>mmproj-F16.gguf</c> cannot coexist, and why the sibling-directory projector scan
    /// offers every model every other model's projector.
    /// Any subdirectory structure inside the repository is flattened into the model folder, so
    /// <c>MTP/mtp-gemma-4-E4B-it.gguf</c> becomes <c>&lt;model folder&gt;/mtp-gemma-4-E4B-it.gguf</c>
    /// and that same sibling scan finds it without changing a line of it.
    /// Falls back to the flat destination when the repo id cannot be resolved to a
    /// safe folder, rather than writing somewhere unexpected.
    /// </summary>
    public static (string DestinationPath, bool Collides) PlanDestination(string modelsDirectory, string repoFilePath, string repoId = "")
    {
        var fileName = Path.GetFileName(repoFilePath.Replace('\\', '/'));
        var llmRoot = Path.Combine(modelsDirectory, LlmFolderName.Resolve(modelsDirectory));

        var destination = !string.IsNullOrWhiteSpace(repoId)
            && ModelRepoFolder.TryResolvePath(llmRoot, repoId, out var modelFolder, out _)
                ? Path.Combine(modelFolder, fileName)
                : Path.Combine(llmRoot, fileName);

        return (destination, File.Exists(destination));
    }
}
