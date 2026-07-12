using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// Single store for chat, RAG, and agent traces. Each viewer is a projection
/// (filter by <see cref="TraceKind"/>); no subsystem keeps its own trace schema.
/// </summary>
public interface ITraceStore
{
    /// <summary>Append a trace. Implementations prune old rows per kind.</summary>
    Task AppendAsync(TraceRecord trace, CancellationToken ct = default);

    /// <summary>Most recent traces, newest first, optionally filtered by kind.</summary>
    Task<List<TraceRecord>> GetRecentAsync(TraceKind? kind = null, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Per-model call/token totals over the trailing <paramref name="days"/>,
    /// from the durable model_usage rollup (unaffected by trace pruning).
    /// </summary>
    Task<List<ModelUsageRow>> GetModelUsageAsync(TraceKind? kind, int days, CancellationToken ct = default);
}
