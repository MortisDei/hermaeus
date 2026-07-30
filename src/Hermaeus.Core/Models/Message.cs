using Hermaeus.Core.Services;

namespace Hermaeus.Core.Models;

public class Message : IConversationNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// r25 doc 01: the message this one replies to, making a conversation a tree
    /// rather than a line. Empty for the first message in a conversation.
    /// Additive JSON: conversations written before r25 have no parent chain and
    /// get one inferred from stored order on load
    /// (<see cref="Services.ConversationTree.BackfillLinearChain"/>).
    /// </summary>
    public string ParentId { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string OriginalContent { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsError { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public List<string> AttachedFilePaths { get; set; } = [];

    /// <summary>
    /// True when generation stopped because the configured max-token cap was
    /// hit ("length" finish reason), not because the model finished
    /// naturally. Additive (r19 1.2); absent/false for pre-existing messages.
    /// </summary>
    public bool WasTruncated { get; set; }
}
