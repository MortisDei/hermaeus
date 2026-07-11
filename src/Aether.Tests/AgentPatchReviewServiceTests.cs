using Aether.Agent.Models;
using Aether.Agent.Services;
using Xunit;

namespace Aether.Tests;

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

        public Task AppendApprovalAsync(string taskId, string action, bool approved, AgentWorkspaceOptions? options = null, CancellationToken ct = default)
        {
            Actions.Add($"{action}:{approved}");
            return Task.CompletedTask;
        }

        public Task AppendUserReplyAsync(string taskId, string reply, CancellationToken ct = default) =>
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
}
