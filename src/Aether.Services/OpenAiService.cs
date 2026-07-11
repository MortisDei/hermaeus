using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class OpenAiService : IDisposable
{
    private const string ProviderTagValue = "openai";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

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

    public OpenAiService(ISettingsService settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    private string Base => _settings.Settings.Llm.OpenAiBaseUrl.TrimEnd('/');
    private async Task AuthAsync(CancellationToken ct) =>
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _secrets.ResolveAsync(_settings.Settings.Llm.OpenAiApiKey, ct));

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return [];
        try
        {
            await AuthAsync(ct);
            var resp = await _http.GetAsync($"{Base}/v1/models", ct);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadFromJsonAsync<ModelsResponse>(JsonOpts, ct);
            return data?.Data?
                .Where(m => m.Id.StartsWith("gpt") || m.Id.StartsWith("o1") ||
                            m.Id.StartsWith("o3") || m.Id.StartsWith("o4"))
                .Select(m => new LlmModel { Id = m.Id, Name = m.Id, Provider = "OpenAI", ProviderTag = ProviderTagValue })
                .ToList() ?? [];
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
        return StreamEventsInternal(modelId, messages, options ?? LlmChatOptions.Default, ct);
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
            await AuthAsync(ct);
            var payload = BuildChatPayload(
                modelId, messages, options,
                options.MaxTokens ?? _settings.Settings.Llm.MaxTokens);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/v1/chat/completions")
                { Content = JsonContent.Create(payload, options: JsonOpts) };
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

        var c = chunk?.Choices?.FirstOrDefault()?.Delta?.Content ?? string.Empty;
        var usage = chunk?.Usage is null
            ? null
            : new ChatTokenUsage(chunk.Usage.PromptTokens, chunk.Usage.CompletionTokens, chunk.Usage.TotalTokens);
        var isFinal = usage is not null || chunk?.Choices?.FirstOrDefault()?.FinishReason is not null;
        if (string.IsNullOrEmpty(c) && usage is null && !isFinal)
            return null;
        return new LlmStreamEvent(c, usage, isFinal);
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
    private record Delta([property: JsonPropertyName("content")] string? Content);
    private record UsageData(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int TotalTokens);
}
