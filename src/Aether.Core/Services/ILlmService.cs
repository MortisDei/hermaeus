using Aether.Core.Models;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Aether.Core.Services;

/// <summary>
/// <paramref name="ToolCallId"/>/<paramref name="ToolCalls"/> only apply to
/// tool round-tripping: an assistant turn that requested tool calls carries
/// <see cref="ToolCalls"/>, and the corresponding tool-result turn (Role
/// "tool") carries <see cref="ToolCallId"/>. Plain chat messages leave both null.
/// </summary>
public record ChatMessage(string Role, string Content, string? ToolCallId = null, IReadOnlyList<LlmToolCallRequest>? ToolCalls = null);

public sealed record ChatTokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);

/// <summary>A tool/function the model may call, declared to the provider using the OpenAI function-calling schema shape.</summary>
public sealed record LlmToolDefinition(string Name, string Description, JsonElement Parameters);

/// <summary>A completed tool call the model asked for; <see cref="ArgumentsJson"/> is the raw JSON object the model produced.</summary>
public sealed record LlmToolCallRequest(string Id, string Name, string ArgumentsJson);

public sealed record LlmStreamEvent(
    string ContentDelta = "",
    ChatTokenUsage? Usage = null,
    bool IsFinal = false,
    IReadOnlyList<LlmToolCallRequest>? ToolCalls = null)
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
    /// <summary>Nucleus sampling cutoff. Provider default when null.</summary>
    public double? TopP { get; init; }
    /// <summary>Top-k sampling cutoff. Provider default when null.</summary>
    public int? TopK { get; init; }
    /// <summary>Minimum token probability relative to the most likely token. Provider default when null.</summary>
    public double? MinP { get; init; }
    /// <summary>Repetition penalty (llama.cpp/Ollama naming). Provider default when null.</summary>
    public double? RepeatPenalty { get; init; }
    /// <summary>OpenAI-style frequency penalty. Provider default when null.</summary>
    public double? FrequencyPenalty { get; init; }
    /// <summary>OpenAI-style presence penalty. Provider default when null.</summary>
    public double? PresencePenalty { get; init; }
    /// <summary>
    /// Tools the model may call. When supported by the provider/model, a
    /// response ends in <see cref="LlmStreamEvent.ToolCalls"/> instead of
    /// (or alongside) text; providers/models without tool-calling support
    /// simply ignore this and respond with text as before. Null or empty
    /// means no tools are offered.
    /// </summary>
    public IReadOnlyList<LlmToolDefinition>? Tools { get; init; }

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

    /// <summary>
    /// Drops any cached model listing so the next GetModelsAsync call re-queries
    /// providers instead of returning stale data. No-op for services that don't cache.
    /// </summary>
    void InvalidateModelCache() { }
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
