using Aether.Core.Models;

namespace Aether.Core.Services;

public enum ConversationExportFormat
{
    Markdown,
    Json
}

public interface IConversationExportService
{
    string BuildExport(Conversation conversation, ConversationExportFormat format);
    Task<string> ExportAsync(Conversation conversation, string path, ConversationExportFormat format, CancellationToken ct = default);
}
