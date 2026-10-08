using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Tests;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class AgentOwnerInteractionTests
{
    [Fact]
    public async Task Parent_owned_question_routes_to_the_child_and_clears_after_the_answer()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("workspace");
        Directory.CreateDirectory(root);
        var (store, service, parent, child) = await NewRoutedPairAsync(temp, root);
        child.Status = AgentTaskStatus.WaitingForUser;
        child.StepCount = 4;
        child.LastUserMessage = "Which source should I use?";
        await store.SaveAsync(child);
        var interaction = Question(child, sequence: 0);
        await SaveParentMirrorAsync(store, parent, child, interaction);

        await service.AppendUserReplyAsync(parent.TaskId, "Use the local source.");

        var savedChild = await store.LoadAsync(child.TaskId);
        var savedParent = await store.LoadAsync(parent.TaskId);
        Assert.Equal(AgentTaskStatus.Running, savedChild!.Status);
        Assert.Equal("Which source should I use?", savedChild.LastAnsweredQuestion);
        Assert.Empty(savedParent!.PendingOwnerInteractions);
        Assert.Empty(savedParent.PendingOwnerInteractionTaskId);
        Assert.Equal(AgentTaskStatus.Running, savedParent.Status);
    }

    [Fact]
    public async Task Parent_owned_approval_routes_to_the_child_authority_and_block_keeps_an_explicit_owner_state()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("workspace");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "answer.txt"), "answer");
        var (store, service, parent, child) = await NewRoutedPairAsync(temp, root);
        child.Status = AgentTaskStatus.WaitingForUser;
        child.StepCount = 2;
        child.PendingToolAction = new AgentPendingToolAction
        {
            ToolName = "read_file",
            Arguments = new Dictionary<string, object?> { ["relative_path"] = "answer.txt" },
            Reason = "Read the selected source."
        };
        child.LastUserMessage = "Review the selected source.";
        await store.SaveAsync(child);
        var interaction = Approval(child, sequence: 0);
        await SaveParentMirrorAsync(store, parent, child, interaction);

        var approval = await service.AppendApprovalAsync(
            parent.TaskId,
            "read_file",
            approved: true,
            interaction.Fingerprint,
            new AgentWorkspaceOptions(root));

        Assert.True(approval.Applied, approval.Message);
        var approvedChild = await store.LoadAsync(child.TaskId);
        var approvedParent = await store.LoadAsync(parent.TaskId);
        Assert.Null(approvedChild!.PendingToolAction);
        Assert.Contains(approvedChild.ToolResults, result => result.Tool == "read_file");
        Assert.Empty(approvedParent!.PendingOwnerInteractions);
        Assert.Equal(AgentTaskStatus.Running, approvedParent.Status);

        approvedChild.Status = AgentTaskStatus.WaitingForUser;
        approvedChild.StepCount++;
        approvedChild.PendingToolAction = new AgentPendingToolAction
        {
            ToolName = "read_file",
            Arguments = new Dictionary<string, object?> { ["relative_path"] = "answer.txt" },
            Reason = "Read the selected source again."
        };
        approvedChild.LastUserMessage = "Review the selected source again.";
        await store.SaveAsync(approvedChild);
        var blockedInteraction = Approval(approvedChild, sequence: 0);
        await SaveParentMirrorAsync(store, approvedParent, approvedChild, blockedInteraction);

        var blocked = await service.BlockPendingActionAsync(
            approvedParent.TaskId,
            "read_file",
            blockedInteraction.Fingerprint,
            new AgentWorkspaceOptions(root));

        Assert.True(blocked.Applied, blocked.Message);
        var blockedParent = await store.LoadAsync(parent.TaskId);
        var blockedChild = await store.LoadAsync(child.TaskId);
        Assert.Null(blockedChild!.PendingToolAction);
        Assert.Equal(AgentTaskStatus.Blocked, blockedChild.Status);
        var mirroredInstruction = Assert.Single(blockedParent!.PendingOwnerInteractions);
        Assert.Equal(AgentOwnerInteractionKind.Instruction, mirroredInstruction.Kind);
        Assert.Contains("blocked", blockedParent.LastUserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AgentTaskStatus.Blocked, blockedParent.Status);
    }

    [Fact]
    public async Task A_stale_parent_question_cannot_satisfy_a_newer_child_question()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("workspace");
        Directory.CreateDirectory(root);
        var (store, service, parent, child) = await NewRoutedPairAsync(temp, root);
        child.Status = AgentTaskStatus.WaitingForUser;
        child.StepCount = 8;
        child.LastUserMessage = "New question";
        await store.SaveAsync(child);

        var stale = new AgentOwnerInteraction
        {
            SourceTaskId = child.TaskId,
            Kind = AgentOwnerInteractionKind.Question,
            SourceStatus = AgentTaskStatus.WaitingForUser,
            SourceStepCount = 7,
            Prompt = "Old question",
            Sequence = 0
        };
        await SaveParentMirrorAsync(store, parent, child, stale);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AppendUserReplyAsync(parent.TaskId, "answer to the old question"));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        var unchangedChild = await store.LoadAsync(child.TaskId);
        Assert.Equal(AgentTaskStatus.WaitingForUser, unchangedChild!.Status);
        Assert.Equal("New question", unchangedChild.LastUserMessage);
    }

    [Fact]
    public async Task Restart_rebuilds_the_parent_interaction_and_keeps_the_child_routable()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var parent = new AgentTaskState
        {
            TaskId = "restart-parent",
            Goal = "Parent goal",
            WorkspaceRoot = workspace,
            Status = AgentTaskStatus.WaitingForUser,
            SubTaskPlan =
            [
                new AgentSubTaskSpec
                {
                    TaskId = "restart-child",
                    Goal = "Child goal",
                    ProfileName = "general",
                    Status = AgentSubTaskStatus.Running
                }
            ],
            LastUserMessage = "stale parent question",
            PendingOwnerInteractionTaskId = "restart-child",
            PendingOwnerInteractionId = "stale-interaction",
            PendingOwnerInteractions =
            [
                new AgentOwnerInteraction
                {
                    SourceTaskId = "restart-child",
                    Kind = AgentOwnerInteractionKind.Question,
                    Prompt = "stale parent question",
                    SourceStatus = AgentTaskStatus.WaitingForUser,
                    SourceStepCount = 1
                }
            ]
        };
        var child = new AgentTaskState
        {
            TaskId = "restart-child",
            ParentTaskId = parent.TaskId,
            Goal = "Child goal",
            WorkspaceRoot = workspace,
            Status = AgentTaskStatus.WaitingForUser,
            StepCount = 2,
            LastUserMessage = "current child question",
            PendingOwnerInteractions =
            [
                new AgentOwnerInteraction
                {
                    SourceTaskId = "restart-child",
                    Kind = AgentOwnerInteractionKind.Question,
                    Prompt = "child interaction that must not be independently queued"
                }
            ]
        };
        await store.SaveAsync(parent);
        await store.SaveAsync(child);

        var restarted = new FileAgentTaskStateStore(settings);
        await restarted.InitializeAsync();

        var queue = await restarted.ListReviewQueueAsync();
        var queued = Assert.Single(queue, item => item.TaskId == parent.TaskId);
        var restoredParent = await restarted.LoadAsync(parent.TaskId);
        var restoredChild = await restarted.LoadAsync(child.TaskId);
        var interaction = Assert.Single(restoredParent!.PendingOwnerInteractions);
        Assert.Equal(parent.TaskId, queued.TaskId);
        Assert.Equal("current child question", interaction.Prompt);
        Assert.Equal(child.TaskId, restoredParent.PendingOwnerInteractionTaskId);
        Assert.Empty(restoredChild!.PendingOwnerInteractions);
        Assert.Contains(queue, item => item.TaskId == child.TaskId && item.ParentTaskId == parent.TaskId);
    }

    [Fact]
    public async Task Restart_terminalizes_an_orphaned_child_and_removes_its_pending_interaction()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var child = new AgentTaskState
        {
            TaskId = "orphan-child",
            ParentTaskId = "missing-parent",
            Goal = "Orphan goal",
            Status = AgentTaskStatus.WaitingForUser,
            LastUserMessage = "orphan question",
            PendingToolAction = new AgentPendingToolAction { ToolName = "read_file" },
            PendingOwnerInteractions =
            [
                new AgentOwnerInteraction
                {
                    SourceTaskId = "orphan-child",
                    Kind = AgentOwnerInteractionKind.Approval,
                    Prompt = "orphan approval"
                }
            ]
        };
        await store.SaveAsync(child);

        var restarted = new FileAgentTaskStateStore(settings);
        await restarted.InitializeAsync();

        var restored = await restarted.LoadAsync(child.TaskId);
        Assert.Equal(AgentTaskStatus.Interrupted, restored!.Status);
        Assert.Null(restored.PendingToolAction);
        Assert.Empty(restored.PendingOwnerInteractions);
        Assert.Contains("persisted parent", restored.InterruptionReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await restarted.ListReviewQueueAsync(), item => item.TaskId == child.TaskId);
    }

    private static AgentOwnerInteraction Question(AgentTaskState child, int sequence) => new()
    {
        SourceTaskId = child.TaskId,
        Kind = AgentOwnerInteractionKind.Question,
        SourceStatus = child.Status,
        SourceStepCount = child.StepCount,
        Prompt = child.LastUserMessage,
        Sequence = sequence
    };

    private static AgentOwnerInteraction Approval(AgentTaskState child, int sequence) => new()
    {
        SourceTaskId = child.TaskId,
        Kind = AgentOwnerInteractionKind.Approval,
        SourceStatus = child.Status,
        SourceStepCount = child.StepCount,
        Prompt = child.LastUserMessage,
        PendingToolAction = child.PendingToolAction,
        ProposalId = child.PendingToolAction?.ProposalId ?? string.Empty,
        ProposalRevision = child.PendingToolAction?.ProposalRevision ?? 0,
        Fingerprint = AgentApprovalFingerprint.Resolve(child.PendingToolAction),
        Sequence = sequence
    };

    private static async Task SaveParentMirrorAsync(
        FileAgentTaskStateStore store,
        AgentTaskState parent,
        AgentTaskState child,
        AgentOwnerInteraction interaction)
    {
        parent.Status = interaction.Kind == AgentOwnerInteractionKind.Instruction
            ? AgentTaskStatus.Blocked
            : AgentTaskStatus.WaitingForUser;
        parent.LastUserMessage = interaction.Prompt;
        parent.PendingToolAction = interaction.PendingToolAction;
        parent.PendingOwnerInteractions = [interaction];
        parent.PendingOwnerInteractionTaskId = child.TaskId;
        parent.PendingOwnerInteractionId = interaction.InteractionId;
        await store.SaveAsync(parent);
    }

    private static async Task<(FileAgentTaskStateStore Store, AgentService Service, AgentTaskState Parent, AgentTaskState Child)> NewRoutedPairAsync(
        TempDir temp,
        string root)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var tools = new AgentWorkspaceTools();
        var service = new AgentService(
            store,
            new FakeAgentContextBuilder(),
            new AgentSafetyGate(),
            new AgentToolExecutor(tools),
            new FakeAgentLlm(),
            settings: settings);
        var parent = new AgentTaskState
        {
            TaskId = "parent-task",
            Goal = "Parent goal",
            Status = AgentTaskStatus.Running,
            WorkspaceRoot = root,
            SubTaskPlan =
            [
                new AgentSubTaskSpec
                {
                    TaskId = "child-task",
                    Goal = "Child goal",
                    ProfileName = "general",
                    Status = AgentSubTaskStatus.Running
                }
            ]
        };
        var child = new AgentTaskState
        {
            TaskId = "child-task",
            Goal = "Child goal",
            ParentTaskId = parent.TaskId,
            WorkspaceRoot = root,
            Status = AgentTaskStatus.New
        };
        await store.SaveAsync(parent);
        await store.SaveAsync(child);
        return (store, service, parent, child);
    }
}
