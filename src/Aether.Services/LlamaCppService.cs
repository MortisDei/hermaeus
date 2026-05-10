using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class LlamaCppService : IDisposable
{
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

    private string Base => _settings.Settings.LlamaCppBaseUrl.TrimEnd('/');

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{Base}/v1/models", ct);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadFromJsonAsync<ModelsResponse>(JsonOpts, ct);
            return data?.Data?
                .Select(m => new LlmModel { Id = m.Id, Name = m.Id, Provider = "llama.cpp" })
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
        return StreamChatInternal(modelId, messages, systemPrompt, temperature, ct);
    }

    private async IAsyncEnumerable<string> StreamChatInternal(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt,
        double temperature,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (success, resp, error) = await GetStreamResponseAsync(modelId, messages, systemPrompt, temperature, ct);
        
        if (!success)
        {
            yield return error;
            yield break;
        }

        using (resp!)
        using (var stream = await resp!.Content.ReadAsStreamAsync(ct))
        using (var reader = new StreamReader(stream))
        {
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;
                var json = line[6..];
                if (json == "[DONE]") break;
                
                var chunk = JsonSerializer.Deserialize<StreamChunk>(json, JsonOpts);
                var c = chunk?.Choices?[0]?.Delta?.Content;
                if (!string.IsNullOrEmpty(c)) yield return c;
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
            var msgs = messages.Select(m => new { role = m.Role, content = m.Content }).ToList<object>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                msgs.Insert(0, new { role = "system", content = systemPrompt });

            var payload = new { model = modelId, messages = msgs, stream = true,
                                temperature, max_tokens = _settings.Settings.MaxTokens };
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

    public Task PullModelAsync(string m, IProgress<string>? p = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteModelAsync(string m, CancellationToken ct = default) => Task.CompletedTask;
    public void Dispose() => _http.Dispose();

    private record ModelsResponse([property: JsonPropertyName("data")] List<ModelData>? Data);
    private record ModelData([property: JsonPropertyName("id")] string Id);
    private record StreamChunk([property: JsonPropertyName("choices")] List<Choice>? Choices);
    private record Choice([property: JsonPropertyName("delta")] Delta? Delta);
    private record Delta([property: JsonPropertyName("content")] string? Content);
}
