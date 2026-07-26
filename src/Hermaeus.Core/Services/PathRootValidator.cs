namespace Hermaeus.Core.Services;

/// <summary>
/// One validator for every user-controlled folder-root input in the app
/// (project folder roots, r24 doc 01 1.1; watched-source roots, doc 03 3.1),
/// so traversal and symlink rejection cannot drift between the two the way
/// r23 called out for glob matching.
/// </summary>
public static class PathRootValidator
{
    public static bool TryValidate(string? input, out string normalized, out string error)
    {
        normalized = string.Empty;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            error = "A folder path is required.";
            return false;
        }

        if (trimmed.Split(['/', '\\']).Any(segment => segment is ".."))
        {
            error = "Path cannot contain '..' segments.";
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Path is not valid.";
            return false;
        }

        if (!Directory.Exists(full))
        {
            error = "Folder does not exist.";
            return false;
        }

        try
        {
            if (File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
            {
                error = "Path cannot be a symbolic link or junction.";
                return false;
            }
        }
        catch (IOException)
        {
            error = "Folder could not be read.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = "Folder could not be read.";
            return false;
        }

        normalized = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        error = string.Empty;
        return true;
    }
}
