using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AgentPatchReviewServiceTests
{
    private sealed class RecordingAgentService : IAgentService
    {
        public readonly List<string> Actions = [];

        public Task<AgentTaskState> CreateTaskAsync(string goal, AgentWorkspaceOptions options, CancellationToken ct = default, string projectId = "") =>
            throw new NotSupportedException();

        public Task<AgentStepResult> RunStepAsync(string taskId, AgentWorkspaceOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AgentStepResult> RunAsync(string taskId, AgentWorkspaceOptions options, Action<AgentStepResult>? onStep = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentTaskListItem>> LoadRecentTasksAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AgentApprovalResult> AppendApprovalAsync(string taskId, string action, bool approved, string expectedFingerprint, AgentWorkspaceOptions? options = null, CancellationToken ct = default)
        {
            Actions.Add($"{action}:{approved}");
            return Task.FromResult(new AgentApprovalResult(true, string.Empty));
        }

        public Task AppendUserReplyAsync(string taskId, string reply, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AgentApprovalResult> DismissTaskAsync(string taskId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AgentTaskState> ContinueTaskAsync(string taskId, string instruction, AgentWorkspaceOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AgentSteeringResult> SteerTaskAsync(string taskId, string instruction, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static (AgentPatchReviewService Service, RecordingAgentService Agent, FileAgentTaskStateStore Store, string Workspace) Build(TempDir temp)
    {
        var settings = Helpers.NewSettings(temp);
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "notes.md"), "original");

        var store = new FileAgentTaskStateStore(settings);
        var agent = new RecordingAgentService();
        var service = new AgentPatchReviewService(new AgentWorkspaceTools(), store, agent);
        return (service, agent, store, workspace);
    }

    [Fact]
    public async Task ApplyAsync_writes_the_file_and_marks_the_patch_applied()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test" };
        var patch = new AgentDraftPatch { RelativePath = "notes.md", ProposedContent = "updated" };

        await service.ApplyAsync(task, patch, new AgentWorkspaceOptions(workspace));

        Assert.Equal(AgentDraftPatchStatus.Applied, patch.Status);
        Assert.Equal("User", patch.ApprovedBy);
        Assert.Null(patch.BlockedAt);
        Assert.Equal("updated", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
        Assert.Contains("draft_patch_apply:True", agent.Actions);
    }

    [Fact]
    public async Task RejectAsync_marks_the_patch_rejected_without_touching_the_file()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test" };
        var patch = new AgentDraftPatch { RelativePath = "notes.md", ProposedContent = "updated" };

        await service.RejectAsync(task, patch, new AgentWorkspaceOptions(workspace));

        Assert.Equal(AgentDraftPatchStatus.Rejected, patch.Status);
        Assert.Equal("Rejected during review.", patch.BlockReason);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
        Assert.Contains("draft_patch_reject:False", agent.Actions);
    }

    [Fact]
    public async Task BlockAsync_preserves_an_existing_block_reason()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test" };
        var patch = new AgentDraftPatch { RelativePath = "notes.md", ProposedContent = "updated", BlockReason = "custom reason" };

        await service.BlockAsync(task, patch, new AgentWorkspaceOptions(workspace));

        Assert.Equal(AgentDraftPatchStatus.Blocked, patch.Status);
        Assert.Equal("custom reason", patch.BlockReason);
        Assert.Contains("draft_patch_block:False", agent.Actions);
    }

    [Fact]
    public async Task ApplyAsync_captures_the_pre_image_so_revert_can_restore_it()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test" };
        var patch = new AgentDraftPatch { RelativePath = "notes.md", ProposedContent = "updated" };

        await service.ApplyAsync(task, patch, new AgentWorkspaceOptions(workspace));
        Assert.Equal("original", patch.PreImageContent);
        Assert.True(patch.PreImageExisted);

        var error = await service.RevertAsync(task, patch, new AgentWorkspaceOptions(workspace));

        Assert.Equal(string.Empty, error);
        Assert.Equal(AgentDraftPatchStatus.Reverted, patch.Status);
        Assert.Equal("User", patch.RevertedBy);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
        Assert.Contains("draft_patch_revert:True", agent.Actions);
    }

    [Fact]
    public async Task RevertAsync_deletes_a_file_that_did_not_exist_before_the_patch()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test" };
        var patch = new AgentDraftPatch { RelativePath = "new-file.md", ProposedContent = "brand new content" };

        await service.ApplyAsync(task, patch, new AgentWorkspaceOptions(workspace));
        Assert.Null(patch.PreImageContent);
        Assert.False(patch.PreImageExisted);
        Assert.True(File.Exists(Path.Combine(workspace, "new-file.md")));

        var error = await service.RevertAsync(task, patch, new AgentWorkspaceOptions(workspace));

        Assert.Equal(string.Empty, error);
        Assert.False(File.Exists(Path.Combine(workspace, "new-file.md")));
    }

    [Fact]
    public async Task RevertAsync_refuses_when_the_file_changed_again_after_the_patch()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test" };
        var patch = new AgentDraftPatch { RelativePath = "notes.md", ProposedContent = "updated" };

        await service.ApplyAsync(task, patch, new AgentWorkspaceOptions(workspace));
        await File.WriteAllTextAsync(Path.Combine(workspace, "notes.md"), "someone edited this after the patch");

        var error = await service.RevertAsync(task, patch, new AgentWorkspaceOptions(workspace));

        Assert.NotEqual(string.Empty, error);
        Assert.Equal(AgentDraftPatchStatus.Applied, patch.Status);
        Assert.Equal("someone edited this after the patch", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
    }

    [Fact]
    public async Task RevertAsync_refuses_a_patch_that_is_not_currently_applied()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test" };
        var patch = new AgentDraftPatch { RelativePath = "notes.md", ProposedContent = "updated" };

        var error = await service.RevertAsync(task, patch, new AgentWorkspaceOptions(workspace));

        Assert.NotEqual(string.Empty, error);
        Assert.Equal(AgentDraftPatchStatus.Pending, patch.Status);
    }

    [Fact]
    public async Task RevertTaskAsync_restores_every_touched_file_to_its_pre_run_content()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        File.WriteAllText(Path.Combine(workspace, "second.md"), "second-original");
        var task = new AgentTaskState { Goal = "test" };
        var patch1 = new AgentDraftPatch { RelativePath = "notes.md", ProposedContent = "updated" };
        var patch2 = new AgentDraftPatch { RelativePath = "second.md", ProposedContent = "second-updated" };
        task.DraftPatches.Add(patch1);
        task.DraftPatches.Add(patch2);
        var options = new AgentWorkspaceOptions(workspace);
        await service.ApplyAsync(task, patch1, options);
        await service.ApplyAsync(task, patch2, options);

        var result = await service.RevertTaskAsync(task, options);

        Assert.Equal(2, result.RevertedCount);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
        Assert.Equal("second-original", await File.ReadAllTextAsync(Path.Combine(workspace, "second.md")));
        Assert.Equal(AgentDraftPatchStatus.Reverted, patch1.Status);
        Assert.Equal(AgentDraftPatchStatus.Reverted, patch2.Status);
        Assert.NotNull(patch1.RevertedAt);
        Assert.Equal("User", patch1.RevertedBy);
        var trace = await File.ReadAllTextAsync(Path.Combine(store.GetTaskDirectory(task.TaskId), "agent.trace.jsonl"));
        Assert.Contains("task_reverted", trace);
    }

    [Fact]
    public async Task RevertTaskAsync_deletes_a_file_the_run_created()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test" };
        var patch = new AgentDraftPatch { RelativePath = "new-file.md", ProposedContent = "brand new content" };
        task.DraftPatches.Add(patch);
        var options = new AgentWorkspaceOptions(workspace);
        await service.ApplyAsync(task, patch, options);
        Assert.True(File.Exists(Path.Combine(workspace, "new-file.md")));

        var result = await service.RevertTaskAsync(task, options);

        Assert.Equal(1, result.RevertedCount);
        Assert.False(File.Exists(Path.Combine(workspace, "new-file.md")));
    }

    [Fact]
    public async Task RevertTaskAsync_reports_partial_success_when_one_file_changed_again()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        File.WriteAllText(Path.Combine(workspace, "second.md"), "second-original");
        var task = new AgentTaskState { Goal = "test" };
        var patch1 = new AgentDraftPatch { RelativePath = "notes.md", ProposedContent = "updated" };
        var patch2 = new AgentDraftPatch { RelativePath = "second.md", ProposedContent = "second-updated" };
        task.DraftPatches.Add(patch1);
        task.DraftPatches.Add(patch2);
        var options = new AgentWorkspaceOptions(workspace);
        await service.ApplyAsync(task, patch1, options);
        await service.ApplyAsync(task, patch2, options);
        await File.WriteAllTextAsync(Path.Combine(workspace, "second.md"), "someone edited this after the patch");

        var result = await service.RevertTaskAsync(task, options);

        Assert.Equal(1, result.RevertedCount);
        Assert.Equal(2, result.TotalCount);
        Assert.Contains("Reverted 1 of 2", result.Summary, StringComparison.Ordinal);
        Assert.Contains("Skipped second.md", result.Summary, StringComparison.Ordinal);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
        Assert.Equal("someone edited this after the patch", await File.ReadAllTextAsync(Path.Combine(workspace, "second.md")));
        Assert.Equal(AgentDraftPatchStatus.Reverted, patch1.Status);
        Assert.Equal(AgentDraftPatchStatus.Applied, patch2.Status);
    }

    [Fact]
    public async Task RevertTaskAsync_refuses_while_the_task_is_running()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test", Status = AgentTaskStatus.Running };
        var options = new AgentWorkspaceOptions(workspace);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevertTaskAsync(task, options));
    }

    [Fact]
    public async Task RevertTaskAsync_refuses_when_a_tool_approval_is_pending()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test", PendingToolAction = new AgentPendingToolAction { ToolName = "run_command" } };
        var options = new AgentWorkspaceOptions(workspace);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevertTaskAsync(task, options));
    }

    [Fact]
    public async Task RevertTaskAsync_refuses_when_an_orchestration_child_is_unfinished()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "test" };
        task.SubTaskPlan.Add(new AgentSubTaskSpec { Goal = "child", Status = AgentSubTaskStatus.Running, TaskId = "child-1" });
        var options = new AgentWorkspaceOptions(workspace);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevertTaskAsync(task, options));
    }

    [Fact]
    public async Task RevertTaskAsync_reverts_a_parent_and_its_finished_children()
    {
        using var temp = new TempDir();
        var (service, agent, store, workspace) = Build(temp);
        var task = new AgentTaskState { Goal = "parent" };
        var child = new AgentTaskState { Goal = "child", ParentTaskId = task.TaskId, WorkspaceRoot = workspace };
        task.SubTaskPlan.Add(new AgentSubTaskSpec { Goal = "child", Status = AgentSubTaskStatus.Complete, TaskId = child.TaskId });

        var childPatch = new AgentDraftPatch { RelativePath = "notes.md", ProposedContent = "child-updated" };
        child.DraftPatches.Add(childPatch);
        var options = new AgentWorkspaceOptions(workspace);
        await service.ApplyAsync(child, childPatch, options);

        var result = await service.RevertTaskAsync(task, options);

        Assert.Equal(1, result.RevertedCount);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
        var reloadedChild = await store.LoadAsync(child.TaskId);
        Assert.Equal(AgentDraftPatchStatus.Reverted, reloadedChild!.DraftPatches.Single().Status);
    }
}
