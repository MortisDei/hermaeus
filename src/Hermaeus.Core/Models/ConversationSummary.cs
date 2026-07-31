namespace Hermaeus.Core.Models;

/// <summary>
/// r27 05-small-open-items.md 5.1: everything the conversation sidebar draws,
/// and nothing else. Reading a full <see cref="Conversation"/> deserialises
/// every message of every conversation and then walks them again to backfill
/// parent links, in order to render a list of titles, folders, tags, and pinned
/// and archived flags.
/// Be honest about the size of this: on the owner's 60 KB database it costs
/// nothing. It is a cliff, not a stall, and it is here because it is four lines
/// of SQL away from never being one.
/// </summary>
public sealed record ConversationSummary(
    string Id,
    string Title,
    string ModelId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Folder,
    List<string> Tags,
    bool IsPinned,
    bool IsArchived,
    string ProjectId,
    string RagDatasetId,
    bool RecallExcluded)
{
    /// <summary>
    /// The projection of a conversation already in memory, for callers that
    /// cannot avoid loading one (and for the default interface fallback that
    /// keeps every existing store implementation compiling).
    /// </summary>
    public static ConversationSummary From(Conversation c) => new(
        c.Id, c.Title, c.ModelId, c.CreatedAt, c.UpdatedAt, c.Folder,
        [.. c.Tags], c.IsPinned, c.IsArchived, c.ProjectId, c.RagDatasetId, c.RecallExcluded);
}
