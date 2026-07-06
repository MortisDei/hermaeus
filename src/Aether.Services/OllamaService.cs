using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class OllamaService : IDisposable
{
    private const string ProviderTagValue = "ollama";
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly IRuntimeProfileService _profiles;
    private readonly HttpClient _http;

    public OllamaService(IRuntimeProfileService profiles)
    {
        _profiles = profiles;
        _http = SharedHttp;
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
                        ModifiedAt = model.ModifiedAt
                    });
                }
            }
            catch { }
        }

        return all;
    }

    public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        options ??= LlmChatOptions.Default;
        var systemPrompt = options.SystemPrompt;
        var temperature = options.Temperature;
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

        var req = new
        {
            model = modelName,
            messages = msgs,
            stream = true,
            options = new { temperature }
        };

        using var resp = await _http.PostAsJsonAsync($"{profile.BaseUrl.TrimEnd('/')}/api/chat", req, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
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

            if (!string.IsNullOrEmpty(chunk?.Message?.Content))
                yield return new LlmStreamEvent(chunk.Message.Content);
            if (chunk?.Done == true)
            {
                if (ToUsage(chunk) is { } usage)
                    yield return new LlmStreamEvent(Usage: usage, IsFinal: true);
                else
                    yield return new LlmStreamEvent(IsFinal: true);
                yield break;
            }
        }
    }

    public static ChatTokenUsage? ParseUsageForTest(string json) =>
        ToUsage(JsonSerializer.Deserialize<ChatChunk>(json));

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
        [property: JsonPropertyName("eval_count")] int? EvalCount);
    private sealed record ChatMessageChunk([property: JsonPropertyName("content")] string Content);
}
