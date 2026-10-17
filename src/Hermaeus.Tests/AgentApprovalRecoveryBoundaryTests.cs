using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class AgentApprovalRecoveryBoundaryTests
{
    [Theory]
    [InlineData(false, false, AgentMutationOutcome.Unknown, false, false)]
    [InlineData(true, false, AgentMutationOutcome.Applied, false, false)]
    [InlineData(true, true, AgentMutationOutcome.Conflict, false, false)]
    [InlineData(true, false, AgentMutationOutcome.Applied, true, false)]
    [InlineData(true, false, AgentMutationOutcome.Applied, true, true)]
    public async Task Public_approval_snapshot_recovers_waiting_execution_without_replay(
        bool writeBeforeBarrier, bool externalEdit, AgentMutationOutcome expected, bool childApproval, bool healthySibling)
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var target = Path.Combine(workspace, "notes.md");
        await File.WriteAllTextAsync(target, "before");
        var tools = new AgentWorkspaceTools();
        var options = new AgentWorkspaceOptions(workspace);
        var preparation = await AgentMutationPreparation.PrepareAsync("edit_file", new()
        {
            ["relative_path"] = "notes.md", ["old_string"] = "before", ["new_string"] = "after"
        }, options, null, tools);
        Assert.True(preparation.IsValid, preparation.Error);
        var pending = preparation.Pending!;
        var fingerprint = AgentApprovalFingerprint.Resolve(pending);
        var task = new AgentTaskState
        {
            TaskId = childApproval ? "z-child-approval-recovery" : Guid.NewGuid().ToString("N"),
            Goal = "Recover an actual prepared approval boundary",
            WorkspaceRoot = workspace,
            Status = AgentTaskStatus.WaitingForUser,
            PendingToolAction = pending
        };
        AgentTaskState? parent = null;
        AgentTaskState? sibling = null;
        if (childApproval)
        {
            var interaction = new AgentOwnerInteraction
            {
                SourceTaskId = task.TaskId, Kind = AgentOwnerInteractionKind.Approval,
                SourceStatus = task.Status, Prompt = "Approve this exact child edit.",
                PendingToolAction = pending, ProposalId = pending.ProposalId,
                ProposalRevision = pending.ProposalRevision, Fingerprint = fingerprint
            };
            parent = new AgentTaskState
            {
                TaskId = "a-parent-approval-recovery", Goal = "Parent of the interrupted approved edit",
                WorkspaceRoot = workspace, Status = AgentTaskStatus.WaitingForUser,
                PendingToolAction = pending, LastUserMessage = interaction.Prompt,
                PendingOwnerInteractionTaskId = task.TaskId,
                PendingOwnerInteractionId = interaction.InteractionId,
                PendingOwnerInteractions = [interaction],
                SubTaskPlan = [new AgentSubTaskSpec { TaskId = task.TaskId, Goal = task.Goal, Status = AgentSubTaskStatus.Running }]
            };
            task.ParentTaskId = parent.TaskId;
            if (healthySibling)
            {
                sibling = new AgentTaskState
                {
                    TaskId = "healthy-sibling", ParentTaskId = parent.TaskId, Goal = "A separate legitimate child question",
                    WorkspaceRoot = workspace, Status = AgentTaskStatus.WaitingForUser,
                    LastUserMessage = "A legitimate sibling question remains unanswered."
                };
                parent.SubTaskPlan.Add(new AgentSubTaskSpec
                {
                    TaskId = sibling.TaskId, Goal = sibling.Goal, Status = AgentSubTaskStatus.Running
                });
            }
            await store.SaveAsync(parent);
            if (sibling is not null) await store.SaveAsync(sibling);
        }
        await store.SaveAsync(task);
        var executor = new ControlledExecutor(new AgentToolExecutor(tools), writeBeforeBarrier);
        var agent = NewAgent(store, executor, tools);
        var approval = agent.AppendApprovalAsync(task.TaskId, "owner-approved", true, fingerprint, options);
        try
        {
            await executor.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var durable = (await store.LoadAsync(task.TaskId))!;
            Assert.Equal(AgentTaskStatus.WaitingForUser, durable.Status);
            Assert.Equal(AgentMutationOutcome.Pending, Assert.Single(durable.MutationReceipts).Outcome);
            Assert.Single(durable.ApprovalHistory);
            Assert.Equal(writeBeforeBarrier ? "after" : "before", await File.ReadAllTextAsync(target));

            // Snapshot actual durable bytes from the public approval path.
            // The original owner remains blocked and uses a different Data
            // Root, then is released/joined in finally. Actual process death
            // is separate external-probe evidence, not claimed by this test.
            var reopenedSettings = new SettingsService(temp.PathFor("reopened-settings/settings.json"));
            reopenedSettings.Settings.DataManagement.DataRootDirectory = temp.PathFor("reopened-data");
            var snapshotStore = new FileAgentTaskStateStore(reopenedSettings);
            var snapshotDirectory = snapshotStore.GetTaskDirectory(task.TaskId);
            Directory.CreateDirectory(snapshotDirectory);
            File.Copy(Path.Combine(store.GetTaskDirectory(task.TaskId), "task_state.json"),
                Path.Combine(snapshotDirectory, "task_state.json"));
            if (parent is not null)
            {
                var parentDirectory = snapshotStore.GetTaskDirectory(parent.TaskId);
                Directory.CreateDirectory(parentDirectory);
                File.Copy(Path.Combine(store.GetTaskDirectory(parent.TaskId), "task_state.json"),
                    Path.Combine(parentDirectory, "task_state.json"));
            }
            if (sibling is not null)
            {
                var siblingDirectory = snapshotStore.GetTaskDirectory(sibling.TaskId);
                Directory.CreateDirectory(siblingDirectory);
                File.Copy(Path.Combine(store.GetTaskDirectory(sibling.TaskId), "task_state.json"),
                    Path.Combine(siblingDirectory, "task_state.json"));
            }
            if (externalEdit) await File.WriteAllTextAsync(target, "neither");
            var targetBytes = await File.ReadAllBytesAsync(target);
            DateTime? firstFinishedAt = null;
            int? firstDecisionCount = null;
            for (var reopen = 0; reopen < 2; reopen++)
            {
                var reopened = new FileAgentTaskStateStore(reopenedSettings);
                await reopened.InitializeAsync();
                var recovered = (await reopened.LoadAsync(task.TaskId))!;
                var receipt = Assert.Single(recovered.MutationReceipts);
                Assert.Equal(durable.MutationReceipts[0].ReceiptId, receipt.ReceiptId);
                Assert.Equal(expected, receipt.Outcome);
                Assert.Equal(expected == AgentMutationOutcome.Applied, receipt.Verified);
                Assert.Equal("startup-recovery:" + receipt.ReceiptId, receipt.EvidenceId);
                Assert.Equal(AgentTaskStatus.Interrupted, recovered.Status);
                Assert.Null(recovered.PendingToolAction);
                Assert.Single(recovered.ApprovalHistory);
                Assert.Equal(targetBytes, await File.ReadAllBytesAsync(target));
                if (parent is not null)
                {
                    var recoveredParent = (await reopened.LoadAsync(parent.TaskId))!;
                    Assert.Equal(healthySibling ? AgentTaskStatus.WaitingForUser : AgentTaskStatus.Interrupted, recoveredParent.Status);
                    Assert.Equal(AgentSubTaskStatus.Interrupted, recoveredParent.SubTaskPlan.Single(item => item.TaskId == task.TaskId).Status);
                    Assert.Null(recoveredParent.PendingToolAction);
                    if (sibling is not null)
                    {
                        var projected = Assert.Single(recoveredParent.PendingOwnerInteractions);
                        Assert.Equal(sibling.TaskId, projected.SourceTaskId);
                        Assert.Equal(AgentOwnerInteractionKind.Question, projected.Kind);
                        Assert.Equal(sibling.LastUserMessage, projected.Prompt);
                        Assert.Equal(sibling.TaskId, recoveredParent.PendingOwnerInteractionTaskId);
                        Assert.Equal(AgentSubTaskStatus.Running, recoveredParent.SubTaskPlan.Single(item => item.TaskId == sibling.TaskId).Status);
                    }
                    else
                    {
                        Assert.Empty(recoveredParent.PendingOwnerInteractions);
                        Assert.Empty(recoveredParent.PendingOwnerInteractionTaskId);
                        Assert.Empty(recoveredParent.PendingOwnerInteractionId);
                    }
                    var staleParent = await NewAgent(reopened, new AgentToolExecutor(tools), tools)
                        .AppendApprovalAsync(parent.TaskId, "stale-parent-approval", true, fingerprint, options);
                    Assert.False(staleParent.Applied);
                    Assert.Equal(targetBytes, await File.ReadAllBytesAsync(target));
                }
                firstFinishedAt ??= receipt.FinishedAt;
                firstDecisionCount ??= recovered.Decisions.Count;
                Assert.Equal(firstFinishedAt, receipt.FinishedAt);
                Assert.Equal(firstDecisionCount, recovered.Decisions.Count);

                var stale = await NewAgent(reopened, new AgentToolExecutor(tools), tools)
                    .AppendApprovalAsync(task.TaskId, "stale-approval", true, fingerprint, options);
                Assert.False(stale.Applied);
                var afterRefusal = (await reopened.LoadAsync(task.TaskId))!;
                Assert.Single(afterRefusal.ApprovalHistory);
                Assert.Single(afterRefusal.MutationReceipts);
                Assert.Null(afterRefusal.PendingToolAction);
                Assert.Equal(targetBytes, await File.ReadAllBytesAsync(target));
            }
        }
        finally
        {
            executor.Release.TrySetResult();
            await approval.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(AgentTaskStatus.Running, AgentTaskStatus.Interrupted)]
    [InlineData(AgentTaskStatus.WaitingForUser, AgentTaskStatus.Interrupted)]
    [InlineData(AgentTaskStatus.Complete, AgentTaskStatus.Complete)]
    [InlineData(AgentTaskStatus.Cancelled, AgentTaskStatus.Cancelled)]
    [InlineData(AgentTaskStatus.Failed, AgentTaskStatus.Failed)]
    [InlineData(AgentTaskStatus.Blocked, AgentTaskStatus.Blocked)]
    public async Task Pending_receipt_recovery_preserves_deliberate_terminal_or_blocked_status(
        AgentTaskStatus original, AgentTaskStatus expected)
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var target = Path.Combine(workspace, "notes.md");
        await File.WriteAllTextAsync(target, "before");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var task = new AgentTaskState
        {
            Goal = "Recover a pending receipt without erasing a deliberate status",
            WorkspaceRoot = workspace,
            Status = original,
            PendingToolAction = new AgentPendingToolAction { ToolName = "edit_file" },
            MutationReceipts =
            [
                new AgentMutationReceipt
                {
                    ToolName = "edit_file", MutationKind = AgentMutationKind.Edit,
                    WorkspaceRoot = workspace, RelativePath = "notes.md",
                    ExpectedPreImageExisted = true,
                    ExpectedPreImageSha256 = AgentMutationPreparation.ComputeContentSha256("before"),
                    ProposedContentSha256 = AgentMutationPreparation.ComputeContentSha256("after"),
                    ApprovalRecorded = true, Outcome = AgentMutationOutcome.Pending
                }
            ]
        };
        await store.SaveAsync(task);

        var reopened = new FileAgentTaskStateStore(settings);
        await reopened.InitializeAsync();
        var recovered = (await reopened.LoadAsync(task.TaskId))!;

        Assert.Equal(expected, recovered.Status);
        Assert.Null(recovered.PendingToolAction);
        Assert.Equal(AgentMutationOutcome.Unknown, Assert.Single(recovered.MutationReceipts).Outcome);
        Assert.Equal("before", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task An_unexecuted_pending_approval_remains_available_for_review_after_reopen()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var task = new AgentTaskState
        {
            Goal = "An owner has not approved this yet",
            Status = AgentTaskStatus.WaitingForUser,
            PendingToolAction = new AgentPendingToolAction { ToolName = "edit_file" }
        };
        await store.SaveAsync(task);

        var reopened = new FileAgentTaskStateStore(settings);
        await reopened.InitializeAsync();
        var recovered = (await reopened.LoadAsync(task.TaskId))!;

        Assert.Equal(AgentTaskStatus.WaitingForUser, recovered.Status);
        Assert.NotNull(recovered.PendingToolAction);
        Assert.Empty(recovered.MutationReceipts);
        Assert.Empty(recovered.ApprovalHistory);
    }

    private static AgentService NewAgent(IAgentTaskStateStore store, IAgentToolExecutor executor, IAgentWorkspaceTools tools) =>
        new(store, new UnusedContext(), new AgentSafetyGate(), executor, new UnusedLlm(), workspaceTools: tools);

    private sealed class ControlledExecutor(IAgentToolExecutor inner, bool writeBeforeBarrier) : IAgentToolExecutor
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanExecute(string toolName) => inner.CanExecute(toolName);
        public Task<AgentToolResult> ExecuteAsync(string toolName, Dictionary<string, object?> arguments,
            AgentWorkspaceOptions options, CancellationToken ct = default, string? operationId = null) =>
            throw new InvalidOperationException("This fixture must use the real prepared execution path.");

        public async Task<AgentToolResult> ExecuteApprovedPreparedMutationAsync(AgentPendingToolAction pending,
            string approvalFingerprint, AgentWorkspaceOptions options, CancellationToken ct = default, string? operationId = null)
        {
            if (!writeBeforeBarrier)
            {
                Ready.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            var result = await inner.ExecuteApprovedPreparedMutationAsync(pending, approvalFingerprint, options, ct, operationId);
            if (writeBeforeBarrier)
            {
                Ready.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }

    private sealed class UnusedContext : IAgentContextBuilder
    {
        public Task<AgentContextPack> BuildAsync(AgentTaskState state, AgentWorkspaceOptions options, CancellationToken ct = default) =>
            throw new InvalidOperationException("Recovery must not invoke a planner/context builder.");
    }

    private sealed class UnusedLlm : ILlmService
    {
        public string ProviderName => "Unused recovery fixture";
        public bool IsConfigured => false;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Recovery must not list models.");
        public IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(string modelId, IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("Recovery must not invoke inference.");
    }
}
