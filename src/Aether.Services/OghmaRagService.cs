using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// Calls the oi FastAPI sidecar (Oghma Infinium) for RAG queries.
/// The sidecar exposes POST /query, GET /datasets, GET /health.
/// Start it with: uvicorn oi_api:app --port 8765 (see docs/oi-sidecar.md)
/// </summary>
public sealed class OghmaRagService : IRagService, IDisposable
{
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;

    public bool IsAvailable { get; private set; }

    public OghmaRagService(ISettingsService settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    private string Base => _settings.Settings.Rag.ServiceUrl.TrimEnd('/');

    public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{Base}/health", ct);
            IsAvailable = resp.IsSuccessStatusCode;
        }
        catch { IsAvailable = false; }
        return IsAvailable;
    }

    public async Task<IReadOnlyList<string>> GetDatasetsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<string>>($"{Base}/datasets", ct);
            return result ?? [];
        }
        catch { return []; }
    }

    public async Task<RagResult> QueryAsync(string dataset, string question, int topK = 5, CancellationToken ct = default)
    {
        var payload = new { dataset, question, top_k = topK };
        var resp = await _http.PostAsJsonAsync($"{Base}/query", payload, ct);
        resp.EnsureSuccessStatusCode();
        var data = await resp.Content.ReadFromJsonAsync<OiQueryResponse>(ct)
            ?? throw new InvalidOperationException("Empty response from oi sidecar");

        return new RagResult(
            data.Answer,
            data.Sources.Select(s => new RagSource(s.Title, s.Type, s.Url, s.Score)).ToList(),
            data.RetrievalMs,
            data.GenerationMs);
    }

    public void Dispose() => _http.Dispose();

    private record OiQueryResponse(
        [property: JsonPropertyName("answer")]       string Answer,
        [property: JsonPropertyName("sources")]      List<OiSource> Sources,
        [property: JsonPropertyName("retrieval_ms")] double RetrievalMs,
        [property: JsonPropertyName("generation_ms")]double GenerationMs);

    private record OiSource(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("type")]  string Type,
        [property: JsonPropertyName("url")]   string Url,
        [property: JsonPropertyName("score")] double Score);
}
