using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hermaeus.LocalApi;

/// <summary>
/// The minimal read/action surface the local API exposes: chat completion
/// (buffered or SSE-streamed), embeddings, memory query, RAG query, and a
/// read-only models list. Deliberately not full feature parity with the
/// desktop app (no agent, benchmark, or settings endpoints in this pass).
/// Every call is logged to the shared
/// <see cref="ITraceStore"/> as <see cref="TraceKind.LocalApi"/>, keyed by
/// the name of the per-app token that authenticated the request (verified,
/// not merely claimed), so Privacy Audit can show which apps have actually
/// been using Hermaeus as their AI substrate. The caller-supplied
/// <c>X-Hermaeus-Client</c> header is still recorded alongside it as an
/// unverified display hint (docs/review/03-next-level-roadmap.md Phase 2).
/// </summary>
public static class LocalApiEndpoints
{
    public const string ClientHeaderName = "X-Hermaeus-Client";

    public static void MapLocalApiEndpoints(this IEndpointRouteBuilder app)
    {
        // Unauthenticated (see LocalApiTokenAuth): used by LocalApiProcessManager
        // to detect that the host process is accepting connections.
        app.MapGet("/health", () => Results.Ok("ok"));

        app.MapPost("/v1/chat/completions", async (ChatCompletionRequest req, ILlmService llm, ModelProfileService profiles, ISettingsService settingsService, ITraceStore traces, HttpContext http, CancellationToken ct) =>
        {
            var (client, selfReported) = Caller(http);
            var sw = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(req.ModelId) || req.Messages is null || req.Messages.Count == 0)
            {
                await LogCallAsync(traces, client, selfReported, "chat.completions", sw, "modelId and messages required", ct);
                return Results.BadRequest("modelId and a non-empty messages array are required.");
            }

            var messages = req.Messages.Select(m => new ChatMessage(m.Role, m.Content)).ToList();
            var options = BuildChatOptions(req, settingsService.Settings.Llm, profiles.Get(req.ModelId));

            if (req.Stream)
            {
                var streamedUsage = await StreamSseAsync(http, llm, req.ModelId, messages, options, ct);
                await LogCallAsync(traces, client, selfReported, "chat.completions", sw, string.Empty, ct,
                    modelId: req.ModelId, promptTokens: streamedUsage?.PromptTokens, completionTokens: streamedUsage?.CompletionTokens);
                return Results.Empty;
            }

            var content = new StringBuilder();
            ChatTokenUsage? usage = null;
            await foreach (var evt in llm.StreamChatAsync(req.ModelId, messages, options, ct))
            {
                content.Append(evt.ContentDelta);
                if (evt.Usage is not null)
                    usage = evt.Usage;
            }

            await LogCallAsync(traces, client, selfReported, "chat.completions", sw, string.Empty, ct,
                modelId: req.ModelId, promptTokens: usage?.PromptTokens, completionTokens: usage?.CompletionTokens);
            return Results.Ok(new ChatCompletionResponse(content.ToString(), usage?.PromptTokens, usage?.CompletionTokens));
        });

