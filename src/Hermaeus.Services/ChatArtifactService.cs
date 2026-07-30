using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed record ChatArtifact(string FileName, string FullPath, long SizeBytes, DateTime SavedAtUtc);

/// <summary>
/// r19 5.4: the other half of the attachments gap - a low-ceremony way for
/// chat output (code blocks today) to become a real file, without a full
/// chat-side tool loop. Every write lands under one fixed, per-conversation
/// sandbox folder; the caller supplies the filename, this only sanitizes it.
/// </summary>
public sealed class ChatArtifactService
{
    private const string MarkerFileName = ".conversation-id";

    private readonly ISettingsService _settings;

    public ChatArtifactService(ISettingsService settings) => _settings = settings;

    private string RootDirectory => Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "chat-artifacts");

    /// <summary>Finds this conversation's existing artifacts folder, if any, without creating
    /// one. Folders created by <see cref="GetOrCreateConversationDirectory"/> carry a hidden
    /// <c>.conversation-id</c> marker so lookup survives the folder being named after a chat
    /// title that later changed; a bare id-named folder (pre-title-naming data) is also
    /// recognized so existing artifacts aren't orphaned by this lookup change.</summary>
    private string? TryGetConversationDirectory(string conversationId)
    {
        if (!Directory.Exists(RootDirectory))
            return null;

        foreach (var dir in Directory.GetDirectories(RootDirectory))
        {
            var markerPath = Path.Combine(dir, MarkerFileName);
            if (File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == conversationId)
                return dir;
        }

        var legacyDir = Path.Combine(RootDirectory, SanitizeConversationId(conversationId));
        return Directory.Exists(legacyDir) ? legacyDir : null;
    }

    /// <summary>Backing folder name defaults to a sanitized, deduped conversation title (so
    /// browsing chat-artifacts in a file manager means something) rather than the raw
    /// conversation id; falls back to the id when no title is available. Stays put once
    /// created even if the conversation is later renamed - see <see cref="TryGetConversationDirectory"/>.</summary>
    private string GetOrCreateConversationDirectory(string conversationId, string? conversationTitle)
    {
        var existing = TryGetConversationDirectory(conversationId);
        if (existing is not null)
            return existing;

        Directory.CreateDirectory(RootDirectory);
        var baseName = SanitizeFolderName(conversationTitle) ?? SanitizeConversationId(conversationId);
        var dir = Path.Combine(RootDirectory, DedupeDirectoryName(RootDirectory, baseName));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, MarkerFileName), conversationId);
        return dir;
    }

    /// <summary>Resolves the given conversation's artifacts folder, creating it (title-named
    /// when available) if it doesn't exist yet - used by explicit user actions like "open
    /// artifacts folder" that need somewhere real to point at.</summary>
    public string GetConversationDirectory(string conversationId, string? conversationTitle = null) =>
        GetOrCreateConversationDirectory(conversationId, conversationTitle);

    /// <summary>Writes <paramref name="content"/> under the conversation's artifacts folder,
    /// sanitizing <paramref name="suggestedFileName"/> (strips any path, rejects traversal,
    /// dedupes an existing name with a " (2)", " (3)", ... suffix), and returns where it landed.</summary>
    public async Task<ChatArtifact> SaveAsync(string conversationId, string suggestedFileName, string content, string? conversationTitle = null, CancellationToken ct = default)
    {
        var dir = GetOrCreateConversationDirectory(conversationId, conversationTitle);

        var fileName = SanitizeFileName(suggestedFileName);
        var path = ResolveSafePath(dir, DedupeFileName(dir, fileName));

        await AtomicFile.WriteAllTextAsync(path, content, ct);
        var info = new FileInfo(path);
        return new ChatArtifact(Path.GetFileName(path), path, info.Length, info.LastWriteTimeUtc);
    }

    public Task<IReadOnlyList<ChatArtifact>> ListAsync(string conversationId, CancellationToken ct = default)
    {
        var dir = TryGetConversationDirectory(conversationId);
        if (dir is null)
            return Task.FromResult<IReadOnlyList<ChatArtifact>>([]);

        IReadOnlyList<ChatArtifact> list = new DirectoryInfo(dir)
            .GetFiles()
            .Where(f => !string.Equals(f.Name, MarkerFileName, StringComparison.Ordinal))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new ChatArtifact(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc))
            .ToList();
        return Task.FromResult(list);
    }

    /// <summary>A Guid-shaped id normally; still defensively stripped of anything that could
    /// escape the artifacts root if a conversation id were ever attacker-influenced.</summary>
    private static string SanitizeConversationId(string conversationId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(conversationId.Where(c => !invalid.Contains(c) && c != '.').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    /// <summary>Fence language (as Markdig reports it, e.g. "csharp", "py") to a file
    /// extension for a saved code-block artifact; unrecognized/blank falls back to .txt.</summary>
    public static string ExtensionForLanguage(string? language)
    {
        var key = (language ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "cs" or "csharp" => ".cs",
            "py" or "python" => ".py",
            "js" or "javascript" or "node" => ".js",
            "jsx" => ".jsx",
            "ts" or "typescript" => ".ts",
            "tsx" => ".tsx",
            "json" or "jsonc" => ".json",
            "xml" => ".xml",
            "html" or "htm" => ".html",
            "css" => ".css",
            "scss" => ".scss",
            "md" or "markdown" => ".md",
            "sh" or "bash" or "shell" or "zsh" => ".sh",
            "ps1" or "powershell" => ".ps1",
            "sql" => ".sql",
            "yaml" or "yml" => ".yaml",
            "toml" => ".toml",
            "go" or "golang" => ".go",
            "rs" or "rust" => ".rs",
            "java" => ".java",
            "kotlin" or "kt" => ".kt",
            "swift" => ".swift",
            "c" => ".c",
            "cpp" or "c++" or "cc" => ".cpp",
            "h" => ".h",
            "hpp" => ".hpp",
            "rb" or "ruby" => ".rb",
            "php" => ".php",
            "lua" => ".lua",
            _ => ".txt"
        };
    }

    /// <summary>Strips any directory component (blocking traversal outright - the result is
    /// always a bare file name) and replaces characters the filesystem would reject.</summary>
    internal static string SanitizeFileName(string suggested)
    {
        var name = Path.GetFileName(StripMarkdownDecoration(suggested.Trim()));
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim('.', ' ');
        return string.IsNullOrWhiteSpace(name) ? "artifact.txt" : name;
    }

    /// <summary>
    /// Strips the markdown a model wraps a filename in when it mentions one in prose,
    /// e.g. `` `calculator.cs` `` or **calculator.cs**.
    ///
    /// Backticks are legal filename characters on Windows, so they survived
    /// sanitization and produced a literal "`calculator.cs`.cs" on disk. The trailing
    /// backtick also defeated extension detection (Path.GetExtension returns ".cs`",
    /// which does not match ".cs"), so the language extension was appended a second
    /// time. Removing the decoration fixes both the name and the doubled extension.
    /// </summary>
    internal static string StripMarkdownDecoration(string suggested)
    {
        var name = suggested.Replace("`", string.Empty).Trim();
        while (name.Length > 2 && name.StartsWith("**", StringComparison.Ordinal) && name.EndsWith("**", StringComparison.Ordinal))
            name = name[2..^2].Trim();
        while (name.Length > 2 && name.StartsWith('*') && name.EndsWith('*'))
            name = name[1..^1].Trim();
        while (name.Length > 2 && name.StartsWith('_') && name.EndsWith('_'))
            name = name[1..^1].Trim();
        return name;
    }

    private static string DedupeFileName(string dir, string fileName)
    {
        if (!File.Exists(Path.Combine(dir, fileName)))
            return fileName;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!File.Exists(Path.Combine(dir, candidate)))
                return candidate;
        }
    }

    /// <summary>Conversation title to a filesystem-safe folder name (invalid characters and
    /// spaces become '-', collapsed and trimmed, capped at 60 chars); null for blank input so
    /// callers can fall back to the conversation id.</summary>
    private static string? SanitizeFolderName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray());
        while (name.Contains("--", StringComparison.Ordinal))
            name = name.Replace("--", "-");
        name = name.Trim('-', '.');
        if (name.Length > 60)
            name = name[..60].Trim('-');
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string DedupeDirectoryName(string parentDir, string name)
    {
        if (!Directory.Exists(Path.Combine(parentDir, name)))
            return name;

        for (var i = 2; ; i++)
        {
            var candidate = $"{name} ({i})";
            if (!Directory.Exists(Path.Combine(parentDir, candidate)))
                return candidate;
        }
    }

    /// <summary>Defense in depth beyond <see cref="SanitizeFileName"/>: refuses to write
    /// anywhere the resolved absolute path is not actually inside <paramref name="dir"/>.</summary>
    private static string ResolveSafePath(string dir, string fileName)
    {
        var fullDir = Path.GetFullPath(dir);
        var path = Path.GetFullPath(Path.Combine(fullDir, fileName));
        if (!path.StartsWith(fullDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to write outside the conversation's artifacts folder.");
        return path;
    }
}
