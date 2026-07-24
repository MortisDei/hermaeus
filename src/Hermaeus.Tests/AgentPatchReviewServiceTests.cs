using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AgentPatchReviewServiceTests
{
    private sealed class RecordingAgentService : IAgentService
    {
        public readonly List<string> Actions = [];

        public Task<AgentTaskState> CreateTaskAsync(string goal, AgentWorkspaceOptions options, CancellationToken ct = default) =>
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

        public Task<AgentTaskState> ContinueTaskAsync(string taskId, string instruction, AgentWorkspaceOptions options, CancellationToken ct = default) =>
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
}
