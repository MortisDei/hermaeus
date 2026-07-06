using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Core.Services;
using Aether.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aether.LocalApi;

/// <summary>
/// The minimal read/action surface the local API exposes: chat completion,
/// memory query, and RAG query. Deliberately not full feature parity with the
/// desktop app (no agent, benchmark, or settings endpoints in this pass).
/// </summary>
public static class LocalApiEndpoints
{
    public static void MapLocalApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/completions", async (ChatCompletionRequest req, ILlmService llm, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.ModelId) || req.Messages is null || req.Messages.Count == 0)
                return Results.BadRequest("modelId and a non-empty messages array are required.");

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

            return Results.Ok(new ChatCompletionResponse(content.ToString(), usage?.PromptTokens, usage?.CompletionTokens));
        });

        app.MapGet("/v1/memory/query", async (string q, int? limit, IMemoryStore memories, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest("q is required.");

            var results = await memories.SearchAsync(q, ct);
            var take = Math.Clamp(limit ?? 10, 1, 100);
            var mapped = results
                .Take(take)
                .Select(m => new MemoryDto(m.Id, m.Category, m.Content, m.ImportanceScore))
                .ToList();
            return Results.Ok(new MemoryQueryResponse(mapped));
        });

        app.MapPost("/v1/rag/query", async (RagQueryRequest req, RagQueryService rag, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.DatasetId) || string.IsNullOrWhiteSpace(req.Question))
                return Results.BadRequest("datasetId and question are required.");

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

            return Results.Ok(new RagQueryResponse(answer.ToString(), sources));
        });
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
