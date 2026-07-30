using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// The shape <see cref="ConversationTree"/> needs to walk a conversation.
/// Implemented by <see cref="Message"/> (the persisted model) and by the chat
/// view model's message type, so the tree walk exists once rather than being
/// copied either side of the persistence boundary.
/// </summary>
public interface IConversationNode
{
    string Id { get; }
    string ParentId { get; }
    DateTime CreatedAt { get; }
}

/// <summary>
/// r25 doc 01: a conversation is a tree of messages linked by
/// <see cref="IConversationNode.ParentId"/>, so regenerating an answer or
/// editing a question adds a sibling instead of destroying what was there.
///
/// Pure functions over a flat list, with no store and no view model, because
/// the tree is the part that has to be provably right: it is walked on the UI
/// thread for every render of every conversation, over a <c>messages_json</c>
/// blob the user owns, syncs and can edit.
/// </summary>
public static class ConversationTree
{
    /// <summary>
    /// Root-to-leaf path ending at <paramref name="leafId"/>, in order.
    ///
    /// Walks parents upward and reverses, refusing to revisit a node it has
    /// already seen: a cycle in a hand-edited or corrupted blob must produce a
    /// truncated path, never an infinite loop on the UI thread.
    /// </summary>
    public static IReadOnlyList<T> PathTo<T>(IReadOnlyList<T> messages, string? leafId)
        where T : IConversationNode
    {
        if (messages.Count == 0 || string.IsNullOrEmpty(leafId))
            return [];

        var byId = BuildIndex(messages);
        if (!byId.TryGetValue(leafId, out var current))
            return [];

        var reversed = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(current.Id))
        {
            reversed.Add(current);
            if (string.IsNullOrEmpty(current.ParentId) || !byId.TryGetValue(current.ParentId, out var parent))
                break;
            current = parent;
        }

        reversed.Reverse();
        return reversed;
    }

    /// <summary>The active path: the path to whichever leaf <see cref="ResolveLeaf"/> settles on.</summary>
    public static IReadOnlyList<T> ActivePath<T>(IReadOnlyList<T> messages, string? activeLeafId)
        where T : IConversationNode =>
        PathTo(messages, ResolveLeaf(messages, activeLeafId));

    /// <summary>
    /// Children of <paramref name="id"/>, oldest first. Ordered by
    /// <see cref="IConversationNode.CreatedAt"/> then id, so sibling order is
    /// stable across loads even when two messages share a timestamp.
    /// </summary>
    public static IReadOnlyList<T> ChildrenOf<T>(IReadOnlyList<T> messages, string? id)
        where T : IConversationNode
    {
        var parentId = id ?? string.Empty;
        return messages
            .Where(m => string.Equals(m.ParentId, parentId, StringComparison.Ordinal))
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Siblings of a message, including the message itself, oldest first.</summary>
    public static IReadOnlyList<T> SiblingsOf<T>(IReadOnlyList<T> messages, T message)
        where T : IConversationNode =>
        ChildrenOf(messages, message.ParentId);

    /// <summary>Messages with no children: the ends of every branch.</summary>
    public static IReadOnlyList<T> Leaves<T>(IReadOnlyList<T> messages)
        where T : IConversationNode
    {
        var parents = new HashSet<string>(
            messages.Where(m => !string.IsNullOrEmpty(m.ParentId)).Select(m => m.ParentId),
            StringComparer.Ordinal);
        return messages.Where(m => !parents.Contains(m.Id)).ToList();
    }

    /// <summary>
    /// The three cases that actually happen: the pointer is empty (an unbranched
    /// or pre-r25 conversation, so take the last message in stored order), it
    /// names a message that no longer exists (its subtree was deleted, so take
    /// the newest remaining leaf), or it is valid.
    /// </summary>
    public static string ResolveLeaf<T>(IReadOnlyList<T> messages, string? activeLeafId)
        where T : IConversationNode
    {
        if (messages.Count == 0)
            return string.Empty;

        if (!string.IsNullOrEmpty(activeLeafId) &&
            messages.Any(m => string.Equals(m.Id, activeLeafId, StringComparison.Ordinal)))
            return activeLeafId;

        if (string.IsNullOrEmpty(activeLeafId))
            return messages[^1].Id;

        return NewestLeaf(messages, Leaves(messages));
    }

    /// <summary>
    /// Descends from <paramref name="message"/> to the newest leaf beneath it,
    /// which is where switching to a sibling branch should land.
    /// </summary>
    public static string NewestLeafUnder<T>(IReadOnlyList<T> messages, T message)
        where T : IConversationNode
    {
        var subtree = Subtree(messages, message.Id);
        var ids = subtree.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var leaves = subtree
            .Where(m => !subtree.Any(c => ids.Contains(c.Id) &&
                                          string.Equals(c.ParentId, m.Id, StringComparison.Ordinal)))
            .ToList();
        return NewestLeaf(messages, leaves);
    }

    /// <summary>
    /// Every message at or beneath <paramref name="rootId"/>. Cycle-safe for the
    /// same reason <see cref="PathTo"/> is.
    /// </summary>
    public static IReadOnlyList<T> Subtree<T>(IReadOnlyList<T> messages, string rootId)
        where T : IConversationNode
    {
        var root = messages.FirstOrDefault(m => string.Equals(m.Id, rootId, StringComparison.Ordinal));
        if (root is null)
            return [];

        var byParent = messages
            .Where(m => !string.IsNullOrEmpty(m.ParentId))
            .GroupBy(m => m.ParentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var collected = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<T>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (!seen.Add(next.Id))
                continue;
            collected.Add(next);
            if (byParent.TryGetValue(next.Id, out var children))
                foreach (var child in children)
                    pending.Push(child);
        }

        return collected;
    }

    /// <summary>
    /// Gives a pre-r25 conversation the parent chain its stored order already
    /// implies: each message's parent is the one before it. Lossless, so a
    /// conversation written by 0.31.0 renders identically in 0.32.0.
    ///
    /// A conversation that already carries any parent was written by r25 or
    /// later and is left alone; re-inferring a chain over a real tree would
    /// flatten its branches into nonsense.
    /// </summary>
    public static bool BackfillLinearChain(IList<Message> messages)
    {
        if (messages.Count < 2)
            return false;
        if (messages.Any(m => !string.IsNullOrEmpty(m.ParentId)))
            return false;

        for (var i = 1; i < messages.Count; i++)
            messages[i].ParentId = messages[i - 1].Id;

        return true;
    }

    private static string NewestLeaf<T>(IReadOnlyList<T> messages, IReadOnlyList<T> leaves)
        where T : IConversationNode
    {
        if (leaves.Count == 0)
            return messages.Count == 0 ? string.Empty : messages[^1].Id;

        return leaves
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id, StringComparer.Ordinal)
            .First().Id;
    }

    /// <summary>
    /// Duplicate ids are possible in a corrupted blob; first one wins rather than
    /// throwing, because a conversation that will not open is worse than one that
    /// renders a shorter path.
    /// </summary>
    private static Dictionary<string, T> BuildIndex<T>(IReadOnlyList<T> messages)
        where T : IConversationNode
    {
        var byId = new Dictionary<string, T>(messages.Count, StringComparer.Ordinal);
        foreach (var message in messages)
            byId.TryAdd(message.Id, message);
        return byId;
    }
}
