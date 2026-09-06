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
}
