namespace Hermaeus.Core.Models;

/// <summary>r24 doc 04 4.3: every activity row carries an explicit outcome, not just a
/// description. Partial matters and must not be collapsed into Succeeded.</summary>
public enum ActivityOutcome
{
    Running,
    Succeeded,
    Partial,
    Failed,
    Cancelled
}

/// <summary>
/// One deterministic fact the app observed about its own background work (doc 04
/// 4.2) - never a model-written summary. Persisted through <see cref="Services.IActivityRecorder"/>
/// as a <see cref="TraceRecord"/> with <see cref="TraceKind.System"/>; this is the
/// decoded, UI-facing shape.
/// </summary>
public sealed record ActivityEvent(
    string Id,
    DateTime Timestamp,
    string Operation,
    string SourceId,
    ActivityOutcome Outcome,
    string Title,
    string Reason,
    string ProjectId);
