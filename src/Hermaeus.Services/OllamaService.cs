using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class OllamaService : IDisposable
{
    private const string ProviderTagValue = "ollama";
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _contextLengthCache = new();
    private readonly RuntimeProfileService _profiles;
    private readonly HttpClient _http;

    public static readonly ProviderDescriptor Descriptor = new(
        ProviderTagValue, "Ollama", ProviderKind.LocalApi,
        ProviderCapabilities.Streaming | ProviderCapabilities.UsageReporting
        | ProviderCapabilities.ModelPull | ProviderCapabilities.ModelDelete);

    /// <summary>
    /// Ollama's <c>format</c> field takes a JSON schema. It has no grammar
    /// surface, so a grammar constraint is refused rather than dropped.
    /// </summary>
    public const LlmConstraintSupport ConstraintSupport = LlmConstraintSupport.JsonSchema;

    public OllamaService(RuntimeProfileService profiles, HttpClient? http = null)
    {
        _profiles = profiles;
        _http = http ?? SharedHttp;
    }

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        var all = new List<LlmModel>();
        foreach (var profile in _profiles.Profiles.Where(p => p.Enabled && p.Kind == RuntimeKind.Ollama))
        {
            try
            {
                var tags = await _http.GetFromJsonAsync<TagsResponse>($"{profile.BaseUrl.TrimEnd('/')}/api/tags", ct);
                foreach (var model in tags?.Models ?? [])
                {
                    all.Add(new LlmModel
                    {
                        Id = BuildId(profile.Id, model.Name),
                        Name = model.Name,
                        Provider = $"Ollama:{profile.Name}",
                        ProviderTag = ProviderTagValue,
                        SizeBytes = model.Size,
                        ModifiedAt = model.ModifiedAt,
                        SupportsOutputConstraints = true,
                        ProbedContextLength = await ProbeContextLengthAsync(profile.BaseUrl, model.Name, ct)
                    });
                }
            }
            catch { }
        }

        return all;
    }

    private async Task<int?> ProbeContextLengthAsync(string baseUrl, string modelName, CancellationToken ct)
    {
        var cacheKey = $"{baseUrl}:{modelName}";
        if (_contextLengthCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            using var resp = await _http.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/api/show", new { model = modelName }, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var contextLength = ParseShowContextLength(await resp.Content.ReadAsStringAsync(ct));
            if (contextLength is { } value)
                _contextLengthCache[cacheKey] = value;

            return contextLength;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads "{architecture}.context_length" from an Ollama /api/show response. Public for tests.</summary>
    public static int? ParseShowContextLength(string showJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(showJson);
            if (!doc.RootElement.TryGetProperty("model_info", out var modelInfo))
                return null;

            var architecture = modelInfo.TryGetProperty("general.architecture", out var arch) ? arch.GetString() : null;
            if (string.IsNullOrEmpty(architecture))
                return null;

            return modelInfo.TryGetProperty($"{architecture}.context_length", out var ctxProp) && ctxProp.TryGetInt32(out var contextLength)
                ? contextLength
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        options ??= LlmChatOptions.Default;
        if (LlmOutputConstraintWire.DescribeRefusal(options.OutputConstraint, ConstraintSupport, "Ollama") is { } refusal)
        {
            yield return LlmStreamEvent.Error(refusal);
            yield break;
        }

        var systemPrompt = options.SystemPrompt;
        var (profileId, modelName) = ParseId(modelId);
        var profile = _profiles.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null)
        {
            yield return LlmStreamEvent.Error("*Ollama runtime profile not found.*");
            yield break;
        }

        var msgs = messages.Select(m => new { role = m.Role, content = m.Content }).ToList<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            msgs.Insert(0, new { role = "system", content = systemPrompt });

        var tools = OpenAiCompatibleToolWire.BuildTools(options.Tools);
        var req = new
        {
            model = modelName,
            messages = msgs,
            stream = true,
            tools,
            // Ollama takes the schema document itself, unwrapped (r28 doc 01 1.3).
            format = LlmOutputConstraintWire.OllamaFormat(options.OutputConstraint),
            options = new
            {
                temperature = options.Temperature,
                top_p = options.TopP,
                top_k = options.TopK,
                min_p = options.MinP,
                repeat_penalty = options.RepeatPenalty,
                frequency_penalty = options.FrequencyPenalty,
                presence_penalty = options.PresencePenalty
            }
        };

        // r11 2.1: PostAsJsonAsync completes only once the full response body is
        // buffered (ResponseContentRead default), so with stream=true the call
        // did not return until generation finished, replaying buffered lines
        // afterward - no incremental tokens, and an unreachable endpoint threw
        // out of the iterator instead of yielding an error event like
        // LlamaCppService/OpenAiService. ResponseHeadersRead plus an explicit
        // error event fixes both.
        var (success, resp, error) = await GetStreamResponseAsync(profile.BaseUrl, req, ct);
        if (!success)
        {
            yield return LlmStreamEvent.Error(error);
            yield break;
        }

        using (resp!)
        await using (var stream = await resp!.Content.ReadAsStreamAsync(ct))
        using (var reader = new StreamReader(stream))
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                ChatChunk? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<ChatChunk>(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(chunk?.Message?.Content) || !string.IsNullOrEmpty(chunk?.Message?.Thinking))
                    yield return new LlmStreamEvent(chunk.Message?.Content ?? string.Empty, ReasoningDelta: chunk.Message?.Thinking ?? string.Empty);
                if (chunk?.Done == true)
                {
                    // Ollama returns tool calls whole in the terminal chunk
                    // rather than fragmenting them across the stream the way
                    // OpenAI-compatible servers do, so no accumulator is needed.
                    var toolCalls = ToToolCalls(chunk);
                    var usage = ToUsage(chunk);
                    yield return new LlmStreamEvent(Usage: usage, IsFinal: true, ToolCalls: toolCalls, FinishReason: chunk.DoneReason);
                    yield break;
                }
            }
        }
    }

    private async Task<(bool Success, HttpResponseMessage? Response, string Error)> GetStreamResponseAsync(
        string baseUrl, object payload, CancellationToken ct)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/chat")
                { Content = JsonContent.Create(payload, options: JsonOpts) };
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            return (true, resp, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, null, $"\n\n*Ollama error: {ex.Message}*");
        }
    }

    private static IReadOnlyList<LlmToolCallRequest>? ToToolCalls(ChatChunk? chunk)
    {
        var calls = chunk?.Message?.ToolCalls;
        if (calls is null || calls.Count == 0) return null;
        return calls
            .Where(c => !string.IsNullOrEmpty(c.Function?.Name))
            .Select(c => new LlmToolCallRequest(
                Guid.NewGuid().ToString("N"),
                c.Function!.Name,
                c.Function.Arguments.ValueKind == JsonValueKind.Undefined ? "{}" : c.Function.Arguments.GetRawText()))
            .ToList();
    }

    public static ChatTokenUsage? ParseUsageForTest(string json) =>
        ToUsage(JsonSerializer.Deserialize<ChatChunk>(json));

    public static IReadOnlyList<LlmToolCallRequest>? ParseToolCallsForTest(string json) =>
        ToToolCalls(JsonSerializer.Deserialize<ChatChunk>(json));

    private static ChatTokenUsage? ToUsage(ChatChunk? chunk)
    {
        if (chunk is null) return null;
        var prompt = chunk.PromptEvalCount ?? 0;
        var completion = chunk.EvalCount ?? 0;
        var total = prompt + completion;
        return total <= 0 ? null : new ChatTokenUsage(prompt, completion, total);
    }

    public static bool IsOllamaModelId(string id) => id.StartsWith("ollama:", StringComparison.OrdinalIgnoreCase);
    private static string BuildId(string profileId, string modelName) => $"ollama:{profileId}:{modelName}";
    private static (string ProfileId, string ModelName) ParseId(string id)
    {
        if (!IsOllamaModelId(id))
            return (string.Empty, id);

        var firstSeparator = id.IndexOf(':');
        var secondSeparator = id.IndexOf(':', firstSeparator + 1);
        if (firstSeparator < 0 || secondSeparator < 0 || secondSeparator == id.Length - 1)
            return (string.Empty, id);

        return (id[(firstSeparator + 1)..secondSeparator], id[(secondSeparator + 1)..]);
    }

    public void Dispose()
    {
    }

    private sealed record TagsResponse([property: JsonPropertyName("models")] List<OllamaModel>? Models);
    private sealed record OllamaModel(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("modified_at")] DateTime? ModifiedAt);
    private sealed record ChatChunk(
        [property: JsonPropertyName("message")] ChatMessageChunk? Message,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount,
        [property: JsonPropertyName("done_reason")] string? DoneReason);
    private sealed record ChatMessageChunk(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("tool_calls")] List<OllamaToolCallChunk>? ToolCalls,
        [property: JsonPropertyName("thinking")] string? Thinking);
    private sealed record OllamaToolCallChunk([property: JsonPropertyName("function")] OllamaFunctionCallChunk? Function);
    private sealed record OllamaFunctionCallChunk(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] JsonElement Arguments);
}
