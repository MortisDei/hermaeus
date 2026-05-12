using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.ViewModels;

public enum ChatContextAttachmentStatus
{
    Ready,
    Skipped,
    Error
}

public sealed partial class ChatContextAttachment : ObservableObject
{
    public const int MaxFileBytes = 512 * 1024;
    public const int MaxTotalBytes = 1024 * 1024;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".cs", ".fs", ".vb", ".csproj", ".props", ".targets",
        ".sln", ".json", ".jsonl", ".xml", ".xaml", ".axaml", ".yaml", ".yml", ".toml",
        ".ini", ".config", ".sh", ".ps1", ".py", ".js", ".jsx", ".ts", ".tsx", ".css",
        ".scss", ".html", ".htm", ".razor", ".sql", ".rs", ".go", ".java", ".c", ".h",
        ".cpp", ".hpp", ".swift", ".kt", ".kts", ".php", ".rb", ".lua", ".Dockerfile"
    };

    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Preview { get; init; } = string.Empty;
    public ChatContextAttachmentStatus Status { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public bool IsReady => Status == ChatContextAttachmentStatus.Ready;
    public string SizeLabel => FormatBytes(SizeBytes);
    public string ChipText => $"{FileName} ({SizeLabel})";

    public static async Task<IReadOnlyList<ChatContextAttachment>> LoadFilesAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var result = new List<ChatContextAttachment>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long total = 0;

        foreach (var raw in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(raw.Trim());
            if (!seen.Add(path))
                continue;

            var attachment = await LoadOneAsync(path, total, ct);
            if (attachment.IsReady)
                total += attachment.SizeBytes;
            result.Add(attachment);
        }

        return result;
    }

    public static string BuildPrompt(string userText, IEnumerable<ChatContextAttachment> attachments)
    {
        var ready = attachments.Where(a => a.IsReady).ToList();
        if (ready.Count == 0)
            return userText.Trim();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Attached file context:");
        sb.AppendLine("The following local files were read once at send time. Use them as direct context for the user request.");
        foreach (var item in ready)
        {
            sb.AppendLine();
            sb.AppendLine($"File: {item.FileName}");
            sb.AppendLine($"Path: {item.FullPath}");
            sb.AppendLine($"Bytes: {item.SizeBytes}");
            sb.AppendLine("Content:");
            sb.AppendLine("```");
            sb.AppendLine(item.Content);
            sb.AppendLine("```");
        }

        sb.AppendLine();
        sb.AppendLine("User request:");
        sb.AppendLine(string.IsNullOrWhiteSpace(userText) ? "Review the attached files." : userText.Trim());
        return sb.ToString().Trim();
    }

    public static string BuildDisplayMessage(string userText, IEnumerable<ChatContextAttachment> attachments)
    {
        var ready = attachments.Where(a => a.IsReady).ToList();
        if (ready.Count == 0)
            return userText.Trim();

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(userText))
            sb.AppendLine(userText.Trim()).AppendLine();
        sb.AppendLine("Attached context injected at send time:");
        foreach (var item in ready)
            sb.AppendLine($"- {item.FileName} ({item.SizeLabel}) - {item.FullPath}");
        return sb.ToString().Trim();
    }

    private static async Task<ChatContextAttachment> LoadOneAsync(string path, long currentTotal, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
                return Skipped(path, "File not found.");
            if (!SupportedExtensions.Contains(Path.GetExtension(path)) && !Path.GetFileName(path).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase))
                return Skipped(path, "Unsupported file type for direct chat context.");

            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
                return Skipped(path, $"File is over {FormatBytes(MaxFileBytes)}.");
            if (currentTotal + info.Length > MaxTotalBytes)
                return Skipped(path, $"Combined context is over {FormatBytes(MaxTotalBytes)}.");

            var bytes = await File.ReadAllBytesAsync(path, ct);
            if (LooksBinary(bytes))
                return Skipped(path, "Binary-looking file skipped.");

            var content = System.Text.Encoding.UTF8.GetString(bytes);
            return new ChatContextAttachment
            {
                FileName = Path.GetFileName(path),
                FullPath = path,
                SizeBytes = info.Length,
                Content = content,
                Preview = content.Replace('\r', ' ').Replace('\n', ' ').Trim(),
                Status = ChatContextAttachmentStatus.Ready,
                StatusMessage = "Ready"
            };
        }
        catch (Exception ex)
        {
            return new ChatContextAttachment
            {
                FileName = Path.GetFileName(path),
                FullPath = path,
                Status = ChatContextAttachmentStatus.Error,
                StatusMessage = ex.Message
            };
        }
    }

    private static ChatContextAttachment Skipped(string path, string message) => new()
    {
        FileName = Path.GetFileName(path),
        FullPath = path,
        Status = ChatContextAttachmentStatus.Skipped,
        StatusMessage = message
    };

    private static bool LooksBinary(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        if (bytes.Any(b => b == 0)) return true;
        var control = bytes.Count(b => b < 32 && b is not 9 and not 10 and not 13);
        return control > Math.Max(8, bytes.Length / 100);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}
