using System.Text.Json;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// One chat send worth of trace data, independent of any ViewModel-bindable
/// shape, so it can be persisted and reloaded without UI plumbing.
/// </summary>
public sealed record ChatTraceEntry(
    string Id,
    DateTime Timestamp,
    string ModelId,
    string Provider,
    string Runtime,
    string SystemPrompt,
    int AttachmentCount,
    int EstimatedTokens,
    ChatTokenUsage? ProviderUsage,
    long FirstTokenMs,
    long TotalLatencyMs,
    string ErrorDetails);

/// <summary>
/// Persists and reloads chat traces through the shared <see cref="ITraceStore"/>.
/// Extracted from ChatViewModel's AddChatTrace/PersistChatTraceAsync/
/// LoadPersistedChatTracesAsync group (docs/review/archived/r1/01-architecture-review.md
/// item 5). The trace store is optional (mirrors the ViewModel's existing
/// best-effort posture: no trace store configured means tracing is a no-op,
/// never a hard failure).
/// </summary>
public sealed class ChatTraceService
{
    private sealed record ChatTraceDetail(string Provider, string Runtime, string SystemPrompt, int AttachmentCount, int EstimatedTokens);

    private readonly ITraceStore? _traceStore;
    private readonly IRuntimeLogService _runtimeLogs;

    public ChatTraceService(IRuntimeLogService runtimeLogs, ITraceStore? traceStore = null)
    {
        _runtimeLogs = runtimeLogs;
        _traceStore = traceStore;
    }

    public async Task PersistAsync(ChatTraceEntry trace, string conversationId, CancellationToken ct = default)
    {
        if (_traceStore is null)
            return;

        try
        {
            await _traceStore.AppendAsync(new TraceRecord
            {
                Id = trace.Id,
                Kind = TraceKind.Chat,
                CreatedAt = trace.Timestamp,
                SourceId = conversationId,
                ModelId = trace.ModelId,
                Operation = "send",
                FirstTokenMs = trace.FirstTokenMs,
                TotalLatencyMs = trace.TotalLatencyMs,
                PromptTokens = trace.ProviderUsage?.PromptTokens ?? 0,
                CompletionTokens = trace.ProviderUsage?.CompletionTokens ?? 0,
                TotalTokens = trace.ProviderUsage?.TotalTokens ?? trace.EstimatedTokens,
                Error = trace.ErrorDetails,
                DetailJson = JsonSerializer.Serialize(new ChatTraceDetail(
                    trace.Provider, trace.Runtime, trace.SystemPrompt, trace.AttachmentCount, trace.EstimatedTokens))
            }, ct);
        }
        catch (Exception ex)
        {
            _runtimeLogs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Service,
                $"Chat trace persistence failed: {ex.Message}"));
        }
    }

    public async Task<List<ChatTraceEntry>> LoadRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        if (_traceStore is null)
            return [];

        try
        {
            var records = await _traceStore.GetRecentAsync(TraceKind.Chat, limit, ct);
            return records.Select(MapFromRecord).ToList();
        }
        catch
        {
            // Trace history is best-effort; callers should simply start empty.
            return [];
        }
    }

    private static ChatTraceEntry MapFromRecord(TraceRecord record)
    {
        var detail = TryParseDetail(record.DetailJson);
        return new ChatTraceEntry(
            record.Id,
            record.CreatedAt,
            record.ModelId,
            detail?.Provider ?? string.Empty,
            detail?.Runtime ?? string.Empty,
            detail?.SystemPrompt ?? string.Empty,
            detail?.AttachmentCount ?? 0,
            detail?.EstimatedTokens ?? record.TotalTokens,
            record.PromptTokens > 0 || record.CompletionTokens > 0
                ? new ChatTokenUsage(record.PromptTokens, record.CompletionTokens, record.TotalTokens)
                : null,
            record.FirstTokenMs,
            record.TotalLatencyMs,
            record.Error);
    }

    private static ChatTraceDetail? TryParseDetail(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ChatTraceDetail>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
