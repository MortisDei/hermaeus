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
