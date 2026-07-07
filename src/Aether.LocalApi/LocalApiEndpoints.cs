using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag;
using Aether.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aether.LocalApi;

/// <summary>
/// The minimal read/action surface the local API exposes: chat completion,
/// memory query, RAG query, and a read-only models list. Deliberately not
/// full feature parity with the desktop app (no agent, benchmark, or
/// settings endpoints in this pass). Every call is logged to the shared
/// <see cref="ITraceStore"/> as <see cref="TraceKind.LocalApi"/>, keyed by
/// the caller's self-reported <c>X-Aether-Client</c> header, so Privacy
/// Audit can show which local apps have been using Aether as their AI
/// substrate.
/// </summary>
public static class LocalApiEndpoints
{
    public const string ClientHeaderName = "X-Aether-Client";

    public static void MapLocalApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/completions", async (ChatCompletionRequest req, ILlmService llm, ITraceStore traces, HttpContext http, CancellationToken ct) =>
        {
            var client = CallerName(http);
            var sw = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(req.ModelId) || req.Messages is null || req.Messages.Count == 0)
            {
                await LogCallAsync(traces, client, "chat.completions", sw, "modelId and messages required", ct);
                return Results.BadRequest("modelId and a non-empty messages array are required.");
            }

            var messages = req.Messages.Select(m => new ChatMessage(m.Role, m.Content)).ToList();
            var options = new LlmChatOptions
            {
                Temperature = req.Temperature ?? LlmChatOptions.Default.Temperature,
                MaxTokens = req.MaxTokens
            };

            var content = new StringBuilder();
            ChatTokenUsage? usage = null;
            await foreach (var evt in llm.StreamChatAsync(req.ModelId, messages, options, ct))
            {
                content.Append(evt.ContentDelta);
                if (evt.Usage is not null)
                    usage = evt.Usage;
            }

            await LogCallAsync(traces, client, "chat.completions", sw, string.Empty, ct,
                modelId: req.ModelId, promptTokens: usage?.PromptTokens, completionTokens: usage?.CompletionTokens);
            return Results.Ok(new ChatCompletionResponse(content.ToString(), usage?.PromptTokens, usage?.CompletionTokens));
        });

        app.MapGet("/v1/memory/query", async (string q, int? limit, IMemoryStore memories, ITraceStore traces, HttpContext http, CancellationToken ct) =>
        {
            var client = CallerName(http);
            var sw = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(q))
            {
                await LogCallAsync(traces, client, "memory.query", sw, "q required", ct);
                return Results.BadRequest("q is required.");
            }

            var results = await memories.SearchAsync(q, ct);
            var take = Math.Clamp(limit ?? 10, 1, 100);
            var mapped = results
                .Take(take)
                .Select(m => new MemoryDto(m.Id, m.Category, m.Content, m.ImportanceScore))
                .ToList();

            await LogCallAsync(traces, client, "memory.query", sw, string.Empty, ct);
            return Results.Ok(new MemoryQueryResponse(mapped));
        });

        app.MapPost("/v1/rag/query", async (RagQueryRequest req, RagQueryService rag, ITraceStore traces, HttpContext http, CancellationToken ct) =>
        {
            var client = CallerName(http);
            var sw = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(req.DatasetId) || string.IsNullOrWhiteSpace(req.Question))
            {
                await LogCallAsync(traces, client, "rag.query", sw, "datasetId and question required", ct);
                return Results.BadRequest("datasetId and question are required.");
            }

            var opts = new RagQueryOptions(TopK: Math.Clamp(req.TopK ?? 5, 1, 20));
            var answer = new StringBuilder();
            var sources = new List<RagSourceDto>();

            await foreach (var token in rag.StreamQueryAsync(req.DatasetId, req.Question, opts, ct))
            {
                if (token.StartsWith("__RAG_SOURCES__", StringComparison.Ordinal))
                {
                    sources = ParseSources(token);
                    continue;
                }
                if (token.StartsWith("__RAG_TRACE__", StringComparison.Ordinal))
                    continue;

                answer.Append(token);
            }

            await LogCallAsync(traces, client, "rag.query", sw, string.Empty, ct, datasetId: req.DatasetId);
            return Results.Ok(new RagQueryResponse(answer.ToString(), sources));
        });

        app.MapGet("/v1/models", async (ILlmService llm, ModelProfileService profiles, ITraceStore traces, HttpContext http, CancellationToken ct) =>
        {
            var client = CallerName(http);
            var sw = Stopwatch.StartNew();
            var models = await llm.GetModelsAsync(ct);
            profiles.ApplyProfiles(models);
            var mapped = models
                .Where(m => m.IsVisible)
                .Select(m => new ModelDto(m.Id, m.Name, m.Provider, m.DefaultContextSize ?? m.ProbedContextLength))
                .ToList();

            await LogCallAsync(traces, client, "models.list", sw, string.Empty, ct);
            return Results.Ok(new ModelsResponse(mapped));
        });
    }

    private static string CallerName(HttpContext http) =>
        http.Request.Headers.TryGetValue(ClientHeaderName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString().Trim()
            : "unknown";

    private static async Task LogCallAsync(
        ITraceStore traces,
        string client,
        string operation,
        Stopwatch sw,
        string error,
        CancellationToken ct,
        string? modelId = null,
        string? datasetId = null,
        int? promptTokens = null,
        int? completionTokens = null)
    {
        try
        {
            await traces.AppendAsync(new TraceRecord
            {
                Kind = TraceKind.LocalApi,
                SourceId = client,
                ModelId = modelId ?? string.Empty,
                Operation = operation,
                TotalLatencyMs = sw.ElapsedMilliseconds,
                PromptTokens = promptTokens ?? 0,
                CompletionTokens = completionTokens ?? 0,
                TotalTokens = (promptTokens ?? 0) + (completionTokens ?? 0),
                Error = error,
                DetailJson = $$"""{"client":"{{client}}","datasetId":"{{datasetId ?? string.Empty}}"}"""
            }, ct);
        }
        catch
        {
            // Tracing must never break a caller's request.
        }
    }

    private static List<RagSourceDto> ParseSources(string header)
    {
        try
        {
            var json = Regex.Match(header, "__RAG_SOURCES__(.+)__END_SOURCES__").Groups[1].Value;
            var elements = JsonSerializer.Deserialize<List<JsonElement>>(json);
            if (elements is null)
                return [];

            return elements.Select(el => new RagSourceDto(
                el.GetProperty("title").GetString() ?? string.Empty,
                el.GetProperty("file").GetString() ?? string.Empty,
                el.TryGetProperty("path", out var path) ? path.GetString() ?? string.Empty : string.Empty,
                el.GetProperty("score").GetSingle())).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return [];
        }
    }
}
