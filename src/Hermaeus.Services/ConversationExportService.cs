using System.Text;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class ConversationExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string BuildExport(Conversation conversation, ConversationExportFormat format) =>
        format switch
        {
            ConversationExportFormat.Json => JsonSerializer.Serialize(conversation, JsonOptions),
            _ => BuildMarkdown(conversation)
        };

    public async Task<string> ExportAsync(Conversation conversation, string path, ConversationExportFormat format, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(path);
        await WriteTextAtomicAsync(full, BuildExport(conversation, format), ct);
        return full;
    }

    private static string BuildMarkdown(Conversation conversation)
    {
        var md = new StringBuilder();
        md.AppendLine($"# {EscapeHeading(conversation.Title)}");
        md.AppendLine();
        md.AppendLine($"- Id: `{conversation.Id}`");
        md.AppendLine($"- Created: `{conversation.CreatedAt:O}`");
        md.AppendLine($"- Updated: `{conversation.UpdatedAt:O}`");
        if (!string.IsNullOrWhiteSpace(conversation.ModelId))
            md.AppendLine($"- Model: `{conversation.ModelId}`");
        if (!string.IsNullOrWhiteSpace(conversation.Folder))
            md.AppendLine($"- Folder: `{conversation.Folder}`");
        if (conversation.Tags.Count > 0)
            md.AppendLine($"- Tags: {string.Join(", ", conversation.Tags.Select(tag => $"`{tag}`"))}");

        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
        {
            md.AppendLine();
            md.AppendLine("## System Prompt");
            md.AppendLine();
            md.AppendLine(conversation.SystemPrompt.Trim());
        }

        md.AppendLine();
        md.AppendLine("## Messages");
        foreach (var message in conversation.Messages.OrderBy(m => m.CreatedAt))
        {
            md.AppendLine();
            md.AppendLine($"### {message.Role} · {message.CreatedAt:O}");
            if (!string.IsNullOrWhiteSpace(message.ModelId))
                md.AppendLine($"Model: `{message.ModelId}`");
            if (message.IsError)
                md.AppendLine("Status: `error or incomplete`");
            if (message.AttachedFilePaths.Count > 0)
            {
                md.AppendLine("Attachments:");
                foreach (var path in message.AttachedFilePaths)
                    md.AppendLine($"- `{path}`");
            }
            md.AppendLine();
            if (!string.IsNullOrWhiteSpace(message.ReasoningContent))
            {
                md.AppendLine("#### Reasoning");
                md.AppendLine();
                md.AppendLine(message.ReasoningContent.Trim());
                md.AppendLine();
                md.AppendLine("#### Answer");
                md.AppendLine();
            }
            md.AppendLine(string.IsNullOrWhiteSpace(message.Content) && !string.IsNullOrWhiteSpace(message.ReasoningContent)
                ? "No final answer was returned."
                : message.Content.Trim());
        }

        return md.ToString();
    }

    private static string EscapeHeading(string value) => value.Replace("#", "\\#", StringComparison.Ordinal).Trim();

    private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content, Encoding.UTF8, ct);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
