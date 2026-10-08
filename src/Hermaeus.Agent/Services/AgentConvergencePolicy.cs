using Hermaeus.Agent.Models;
using Hermaeus.Core.Models;

namespace Hermaeus.Agent.Services;

/// <summary>
/// Bounds planner loops whose observable action is not making progress. Read
/// results retain a deterministic signature so a changed observation resets
/// the sequence, while mixed blocked or no-effect actions still advance the
/// same task-level bound.
/// </summary>
public static class AgentConvergencePolicy
{
    public const int MaxEquivalentNonProgressSteps = 3;

    public sealed record Observation(string Description, int Count, bool ShouldStop);

    /// <summary>
    /// Finds a verified file mutation that establishes the same requested
    /// action or the same complete post-image. The comparison is deliberately
    /// generic: it does not inspect a language, file extension, or arbitrary
    /// textual threshold.
    /// </summary>
    public static string? FindEquivalentMutation(AgentTaskState state, AgentPendingToolAction pending)
    {
        if (pending.MutationKind is AgentMutationKind.Command or AgentMutationKind.SubTaskPlan)
            return null;

        var receipts = state.MutationReceipts.Where(receipt =>
            receipt.Verified
            && receipt.Outcome is AgentMutationOutcome.Applied or AgentMutationOutcome.AlreadySatisfied
            && string.Equals(receipt.WorkspaceRoot, pending.WorkspaceRoot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(receipt.RelativePath, pending.RelativePath, StringComparison.Ordinal)
            && receipt.MutationKind == pending.MutationKind);
        var requestedFingerprint = AgentApprovalFingerprint.ResolveRequestedAction(pending);
        if (requestedFingerprint.Length > 0
            && receipts.Any(receipt => string.Equals(
                receipt.RequestedActionFingerprint.Length > 0
                    ? receipt.RequestedActionFingerprint
                    : string.Empty,
                requestedFingerprint,
                StringComparison.Ordinal)))
        {
            return $"The identical {pending.ToolName} request for '{pending.RelativePath}' was already applied and verified. Re-read the file and propose a different mutation.";
        }

        if (pending.ProposedContentSha256.Length > 0
            && receipts.Any(receipt => receipt.ObservedPostImageExisted
                && string.Equals(receipt.ObservedPostImageSha256, pending.ProposedContentSha256, StringComparison.Ordinal)))
        {
            return $"The proposed output for '{pending.RelativePath}' is already present and verified. Re-read the file and propose a different mutation.";
        }

        return null;
    }

    /// <summary>
    /// Records an approved mutation's verified outcome for convergence. A
    /// verified write that changed the file resets the sequence; an already
    /// satisfied or verified no-change result remains non-progress.
    /// </summary>
    public static Observation ObserveMutationReceipt(AgentTaskState state, AgentMutationReceipt receipt)
    {
        if (receipt.Outcome == AgentMutationOutcome.Applied && receipt.Changed)
        {
            Reset(state);
            return new Observation(string.Empty, 0, false);
        }

        if (receipt.Outcome is not (AgentMutationOutcome.AlreadySatisfied
            or AgentMutationOutcome.Applied
            or AgentMutationOutcome.Blocked
            or AgentMutationOutcome.Unavailable
            or AgentMutationOutcome.Failed
            or AgentMutationOutcome.Conflict))
        {
            Reset(state);
            return new Observation(string.Empty, 0, false);
        }

        var identity = receipt.RequestedActionFingerprint.Length > 0
            ? receipt.RequestedActionFingerprint
            : $"{receipt.ToolName}:{receipt.RelativePath}:{receipt.ProposedContentSha256}";
        var signature = $"receipt:{identity}:{receipt.Outcome}:{receipt.ObservedPostImageSha256}";
        var description = receipt.Outcome == AgentMutationOutcome.AlreadySatisfied
            ? $"{receipt.ToolName} verified an already-satisfied output for {receipt.RelativePath}"
            : $"{receipt.ToolName} did not establish new verified progress for {receipt.RelativePath}";
        return RecordNonProgress(state, signature, description);
    }

    public static Observation ObserveTool(
        AgentTaskState state,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolPolicyDecision decision,
        AgentToolResult? result)
    {
        if (decision.Disposition == AgentToolDisposition.RequiresApproval)
        {
            Reset(state);
            return new Observation(string.Empty, 0, false);
        }

        var isReadOnly = IsReadOnly(toolName);
        var outcome = result?.NormalizedOutcome.Outcome;
        var identity = AgentApprovalFingerprint.Compute(toolName, arguments);
        var detail = result?.ResultSummary ?? decision.Reason;
        var detailHash = AgentMutationPreparation.ComputeContentSha256(Normalize(detail));

        if (isReadOnly && result is not null)
        {
            var readSignature = $"read:{identity}:{detailHash}";
            var readDescription = $"{toolName} returned {outcome}: {Normalize(detail)}";
            return RecordRead(state, readSignature, readDescription);
        }

        var nonProgress = decision.Disposition == AgentToolDisposition.Blocked
            || outcome is NormalizedOutcome.NoEffect
                or NormalizedOutcome.Unavailable
                or NormalizedOutcome.Denied
                or NormalizedOutcome.Blocked
                or NormalizedOutcome.Failed
                or NormalizedOutcome.TimedOut;

        if (!nonProgress)
        {
            Reset(state);
            return new Observation(string.Empty, 0, false);
        }

        var outcomeText = result?.NormalizedOutcome.Outcome.ToString() ?? decision.Disposition.ToString();
        var signature = $"tool:{identity}:{outcomeText}:{detailHash}";
        var description = result is not null
            ? $"{toolName} returned {outcomeText}: {Normalize(detail)}"
            : $"{toolName} was blocked: {Normalize(detail)}";
        return RecordNonProgress(state, signature, description);
    }

    public static Observation ObserveAskUser(AgentTaskState state, string question)
    {
        var normalizedQuestion = Normalize(question);
        var identity = AgentMutationPreparation.ComputeContentSha256(normalizedQuestion);
        var description = string.IsNullOrWhiteSpace(normalizedQuestion)
            ? "the agent asked for an empty question"
            : state.LastAnsweredQuestion.Length > 0
                && string.Equals(Normalize(state.LastAnsweredQuestion), normalizedQuestion, StringComparison.Ordinal)
                ? $"the agent repeated the question after it was answered: {normalizedQuestion}"
                : $"the agent repeated an unanswered question: {normalizedQuestion}";
        return RecordExact(state, $"ask_user:{identity}", description);
    }

    public static void Reset(AgentTaskState state)
    {
        state.LastNonProgressSignature = string.Empty;
        state.LastNonProgressDescription = string.Empty;
        state.ConsecutiveNonProgressCount = 0;
        state.LastReadResultSignature = string.Empty;
        state.LastAnsweredQuestion = string.Empty;
    }

    private static Observation RecordRead(AgentTaskState state, string signature, string description)
    {
        state.ConsecutiveNonProgressCount = string.Equals(
            state.LastReadResultSignature, signature, StringComparison.Ordinal)
            ? state.ConsecutiveNonProgressCount + 1
            : 1;
        state.LastReadResultSignature = signature;
        state.LastNonProgressSignature = signature;
        state.LastNonProgressDescription = description;
        return new Observation(
            description,
            state.ConsecutiveNonProgressCount,
            state.ConsecutiveNonProgressCount >= MaxEquivalentNonProgressSteps);
    }

    private static Observation RecordNonProgress(AgentTaskState state, string signature, string description)
    {
        state.ConsecutiveNonProgressCount++;
        state.LastNonProgressSignature = signature;
        state.LastNonProgressDescription = description;
        return new Observation(
            description,
            state.ConsecutiveNonProgressCount,
            state.ConsecutiveNonProgressCount >= MaxEquivalentNonProgressSteps);
    }

    private static Observation RecordExact(AgentTaskState state, string signature, string description)
    {
        state.ConsecutiveNonProgressCount = string.Equals(
            state.LastNonProgressSignature, signature, StringComparison.Ordinal)
            ? state.ConsecutiveNonProgressCount + 1
            : 1;
        state.LastReadResultSignature = string.Empty;
        state.LastNonProgressSignature = signature;
        state.LastNonProgressDescription = description;
        return new Observation(
            description,
            state.ConsecutiveNonProgressCount,
            state.ConsecutiveNonProgressCount >= MaxEquivalentNonProgressSteps);
    }

    private static bool IsReadOnly(string toolName) => toolName.Equals("list_files", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("search_files", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("glob_files", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("read_file", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("summarize_file", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("inspect_git_diff", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => string.Join(
        ' ',
        (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
