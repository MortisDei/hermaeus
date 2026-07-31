using System.Security.Cryptography;
using System.Text;

namespace Hermaeus.Services;

/// <summary>
/// r27 04-models-arrive-complete.md 4.2: turns a repository id into the single
/// folder segment its files live in, under <c>&lt;models&gt;/llm/</c>.
/// Per-model folders are not tidiness here. Projector discovery is a sibling
/// directory scan (<c>ServicesViewModel</c> enumerates <c>mmproj-*.gguf</c> next
/// to the model), so in a flat folder every model is offered every other model's
/// projector, and companion filenames collide outright: the owner has seven
/// files named <c>mmproj-F16.gguf</c> and a flat folder can hold one.
/// Repository ids are user-supplied through a text field and arrive from a
/// remote service, so this is a security-relevant path (CLAUDE.md, path
/// traversal and symlink rejection).
/// </summary>
public static class ModelRepoFolder
{
    /// <summary>Long enough to make an accidental collision implausible, short enough to read.</summary>
    private const int DisambiguatorHexChars = 8;

    private static readonly char[] Invalid = [.. Path.GetInvalidFileNameChars(), ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// The folder segment for a repository id. Stable (the same id always
    /// resolves to the same folder, so a later companion download lands beside
    /// its model) and collision-handled (two ids that sanitise to the same
    /// readable name get distinct folders via a hash of the original id).
    /// </summary>
    public static string Resolve(string repoId)
    {
        var trimmed = (repoId ?? string.Empty).Trim().Trim('/', '\\');
        if (trimmed.Length == 0)
            return "unknown-model";

        // The readable form: one segment, "/" rendered as "__" the way the HF
        // hub cache renders it as "--".
        var canonical = trimmed.Replace('\\', '/').Replace("/", "__");
        var sanitised = Sanitise(canonical);

        // If sanitising changed anything, the readable name is no longer a
        // faithful rendering of the id, so two different ids could reach it.
        // A hash of the original keeps them apart rather than merging them.
        if (!string.Equals(sanitised, canonical, StringComparison.Ordinal))
            sanitised = $"{sanitised}-{ShortHash(trimmed)}";

        return sanitised.Length == 0 ? $"model-{ShortHash(trimmed)}" : sanitised;
    }

    /// <summary>
    /// Resolves the full destination folder and proves it stays inside
    /// <paramref name="root"/>. Returns false rather than throwing, because the
    /// caller is a download button and needs a message, not an exception.
    /// </summary>
    public static bool TryResolvePath(string root, string repoId, out string folderPath, out string error)
    {
        folderPath = string.Empty;
        var segment = Resolve(repoId);

        // A defence in depth check rather than the only one: Resolve already
        // reduces the id to a single sanitised segment with no separators.
        if (segment.Split(['/', '\\']).Any(part => part is ".." or "." or ""))
        {
            error = $"'{repoId}' does not resolve to a usable folder name.";
            return false;
        }

        string fullRoot;
        string candidate;
        try
        {
            fullRoot = Path.GetFullPath(root);
            candidate = Path.GetFullPath(Path.Combine(fullRoot, segment));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"'{repoId}' does not resolve to a valid path.";
            return false;
        }

        var rootWithSeparator = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            error = $"'{repoId}' would place files outside the models folder.";
            return false;
        }

        folderPath = candidate;
        error = string.Empty;
        return true;
    }

    private static string Sanitise(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
            builder.Append(Invalid.Contains(c) || char.IsControl(c) ? '_' : c);

        // Trailing dots and spaces are silently stripped by Windows, which would
        // make two different names resolve to the same directory.
        return builder.ToString().Trim().TrimEnd('.', ' ');
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes)[..DisambiguatorHexChars];
    }
}
