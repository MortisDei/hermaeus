using Hermaeus.Core.Models;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Hermaeus.Core.Services;

/// <summary>
/// <paramref name="ToolCallId"/>/<paramref name="ToolCalls"/> only apply to
/// tool round-tripping: an assistant turn that requested tool calls carries
/// <see cref="ToolCalls"/>, and the corresponding tool-result turn (Role
/// "tool") carries <see cref="ToolCallId"/>. Plain chat messages leave both null.
/// </summary>
/// <param name="Images">r19 5.3: images attached to this turn, already encoded as data: URIs so
/// the wire-format builder that consumes this stays pure (no file IO / re-encoding per send).</param>
public record ChatMessage(
    string Role,
    string Content,
    string? ToolCallId = null,
    IReadOnlyList<LlmToolCallRequest>? ToolCalls = null,
    IReadOnlyList<ChatMessageImage>? Images = null);

/// <summary>One image attached to a chat turn (r19 5.3). <see cref="DataUri"/> is a complete
/// <c>data:&lt;mediaType&gt;;base64,...</c> string, ready to embed directly in an OpenAI-style
/// image_url content part.</summary>
public sealed record ChatMessageImage(string FileName, string DataUri);

public sealed record ChatTokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);

/// <summary>A tool/function the model may call, declared to the provider using the OpenAI function-calling schema shape.</summary>
public sealed record LlmToolDefinition(string Name, string Description, JsonElement Parameters);

/// <summary>A completed tool call the model asked for; <see cref="ArgumentsJson"/> is the raw JSON object the model produced.</summary>
public sealed record LlmToolCallRequest(string Id, string Name, string ArgumentsJson);

/// <summary>
/// llama.cpp server's own timing breakdown for one completion, carried on the
/// final streamed chunk (r10 03-field-follow-ups.md 3.2). Decomposes a large
/// FirstTokenMs into "server was evaluating the prompt" vs "request waited
/// before evaluation". Providers that do not report this leave it null; no
/// per-provider special cases beyond llama.cpp.
/// </summary>
/// <param name="DraftTokens">
/// Tokens the speculative decoder drafted, from llama-server's own
/// <c>draft_n</c> (r28 doc 02 2.1). Null when the provider reports nothing,
/// which is a different fact from a measured zero: zero means drafting ran and
/// produced nothing, null means nobody counted.
/// </param>
/// <param name="DraftTokensAccepted">Drafted tokens the target model accepted, from <c>draft_n_accepted</c>.</param>
public sealed record ChatServerTimings(
    int? PromptTokens,
    double? PromptMs,
    int? PredictedTokens,
    double? PredictedMs,
    int? DraftTokens = null,
    int? DraftTokensAccepted = null);

/// <param name="FinishReason">
/// Provider-reported reason the stream ended (e.g. "stop", "length",
/// "tool_calls"). "length" means generation was cut off by the configured
/// token cap, not that the model finished naturally. Null when the
/// provider does not report one.
/// </param>
public sealed record LlmStreamEvent(
    string ContentDelta = "",
    ChatTokenUsage? Usage = null,
    bool IsFinal = false,
    IReadOnlyList<LlmToolCallRequest>? ToolCalls = null,
    ChatServerTimings? ServerTimings = null,
    string? FinishReason = null)
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

    /// <summary>
    /// Disables llama-server's prompt-cache reuse for this request (r17 02-benchmark-truth.md
    /// 2.6). Only <c>LlamaCppService</c> honors this, as <c>cache_prompt = !DisablePromptCache</c>;
    /// other providers ignore it. Benchmark-only in practice: the chat path never sets this, so
    /// <c>cache_prompt: true</c> stays the chat default.
    /// </summary>
    public bool DisablePromptCache { get; init; }

    /// <summary>
    /// Constrains generation to a shape. Null means unconstrained, which is
    /// the behaviour every caller had before r28. Providers that cannot
    /// enforce a constraint report that through
    /// <see cref="LlmStreamEvent"/> rather than ignoring it, because a caller
    /// that sets one intends to parse the result without defending against
    /// prose.
    /// </summary>
    public LlmOutputConstraint? OutputConstraint { get; init; }

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
