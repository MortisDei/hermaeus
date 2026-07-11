using Aether.Agent.Models;

namespace Aether.Agent.Services;

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
        await _workspaceTools.ApplyDraftPatchAsync(options, patch.RelativePath, patch.ProposedContent, ct);
        patch.Status = AgentDraftPatchStatus.Applied;
        patch.ApprovedAt = DateTime.UtcNow;
        patch.ApprovedBy = "User";
        patch.BlockedAt = null;
        patch.BlockedBy = null;
        patch.BlockReason = string.Empty;
        await _store.SaveAsync(task, ct);
        await _agent.AppendApprovalAsync(task.TaskId, "draft_patch_apply", approved: true, options, ct);
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
