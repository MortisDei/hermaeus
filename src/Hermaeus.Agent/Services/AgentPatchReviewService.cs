using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

/// <summary>
/// The status-transition, persist, and audit sequence shared by approving,
/// rejecting, and blocking a queued draft patch. Extracted from
/// AgentViewModel's ApprovePatchAsync/RejectPatchAsync/BlockPatchAsync group
/// (docs/review/archived/r1/01-architecture-review.md item 5); the ViewModel still owns
/// the approval-preview UI flow, this owns what happens once a decision is
/// made.
/// </summary>
public sealed class AgentPatchReviewService
{
    private readonly IAgentWorkspaceTools _workspaceTools;
    private readonly IAgentTaskStateStore _store;
    private readonly IWorkspaceManifestStore? _manifests;

    public AgentPatchReviewService(IAgentWorkspaceTools workspaceTools, IAgentTaskStateStore store, IWorkspaceManifestStore? manifests = null)
    {
        _workspaceTools = workspaceTools;
        _store = store;
        _manifests = manifests;
    }

    /// <summary>
    /// The draft-patch queue and Rewind apply the same write-policy rules as
    /// the direct-approval path, through this same enrichment, rather than a
    /// second implementation (r23 3.2).
    /// </summary>
    private async Task<AgentWorkspaceOptions> WithPolicyAsync(AgentWorkspaceOptions options, CancellationToken ct)
    {
        if (_manifests is null) return options;
        var manifest = await _manifests.LoadAsync(options.WorkspaceRoot, ct);
        return manifest?.Policy is null ? options : options with { Policy = manifest.Policy };
    }

    public async Task ApplyAsync(AgentTaskState task, AgentDraftPatch patch, AgentWorkspaceOptions options, CancellationToken ct = default)
    {
        options = await WithPolicyAsync(options, ct);
        // Captured before the write so a later revert can restore exactly
        // what was there, or delete the file if it did not exist yet
        // (r6 01-first-five-minutes.md 1.8).
        string? preImage = null;
        try { preImage = await _workspaceTools.ReadFileForRevertAsync(options, patch.RelativePath, ct); }
        catch { /* best effort; a failed pre-image read should not block the apply */ }

        var applied = await _workspaceTools.ApplyDraftPatchAsync(options, patch.RelativePath, patch.ProposedContent, ct);
        patch.Status = AgentDraftPatchStatus.Applied;
        patch.ApprovedAt = DateTime.UtcNow;
        patch.ApprovedBy = "User";
        patch.BlockedAt = null;
        patch.BlockedBy = null;
        patch.BlockReason = string.Empty;
        patch.PreImageContent = preImage;
        patch.PreImageExisted = preImage is not null;
        patch.AppliedContent = applied.Content;
        await _store.SaveAsync(task, ct);
    }

    /// <summary>
    /// Restores an applied patch's pre-image, or deletes the file if it did
    /// not exist before the patch. Refuses (returns a non-empty message) if
    /// the file changed again after the patch was applied, rather than
    /// silently overwriting newer content.
    /// </summary>
    public async Task<string> RevertAsync(AgentTaskState task, AgentDraftPatch patch, AgentWorkspaceOptions options, CancellationToken ct = default)
    {
        if (patch.Status != AgentDraftPatchStatus.Applied)
            return "Only an applied patch can be reverted.";

        options = await WithPolicyAsync(options, ct);
        var result = await _workspaceTools.RevertAppliedPatchAsync(
            options, patch.RelativePath, patch.PreImageExisted ? patch.PreImageContent : null, patch.AppliedContent, ct);
        if (!result.Reverted)
            return result.Message;

        patch.Status = AgentDraftPatchStatus.Reverted;
        patch.RevertedAt = DateTime.UtcNow;
        patch.RevertedBy = "User";
        await _store.SaveAsync(task, ct);
        return string.Empty;
    }

    public async Task RejectAsync(AgentTaskState task, AgentDraftPatch patch, CancellationToken ct = default)
    {
        patch.Status = AgentDraftPatchStatus.Rejected;
        patch.BlockedAt = DateTime.UtcNow;
        patch.BlockedBy = "User";
        patch.BlockReason = "Rejected during review.";
        await _store.SaveAsync(task, ct);
    }

    public async Task BlockAsync(AgentTaskState task, AgentDraftPatch patch, CancellationToken ct = default)
    {
        patch.Status = AgentDraftPatchStatus.Blocked;
        patch.BlockedAt = DateTime.UtcNow;
        patch.BlockedBy = "User";
        patch.BlockReason = string.IsNullOrWhiteSpace(patch.BlockReason) ? "Blocked during review." : patch.BlockReason;
        await _store.SaveAsync(task, ct);
    }

