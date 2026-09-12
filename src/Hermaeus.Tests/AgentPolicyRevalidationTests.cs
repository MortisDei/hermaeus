using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class AgentPolicyRevalidationTests
{
    private const string EditResponse = """
        {
          "thought_summary": "Prepare the requested edit.",
          "current_step": "Awaiting approval.",
          "next_action": {
            "type": "tool",
            "tool_name": "edit_file",
            "arguments": { "relative_path": "allowed/notes.md", "old_string": "before", "new_string": "after" },
            "requires_approval": true,
            "risk_level": "high"
          },
          "state_update": { "completed": [], "pending": ["Approve the edit."], "new_facts": [], "blockers": [] },
          "user_message": "Review the prepared edit."
        }
        """;

    [Fact]
    public async Task Direct_approval_refuses_when_the_persisted_policy_changes_after_review()
    {
        using var temp = new TempDir();
        var fixture = await BuildAsync(temp);
        var created = await fixture.Agent.CreateTaskAsync("Edit the note", fixture.Options);
        var proposed = await fixture.Agent.RunStepAsync(created.TaskId, fixture.Options);
        var pending = proposed.State.PendingToolAction
            ?? throw new InvalidOperationException("The scripted edit did not produce a pending proposal.");

        await fixture.Manifests.SaveAsync(fixture.Workspace, DenyAllowedPathPolicy());
        var result = await fixture.Agent.AppendApprovalAsync(
            created.TaskId,
            "direct-policy-change",
            approved: true,
            AgentApprovalFingerprint.Resolve(pending),
            fixture.Options);

        Assert.False(result.Applied);
        Assert.Equal(AgentMutationOutcome.Conflict, result.Outcome);
        Assert.Equal("before", await File.ReadAllTextAsync(Path.Combine(fixture.Workspace, "allowed/notes.md")));
        var state = await fixture.Store.LoadAsync(created.TaskId);
        Assert.Equal(AgentTaskStatus.Blocked, state!.Status);
        Assert.NotNull(state.PendingToolAction);
    }

    [Fact]
    public async Task Queued_patch_refuses_when_the_persisted_policy_changes_after_review()
    {
        using var temp = new TempDir();
        var fixture = await BuildAsync(temp);
        var task = new AgentTaskState
        {
            TaskId = "queued-policy-change",
            Goal = "Queue an edit",
            Status = AgentTaskStatus.WaitingForUser,
            WorkspaceRoot = fixture.Workspace
        };
        await fixture.Store.SaveAsync(task);

        var patch = await fixture.Review.QueueAsync(
            task.TaskId,
            "allowed/notes.md",
            "Replace the marker.",
            "after",
            fixture.Options);
        await fixture.Manifests.SaveAsync(fixture.Workspace, DenyAllowedPathPolicy());

        var reloaded = await fixture.Store.LoadAsync(task.TaskId);
        var outcome = await fixture.Review.ApplyAsync(reloaded!, patch, fixture.Options);

        Assert.Equal(AgentMutationOutcome.Conflict, outcome);
        Assert.Equal("before", await File.ReadAllTextAsync(Path.Combine(fixture.Workspace, "allowed/notes.md")));
        Assert.Equal(AgentDraftPatchStatus.Blocked, patch.Status);
        Assert.Contains("write blocked", patch.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkspaceManifest DenyAllowedPathPolicy() => new()
    {
        Policy = new WorkspacePolicy
        {
            ReadAllow = ["allowed/**"],
            WriteAllow = ["other/**"],
            Never = ["allowed/**"]
        }
    };

    private static async Task<Fixture> BuildAsync(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();

        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(Path.Combine(workspace, "allowed"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "allowed/notes.md"), "before");

        var manifests = new WorkspaceManifestService();
        await manifests.SaveAsync(workspace, new WorkspaceManifest
        {
            Policy = new WorkspacePolicy
            {
                ReadAllow = ["allowed/**"],
                WriteAllow = ["allowed/**"]
            }
        });
        var tools = new AgentWorkspaceTools();
        var agent = new AgentService(
            store,
            new FakeAgentContextBuilder(),
            new AgentSafetyGate(),
            new AgentToolExecutor(tools),
            new FakeSequencedAgentLlm([EditResponse]),
            manifests: manifests,
            settings: settings,
            workspaceTools: tools);
        var options = new AgentWorkspaceOptions(workspace, null, "fake-sequenced-agent");
        var review = new AgentPatchReviewService(tools, store, manifests);
        return new Fixture(agent, store, manifests, review, options, workspace);
    }

    private sealed record Fixture(
        AgentService Agent,
        FileAgentTaskStateStore Store,
        WorkspaceManifestService Manifests,
        AgentPatchReviewService Review,
        AgentWorkspaceOptions Options,
        string Workspace);
}
