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
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _contextLengthCache = new();
    private readonly ISettingsService _settings;

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

    public LlamaCppService(ISettingsService settings)
    {
        _settings = settings;
    }

    private string Base => _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{Base}/v1/models", ct);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadFromJsonAsync<ModelsResponse>(JsonOpts, ct);
            var models = data?.Data?
                .Select(m => new LlmModel { Id = m.Id, Name = m.Id, Provider = "llama.cpp", ProviderTag = ProviderTagValue })
                .ToList() ?? [];

            // llama-server hosts exactly one model at a time, so the probed context
            // length from /props applies to whatever it returned above.
            if (models.Count > 0)
            {
                var contextLength = await ProbeContextLengthAsync(ct);
                foreach (var model in models)
                    model.ProbedContextLength = contextLength;
            }

            return models;
        }
        catch { return []; }
    }

    private async Task<int?> ProbeContextLengthAsync(CancellationToken ct)
    {
        var baseUrl = Base;
        if (_contextLengthCache.TryGetValue(baseUrl, out var cached))
            return cached;

        try
        {
            var resp = await _http.GetAsync($"{baseUrl}/props", ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var nCtx = ParsePropsContextLength(await resp.Content.ReadAsStringAsync(ct));
            if (nCtx is { } value)
            {
                _contextLengthCache[baseUrl] = value;
                return value;
            }

            return null;
        }
        catch
        {
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

                var evt = ParseStreamEvent(json);
                if (evt is not null)
                    yield return evt;
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
                modelId, messages, options.SystemPrompt, options.Temperature,
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

    public static object BuildChatPayload(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt,
        double temperature,
        int maxTokens)
    {
        var msgs = messages.Select(m => new { role = m.Role, content = m.Content }).ToList<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            msgs.Insert(0, new { role = "system", content = systemPrompt });

        return new
        {
            model = modelId,
            messages = msgs,
            stream = true,
            stream_options = new { include_usage = true },
            temperature,
            max_tokens = maxTokens
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
