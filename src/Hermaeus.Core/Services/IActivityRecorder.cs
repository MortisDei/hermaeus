using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>
/// r24 doc 04 4.2: the one path background work records through instead of
/// vanishing (managed servers, downloads, ingest, Doctor, backup, memory
/// sweeps). Wraps <see cref="ITraceStore.AppendAsync"/> with TraceKind.System
/// so callers never hand-build a TraceRecord themselves; redaction is applied
/// before persistence, exactly like runtime logs.
/// </summary>
public interface IActivityRecorder
{
    Task RecordAsync(
        string operation,
        string sourceId,
        ActivityOutcome outcome,
        string title,
        string reason = "",
        string projectId = "",
        CancellationToken ct = default);
}

public static class ActivityRecorderExtensions
{
    /// <summary>
    /// Records without ever throwing at the call site (r28 doc 03 3.3). The
    /// production recorder already swallows its own failures, but a call site
    /// that writes <c>_ = recorder.RecordAsync(...)</c> still propagates
    /// anything thrown before the first await, and a recorder must never fail
    /// the operation it is describing. Every observing call site uses this.
    /// </summary>
    public static void RecordSafe(
        this IActivityRecorder? recorder,
        string operation,
        string sourceId,
        ActivityOutcome outcome,
        string title,
        string reason = "",
        string projectId = "")
    {
        if (recorder is null) return;

        try
        {
            _ = recorder.RecordAsync(operation, sourceId, outcome, title, reason, projectId)
                .ContinueWith(static t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch
        {
        }
    }
}
