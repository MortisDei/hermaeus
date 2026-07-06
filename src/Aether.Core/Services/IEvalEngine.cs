using Aether.Core.Models;

namespace Aether.Core.Services;

/// <summary>
/// Shared execution engine for the Evaluation System (Compare Models,
/// Benchmarks, RAG eval): one case run against N targets, one run per
/// target. See docs/review/10-evaluation-system.md.
/// </summary>
public interface IEvalEngine
{
    /// <summary>
    /// Runs one case (a message history ending in the prompt to answer)
    /// against each target sequentially, transient (not persisted) unless
    /// the caller chooses to save the returned runs.
    /// </summary>
    Task<IReadOnlyList<EvalRun>> RunQuickCompareAsync(
        string caseId,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<EvalTarget> targets,
        LlmChatOptions? options = null,
        CancellationToken ct = default);
}
