namespace Hermaeus.Core.Models;

public class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Conversation";
    public string ModelId { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }

    /// <summary>
    /// r21: RAG dataset id attached to this conversation for per-turn
    /// retrieval injection ("Knowledge" in the chat UI). Empty means no
    /// dataset attached.
    /// </summary>
    public string RagDatasetId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<Message> Messages { get; set; } = [];
}
