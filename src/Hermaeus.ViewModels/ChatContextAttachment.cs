using Hermaeus.Rag.Pipeline;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermaeus.ViewModels;

public enum ChatContextAttachmentStatus
{
    Ready,
    Skipped,
    Error
}

/// <summary>r19 5.3: an Image attachment never counts against the text prompt budget and
/// carries a pre-encoded data URI instead of text Content.</summary>
public enum ChatContextAttachmentKind
{
    Text,
    Image
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

    /// <summary>r19 5.1/5.2: these extensions never pass through the plain-text/LooksBinary
    /// path - their raw bytes are always binary. Text is pulled out first, and the extracted
    /// text's byte count (not the raw file size) is what counts against the prompt budget.</summary>
    private static readonly HashSet<string> ExtractThenAttachExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".pdf"
    };

    /// <summary>r19 5.2: a .pdf is read whole by PdfPig (already a dependency of this
    /// solution via Hermaeus.Rag's ingest pipeline - reused here rather than a hand-rolled
    /// BCL-only parser, since it is not actually a new package), so oversized files are
    /// refused before that read instead of after.</summary>
    private const long MaxPdfFileBytes = 20 * 1024 * 1024;

    /// <summary>r19 5.3: extensions accepted as vision content parts, not text.</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };
    private const long MaxImageFileBytes = 8 * 1024 * 1024;
    private const int MaxImagesPerSend = 4;

    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Preview { get; init; } = string.Empty;
    public ChatContextAttachmentStatus Status { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public ChatContextAttachmentKind Kind { get; init; } = ChatContextAttachmentKind.Text;

    /// <summary>Populated only for <see cref="ChatContextAttachmentKind.Image"/>: a complete
    /// <c>data:&lt;mediaType&gt;;base64,...</c> string ready to send as-is.</summary>
    public string ImageDataUri { get; init; } = string.Empty;
    public bool IsReady => Status == ChatContextAttachmentStatus.Ready;
    public bool IsImage => Kind == ChatContextAttachmentKind.Image;
    public string SizeLabel => FormatBytes(SizeBytes);
    public string ChipText => $"{FileName} ({SizeLabel})";

    /// <summary>r19 5.3: <paramref name="visionAvailable"/> reflects whether the server the
    /// active chat model would run against has a vision projector configured; when false, any
    /// image is refused with an honest reason instead of silently degrading to text-only.</summary>
    public static async Task<IReadOnlyList<ChatContextAttachment>> LoadFilesAsync(IEnumerable<string> paths, bool visionAvailable = true, CancellationToken ct = default)
    {
        var result = new List<ChatContextAttachment>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long total = 0;
        var imageCount = 0;

        foreach (var raw in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(raw.Trim());
            if (!seen.Add(path))
                continue;

            var attachment = await LoadOneAsync(path, total, imageCount, visionAvailable, ct);
            if (attachment.IsReady)
            {
                if (attachment.IsImage) imageCount++;
                else total += attachment.SizeBytes;
            }
            result.Add(attachment);
        }

        return result;
    }

    public static string BuildPrompt(string userText, IEnumerable<ChatContextAttachment> attachments)
    {
        // Images never enter the text prompt - they ride ChatMessage.Images instead.
        var ready = attachments.Where(a => a.IsReady && a.Kind == ChatContextAttachmentKind.Text).ToList();
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

    private static async Task<ChatContextAttachment> LoadOneAsync(string path, long currentTotal, int currentImageCount, bool visionAvailable, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
                return Skipped(path, "File not found.");

            var ext = Path.GetExtension(path);
            if (ImageExtensions.Contains(ext))
                return await LoadImageAsync(path, ext, currentImageCount, visionAvailable, ct);

            var isDockerfile = Path.GetFileName(path).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase);
            var isExtractThenAttach = ExtractThenAttachExtensions.Contains(ext);
            if (!SupportedExtensions.Contains(ext) && !isDockerfile && !isExtractThenAttach)
                return Skipped(path, "Unsupported file type for direct chat context.");

            if (isExtractThenAttach)
                return await LoadExtractedAsync(path, ext, currentTotal, ct);

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

    /// <summary>r19 5.1/5.2: .docx/.pdf never pass LooksBinary (their raw bytes always look
    /// binary); text is extracted first, and the extracted text's byte count - not the raw
    /// file size, which is meaningless to the prompt budget - is what the caps below apply to.</summary>
    private static async Task<ChatContextAttachment> LoadExtractedAsync(string path, string ext, long currentTotal, CancellationToken ct)
    {
        FileTextExtractionResult result;
        if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            result = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return DocxTextExtractor.Extract(stream);
            }, ct);
        }
        else if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            if (new FileInfo(path).Length > MaxPdfFileBytes)
                return Skipped(path, $"File is over {FormatBytes(MaxPdfFileBytes)}.");

            try
            {
                var pdf = await PdfTextExtractor.ExtractAsync(path, ct);
                result = pdf.HasText
                    ? FileTextExtractionResult.Ok(pdf.Text)
                    : FileTextExtractionResult.Skip("Could not extract text (likely scanned or uses embedded font encodings).");
            }
            catch (Exception ex)
            {
                result = FileTextExtractionResult.Skip($"Could not read this PDF: {ex.Message}");
            }
        }
        else
        {
            result = FileTextExtractionResult.Skip("Unsupported extraction type.");
        }

        if (result.Status != FileTextExtractionStatus.Success)
            return Skipped(path, result.Reason);

        var textBytes = System.Text.Encoding.UTF8.GetByteCount(result.Text);
        if (textBytes == 0)
            return Skipped(path, "No text could be extracted from this file.");
        if (textBytes > MaxFileBytes)
            return Skipped(path, $"Extracted text is over {FormatBytes(MaxFileBytes)}.");
        if (currentTotal + textBytes > MaxTotalBytes)
            return Skipped(path, $"Combined context is over {FormatBytes(MaxTotalBytes)}.");

        return new ChatContextAttachment
        {
            FileName = Path.GetFileName(path),
            FullPath = path,
            SizeBytes = textBytes,
            Content = result.Text,
            Preview = result.Text.Replace('\r', ' ').Replace('\n', ' ').Trim(),
            Status = ChatContextAttachmentStatus.Ready,
            StatusMessage = "Ready"
        };
    }

    /// <summary>r19 5.3: refuses with an honest reason rather than silently dropping to
    /// text-only when the active server has no vision projector, or when the per-send image
    /// cap/size cap is exceeded.</summary>
    private static async Task<ChatContextAttachment> LoadImageAsync(string path, string ext, int currentImageCount, bool visionAvailable, CancellationToken ct)
    {
        if (!visionAvailable)
            return Skipped(path, "This server has no vision projector configured (Services > Vision projector).");
        if (currentImageCount >= MaxImagesPerSend)
            return Skipped(path, $"Only {MaxImagesPerSend} images can be attached per message.");

        var info = new FileInfo(path);
        if (info.Length > MaxImageFileBytes)
            return Skipped(path, $"Image is over {FormatBytes(MaxImageFileBytes)}.");

        var bytes = await File.ReadAllBytesAsync(path, ct);
        var mediaType = ext.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
        var dataUri = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";

        return new ChatContextAttachment
        {
            FileName = Path.GetFileName(path),
            FullPath = path,
            SizeBytes = info.Length,
            Kind = ChatContextAttachmentKind.Image,
            ImageDataUri = dataUri,
            Preview = "Image",
            Status = ChatContextAttachmentStatus.Ready,
            StatusMessage = "Ready"
        };
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
