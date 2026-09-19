using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class WorkspaceOwnerSaveTests
{
    [Fact]
    public async Task Owner_save_writes_directly_and_returns_a_new_revision_hash()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var path = Path.Combine(workspace, "notes.md");
        await File.WriteAllTextAsync(path, "before");

        var tools = new AgentWorkspaceTools();
        var result = await tools.SaveOwnerFileAsync(
            new AgentWorkspaceOptions(workspace),
            "notes.md",
            "after",
            AgentMutationPreparation.ComputeContentSha256("before"),
            expectedExisted: true);

        Assert.True(result.Changed);
        Assert.False(result.Conflict);
        Assert.Equal(AgentMutationPreparation.ComputeContentSha256("after"), result.ContentSha256);
        Assert.Equal("after", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Owner_save_refuses_to_overwrite_an_external_revision()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var path = Path.Combine(workspace, "notes.md");
        await File.WriteAllTextAsync(path, "before");
        var expectedHash = AgentMutationPreparation.ComputeContentSha256("before");
        await File.WriteAllTextAsync(path, "external");

        var result = await new AgentWorkspaceTools().SaveOwnerFileAsync(
            new AgentWorkspaceOptions(workspace), "notes.md", "owner edit", expectedHash, expectedExisted: true);

        Assert.False(result.Changed);
        Assert.True(result.Conflict);
        Assert.Contains("Reload", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("external", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Owner_save_rejects_unsafe_replacement_without_mutating_the_file()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var path = Path.Combine(workspace, "notes.md");
        await File.WriteAllTextAsync(path, "before");
        var expectedHash = AgentMutationPreparation.ComputeContentSha256("before");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentWorkspaceTools().SaveOwnerFileAsync(
            new AgentWorkspaceOptions(workspace), "notes.md", "unsafe\0content", expectedHash, expectedExisted: true));

        Assert.Equal("before", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Owner_save_makes_a_prepared_agent_patch_conflict_instead_of_clobbering_it()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "notes.md"), "before");
        var options = new AgentWorkspaceOptions(workspace);
        var task = new AgentTaskState
        {
            TaskId = "owner-save-stale-agent",
            Goal = "change notes",
            WorkspaceRoot = workspace,
            Status = AgentTaskStatus.Complete
        };
        await store.SaveAsync(task);

        var tools = new AgentWorkspaceTools();
        var review = new AgentPatchReviewService(tools, store);
        var patch = await review.QueueAsync(task.TaskId, "notes.md", "agent proposal", "agent", options);
        var ownerResult = await tools.SaveOwnerFileAsync(
            options,
            "notes.md",
            "owner edit",
            AgentMutationPreparation.ComputeContentSha256("before"),
            expectedExisted: true);

        var outcome = await review.ApplyAsync(task, patch, options);

        Assert.True(ownerResult.Changed);
        Assert.Equal(AgentMutationOutcome.Conflict, outcome);
        Assert.Equal("owner edit", await File.ReadAllTextAsync(Path.Combine(workspace, "notes.md")));
        Assert.Equal(AgentDraftPatchStatus.Blocked, patch.Status);
    }
}
