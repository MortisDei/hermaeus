using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Serialization;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Rag.Embeddings;

/// <summary>
/// Calls the llama.cpp server's /v1/embeddings endpoint (OpenAI-compatible).
/// Default: RAG EmbeddingBaseUrl, which now defaults to a separate localhost port.
/// Model: nomic-embed-text (768 dims)
/// </summary>
public sealed class LlamaCppEmbeddingService : IEmbeddingService, IDisposable
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly IRuntimeLogService? _runtimeLogs;
    private bool _fallbackLogged;
    private readonly object _fallbackLogGate = new();

    // nomic-embed-text outputs 768 dims; update if you switch models
    public int Dimensions => 768;

    public LlamaCppEmbeddingService(ISettingsService settings, HttpClient? http = null, IRuntimeLogService? runtimeLogs = null)
    {
        _settings = settings;
        _http = http ?? SharedHttp;
        _runtimeLogs = runtimeLogs;
    }

    private string Base
    {
        get
        {
            var configured = _settings.Settings.Rag.EmbeddingBaseUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.TrimEnd('/');

            return _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var results = await EmbedBatchAsync([text], ct);
        return results[0];
    }

    public async Task<List<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        LogFallbackOnce();

        var payload = new
        {
            model  = _settings.Settings.Rag.EmbeddingModel,
            input  = texts,
            encoding_format = "float"
        };

        using var resp = await _http.PostAsJsonAsync($"{Base}/v1/embeddings", payload, ct);
        if (!resp.IsSuccessStatusCode)
            throw await CreateEmbeddingEndpointExceptionAsync(resp, ct);

        var data = await resp.Content.ReadFromJsonAsync<EmbedResponse>(ct)
            ?? throw new InvalidOperationException("Null response from embedding endpoint");

        return data.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();
    }

    /// <summary>
    /// The zero-config fallback to the chat server queues embed calls behind
    /// generation on a single-slot llama-server (r9 01-send-path-latency.md
    /// 1.4). Kept, but surfaced once so it stops being a silent footgun.
    /// </summary>
    private void LogFallbackOnce()
    {
        if (_runtimeLogs is null) return;
        var configured = _settings.Settings.Rag.EmbeddingBaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(configured)) return;

        lock (_fallbackLogGate)
        {
            if (_fallbackLogged) return;
            _fallbackLogged = true;
        }

        var chatUrl = _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');
        _runtimeLogs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
            $"Rag.EmbeddingBaseUrl is not set; embedding requests fall back to the chat server at {chatUrl}. " +
            "Configure a dedicated embeddings server to avoid queuing behind chat generation."));
    }

    private async Task<Exception> CreateEmbeddingEndpointExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var reason = response.ReasonPhrase ?? "Unknown";
        var body = await response.Content.ReadAsStringAsync(ct);
        var baseMessage = $"Embedding request failed at {Base}/v1/embeddings with HTTP {status} ({reason}).";

        if (response.StatusCode is HttpStatusCode.NotImplemented or HttpStatusCode.NotFound)
        {
            var hint = "Start llama-server with --embeddings and point the RAG EmbeddingBaseUrl (older configs may refer to LlamaCppBaseUrl) to that embeddings-capable server.";
            if (string.IsNullOrWhiteSpace(body))
                return new InvalidOperationException($"{baseMessage} {hint}");

            return new InvalidOperationException($"{baseMessage} {hint} Server response: {body.Trim()}");
        }

        if (response.StatusCode == HttpStatusCode.BadRequest
            && body.Contains("Pooling type 'none'", StringComparison.OrdinalIgnoreCase))
        {
            var hint = "Your llama.cpp model/server is not configured for OpenAI-compatible embeddings pooling. Use an embedding model and start the server with --embeddings --pooling mean (or cls), then retry.";
            return new InvalidOperationException($"{baseMessage} {hint} Server response: {body.Trim()}");
        }

        if (string.IsNullOrWhiteSpace(body))
            return new HttpRequestException(baseMessage, null, response.StatusCode);

        return new HttpRequestException($"{baseMessage} Server response: {body.Trim()}", null, response.StatusCode);
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
