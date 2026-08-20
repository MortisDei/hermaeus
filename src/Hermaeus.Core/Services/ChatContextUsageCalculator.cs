namespace Hermaeus.Core.Services;

/// <summary>
/// Display-ready result of comparing a token usage figure against a context
/// window limit: the label/percent/warning-level math Chat used to compute
/// inline (docs/review/archived/r1/01-architecture-review.md item 5).
/// </summary>
public sealed record ChatContextUsageResult(
    string Label,
    double Percent,
    string WarningLevel,
    bool IsCritical,
    bool IsWarning,
    string Tooltip);

/// <summary>
/// Pure token-budget math for the Chat surface: resolving the effective
/// context window for the active model/server, computing usage display
/// values, and truncating history to fit a budget. Extracted from
/// ChatViewModel so it's testable without UI plumbing.
/// </summary>
public static class ChatContextUsageCalculator
{
    /// <summary>
    /// Resolves the effective context window: an explicit model override,
    /// else the managed chat server's configured context size, else a
    /// settings-level fallback.
    /// </summary>
    public static int ResolveContextWindowLimit(
        int? modelDefaultContextSize,
        int? managedChatServerContextSize,
        int fallbackMaxTokens)
    {
        if (modelDefaultContextSize is > 0)
            return modelDefaultContextSize.Value;
        if (managedChatServerContextSize is > 0)
            return managedChatServerContextSize.Value;
        return Math.Max(1, fallbackMaxTokens);
    }

    public static ChatContextUsageResult Compute(ChatTokenUsage usage, int limit, string kind)
    {
        var total = usage.TotalTokens > 0 ? usage.TotalTokens : usage.PromptTokens + usage.CompletionTokens;
        var percent = limit <= 0 ? 0 : Math.Clamp(total * 100.0 / limit, 0, 999);
        var warningLevel = percent >= 95 ? "Critical" : percent >= 80 ? "Warning" : "None";
        var tooltip = kind == "Reported by provider"
            ? $"Reported by provider. Prompt {usage.PromptTokens:N0}, completion {usage.CompletionTokens:N0}, total {total:N0}."
            : $"Estimated locally from visible chat, system prompt, draft input, and ready attachments. About {percent:F0}% of the selected context window.";

        return new ChatContextUsageResult(
            $"{total:N0} / {limit:N0} tokens",
            percent,
            warningLevel,
            IsCritical: warningLevel == "Critical",
            IsWarning: warningLevel == "Warning",
            tooltip);
    }

    /// <summary>
    /// Keeps the newest messages that fit a token budget (reserving room for
    /// system prompt, the current prompt, and the model's response), oldest
    /// messages dropped first.
    /// </summary>
    public static List<ChatMessage> TruncateHistoryToContextWindow(
        IReadOnlyList<ChatMessage> messages,
        int contextWindow,
        int systemTokens = 0,
        int currentPromptTokens = 0)
    {
        var reservedResponseTokens = Math.Max(256, contextWindow / 8);
        var budget = Math.Max(256, contextWindow - systemTokens - currentPromptTokens - reservedResponseTokens);
        var selected = new Stack<ChatMessage>();
        var used = 0;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
            var tokens = ContextPackBuilder.EstimateTokens(message.Content)
                + ContextPackBuilder.EstimateTokens(message.ReasoningContent ?? string.Empty);
            if (selected.Count > 0 && used + tokens > budget)
                break;
            selected.Push(message);
            used += tokens;
        }

        return selected.ToList();
    }
}
