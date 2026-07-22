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
    private readonly IAgentService _agent;

    public AgentPatchReviewService(IAgentWorkspaceTools workspaceTools, IAgentTaskStateStore store, IAgentService agent)
    {
        _workspaceTools = workspaceTools;
        _store = store;
        _agent = agent;
    }

    public async Task ApplyAsync(AgentTaskState task, AgentDraftPatch patch, AgentWorkspaceOptions options, CancellationToken ct = default)
    {
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
        await _agent.AppendApprovalAsync(task.TaskId, "draft_patch_apply", approved: true, options, ct);
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

        var result = await _workspaceTools.RevertAppliedPatchAsync(
            options, patch.RelativePath, patch.PreImageExisted ? patch.PreImageContent : null, patch.AppliedContent, ct);
        if (!result.Reverted)
            return result.Message;

        patch.Status = AgentDraftPatchStatus.Reverted;
        patch.RevertedAt = DateTime.UtcNow;
        patch.RevertedBy = "User";
        await _store.SaveAsync(task, ct);
        await _agent.AppendApprovalAsync(task.TaskId, "draft_patch_revert", approved: true, options, ct);
        return string.Empty;
    }

    public async Task RejectAsync(AgentTaskState task, AgentDraftPatch patch, AgentWorkspaceOptions options, CancellationToken ct = default)
    {
        patch.Status = AgentDraftPatchStatus.Rejected;
        patch.BlockedAt = DateTime.UtcNow;
        patch.BlockedBy = "User";
        patch.BlockReason = "Rejected during review.";
        await _store.SaveAsync(task, ct);
        await _agent.AppendApprovalAsync(task.TaskId, "draft_patch_reject", approved: false, options, ct);
    }

    public async Task BlockAsync(AgentTaskState task, AgentDraftPatch patch, AgentWorkspaceOptions options, CancellationToken ct = default)
    {
        patch.Status = AgentDraftPatchStatus.Blocked;
        patch.BlockedAt = DateTime.UtcNow;
        patch.BlockedBy = "User";
        patch.BlockReason = string.IsNullOrWhiteSpace(patch.BlockReason) ? "Blocked during review." : patch.BlockReason;
        await _store.SaveAsync(task, ct);
        await _agent.AppendApprovalAsync(task.TaskId, "draft_patch_block", approved: false, options, ct);
    }
}
