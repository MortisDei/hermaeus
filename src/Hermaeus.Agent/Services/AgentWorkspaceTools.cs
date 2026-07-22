using System.Text;
using System.Text.RegularExpressions;
using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

public sealed class AgentWorkspaceTools : IAgentWorkspaceTools
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "dist"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".props", ".targets", ".xaml", ".axaml",
        ".md", ".txt", ".json", ".xml", ".yml", ".yaml", ".sh", ".ps1",
        ".css", ".html", ".js", ".ts", ".sql", ".toml", ".ini", ".gitignore"
    };

    public IReadOnlyList<string> ListFiles(AgentWorkspaceOptions options, string? subdirectory = null, int? maxDepth = null)
    {
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var scope = string.IsNullOrWhiteSpace(subdirectory) ? root : ResolveSafePath(root, subdirectory);
        var depth = maxDepth is > 0 ? maxDepth.Value : int.MaxValue;
        return EnumerateSafeFiles(scope, options.MaxFileBytes)
            .Where(path => PathDepthBelow(scope, path) <= depth)
            .Take(options.MaxSearchResults)
            .Select(path => ToRelative(root, path))
            .ToList();
    }

    public IReadOnlyList<AgentFileSearchResult> SearchFiles(AgentWorkspaceOptions options, string query, bool regex = false, int contextLines = 0)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        Regex? pattern = null;
        if (regex)
        {
            try { pattern = new Regex(query, RegexOptions.None, TimeSpan.FromMilliseconds(500)); }
            catch (ArgumentException ex) { throw new InvalidOperationException($"Invalid search regex: {ex.Message}"); }
        }

        var boundedContext = Math.Clamp(contextLines, 0, 10);
        var results = new List<AgentFileSearchResult>();
        foreach (var file in EnumerateSafeFiles(root, options.MaxFileBytes))
        {
            if (results.Count >= options.MaxSearchResults) break;
            var relative = ToRelative(root, file);
            var nameMatch = !regex && relative.Contains(query, StringComparison.OrdinalIgnoreCase);
            var snippet = string.Empty;
            if (!nameMatch)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch { continue; }

                var matchIndex = -1;
                for (var i = 0; i < lines.Length; i++)
                {
                    var isMatch = pattern is not null
                        ? pattern.IsMatch(lines[i])
                        : lines[i].Contains(query, StringComparison.OrdinalIgnoreCase);
                    if (!isMatch) continue;
                    matchIndex = i;
                    break;
                }

                if (matchIndex < 0) continue;

                if (boundedContext == 0)
                {
                    snippet = lines[matchIndex];
                }
                else
                {
                    var start = Math.Max(0, matchIndex - boundedContext);
                    var end = Math.Min(lines.Length - 1, matchIndex + boundedContext);
                    snippet = string.Join('\n', lines[start..(end + 1)]);
                }
            }

            results.Add(new AgentFileSearchResult(
                relative,
                boundedContext == 0 ? CompactSnippet(string.IsNullOrWhiteSpace(snippet) ? relative : snippet) : snippet,
                File.GetLastWriteTimeUtc(file)));
        }

        return results;
    }

    public IReadOnlyList<string> GlobFiles(AgentWorkspaceOptions options, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return [];
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var regex = GlobToRegex(pattern);
        return EnumerateSafeFiles(root, options.MaxFileBytes)
            .Select(path => ToRelative(root, path))
            .Where(relative => regex.IsMatch(relative))
            .Take(options.MaxSearchResults)
            .ToList();
    }

    public AgentFileReadResult ReadFile(AgentWorkspaceOptions options, string relativePath, int? lineOffset = null, int? lineLimit = null)
    {
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var full = ResolveSafePath(root, relativePath);
        if (!File.Exists(full))
            throw new FileNotFoundException("Agent file read target does not exist.", relativePath);

        var info = new FileInfo(full);
        if (!IsSafeTextFile(info, options.MaxFileBytes))
            throw new InvalidOperationException("Agent file read target is ignored, too large, or not a supported text file.");

        if (lineOffset is not null || lineLimit is not null)
            return ReadFileByLines(root, full, Math.Max(lineOffset ?? 0, 0), lineLimit is > 0 ? lineLimit.Value : int.MaxValue);

        using var fs = File.OpenRead(full);
        var max = Math.Max(1024, options.MaxFileBytes);
        var buffer = new byte[Math.Min(max, (int)Math.Min(info.Length, int.MaxValue))];
        var read = fs.Read(buffer, 0, buffer.Length);
        var truncated = fs.Position < fs.Length;
        var content = Encoding.UTF8.GetString(buffer, 0, read);
        if (content.Contains('\0'))
            throw new InvalidOperationException("Agent file read target appears to be binary.");

        return new AgentFileReadResult(ToRelative(root, full), content, truncated);
    }

    private static AgentFileReadResult ReadFileByLines(string root, string full, int lineOffset, int lineLimit)
    {
        var allLines = File.ReadAllLines(full);
        if (allLines.Any(l => l.Contains('\0')))
            throw new InvalidOperationException("Agent file read target appears to be binary.");

        var slice = allLines.Skip(lineOffset).Take(lineLimit).ToList();
        var truncated = lineOffset + slice.Count < allLines.Length;
        return new AgentFileReadResult(ToRelative(root, full), string.Join('\n', slice), truncated);
    }

    public AgentFileSummaryResult SummarizeFile(AgentWorkspaceOptions options, string relativePath)
    {
        var read = ReadFile(options, relativePath);
        var lines = read.Content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .Take(8)
            .ToList();
        var summary = lines.Count == 0
            ? "No readable text found."
            : CompactSnippet(string.Join(" ", lines));
        return new AgentFileSummaryResult(read.RelativePath, summary, read.Truncated);
    }

    public async Task<AgentFileReadResult> ApplyDraftPatchAsync(AgentWorkspaceOptions options, string relativePath, string proposedContent, CancellationToken ct = default)
    {
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var full = ResolveSafePath(root, relativePath);
        var content = proposedContent.Replace("\r\n", "\n").Replace('\r', '\n');

        // This is the one write path that touches files the user did not ask
        // Hermaeus to back up (their own source code), so it gets the same
        // temp-plus-move discipline as settings/secrets writes rather than a
        // plain WriteAllText that a crash mid-write could truncate
        // (docs/review/01-code-audit.md P2-5).
        await AtomicFileWriter.WriteAllTextAsync(full, content, ct);
        return new AgentFileReadResult(ToRelative(root, full), content, false);
    }

    public string DraftPatch(string relativePath, string rationale, string proposedContent)
    {
        var path = relativePath.Replace('\\', '/').Trim();
        return $"Draft patch for {path}\n\nRationale:\n{rationale.Trim()}\n\nProposed content:\n{proposedContent.Trim()}\n";
    }

    public async Task<AgentFileReadResult> EditFileAsync(AgentWorkspaceOptions options, string relativePath, string oldString, string newString, CancellationToken ct = default)
    {
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var full = ResolveSafePath(root, relativePath);
        if (!File.Exists(full))
            throw new FileNotFoundException("Agent edit target does not exist; use create_file for new files.", relativePath);
        if (string.IsNullOrEmpty(oldString))
            throw new InvalidOperationException("edit_file requires a non-empty old_string to match against the file's current content.");

        var content = await File.ReadAllTextAsync(full, ct);
        var occurrences = CountOccurrences(content, oldString);
        if (occurrences == 0)
            throw new InvalidOperationException("edit_file's old_string was not found in the target file; it may be stale, re-read the file first.");
        if (occurrences > 1)
            throw new InvalidOperationException($"edit_file's old_string matched {occurrences} times; it must match exactly once. Include more surrounding context to disambiguate.");

        var index = content.IndexOf(oldString, StringComparison.Ordinal);
        var updated = string.Concat(content.AsSpan(0, index), newString, content.AsSpan(index + oldString.Length));
        await AtomicFileWriter.WriteAllTextAsync(full, updated, ct);
        return new AgentFileReadResult(ToRelative(root, full), updated, false);
    }

    public async Task<AgentFileReadResult> CreateFileAsync(AgentWorkspaceOptions options, string relativePath, string content, CancellationToken ct = default)
    {
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var full = ResolveSafePath(root, relativePath);
        if (File.Exists(full))
            throw new InvalidOperationException("create_file refuses to overwrite an existing file; use edit_file instead.");

        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await AtomicFileWriter.WriteAllTextAsync(full, content, ct);
        return new AgentFileReadResult(ToRelative(root, full), content, false);
    }

    public async Task<string?> ReadFileForRevertAsync(AgentWorkspaceOptions options, string relativePath, CancellationToken ct = default)
    {
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var full = ResolveSafePath(root, relativePath);
        return File.Exists(full) ? await File.ReadAllTextAsync(full, ct) : null;
    }

    public async Task<AgentRevertResult> RevertAppliedPatchAsync(
        AgentWorkspaceOptions options, string relativePath, string? preImageContent, string expectedCurrentContent, CancellationToken ct = default)
    {
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var full = ResolveSafePath(root, relativePath);
        var currentContent = File.Exists(full) ? await File.ReadAllTextAsync(full, ct) : null;
        if (currentContent != expectedCurrentContent)
        {
            return new AgentRevertResult(
                Reverted: false,
                "The file changed again after this patch was applied; revert refused to avoid overwriting the newer content.");
        }

        if (preImageContent is null)
        {
            if (File.Exists(full))
                File.Delete(full);
        }
        else
        {
            await AtomicFileWriter.WriteAllTextAsync(full, preImageContent, ct);
        }

        return new AgentRevertResult(Reverted: true, Message: string.Empty);
    }

    private static int CountOccurrences(string content, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static Regex GlobToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        var normalized = pattern.Replace('\\', '/');
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (c == '*')
            {
                if (i + 1 < normalized.Length && normalized[i + 1] == '*')
                {
                    // "**/" matches zero or more whole path segments (so
                    // "src/**/*.cs" also matches "src/Foo.cs" directly, not
                    // just files at least one directory deeper); a bare "**"
                    // with no following separator matches anything.
                    if (i + 2 < normalized.Length && normalized[i + 2] == '/')
                    {
                        sb.Append("(?:.*/)?");
                        i += 2;
                    }
                    else
                    {
                        sb.Append(".*");
                        i++;
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
    }

    private static int PathDepthBelow(string scope, string fullPath)
    {
        var relative = Path.GetRelativePath(scope, fullPath).Replace('\\', '/');
        return relative.Count(c => c == '/') + 1;
    }

    public static string ResolveWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidOperationException("Workspace root is required.");

        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Workspace root does not exist: {root}");

        return root;
    }

    public static string ResolveSafePath(string workspaceRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("Agent paths must be relative to the workspace root.");

        var full = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
        var rootWithSep = workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? workspaceRoot
            : workspaceRoot + Path.DirectorySeparatorChar;
        var comparison = PathComparison;
        if (!full.StartsWith(rootWithSep, comparison)
            && !string.Equals(full, workspaceRoot, comparison))
        {
            throw new InvalidOperationException("Agent path escapes the workspace root.");
        }

        if (full.Split(Path.DirectorySeparatorChar).Any(part => IgnoredDirectories.Contains(part)))
            throw new InvalidOperationException("Agent path is inside an ignored directory.");

        if (PathHasSymlinkAncestor(workspaceRoot, full))
            throw new InvalidOperationException("Agent paths cannot reference symbolic links.");

        return full;
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root, int maxFileBytes)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> childDirs;
            try { childDirs = Directory.EnumerateDirectories(dir); }
            catch { continue; }

            foreach (var child in childDirs)
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)) && !IsSymlink(child))
                    pending.Push(child);
            }

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { continue; }

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                if (IsSafeTextFile(info, maxFileBytes))
                    yield return file;
            }
        }
    }

    private static bool IsSafeTextFile(FileInfo info, int maxFileBytes)
    {
        if (!info.Exists || info.Length > maxFileBytes) return false;
        if (info.DirectoryName?.Split(Path.DirectorySeparatorChar).Any(part => IgnoredDirectories.Contains(part)) == true)
            return false;
        if (IsSymlink(info.FullName))
            return false;
        return TextExtensions.Contains(info.Extension) || TextExtensions.Contains(info.Name);
    }

    private static bool PathHasSymlinkAncestor(string root, string fullPath)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(fullPath);
        var directory = Path.GetDirectoryName(current);
        var comparison = PathComparison;
        while (!string.IsNullOrWhiteSpace(directory)
               && directory.StartsWith(rootFull, comparison))
        {
            if (IsSymlink(directory))
                return true;
            if (string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), rootFull, comparison))
                break;
            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static string ToRelative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string CompactSnippet(string text)
    {
        var flat = string.Join(' ', text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > 240 ? flat[..237] + "..." : flat;
    }
}
