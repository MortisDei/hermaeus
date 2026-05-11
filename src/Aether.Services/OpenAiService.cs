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
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ProviderName => "OpenAI";
    public bool   IsConfigured => !string.IsNullOrWhiteSpace(_settings.Settings.OpenAiApiKey);

    public OpenAiService(ISettingsService settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    private string Base => _settings.Settings.OpenAiBaseUrl.TrimEnd('/');
    private async Task AuthAsync(CancellationToken ct) =>
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _secrets.ResolveAsync(_settings.Settings.OpenAiApiKey, ct));

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
                .Select(m => new LlmModel { Id = m.Id, Name = m.Id, Provider = "OpenAI" })
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
        if (!IsConfigured) return YieldError("*OpenAI API key not configured.*");
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
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
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
            await AuthAsync(ct);
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
            return (false, null, $"\n\n*Error: {ex.Message}*");
        }
    }

    private static async IAsyncEnumerable<string> YieldError(string message)
    {
        yield return message;
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
