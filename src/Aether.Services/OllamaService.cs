using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class OllamaService : IDisposable
{
    private readonly IRuntimeProfileService _profiles;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public OllamaService(IRuntimeProfileService profiles)
    {
        _profiles = profiles;
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
                        SizeBytes = model.Size,
                        ModifiedAt = model.ModifiedAt
                    });
                }
            }
            catch { }
        }

        return all;
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
        var (profileId, modelName) = ParseId(modelId);
        var profile = _profiles.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null)
        {
            yield return "*Ollama runtime profile not found.*";
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
            var chunk = JsonSerializer.Deserialize<ChatChunk>(line);
            if (!string.IsNullOrEmpty(chunk?.Message?.Content))
                yield return chunk.Message.Content;
            if (chunk?.Done == true) yield break;
        }
    }

    public static bool IsOllamaModelId(string id) => id.StartsWith("ollama:", StringComparison.OrdinalIgnoreCase);
    private static string BuildId(string profileId, string modelName) => $"ollama:{profileId}:{modelName}";
    private static (string ProfileId, string ModelName) ParseId(string id)
    {
        var parts = id.Split(':', 3);
        return parts.Length == 3 ? (parts[1], parts[2]) : (string.Empty, id);
    }

    public void Dispose() => _http.Dispose();

    private sealed record TagsResponse([property: JsonPropertyName("models")] List<OllamaModel>? Models);
    private sealed record OllamaModel(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("modified_at")] DateTime? ModifiedAt);
    private sealed record ChatChunk(
        [property: JsonPropertyName("message")] ChatMessageChunk? Message,
        [property: JsonPropertyName("done")] bool Done);
    private sealed record ChatMessageChunk([property: JsonPropertyName("content")] string Content);
}
