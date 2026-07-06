using Aether.Core.Models;
using System.Runtime.CompilerServices;

namespace Aether.Core.Services;

public record ChatMessage(string Role, string Content);

public sealed record ChatTokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);

public sealed record LlmStreamEvent(string ContentDelta = "", ChatTokenUsage? Usage = null, bool IsFinal = false)
{
    public static LlmStreamEvent Error(string message) => new(message, IsFinal: true);
}

/// <summary>
/// Sampling and prompt options for a chat completion. Extend this record
/// instead of adding parameters to <see cref="ILlmService.StreamChatAsync"/>.
/// </summary>
public sealed record LlmChatOptions
{
    public string? SystemPrompt { get; init; }
    public double Temperature { get; init; } = 0.7;
    /// <summary>Provider default (or configured LLM setting) when null.</summary>
    public int? MaxTokens { get; init; }

    public static readonly LlmChatOptions Default = new();
}

public interface ILlmService
{
    string ProviderName { get; }
    bool   IsConfigured { get; }
    Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default);
    IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions? options = null,
        CancellationToken ct = default);
}

public static class LlmServiceExtensions
{
    /// <summary>Streams content deltas only, for callers that accumulate plain text.</summary>
    public static async IAsyncEnumerable<string> StreamChatTextAsync(
        this ILlmService llm,
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in llm.StreamChatAsync(modelId, messages, options, ct).WithCancellation(ct))
        {
            if (!string.IsNullOrEmpty(evt.ContentDelta))
                yield return evt.ContentDelta;
        }
    }
}
