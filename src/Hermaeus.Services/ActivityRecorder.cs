using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class ActivityRecorder : IActivityRecorder
{
    private sealed record ActivityDetail(string Outcome, string Title, string Reason, string ProjectId);

    private readonly ITraceStore? _traceStore;
    private readonly RedactionService _redaction;

    public ActivityRecorder(RedactionService redaction, ITraceStore? traceStore = null)
    {
        _redaction = redaction;
        _traceStore = traceStore;
    }

    public async Task RecordAsync(
        string operation,
        string sourceId,
        ActivityOutcome outcome,
        string title,
        string reason = "",
        string projectId = "",
        CancellationToken ct = default)
    {
        if (_traceStore is null) return;

        // Redaction before persistence, same discipline as runtime logs - a
        // download URL with a token in the query string must not land here
        // in the clear (doc 04 4.2).
        var safeTitle = _redaction.Redact(title);
        var safeReason = _redaction.Redact(reason);

        try
        {
            await _traceStore.AppendAsync(new TraceRecord
            {
                Kind = TraceKind.System,
                CreatedAt = DateTime.UtcNow,
                SourceId = sourceId,
                Operation = operation,
                Error = outcome == ActivityOutcome.Failed ? safeReason : string.Empty,
                DetailJson = JsonSerializer.Serialize(new ActivityDetail(outcome.ToString(), safeTitle, safeReason, projectId))
            }, ct);
        }
        catch
        {
            // Activity recording is fire-and-forget and must never break the
            // operation it is describing (doc 06: "Activity recording is fire-and-forget").
        }
    }
}
