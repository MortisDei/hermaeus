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
    private readonly IAgentTaskCommandOwner _commandOwner;

    public AgentPatchReviewService(
        IAgentWorkspaceTools workspaceTools,
        IAgentTaskStateStore store,
        IWorkspaceManifestStore? manifests = null,
        IAgentTaskCommandOwner? commandOwner = null)
    {
        _workspaceTools = workspaceTools;
        _store = store;
        _manifests = manifests;
        _commandOwner = commandOwner ?? new AgentTaskCommandOwner();
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
        // The persisted manifest is authoritative when this service has a
        // manifest store. Clearing the caller's policy when the manifest no
        // longer contains one is important: removal or corruption of a policy
        // is a reviewed change, not permission to keep using a stale policy
        // snapshot from the editor.
        return options with { Policy = manifest?.Policy };
    }

    /// <summary>
    /// Queues a fully prepared patch through the Agent service boundary. The
    /// ViewModel supplies user-entered proposal text, but never mutates or
    /// saves task state itself.
    /// </summary>
    public Task<AgentDraftPatch> QueueAsync(
        string taskId,
        string relativePath,
        string rationale,
        string proposedContent,
        AgentWorkspaceOptions options,
        CancellationToken ct = default) =>
        _commandOwner.ExecuteTaskAsync(taskId,
            ownerToken => QueueOwnedAsync(taskId, relativePath, rationale, proposedContent, options, ownerToken), ct);

    private async Task<AgentDraftPatch> QueueOwnedAsync(
        string taskId,
        string relativePath,
        string rationale,
        string proposedContent,
        AgentWorkspaceOptions options,
        CancellationToken ct)
    {
        var task = await _store.LoadAsync(taskId, ct)
            ?? throw new InvalidOperationException("Agent task was not found.");
        if (task.Status == AgentTaskStatus.Running)
            throw new InvalidOperationException("Stop the task before queueing a manual patch.");

        if (task.WorkspaceRoot is { Length: > 0 } persistedRoot)
            options = options with { WorkspaceRoot = persistedRoot };
        options = await WithPolicyAsync(options, ct);
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["relative_path"] = relativePath,
            ["proposed_content"] = proposedContent
        };
        var preparation = await AgentMutationPreparation.PrepareAsync(
            "apply_draft_patch", arguments, options, options.Policy, _workspaceTools, ct);
        if (!preparation.IsValid)
            throw new InvalidOperationException(preparation.Error);

        var pending = preparation.Pending!;
        var patch = new AgentDraftPatch
        {
            RelativePath = pending.RelativePath,
            Rationale = rationale?.Trim() ?? string.Empty,
            ProposedContent = pending.ProposedContent,
            WorkspaceRoot = pending.WorkspaceRoot,
            ProposalId = pending.ProposalId,
            ProposalRevision = pending.ProposalRevision,
            ExpectedPreImageSha256 = pending.ExpectedPreImageSha256,
            ExpectedPreImageExisted = pending.ExpectedPreImageExisted,
            ProposedContentSha256 = pending.ProposedContentSha256,
            PolicyFingerprint = pending.PolicyFingerprint,
            PreparedAt = pending.PreparedAt
        };
        task.DraftPatches.Add(patch);
        await _store.SaveAsync(task, ct);
        return patch;
    }

    public Task<AgentMutationOutcome> ApplyAsync(AgentTaskState task, AgentDraftPatch patch, AgentWorkspaceOptions options, CancellationToken ct = default) =>
        _commandOwner.ExecuteTaskAsync(task.TaskId,
            ownerToken => ApplyOwnedAsync(task, patch, options, ownerToken), ct);

    private async Task<AgentMutationOutcome> ApplyOwnedAsync(AgentTaskState task, AgentDraftPatch patch, AgentWorkspaceOptions options, CancellationToken ct)
    {
        if (task.WorkspaceRoot is { Length: > 0 } persistedRoot)
            options = options with { WorkspaceRoot = persistedRoot };
        options = await WithPolicyAsync(options, ct);
        var initial = await PreparePatchAsync(patch, options, ct);
        if (!initial.IsValid)
        {
            await BlockApplyAsync(task, patch, initial.Error, ct);
            return patch.IsPrepared
                ? AgentMutationOutcome.Conflict
                : AgentMutationOutcome.Blocked;
        }

        var prepared = initial.Pending!;
        return await _commandOwner.ExecuteTargetAsync(prepared.WorkspaceRoot, prepared.RelativePath,
            executionToken => ApplyPreparedAsync(task, patch, options, executionToken), ct);
    }

    private async Task<AgentMutationOutcome> ApplyPreparedAsync(
        AgentTaskState task,
        AgentDraftPatch patch,
        AgentWorkspaceOptions options,
        CancellationToken ct)
    {
        // Prepare again while holding the target lock so the approval cannot
        // rely on the initial read if another task or editor changed the file.
        var preparation = await PreparePatchAsync(patch, options, ct);
        if (!preparation.IsValid)
        {
            await BlockApplyAsync(task, patch, preparation.Error, ct);
            return AgentMutationOutcome.Conflict;
        }

        var pending = preparation.Pending!;
        var receipt = new AgentMutationReceipt
        {
            TaskId = task.TaskId,
            ProposalId = patch.IsPrepared ? patch.ProposalId : patch.Id,
            ProposalRevision = patch.IsPrepared ? patch.ProposalRevision : 1,
            ToolName = "apply_draft_patch",
            MutationKind = AgentMutationKind.ApplyDraftPatch,
            RelativePath = pending.RelativePath,
            ExpectedPreImageSha256 = pending.ExpectedPreImageSha256,
            ExpectedPreImageExisted = pending.ExpectedPreImageExisted,
            ProposedContentSha256 = pending.ProposedContentSha256,
            ApprovalRecorded = true,
            Outcome = AgentMutationOutcome.Pending,
            StartedAt = DateTime.UtcNow
        };
        task.MutationReceipts.Add(receipt);
        await _store.SaveAsync(task, ct);

        AgentFileReadResult applied;
        try
        {
            applied = await _workspaceTools.ApplyDraftPatchAsync(
                options, pending.RelativePath, pending.ProposedContent, ct);
        }
        catch (OperationCanceledException)
        {
            receipt.Outcome = AgentMutationOutcome.Unknown;
            receipt.CompletionReason = "Execution was cancelled before the outcome could be durably confirmed.";
            receipt.FinishedAt = DateTime.UtcNow;
            await _store.SaveAsync(task, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            receipt.Outcome = AgentMutationOutcome.Failed;
            receipt.CompletionReason = ex.Message;
            receipt.FinishedAt = DateTime.UtcNow;
            patch.Status = AgentDraftPatchStatus.Blocked;
            patch.BlockedAt = DateTime.UtcNow;
            patch.BlockedBy = "System";
            patch.BlockReason = ex.Message;
            await _store.SaveAsync(task, ct);
            return AgentMutationOutcome.Failed;
        }

        string? postContent = null;
        try
        {
            postContent = await _workspaceTools.ReadFileForRevertAsync(options, pending.RelativePath, CancellationToken.None);
            receipt.ObservedPostImageExisted = postContent is not null;
            receipt.ObservedPostImageSha256 = postContent is null
                ? string.Empty
                : AgentMutationPreparation.ComputeContentSha256(postContent);
        }
        catch (Exception ex)
        {
            receipt.CompletionReason = $"The write returned, but readback failed: {ex.Message}";
        }

        var postMatches = receipt.ObservedPostImageExisted
            && string.Equals(receipt.ObservedPostImageSha256, pending.ProposedContentSha256, StringComparison.Ordinal);
        receipt.Changed = receipt.ObservedPostImageExisted != pending.ExpectedPreImageExisted
            || !string.Equals(receipt.ObservedPostImageSha256, pending.ExpectedPreImageSha256, StringComparison.Ordinal);
        receipt.Verified = postMatches;
        receipt.Outcome = postMatches && receipt.Changed
            ? AgentMutationOutcome.Applied
            : postMatches
                ? AgentMutationOutcome.AlreadySatisfied
                : AgentMutationOutcome.Unknown;
        if (receipt.CompletionReason.Length == 0)
            receipt.CompletionReason = receipt.Outcome switch
            {
                AgentMutationOutcome.Applied => "The post-image matched the complete prepared output.",
                AgentMutationOutcome.AlreadySatisfied => "The complete prepared output was already present and was verified.",
                _ => applied.Changed
                    ? "The write returned, but the expected post-image was not observed."
                    : "The executor reported no effect and the expected post-image was not observed."
            };

        patch.ApprovedAt = DateTime.UtcNow;
        patch.ApprovedBy = "User";
        patch.BlockedAt = null;
        patch.BlockedBy = null;
        patch.BlockReason = string.Empty;
        patch.PreImageContent = preparation.Content;
        patch.PreImageExisted = preparation.Existed;
        patch.AppliedContent = postContent ?? string.Empty;
        patch.MutationReceiptId = receipt.ReceiptId;
        patch.Status = receipt.Outcome switch
        {
            AgentMutationOutcome.Applied => AgentDraftPatchStatus.Applied,
            AgentMutationOutcome.AlreadySatisfied => AgentDraftPatchStatus.AlreadySatisfied,
            _ => AgentDraftPatchStatus.Blocked
        };
        if (patch.Status == AgentDraftPatchStatus.Blocked)
        {
            patch.BlockedAt = DateTime.UtcNow;
            patch.BlockedBy = "System";
            patch.BlockReason = receipt.CompletionReason;
        }

        receipt.FinishedAt = DateTime.UtcNow;
        await _store.AppendTraceAsync(task.TaskId, new
        {
            task_id = task.TaskId,
            type = "mutation_receipt",
            receipt_id = receipt.ReceiptId,
            attempt_id = receipt.AttemptId,
            proposal_id = receipt.ProposalId,
            tool = receipt.ToolName,
            outcome = receipt.Outcome.ToString(),
            verified = receipt.Verified,
            changed = receipt.Changed,
            logged_at = DateTime.UtcNow
        }, ct);
        await _store.SaveAsync(task, ct);
        return receipt.Outcome;
    }

    private async Task<AgentMutationPreparationResult> PreparePatchAsync(
        AgentDraftPatch patch,
        AgentWorkspaceOptions options,
        CancellationToken ct)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["relative_path"] = patch.RelativePath,
            ["proposed_content"] = patch.ProposedContent
        };
        var preparation = await AgentMutationPreparation.PrepareAsync(
            "apply_draft_patch", arguments, options, options.Policy, _workspaceTools, ct);
        if (!preparation.IsValid || !patch.IsPrepared)
            return preparation;

        var pending = preparation.Pending!;
        if (!string.Equals(patch.WorkspaceRoot, pending.WorkspaceRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(patch.RelativePath, pending.RelativePath, StringComparison.Ordinal)
            || patch.ExpectedPreImageExisted != pending.ExpectedPreImageExisted
            || !string.Equals(patch.ExpectedPreImageSha256, pending.ExpectedPreImageSha256, StringComparison.Ordinal)
            || !string.Equals(patch.ProposedContentSha256, pending.ProposedContentSha256, StringComparison.Ordinal)
            || !string.Equals(patch.PolicyFingerprint, pending.PolicyFingerprint, StringComparison.Ordinal))
            return AgentMutationPreparationResult.Reject("The queued patch target, pre-image, proposed output or policy changed since it was prepared.");

        return preparation;
    }

    private async Task BlockApplyAsync(AgentTaskState task, AgentDraftPatch patch, string reason, CancellationToken ct)
    {
        patch.Status = AgentDraftPatchStatus.Blocked;
        patch.BlockedAt = DateTime.UtcNow;
        patch.BlockedBy = "System";
        patch.BlockReason = reason;
        await _store.SaveAsync(task, ct);
    }

    /// <summary>
    /// Restores an applied patch's pre-image, or deletes the file if it did
    /// not exist before the patch. Refuses (returns a non-empty message) if
    /// the file changed again after the patch was applied, rather than
    /// silently overwriting newer content.
    /// </summary>
    public Task<string> RevertAsync(AgentTaskState task, AgentDraftPatch patch, AgentWorkspaceOptions options, CancellationToken ct = default) =>
        _commandOwner.ExecuteTaskAsync(task.TaskId,
            ownerToken => RevertOwnedAsync(task, patch, options, ownerToken), ct);

    private async Task<string> RevertOwnedAsync(AgentTaskState task, AgentDraftPatch patch, AgentWorkspaceOptions options, CancellationToken ct)
    {
        if (patch.Status != AgentDraftPatchStatus.Applied)
            return "Only an applied patch can be reverted.";

        if (task.WorkspaceRoot is { Length: > 0 } persistedRoot)
            options = options with { WorkspaceRoot = persistedRoot };
        options = await WithPolicyAsync(options, ct);
        var result = await _commandOwner.ExecuteTargetAsync(options.WorkspaceRoot, patch.RelativePath,
            executionToken => _workspaceTools.RevertAppliedPatchAsync(
                options, patch.RelativePath, patch.PreImageExisted ? patch.PreImageContent : null, patch.AppliedContent, executionToken), ct);
        if (!result.Reverted)
            return result.Message;

        patch.Status = AgentDraftPatchStatus.Reverted;
        patch.RevertedAt = DateTime.UtcNow;
        patch.RevertedBy = "User";
        await _store.SaveAsync(task, ct);
        return string.Empty;
    }

    public Task RejectAsync(AgentTaskState task, AgentDraftPatch patch, CancellationToken ct = default) =>
        _commandOwner.ExecuteTaskAsync(task.TaskId,
            ownerToken => RejectOwnedAsync(task, patch, ownerToken), ct);

    private async Task RejectOwnedAsync(AgentTaskState task, AgentDraftPatch patch, CancellationToken ct)
    {
        patch.Status = AgentDraftPatchStatus.Rejected;
        patch.BlockedAt = DateTime.UtcNow;
        patch.BlockedBy = "User";
        patch.BlockReason = "Rejected during review.";
        await _store.SaveAsync(task, ct);
    }

    public Task BlockAsync(AgentTaskState task, AgentDraftPatch patch, CancellationToken ct = default) =>
        _commandOwner.ExecuteTaskAsync(task.TaskId,
            ownerToken => BlockOwnedAsync(task, patch, ownerToken), ct);

    private async Task BlockOwnedAsync(AgentTaskState task, AgentDraftPatch patch, CancellationToken ct)
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
    public Task<AgentTaskRevertResult> RevertTaskAsync(AgentTaskState task, AgentWorkspaceOptions options, CancellationToken ct = default) =>
        _commandOwner.ExecuteTaskAsync(task.TaskId,
            ownerToken => RevertTaskCoreAsync(task, options, ownerToken), ct);

    private async Task<AgentTaskRevertResult> RevertTaskCoreAsync(AgentTaskState task, AgentWorkspaceOptions options, CancellationToken ct)
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
                var result = await _commandOwner.ExecuteTargetAsync(taskOptions.WorkspaceRoot, group.Key,
                    executionToken => _workspaceTools.RevertAppliedPatchAsync(
                        taskOptions, group.Key, first.PreImageContent, latest.AppliedContent, executionToken), ct);
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
