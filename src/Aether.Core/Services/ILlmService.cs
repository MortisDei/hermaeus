using Aether.Core.Models;
using System.Runtime.CompilerServices;

namespace Aether.Core.Services;

public record ChatMessage(string Role, string Content);

public sealed record ChatTokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);

public sealed record LlmStreamEvent(string ContentDelta = "", ChatTokenUsage? Usage = null, bool IsFinal = false)
{
    public static LlmStreamEvent Error(string message) => new(message, IsFinal: true);
}

public interface ILlmService
{
    string ProviderName { get; }
    bool   IsConfigured { get; }
    Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        CancellationToken ct = default);
    async IAsyncEnumerable<LlmStreamEvent> StreamChatEventsAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var token in StreamChatAsync(modelId, messages, systemPrompt, temperature, ct)
                           .WithCancellation(ct))
        {
            yield return new LlmStreamEvent(token);
        }
    }

    Task PullModelAsync(string modelId, IProgress<string>? progress = null, CancellationToken ct = default);
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
}
