using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class LlamaCppService : IDisposable
{
    private const string ProviderTagValue = "llama.cpp";
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _contextLengthCache = new();

    // r14 4.3: tracks whether the last model-fetch against a base URL failed, so
    // a persistently-down server logs one line per up->down transition instead
    // of one per call. true = currently in a logged down-state.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _modelsFetchDown = new();

    /// <summary>
    /// Optional gate (r14 4.3): returns true when the managed server for a base
    /// URL is known Stopped, letting GetModelsAsync skip the HTTP attempt and
    /// the error entirely. Unset means always probe.
    /// </summary>
    public Func<string, bool>? IsBaseUrlKnownStopped { get; set; }
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly IRuntimeLogService _logs;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly ProviderDescriptor Descriptor = new(
        ProviderTagValue, "llama.cpp", ProviderKind.ManagedLocal,
        ProviderCapabilities.Streaming | ProviderCapabilities.UsageReporting);

    public string ProviderName => "llama.cpp";
    public bool   IsConfigured => true;

    public LlamaCppService(ISettingsService settings, IRuntimeLogService logs, HttpClient? http = null)
    {
        _settings = settings;
        _logs = logs;
        _http = http ?? SharedHttp;
    }

    private string Base => _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        var baseUrl = Base;

        // r14 4.3: a connection-refused probe against our own stopped managed
        // server is the expected state, not an error. Skip the attempt (and any
        // log) entirely when the caller knows the server is Stopped.
        if (IsBaseUrlKnownStopped?.Invoke(baseUrl) == true)
            return [];

        try
        {
            var resp = await _http.GetAsync($"{baseUrl}/v1/models", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                _modelsFetchDown[baseUrl] = false;
                return [];
            }

            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadFromJsonAsync<ModelsResponse>(JsonOpts, ct);
            var models = data?.Data?
                .Select(m => new LlmModel { Id = m.Id, Name = Path.GetFileNameWithoutExtension(m.Id), Provider = "llama.cpp", ProviderTag = ProviderTagValue })
                .ToList() ?? [];

            // llama-server hosts exactly one model at a time, so the probed context
            // length from /props applies to whatever it returned above.
            if (models.Count > 0)
            {
                var contextLength = await ProbeContextLengthAsync(models[0].Id, ct);
                foreach (var model in models)
                    model.ProbedContextLength = contextLength;
            }

            _modelsFetchDown[baseUrl] = false;
            return models;
        }
        catch (Exception ex)
        {
            // r14 4.3: log once per up->down transition; repeats within the same
            // down-state are silent so a never-started server produces at most
            // one line per app run.
            var wasDown = _modelsFetchDown.TryGetValue(baseUrl, out var down) && down;
            _modelsFetchDown[baseUrl] = true;
            if (!wasDown)
                _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service, $"llama.cpp models unavailable at {baseUrl}: {ex.Message}"));
            return [];
        }
    }

    /// <summary>
    /// Cached by (baseUrl, modelId) rather than baseUrl alone (r11 2.5):
    /// restarting the managed server with a different model or --ctx-size
    /// keeps the same 127.0.0.1:port, so a baseUrl-only key served the
    /// previous model's n_ctx forever. Keying on the id /v1/models just
    /// reported means a model swap is itself a cache miss - no separate
    /// invalidation hook needed.
    /// </summary>
    private async Task<int?> ProbeContextLengthAsync(string modelId, CancellationToken ct)
    {
        var baseUrl = Base;
        var cacheKey = $"{baseUrl}|{modelId}";
        if (_contextLengthCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var resp = await _http.GetAsync($"{baseUrl}/props", ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            // r14 2.3: a conversation hits the per-slot ceiling, not the total
            // context. Slots default to 1 so the two coincide, but the math
            // stays honest for anyone who raises Slots.
            var slots = _settings.Settings.ManagedServers
                .Where(s => !s.EmbeddingsMode)
                .Select(s => s.Slots)
                .FirstOrDefault(1);
            var nCtx = ParsePerSlotContextLength(await resp.Content.ReadAsStringAsync(ct), slots);
            if (nCtx is { } value)
            {
                if (_contextLengthCache.Count > 50)
                    _contextLengthCache.Clear();

                _contextLengthCache[cacheKey] = value;
                return value;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service, $"Failed to probe context length for {baseUrl}: {ex.Message}"));
            return null;
        }
    }

    /// <summary>Reads n_ctx from a llama-server /props response. Public for tests.</summary>
    public static int? ParsePropsContextLength(string propsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(propsJson);
            var root = doc.RootElement;
            return TryGetInt(root, "n_ctx")
                ?? (root.TryGetProperty("default_generation_settings", out var settings) ? TryGetInt(settings, "n_ctx") : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the per-slot context length from a llama-server /props response
    /// (r14 2.3). <c>default_generation_settings.n_ctx</c> is already the
    /// per-slot ceiling (n_ctx_slot); when only the top-level total
    /// <c>n_ctx</c> is exposed it is divided by the configured slot count.
    /// Public for tests.
    /// </summary>
    public static int? ParsePerSlotContextLength(string propsJson, int slots)
    {
        try
        {
            using var doc = JsonDocument.Parse(propsJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("default_generation_settings", out var settings) && TryGetInt(settings, "n_ctx") is int perSlot)
                return perSlot;
            return TryGetInt(root, "n_ctx") is int total ? total / Math.Max(1, slots) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? TryGetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var prop) && prop.TryGetInt32(out var value) ? value : null;

    public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        options ??= LlmChatOptions.Default;
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
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            return (true, resp, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, null, $"\n\n*llama.cpp error: {ex.Message}*");
        }
    }

    /// <summary>
    /// llama.cpp's OpenAI-compatible endpoint accepts several sampling
    /// parameters beyond the OpenAI spec as flat top-level extensions
    /// (top_k, min_p, repeat_penalty); all six new sampling options are
    /// forwarded here, and omitted from the JSON body when unset because
    /// <c>JsonOpts</c> ignores null properties.
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
            // r14 2.2: pin the prompt-cache on explicitly rather than relying on
            // llama-server's current default, so a follow-up send reprocesses
            // only the changed suffix instead of the whole prompt.
            cache_prompt = true,
            temperature = options.Temperature,
            max_tokens = maxTokens,
            top_p = options.TopP,
            top_k = options.TopK,
            min_p = options.MinP,
            repeat_penalty = options.RepeatPenalty,
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
        var serverTimings = chunk?.Timings is null
            ? null
            : new ChatServerTimings(chunk.Timings.PromptN, chunk.Timings.PromptMs, chunk.Timings.PredictedN, chunk.Timings.PredictedMs);
        var isFinal = usage is not null || chunk?.Choices?.FirstOrDefault()?.FinishReason is not null;
        if (string.IsNullOrEmpty(c) && usage is null && serverTimings is null && !isFinal)
            return null;
        return new LlmStreamEvent(c, usage, isFinal, ServerTimings: serverTimings);
    }

    public void Dispose()
    {
        // HttpClient is static and shared; do not dispose
    }

    private record ModelsResponse([property: JsonPropertyName("data")] List<ModelData>? Data);
    private record ModelData([property: JsonPropertyName("id")] string Id);
    private record StreamChunk(
        [property: JsonPropertyName("choices")] List<Choice>? Choices,
        [property: JsonPropertyName("usage")] UsageData? Usage,
        [property: JsonPropertyName("timings")] TimingsData? Timings);
    private record Choice(
        [property: JsonPropertyName("delta")] Delta? Delta,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);
    private record Delta([property: JsonPropertyName("content")] string? Content);
    private record UsageData(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int TotalTokens);
    /// <summary>llama-server's own prompt/generation timing, present on the final streamed chunk.</summary>
    private record TimingsData(
        [property: JsonPropertyName("prompt_n")] int? PromptN,
        [property: JsonPropertyName("prompt_ms")] double? PromptMs,
        [property: JsonPropertyName("predicted_n")] int? PredictedN,
        [property: JsonPropertyName("predicted_ms")] double? PredictedMs);
}
