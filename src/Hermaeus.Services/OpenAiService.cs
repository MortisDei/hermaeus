using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class OpenAiService : IDisposable
{
    private const string ProviderTagValue = "openai";
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

    /// <summary>
    /// Ids that are never chat-usable regardless of how a real OpenAI account
    /// names its models. Only consulted against api.openai.com (r11 2.3):
    /// pointing OpenAiBaseUrl at LM Studio, Groq, OpenRouter, vLLM, etc. must
    /// surface every model that endpoint reports, since the old gpt/o1/o3/o4
    /// prefix allow-list made "OpenAI-compatible" unusable for the compatible
    /// endpoints it is named for.
    /// </summary>
    private static readonly string[] NonChatModelIdMarkers = ["embedding", "tts", "whisper", "dall-e", "moderation"];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly ProviderDescriptor Descriptor = new(
        ProviderTagValue, "OpenAI-compatible", ProviderKind.RemoteApi,
        ProviderCapabilities.Streaming | ProviderCapabilities.UsageReporting);

    public string ProviderName => "OpenAI";
    public bool   IsConfigured => !string.IsNullOrWhiteSpace(_settings.Settings.Llm.OpenAiApiKey);

    public OpenAiService(ISettingsService settings, ISecretStore secrets, HttpClient? http = null)
    {
        _settings = settings;
        _secrets = secrets;
        _http = http ?? SharedHttp;
    }

    private string Base => _settings.Settings.Llm.OpenAiBaseUrl.TrimEnd('/');

    private bool IsRealOpenAiEndpoint =>
        Uri.TryCreate(Base, UriKind.Absolute, out var uri) && uri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Structured outputs are documented for api.openai.com and vary by server
    /// and by model everywhere else this base URL can point (LM Studio, vLLM,
    /// Groq, OpenRouter). Nothing is probed, and nothing is assumed of an
    /// endpoint that has not declared itself: a constraint aimed at an
    /// arbitrary compatible server is refused in words rather than sent as a
    /// field that server may silently drop (r28 doc 01 1.3/1.4).
    ///
    /// The declaration can come from the user instead, through
    /// <see cref="LlmSettings.OpenAiSupportsStructuredOutputs"/>, for the many
    /// servers that support the field and have no way to say so. That is a
    /// user asserting what their own server does, which is still a
    /// declaration and still not a probe.
    /// </summary>
    public LlmConstraintSupport ConstraintSupport =>
        IsRealOpenAiEndpoint || _settings.Settings.Llm.OpenAiSupportsStructuredOutputs
            ? LlmConstraintSupport.JsonSchema
            : LlmConstraintSupport.None;

    private async Task<AuthenticationHeaderValue> BuildAuthHeaderAsync(CancellationToken ct) =>
        new("Bearer", await _secrets.ResolveAsync(_settings.Settings.Llm.OpenAiApiKey, ct));

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return [];
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}/v1/models");
            req.Headers.Authorization = await BuildAuthHeaderAsync(ct);
            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadFromJsonAsync<ModelsResponse>(JsonOpts, ct);
            var models = data?.Data ?? [];
            var chatUsable = IsRealOpenAiEndpoint
                ? models.Where(m => !NonChatModelIdMarkers.Any(marker => m.Id.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                : models;
            return chatUsable
                .Select(m => new LlmModel { Id = m.Id, Name = m.Id, Provider = "OpenAI", ProviderTag = ProviderTagValue, SupportsOutputConstraints = ConstraintSupport != LlmConstraintSupport.None })
                .ToList();
        }
        catch { return []; }
    }

    public IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions? options = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return YieldEventError("*OpenAI API key not configured.*");
        options ??= LlmChatOptions.Default;
        if (LlmOutputConstraintWire.DescribeRefusal(options.OutputConstraint, ConstraintSupport, ProviderName) is { } refusal)
            return YieldEventError(refusal);
        return StreamEventsInternal(modelId, messages, options, ct);
    }

    private async IAsyncEnumerable<LlmStreamEvent> StreamEventsInternal(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (success, resp, error) = await GetStreamResponseAsync(modelId, messages, options, ct);

        if (!success)
        {
            yield return LlmStreamEvent.Error(error);
            yield break;
        }

        var toolCalls = new OpenAiCompatibleToolWire.ToolCallAccumulator();
        using (resp!)
        using (var stream = await resp!.Content.ReadAsStreamAsync(ct))
        using (var reader = new StreamReader(stream))
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;
                var json = line[6..];
                if (json == "[DONE]") break;

                OpenAiCompatibleToolWire.AccumulateFromChunk(json, toolCalls);
                var evt = ParseStreamEvent(json);
                if (evt is null) continue;
                yield return evt.IsFinal && toolCalls.HasCalls ? evt with { ToolCalls = toolCalls.Complete() } : evt;
            }
        }
    }

    private async Task<(bool Success, HttpResponseMessage? Response, string Error)> GetStreamResponseAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions options,
        CancellationToken ct)
    {
        try
        {
            var payload = BuildChatPayload(
                modelId, messages, options,
                options.MaxTokens ?? _settings.Settings.Llm.MaxTokens);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/v1/chat/completions")
                { Content = JsonContent.Create(payload, options: JsonOpts) };
            req.Headers.Authorization = await BuildAuthHeaderAsync(ct);
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            return (true, resp, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, null, $"\n\n*Error: {ex.Message}*");
        }
    }

    /// <summary>
    /// Only forwards the sampling parameters the real OpenAI chat completions
    /// API actually accepts (top_p, frequency_penalty, presence_penalty);
    /// top_k/min_p/repeat_penalty are llama.cpp-only extensions and are
    /// intentionally not sent here, since a strict OpenAI-compatible backend
    /// may reject unknown fields.
    /// </summary>
    public static object BuildChatPayload(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions options,
        int maxTokens)
    {
        var msgs = OpenAiCompatibleToolWire.BuildMessages(messages, options.SystemPrompt);
        var tools = OpenAiCompatibleToolWire.BuildTools(options.Tools);

        return new
        {
            model = modelId,
            messages = msgs,
            stream = true,
            stream_options = new { include_usage = true },
            temperature = options.Temperature,
            max_tokens = maxTokens,
            top_p = options.TopP,
            // Only reached once ConstraintSupport has allowed it; a server that
            // rejects the field returns an error, which GetStreamResponseAsync
            // surfaces rather than retrying unconstrained (r28 doc 01 1.3).
            response_format = LlmOutputConstraintWire.ResponseFormat(options.OutputConstraint),
            frequency_penalty = options.FrequencyPenalty,
            presence_penalty = options.PresencePenalty,
            tools,
            tool_choice = tools is null ? null : "auto"
        };
    }

    public static LlmStreamEvent? ParseStreamEvent(string json)
    {
        StreamChunk? chunk;
        try
        {
            chunk = JsonSerializer.Deserialize<StreamChunk>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }

        var delta = chunk?.Choices?.FirstOrDefault()?.Delta;
        var c = delta?.Content ?? string.Empty;
        var reasoning = delta?.ReasoningContent ?? string.Empty;
        var usage = chunk?.Usage is null
            ? null
            : new ChatTokenUsage(chunk.Usage.PromptTokens, chunk.Usage.CompletionTokens, chunk.Usage.TotalTokens);
        var finishReason = chunk?.Choices?.FirstOrDefault()?.FinishReason;
        var isFinal = usage is not null || finishReason is not null;
        if (string.IsNullOrEmpty(c) && string.IsNullOrEmpty(reasoning) && usage is null && !isFinal)
            return null;
        return new LlmStreamEvent(c, usage, isFinal, FinishReason: finishReason, ReasoningDelta: reasoning);
    }

    private static async IAsyncEnumerable<LlmStreamEvent> YieldEventError(string message)
    {
        yield return LlmStreamEvent.Error(message);
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        // HttpClient is static and shared; do not dispose
    }

    private record ModelsResponse([property: JsonPropertyName("data")] List<ModelData>? Data);
    private record ModelData([property: JsonPropertyName("id")] string Id);
    private record StreamChunk(
        [property: JsonPropertyName("choices")] List<Choice>? Choices,
        [property: JsonPropertyName("usage")] UsageData? Usage);
    private record Choice(
        [property: JsonPropertyName("delta")] Delta? Delta,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);
    private record Delta(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("reasoning_content")] string? ReasoningContent);
    private record UsageData(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int TotalTokens);
}
