using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// Shared storage for the Evaluation System (Compare Models, Benchmarks, RAG
/// eval): one run shape, three projections. See docs/review/10-evaluation-system.md.
/// </summary>
public interface IEvalStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task SaveRunAsync(EvalRun run, CancellationToken ct = default);
    Task<IReadOnlyList<EvalRun>> GetRunsAsync(EvalMode? mode = null, CancellationToken ct = default);
    Task<EvalRun?> GetRunAsync(string id, CancellationToken ct = default);
}
