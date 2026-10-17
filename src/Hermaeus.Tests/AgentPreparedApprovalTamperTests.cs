using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class AgentPreparedApprovalTamperTests
{
    [Theory]
    [InlineData("proposal-id")]
    [InlineData("revision")]
    [InlineData("workspace")]
    [InlineData("preimage-hash")]
    [InlineData("preimage-existed")]
    [InlineData("proposed-hash")]
    [InlineData("arguments")]
    [InlineData("policy-fingerprint")]
    [InlineData("schema")]
    [InlineData("reviewed-fingerprint")]
    public async Task Stale_rendered_approval_cannot_execute_or_consume_a_changed_persisted_proposal(string changedField)
    {
        using var temp = new TempDir();
        var fixture = await CreateAsync(temp);
        var persisted = (await fixture.Store.LoadAsync(fixture.Task.TaskId))!;
        var pending = persisted.PendingToolAction!;
        var fingerprint = AgentApprovalFingerprint.Resolve(pending);
        switch (changedField)
        {
            case "proposal-id": pending.ProposalId = Guid.NewGuid().ToString("N"); break;
            case "revision": pending.ProposalRevision++; break;
            case "workspace": pending.WorkspaceRoot = fixture.OtherWorkspace; break;
            case "preimage-hash": pending.ExpectedPreImageSha256 = new string('0', 64); break;
            case "preimage-existed": pending.ExpectedPreImageExisted = false; break;
            case "proposed-hash": pending.ProposedContentSha256 = new string('0', 64); break;
            case "arguments": pending.Arguments["new_string"] = "changed after review"; break;
            case "policy-fingerprint": pending.PolicyFingerprint = new string('0', 64); break;
            case "schema": pending.SchemaVersion++; break;
            case "reviewed-fingerprint": fingerprint = new string('0', 64); break;
            default: throw new ArgumentException(nameof(changedField));
        }
        await fixture.Store.SaveAsync(persisted);
        var pendingBefore = JsonSerializer.Serialize((await fixture.Store.LoadAsync(persisted.TaskId))!.PendingToolAction);

        var result = await fixture.Agent.AppendApprovalAsync(persisted.TaskId, "tamper-review", true, fingerprint, fixture.Options);

        Assert.False(result.Applied);
        var after = (await fixture.Store.LoadAsync(persisted.TaskId))!;
        Assert.Equal(AgentTaskStatus.WaitingForUser, after.Status);
        Assert.Equal(pendingBefore, JsonSerializer.Serialize(after.PendingToolAction));
        Assert.Empty(after.ApprovalHistory);
        Assert.Empty(after.MutationReceipts);
        Assert.Equal("before", await File.ReadAllTextAsync(Path.Combine(fixture.Options.WorkspaceRoot, "note.txt")));
        Assert.Equal("other owner data", await File.ReadAllTextAsync(Path.Combine(fixture.OtherWorkspace, "note.txt")));
        Assert.Contains(after.ToolResults, tool => tool.NormalizedOutcome.Outcome == NormalizedOutcome.Blocked);
    }

    [Fact]
    public async Task Tampered_payload_with_unchanged_fingerprint_fields_is_refused_by_fresh_execution_validation()
    {
        using var temp = new TempDir();
        var fixture = await CreateAsync(temp);
        var pending = fixture.Task.PendingToolAction!;
        var fingerprint = AgentApprovalFingerprint.Resolve(pending);
        pending.ProposedContent = "not the reviewed complete replacement";
        await fixture.Store.SaveAsync(fixture.Task);

        var result = await fixture.Agent.AppendApprovalAsync(fixture.Task.TaskId, "payload-review", true, fingerprint, fixture.Options);

        Assert.False(result.Applied);
        Assert.Equal(AgentMutationOutcome.Conflict, result.Outcome);
        var after = (await fixture.Store.LoadAsync(fixture.Task.TaskId))!;
        Assert.Equal(AgentTaskStatus.Blocked, after.Status);
        Assert.NotNull(after.PendingToolAction);
        Assert.Equal(pending.ProposalId, after.PendingToolAction.ProposalId);
        Assert.Empty(after.MutationReceipts);
        Assert.Equal("before", await File.ReadAllTextAsync(Path.Combine(fixture.Options.WorkspaceRoot, "note.txt")));
        Assert.Equal("other owner data", await File.ReadAllTextAsync(Path.Combine(fixture.OtherWorkspace, "note.txt")));
    }

    [Fact]
    public async Task Approval_recomputes_prepared_identity_instead_of_trusting_the_stored_fingerprint_cache()
    {
        using var temp = new TempDir();
        var fixture = await CreateAsync(temp);
        var pending = fixture.Task.PendingToolAction!;
        var fingerprint = AgentApprovalFingerprint.Resolve(pending);
        pending.Fingerprint = new string('0', 64);
        await fixture.Store.SaveAsync(fixture.Task);

        var result = await fixture.Agent.AppendApprovalAsync(fixture.Task.TaskId, "cache-review", true, fingerprint, fixture.Options);

        Assert.True(result.Applied);
        Assert.Equal("after", await File.ReadAllTextAsync(Path.Combine(fixture.Options.WorkspaceRoot, "note.txt")));
        var after = (await fixture.Store.LoadAsync(fixture.Task.TaskId))!;
        Assert.Null(after.PendingToolAction);
        Assert.Single(after.ApprovalHistory);
        Assert.Contains(after.MutationReceipts, receipt => receipt.Outcome == AgentMutationOutcome.Applied && receipt.Verified);
    }

    private static async Task<Fixture> CreateAsync(TempDir temp)
    {
        var settings = NewSettings(temp);
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        var other = temp.PathFor("other-workspace");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(other);
        await File.WriteAllTextAsync(Path.Combine(workspace, "note.txt"), "before");
        await File.WriteAllTextAsync(Path.Combine(other, "note.txt"), "other owner data");
        var tools = new AgentWorkspaceTools();
        var options = new AgentWorkspaceOptions(workspace);
        var prepared = await AgentMutationPreparation.PrepareAsync("edit_file", new()
        {
            ["relative_path"] = "note.txt", ["old_string"] = "before", ["new_string"] = "after"
        }, options, policy: null, workspaceTools: tools);
        Assert.True(prepared.IsValid, prepared.Error);
        var task = new AgentTaskState
        {
            Goal = "Edit only the reviewed note", WorkspaceRoot = workspace,
            Status = AgentTaskStatus.WaitingForUser, PendingToolAction = prepared.Pending
        };
        await store.SaveAsync(task);
        var agent = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(),
            new AgentToolExecutor(tools), new FakeAgentLlm(), settings: settings, workspaceTools: tools);
        return new(agent, store, task, options, other);
    }

    private sealed record Fixture(AgentService Agent, FileAgentTaskStateStore Store, AgentTaskState Task,
        AgentWorkspaceOptions Options, string OtherWorkspace);
}