        app.MapGet("/v1/memory/query", async (string q, int? limit, IMemoryStore memories, ITraceStore traces, HttpContext http, CancellationToken ct) =>
        {
            var (client, selfReported) = Caller(http);
            var sw = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(q))
            {
                await LogCallAsync(traces, client, selfReported, "memory.query", sw, "q required", ct);
                return Results.BadRequest("q is required.");
            }

            var results = await memories.SearchAsync(q, ct);
            var take = Math.Clamp(limit ?? 10, 1, 100);
            var mapped = results
                .Take(take)
                .Select(m => new MemoryDto(m.Id, m.Category, m.Content, m.ImportanceScore))
                .ToList();

            await LogCallAsync(traces, client, selfReported, "memory.query", sw, string.Empty, ct);
            return Results.Ok(new MemoryQueryResponse(mapped));
        });

        app.MapPost("/v1/rag/query", async (RagQueryRequest req, RagQueryService rag, ITraceStore traces, HttpContext http, CancellationToken ct) =>
        {
            var (client, selfReported) = Caller(http);
            var sw = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(req.DatasetId) || string.IsNullOrWhiteSpace(req.Question))
            {
                await LogCallAsync(traces, client, selfReported, "rag.query", sw, "datasetId and question required", ct);
                return Results.BadRequest("datasetId and question are required.");
            }

            var opts = new RagQueryOptions(TopK: Math.Clamp(req.TopK ?? 5, 1, 20));
            var answer = new StringBuilder();
            var sources = new List<RagSourceDto>();

            await foreach (var evt in rag.StreamQueryAsync(req.DatasetId, req.Question, opts, ct))
            {
                switch (evt.Kind)
                {
                    case RagStreamEventKind.Sources:
                        sources = evt.Sources!.Select(chunk => new RagSourceDto(chunk.Title, chunk.File, chunk.Path, chunk.Score)).ToList();
                        break;
                    case RagStreamEventKind.Token:
                        answer.Append(evt.Text);
                        break;
                }
            }

            await LogCallAsync(traces, client, selfReported, "rag.query", sw, string.Empty, ct, datasetId: req.DatasetId);
            return Results.Ok(new RagQueryResponse(answer.ToString(), sources));
        });

        app.MapGet("/v1/models", async (ILlmService llm, ModelProfileService profiles, ITraceStore traces, HttpContext http, CancellationToken ct) =>
        {
            var (client, selfReported) = Caller(http);
            var sw = Stopwatch.StartNew();
            var models = await llm.GetModelsAsync(ct);
            profiles.ApplyProfiles(models);
            var mapped = models
                .Where(m => m.IsVisible)
                .Select(m => new ModelDto(m.Id, m.Name, m.Provider, m.DefaultContextSize ?? m.ProbedContextLength))
                .ToList();

            await LogCallAsync(traces, client, selfReported, "models.list", sw, string.Empty, ct);
            return Results.Ok(new ModelsResponse(mapped));
        });

        app.MapPost("/v1/embeddings", async (EmbeddingsRequest req, IEmbeddingService embeddings, ITraceStore traces, HttpContext http, CancellationToken ct) =>
        {
            var (client, selfReported) = Caller(http);
            var sw = Stopwatch.StartNew();
            if (req.Input is null || req.Input.Count == 0 || req.Input.Any(string.IsNullOrWhiteSpace))
            {
                await LogCallAsync(traces, client, selfReported, "embeddings", sw, "input must be a non-empty array of non-empty strings", ct);
                return Results.BadRequest("input must be a non-empty array of non-empty strings.");
            }

            var vectors = await embeddings.EmbedBatchAsync(req.Input, ct);
            var data = vectors.Select((v, i) => new EmbeddingItemDto(i, v)).ToList();

            await LogCallAsync(traces, client, selfReported, "embeddings", sw, string.Empty, ct);
            return Results.Ok(new EmbeddingsResponse(data, embeddings.Dimensions));
        });
    }

    /// <summary>
    /// Mirrors ChatViewModel's precedence for the sampling parameters added in
    /// 0.9.39 (explicit value, then the model's saved profile default, then
    /// the global LLM setting, then the provider's own default via null): the
    /// local API previously only forwarded Temperature/MaxTokens, so a caller
    /// got different sampling behavior than the desktop app for the same
    /// model (docs/review/01-code-audit.md P3-1).
    /// </summary>
    private static LlmChatOptions BuildChatOptions(ChatCompletionRequest req, LlmSettings defaults, ModelProfile? profile) => new()
    {
        Temperature = req.Temperature ?? LlmChatOptions.Default.Temperature,
        MaxTokens = req.MaxTokens,
        TopP = req.TopP ?? profile?.DefaultTopP ?? defaults.TopP,
        TopK = req.TopK ?? profile?.DefaultTopK ?? defaults.TopK,
        MinP = req.MinP ?? profile?.DefaultMinP ?? defaults.MinP,
        RepeatPenalty = req.RepeatPenalty ?? profile?.DefaultRepeatPenalty ?? defaults.RepeatPenalty,
        FrequencyPenalty = req.FrequencyPenalty ?? profile?.DefaultFrequencyPenalty ?? defaults.FrequencyPenalty,
        PresencePenalty = req.PresencePenalty ?? profile?.DefaultPresencePenalty ?? defaults.PresencePenalty
    };

    /// <summary>
    /// Streams the completion as Server-Sent Events in the OpenAI
    /// chat.completion.chunk wire shape (docs/review/03-next-level-roadmap.md
    /// Phase 2): deliberate wire compatibility with an ecosystem of existing
    /// clients, not a dependency on OpenAI. Buffered JSON stays the default
    /// (<c>Stream</c> unset); this is opt-in per request.
    /// </summary>
    private static async Task<ChatTokenUsage?> StreamSseAsync(
        HttpContext http,
        ILlmService llm,
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        LlmChatOptions options,
        CancellationToken ct)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers.Append("X-Accel-Buffering", "no");

        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ChatTokenUsage? usage = null;

        await foreach (var evt in llm.StreamChatAsync(modelId, messages, options, ct))
        {
            if (evt.Usage is not null)
                usage = evt.Usage;

            if (string.IsNullOrEmpty(evt.ContentDelta))
                continue;

            await WriteChunkAsync(http, completionId, created, modelId, evt.ContentDelta, finishReason: null, ct);
        }

        await WriteChunkAsync(http, completionId, created, modelId, string.Empty, finishReason: "stop", ct);
        await http.Response.WriteAsync("data: [DONE]\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
        return usage;
    }

    private static async Task WriteChunkAsync(HttpContext http, string id, long created, string modelId, string delta, string? finishReason, CancellationToken ct)
    {
        var chunk = new
        {
            id,
            @object = "chat.completion.chunk",
            created,
            model = modelId,
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new { content = delta },
                    finish_reason = finishReason
                }
            }
        };
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    private const int MaxCallerNameLength = 64;

    /// <summary>
    /// Returns (verified caller identity, self-reported client hint). The
    /// verified name comes from <see cref="LocalApiTokenAuth"/>, which has
    /// already authenticated the request against a named per-app token by
    /// the time any endpoint handler runs; the self-reported name is the
    /// unverified <c>X-Hermaeus-Client</c> header, kept only for display
    /// alongside the verified identity (docs/review/03-next-level-roadmap.md
    /// Phase 2).
    /// </summary>
    private static (string Verified, string SelfReported) Caller(HttpContext http)
    {
        var verified = http.Items.TryGetValue(LocalApiTokenAuth.VerifiedTokenNameItemKey, out var name) && name is string s && !string.IsNullOrWhiteSpace(s)
            ? s
            : "unknown";

        var selfReported = string.Empty;
        if (http.Request.Headers.TryGetValue(ClientHeaderName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            var trimmed = value.ToString().Trim();
            var stripped = new string(trimmed.Where(c => !char.IsControl(c)).ToArray());
            selfReported = stripped.Length > MaxCallerNameLength ? stripped[..MaxCallerNameLength] : stripped;
        }

        return (verified, selfReported);
    }

    private static async Task LogCallAsync(
        ITraceStore traces,
        string client,
        string selfReportedClient,
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
                DetailJson = JsonSerializer.Serialize(new { client, selfReportedClient, datasetId = datasetId ?? string.Empty })
            }, ct);
        }
        catch
        {
            // Tracing must never break a caller's request.
        }
    }

}
