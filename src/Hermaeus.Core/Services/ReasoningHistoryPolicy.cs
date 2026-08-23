using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public static class ReasoningHistoryPolicy
{
    public static bool CanReplay(string providerTag, bool providerAccepts, bool templatePreserves, bool preserveSetting, bool launchApplied) =>
        string.Equals(providerTag, "llama.cpp", StringComparison.OrdinalIgnoreCase)
        && providerAccepts && templatePreserves && preserveSetting && launchApplied;

    public static ChatMessage WithOptionalReasoning(ChatMessage message, bool include) =>
        include && !string.IsNullOrWhiteSpace(message.ReasoningContent)
            ? message
            : message with { ReasoningContent = null };
}
