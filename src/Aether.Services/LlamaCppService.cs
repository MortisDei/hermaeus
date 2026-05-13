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
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ProviderName => "llama.cpp";
    public bool   IsConfigured => true;

    public LlamaCppService(ISettingsService settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    private string Base => _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{Base}/v1/models", ct);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadFromJsonAsync<ModelsResponse>(JsonOpts, ct);
            return data?.Data?
                .Select(m => new LlmModel { Id = m.Id, Name = m.Id, Provider = "llama.cpp", ProviderTag = ProviderTagValue })
                .ToList() ?? [];
        }
        catch { return []; }
    }

    public IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        CancellationToken ct = default)
    {
        return StreamTextInternal(modelId, messages, systemPrompt, temperature, ct);
    }

    public IAsyncEnumerable<LlmStreamEvent> StreamChatEventsAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        CancellationToken ct = default)
    {
        return StreamEventsInternal(modelId, messages, systemPrompt, temperature, ct);
    }

    private async IAsyncEnumerable<string> StreamTextInternal(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt,
        double temperature,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in StreamEventsInternal(modelId, messages, systemPrompt, temperature, ct))
        {
            if (!string.IsNullOrEmpty(evt.ContentDelta))
                yield return evt.ContentDelta;
        }
    }

    private async IAsyncEnumerable<LlmStreamEvent> StreamEventsInternal(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt,
        double temperature,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (success, resp, error) = await GetStreamResponseAsync(modelId, messages, systemPrompt, temperature, ct);
        
        if (!success)
        {
            yield return new LlmStreamEvent(error, IsFinal: true);
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
        string? systemPrompt,
        double temperature,
        CancellationToken ct)
    {
        try
        {
            var payload = BuildChatPayload(modelId, messages, systemPrompt, temperature, _settings.Settings.Llm.MaxTokens);
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
        var chunk = JsonSerializer.Deserialize<StreamChunk>(json, JsonOpts);
        var c = chunk?.Choices?.FirstOrDefault()?.Delta?.Content ?? string.Empty;
        var usage = chunk?.Usage is null
            ? null
            : new ChatTokenUsage(chunk.Usage.PromptTokens, chunk.Usage.CompletionTokens, chunk.Usage.TotalTokens);
        var isFinal = usage is not null || chunk?.Choices?.FirstOrDefault()?.FinishReason is not null;
        if (string.IsNullOrEmpty(c) && usage is null && !isFinal)
            return null;
        return new LlmStreamEvent(c, usage, isFinal);
    }

    public Task PullModelAsync(string m, IProgress<string>? p = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteModelAsync(string m, CancellationToken ct = default) => Task.CompletedTask;
    public void Dispose() => _http.Dispose();

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
