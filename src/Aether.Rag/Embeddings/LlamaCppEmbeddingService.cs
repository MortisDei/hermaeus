using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aether.Core.Services;

namespace Aether.Rag.Embeddings;

/// <summary>
/// Calls the llama.cpp server's /v1/embeddings endpoint (OpenAI-compatible).
/// Default: http://localhost:8080
/// Model: nomic-embed-text (768 dims)
/// </summary>
public sealed class LlamaCppEmbeddingService : IEmbeddingService, IDisposable
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly ISettingsService _settings;

    // nomic-embed-text outputs 768 dims; update if you switch models
    public int Dimensions => 768;

    public LlamaCppEmbeddingService(ISettingsService settings)
    {
        _settings = settings;
    }

    private string Base => _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var results = await EmbedBatchAsync([text], ct);
        return results[0];
    }

    public async Task<List<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var payload = new
        {
            model  = _settings.Settings.Rag.EmbeddingModel,
            input  = texts,
            encoding_format = "float"
        };

        var resp = await _http.PostAsJsonAsync($"{Base}/v1/embeddings", payload, ct);
        resp.EnsureSuccessStatusCode();

        var data = await resp.Content.ReadFromJsonAsync<EmbedResponse>(ct)
            ?? throw new InvalidOperationException("Null response from embedding endpoint");

        return data.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();
    }

    public void Dispose()
    {
        // HttpClient is static and shared; do not dispose
    }

    private record EmbedResponse(
        [property: JsonPropertyName("data")] List<EmbedData> Data);

    private record EmbedData(
        [property: JsonPropertyName("index")]     int     Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
