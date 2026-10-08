using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class AgentLifecycleRecoveryTests
{
    [Fact]
    public async Task Startup_recovery_interrupts_running_parent_and_child_with_a_reason()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var first = new FileAgentTaskStateStore(settings);
        await first.InitializeAsync();

        const string parentId = "parent-recovery";
        const string childId = "child-recovery";
        var parent = new AgentTaskState
        {
            TaskId = parentId,
            Goal = "Parent goal",
            Status = AgentTaskStatus.Running,
            SubTaskPlan =
            [
                new AgentSubTaskSpec
                {
                    TaskId = childId,
                    Goal = "Child goal",
                    Status = AgentSubTaskStatus.Running
                }
            ]
        };
        var child = new AgentTaskState
        {
            TaskId = childId,
            ParentTaskId = parentId,
            Goal = "Child goal",
            Status = AgentTaskStatus.Running
        };
        await first.SaveAsync(parent);
        await first.SaveAsync(child);

        var recovered = new FileAgentTaskStateStore(settings);
        await recovered.InitializeAsync();

        var recoveredParent = await recovered.LoadAsync(parentId);
        var recoveredChild = await recovered.LoadAsync(childId);
        Assert.NotNull(recoveredParent);
        Assert.NotNull(recoveredChild);
        Assert.Equal(AgentTaskStatus.Interrupted, recoveredParent!.Status);
        Assert.Equal(AgentTaskStatus.Interrupted, recoveredChild!.Status);
        Assert.False(string.IsNullOrWhiteSpace(recoveredParent.InterruptionReason));
        Assert.Contains("startup recovery", recoveredChild.InterruptionReason, StringComparison.Ordinal);
        var spec = Assert.Single(recoveredParent.SubTaskPlan);
        Assert.Equal(AgentSubTaskStatus.Interrupted, spec.Status);
        Assert.Contains(recoveredChild.InterruptionReason, spec.ResultSummary, StringComparison.Ordinal);

        var recent = await recovered.ListRecentAsync();
        Assert.Contains(recent, task => task.TaskId == parentId && task.Status == AgentTaskStatus.Interrupted);
        Assert.Contains(recent, task => task.TaskId == childId && task.Status == AgentTaskStatus.Interrupted);
    }

    [Fact]
    public async Task Delete_still_refuses_a_genuinely_running_task()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        await store.SaveAsync(new AgentTaskState
        {
            TaskId = "active-run",
            Goal = "Active goal",
            Status = AgentTaskStatus.Running
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync("active-run"));
        Assert.Contains("Stop the run", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_recovery_blocks_a_terminal_parent_with_unfinished_children()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var first = new FileAgentTaskStateStore(settings);
        await first.InitializeAsync();
        await first.SaveAsync(new AgentTaskState
        {
            TaskId = "terminal-parent",
            Goal = "parent",
            Status = AgentTaskStatus.Complete,
            Summary = "Reported complete.",
            SubTaskPlan =
            [
                new AgentSubTaskSpec
                {
                    TaskId = "pending-child",
                    Goal = "child",
                    Status = AgentSubTaskStatus.Pending
                }
            ]
        });

        var recoveredStore = new FileAgentTaskStateStore(settings);
        await recoveredStore.InitializeAsync();
        var recovered = await recoveredStore.LoadAsync("terminal-parent");

        Assert.NotNull(recovered);
        Assert.Equal(AgentTaskStatus.Blocked, recovered!.Status);
        Assert.Contains("unfinished", recovered.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Startup_recovery_marks_a_written_pending_receipt_applied_without_replay()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var target = Path.Combine(workspace, "rewrite.md");
        await File.WriteAllTextAsync(target, "after");

        var first = new FileAgentTaskStateStore(settings);
        await first.InitializeAsync();
        var task = new AgentTaskState
        {
            TaskId = "pending-written",
            Goal = "recover written mutation",
            Status = AgentTaskStatus.Running,
            WorkspaceRoot = workspace,
            PendingToolAction = new AgentPendingToolAction { ToolName = "edit_file" },
            MutationReceipts =
            [
                new AgentMutationReceipt
                {
                    TaskId = "pending-written",
                    ToolName = "edit_file",
                    MutationKind = AgentMutationKind.Replace,
                    WorkspaceRoot = workspace,
                    RelativePath = "rewrite.md",
                    ExpectedPreImageExisted = true,
                    ExpectedPreImageSha256 = AgentMutationPreparation.ComputeContentSha256("before"),
                    ProposedContentSha256 = AgentMutationPreparation.ComputeContentSha256("after"),
                    Outcome = AgentMutationOutcome.Pending
                }
            ]
        };
        await first.SaveAsync(task);

        var recoveredStore = new FileAgentTaskStateStore(settings);
        await recoveredStore.InitializeAsync();
        var recovered = await recoveredStore.LoadAsync(task.TaskId);

        var receipt = Assert.Single(recovered!.MutationReceipts);
        Assert.Equal(AgentMutationOutcome.Applied, receipt.Outcome);
        Assert.True(receipt.Verified);
        Assert.True(receipt.Changed);
        Assert.Equal("startup-recovery:" + receipt.ReceiptId, receipt.EvidenceId);
        Assert.Equal(AgentTaskStatus.Interrupted, recovered.Status);
        Assert.Null(recovered.PendingToolAction);
        Assert.Equal("after", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Startup_recovery_does_not_replay_when_only_the_preimage_exists()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "rewrite.md"), "before");

        var first = new FileAgentTaskStateStore(settings);
        await first.InitializeAsync();
        var task = PendingReceiptTask("pending-before", workspace, "before", "after");
        await first.SaveAsync(task);

        var recoveredStore = new FileAgentTaskStateStore(settings);
        await recoveredStore.InitializeAsync();
        var recovered = await recoveredStore.LoadAsync(task.TaskId);

        var receipt = Assert.Single(recovered!.MutationReceipts);
        Assert.Equal(AgentMutationOutcome.Unknown, receipt.Outcome);
        Assert.False(receipt.Verified);
        Assert.Contains("No replay", receipt.CompletionReason, StringComparison.Ordinal);
        Assert.Equal(AgentTaskStatus.Interrupted, recovered.Status);
        Assert.Equal("before", await File.ReadAllTextAsync(Path.Combine(workspace, "rewrite.md")));
    }

    [Fact]
    public async Task Startup_recovery_classifies_unexpected_content_as_conflict()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "rewrite.md"), "someone-else");

        var first = new FileAgentTaskStateStore(settings);
        await first.InitializeAsync();
        var task = PendingReceiptTask("pending-conflict", workspace, "before", "after");
        await first.SaveAsync(task);

        var recoveredStore = new FileAgentTaskStateStore(settings);
        await recoveredStore.InitializeAsync();
        var recovered = await recoveredStore.LoadAsync(task.TaskId);

        var receipt = Assert.Single(recovered!.MutationReceipts);
        Assert.Equal(AgentMutationOutcome.Conflict, receipt.Outcome);
        Assert.False(receipt.Verified);
        Assert.Contains("neither", receipt.CompletionReason, StringComparison.Ordinal);
        Assert.Equal("someone-else", await File.ReadAllTextAsync(Path.Combine(workspace, "rewrite.md")));
    }

    private static AgentTaskState PendingReceiptTask(string taskId, string workspace, string before, string after) => new()
    {
        TaskId = taskId,
        Goal = "recover pending mutation",
        Status = AgentTaskStatus.Running,
        WorkspaceRoot = workspace,
        MutationReceipts =
        [
            new AgentMutationReceipt
            {
                TaskId = taskId,
                ToolName = "apply_draft_patch",
                MutationKind = AgentMutationKind.Replace,
                WorkspaceRoot = workspace,
                RelativePath = "rewrite.md",
                ExpectedPreImageExisted = true,
                ExpectedPreImageSha256 = AgentMutationPreparation.ComputeContentSha256(before),
                ProposedContentSha256 = AgentMutationPreparation.ComputeContentSha256(after),
                Outcome = AgentMutationOutcome.Pending
            }
        ]
    };
}