    /// <summary>
    /// Reverts an entire run: every distinct file path the task (and, for an
    /// orchestration parent, its children) touched, restored to the content
    /// from before the run first touched it (r23 1.3, doc
    /// "01-run-ledger-and-task-rewind.md"). Per file this is exactly the
    /// existing per-patch revert rule - refuse if the file changed again
    /// after the patch was applied - so Rewind can never overwrite content
    /// the user or anyone else wrote afterward. Partial success is reported
    /// truthfully rather than treated as a failure. Records no lesson:
    /// reverting is user judgment about wanted-ness, not evidence a tool or
    /// command failed.
    /// </summary>
    public async Task<AgentTaskRevertResult> RevertTaskAsync(AgentTaskState task, AgentWorkspaceOptions options, CancellationToken ct = default)
    {
        if (task.Status == AgentTaskStatus.Running)
            throw new InvalidOperationException("This task is still running; wait for it to finish before reverting the run.");
        if (task.PendingToolAction is not null)
            throw new InvalidOperationException("This task has a pending approval; resolve it before reverting the run.");

        // Loaded once from the top-level task's workspace and carried
        // through the per-child `with { WorkspaceRoot = ... }` below (r23
        // 3.2): children share the same policy in the ordinary case of one
        // physical workspace per orchestration run.
        options = await WithPolicyAsync(options, ct);

        var children = new List<AgentTaskState>();
        foreach (var spec in task.SubTaskPlan)
        {
            if (spec.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running)
                throw new InvalidOperationException("This task has an unfinished sub-task; wait for orchestration to finish before reverting the run.");
            if (string.IsNullOrEmpty(spec.TaskId))
                continue;
            var child = await _store.LoadAsync(spec.TaskId, ct)
                ?? throw new InvalidOperationException($"Sub-task {spec.TaskId} could not be loaded.");
            children.Add(child);
        }

        var tasksToRevert = new List<AgentTaskState> { task };
        tasksToRevert.AddRange(children);

        var outcomes = new List<AgentTaskRevertFileOutcome>();
        foreach (var t in tasksToRevert)
        {
            var taskOptions = t.WorkspaceRoot is { Length: > 0 } root ? options with { WorkspaceRoot = root } : options;
            var groups = t.DraftPatches
                .Where(p => p.Status is AgentDraftPatchStatus.Applied or AgentDraftPatchStatus.Reverted)
                .GroupBy(p => p.RelativePath);

            foreach (var group in groups)
            {
                var patches = group.ToList();
                if (patches.All(p => p.Status == AgentDraftPatchStatus.Reverted))
                    continue;

                var first = patches[0];
                var latest = patches[^1];
                var result = await _workspaceTools.RevertAppliedPatchAsync(
                    taskOptions, group.Key, first.PreImageContent, latest.AppliedContent, ct);
                outcomes.Add(new AgentTaskRevertFileOutcome(group.Key, result.Reverted, result.Message)
                {
                    NormalizedOutcome = result.NormalizedOutcome
                });

                if (result.Reverted)
                {
                    foreach (var patch in patches)
                    {
                        patch.Status = AgentDraftPatchStatus.Reverted;
                        patch.RevertedAt = DateTime.UtcNow;
                        patch.RevertedBy = "User";
                    }
                    await _store.SaveAsync(t, ct);
                }
            }
        }

        var summary = BuildSummary(outcomes);
        await _store.AppendLogAsync(task.TaskId, summary, ct);
        await _store.AppendTraceAsync(task.TaskId, new
        {
            task_id = task.TaskId,
            type = "task_reverted",
            files = outcomes.Select(o => new { path = o.RelativePath, reverted = o.Reverted, message = o.Message }),
            logged_at = DateTime.UtcNow
        }, ct);

        var aggregateSignal = outcomes.Count switch
        {
            0 => AgentToolOutcomeSignal.NoEffect,
            _ when outcomes.All(o => o.Reverted) => AgentToolOutcomeSignal.Completed,
            _ when outcomes.Any(o => o.Reverted) => AgentToolOutcomeSignal.Partial,
            _ => AgentToolOutcomeSignal.PolicyBlocked
        };
        return new AgentTaskRevertResult(outcomes, summary)
        {
            NormalizedOutcome = AgentToolOutcomeNormalizer.Normalize("apply_draft_patch",
                new AgentToolOutcomeEvidence(aggregateSignal,
                    Detail: "The whole-run rewind outcome was derived from its per-file results."))
        };
    }

    private static string BuildSummary(IReadOnlyList<AgentTaskRevertFileOutcome> outcomes)
    {
        if (outcomes.Count == 0)
            return "Nothing to revert.";

        var revertedCount = outcomes.Count(o => o.Reverted);
        var summary = $"Reverted {revertedCount} of {outcomes.Count} file(s).";
        var skipped = outcomes.Where(o => !o.Reverted);
        foreach (var skip in skipped)
            summary += $" Skipped {skip.RelativePath}: {skip.Message}";
        return summary;
    }
}
