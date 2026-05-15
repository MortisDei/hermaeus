using System.Text;
using Aether.Agent.Models;

namespace Aether.Agent.Services;

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

    public IReadOnlyList<string> ListFiles(AgentWorkspaceOptions options)
    {
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        return EnumerateSafeFiles(root, options.MaxFileBytes)
            .Take(options.MaxSearchResults)
            .Select(path => ToRelative(root, path))
            .ToList();
    }

    public IReadOnlyList<AgentFileSearchResult> SearchFiles(AgentWorkspaceOptions options, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var results = new List<AgentFileSearchResult>();
        foreach (var file in EnumerateSafeFiles(root, options.MaxFileBytes))
        {
            if (results.Count >= options.MaxSearchResults) break;
            var relative = ToRelative(root, file);
            var nameMatch = relative.Contains(query, StringComparison.OrdinalIgnoreCase);
            var snippet = string.Empty;
            if (!nameMatch)
            {
                try
                {
                    snippet = File.ReadLines(file)
                        .FirstOrDefault(line => line.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(snippet)) continue;
            }

            results.Add(new AgentFileSearchResult(
                relative,
                CompactSnippet(string.IsNullOrWhiteSpace(snippet) ? relative : snippet),
                File.GetLastWriteTimeUtc(file)));
        }

        return results;
    }

    public AgentFileReadResult ReadFile(AgentWorkspaceOptions options, string relativePath)
    {
        var root = ResolveWorkspaceRoot(options.WorkspaceRoot);
        var full = ResolveSafePath(root, relativePath);
        if (!File.Exists(full))
            throw new FileNotFoundException("Agent file read target does not exist.", relativePath);

        var info = new FileInfo(full);
        if (!IsSafeTextFile(info, options.MaxFileBytes))
            throw new InvalidOperationException("Agent file read target is ignored, too large, or not a supported text file.");

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

    public string DraftPatch(string relativePath, string rationale, string proposedContent)
    {
        var path = relativePath.Replace('\\', '/').Trim();
        return $"Draft patch for {path}\n\nRationale:\n{rationale.Trim()}\n\nProposed content:\n{proposedContent.Trim()}\n";
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
        if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Agent path escapes the workspace root.");
        }

        if (full.Split(Path.DirectorySeparatorChar).Any(part => IgnoredDirectories.Contains(part)))
            throw new InvalidOperationException("Agent path is inside an ignored directory.");

        // Reject symbolic links for security
        var info = new FileInfo(full);
        if (info.LinkTarget is not null)
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
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)))
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
        return TextExtensions.Contains(info.Extension) || TextExtensions.Contains(info.Name);
    }

    private static string ToRelative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static string CompactSnippet(string text)
    {
        var flat = string.Join(' ', text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > 240 ? flat[..237] + "..." : flat;
    }
}
