namespace Hermaeus.Core.Services;

/// <summary>
/// Pure formatting helpers extracted from <c>ChatViewModel.PersistAsync</c>
/// (docs/review/archived/r1/01-architecture-review.md item 5).
/// </summary>
public static class ChatConversationBuilder
{
    /// <summary>
    /// Derives a conversation title from its first user message: collapse to
    /// one line, and truncate with an ellipsis past <paramref name="maxLength"/>.
    /// </summary>
    public static string AutoTitleFrom(string firstUserMessageContent, int maxLength = 60)
    {
        var collapsed = firstUserMessageContent.Replace('\n', ' ').Trim();
        return collapsed.Length > maxLength
            ? collapsed[..(maxLength - 3)] + "..."
            : collapsed;
    }
}
