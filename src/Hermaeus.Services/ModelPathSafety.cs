namespace Hermaeus.Services;

/// <summary>Validates exact model files before download or destructive operations.</summary>
public static class ModelPathSafety
{
    /// <summary>
    /// Comparison for local filesystem paths. Windows paths are normally
    /// case-insensitive; Unix-like paths are kept case-sensitive so two files
    /// whose names differ only by case are not silently treated as one model.
    /// Other supported non-Windows platforms deliberately use the conservative
    /// case-sensitive rule rather than assuming a case-insensitive volume.
    /// </summary>
    public static StringComparison LocalPathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static StringComparer LocalPathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static bool AreSameLocalPath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim()),
                Path.GetFullPath(right.Trim()),
                LocalPathComparison);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    public static bool TryResolveFileUnderRoot(string root, string filePath, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(filePath))
        {
            error = "A model root and file path are required.";
            return false;
        }

        try
        {
            var fullRoot = Path.GetFullPath(root.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(filePath.Trim());
            var comparison = LocalPathComparison;
            var prefix = fullRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, comparison))
            {
                error = "The model path must remain inside the configured AI root.";
                return false;
            }

            var parent = Path.GetDirectoryName(fullPath);
            if (parent is null || !ValidateExistingPath(fullRoot, parent, comparison, out error))
                return false;

            if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                error = "Symbolic links and junctions are not accepted for model files.";
                return false;
            }

            normalized = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = "The model path could not be validated.";
            return false;
        }
    }

    private static bool ValidateExistingPath(string root, string path, StringComparison comparison, out string error)
    {
        error = string.Empty;
        var current = path;
        while (!string.Equals(current, root, comparison))
        {
            if (current.Length < root.Length || !current.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            {
                error = "The model path must remain inside the configured AI root.";
                return false;
            }

            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                error = "Symbolic links and junctions are not accepted in the model path.";
                return false;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null)
                break;
            current = parent;
        }

        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            error = "The AI root cannot be a symbolic link or junction.";
            return false;
        }

        return true;
    }
}
