using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// The review queue lists tasks that need a decision now, and an approval
/// with nothing pending changes nothing. Before this, the query also returned
/// every task that had ever been approved, and approving one of those rows
/// appended a history record and set the task back to Running, which
/// un-completed a finished task and let the workbench restart the agent loop
/// on it (docs/review/archived/r26 doc 01).
/// </summary>
public sealed class AgentReviewQueueTests
{
    private const string GatedToolResponse = """
        {
          "thought_summary": "Need to write something.",
          "current_step": "Draft a patch.",
          "next_action": {
            "type": "tool",
            "tool_name": "draft_patch",
            "arguments": { "path": "notes.md" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Proposing a patch."
        }
        """;

    private static async Task<(AgentService Agent, FileAgentTaskStateStore Store, AgentWorkspaceOptions Options)> BuildAsync(
        TempDir temp, ILlmService? llm = null)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();

        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var retrieval = new AgentRetrievalService(rag, ragStore);
        var activation = new WorkspaceActivationService(new WorkspaceManifestService(), new FileWorkspaceProfileStore(settings));
        var memoryStore = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memoryStore.InitializeAsync();
        var tools = new AgentWorkspaceTools();
        var contextBuilder = new AgentContextBuilder(tools, retrieval, memoryStore, activation, store, settings);

        var agent = new AgentService(store, contextBuilder, new AgentSafetyGate(), new AgentToolExecutor(tools),
            llm ?? new FakeSequencedAgentLlm([GatedToolResponse]), settings: settings, workspaceTools: tools);
        return (agent, store, new AgentWorkspaceOptions(workspace, null, "fake-sequenced-agent"));
    }

    private static AgentTaskState TaskWithApprovals(string id, AgentTaskStatus status, int approvals)
    {
        var state = new AgentTaskState { TaskId = id, Goal = $"goal for {id}", Status = status };
        for (var i = 0; i < approvals; i++)
            state.ApprovalHistory.Add(new AgentApprovalRecord("review_queue", true, DateTime.UtcNow));
        return state;
    }

    [Fact]
    public async Task A_completed_task_with_approval_history_is_not_in_the_review_queue()
    {
        using var temp = new TempDir();
        var (_, store, _) = await BuildAsync(temp);

        await store.SaveAsync(TaskWithApprovals("done", AgentTaskStatus.Complete, 3));

        var queue = await store.ListReviewQueueAsync();
        Assert.DoesNotContain(queue, item => item.TaskId == "done");
    }

    [Fact]
    public async Task A_failed_task_with_approval_history_is_not_in_the_review_queue()
    {
        using var temp = new TempDir();
        var (_, store, _) = await BuildAsync(temp);

        await store.SaveAsync(TaskWithApprovals("failed", AgentTaskStatus.Failed, 2));

        var queue = await store.ListReviewQueueAsync();
        Assert.DoesNotContain(queue, item => item.TaskId == "failed");
    }

    [Fact]
    public async Task A_waiting_task_with_no_approvals_is_in_the_review_queue()
    {
        using var temp = new TempDir();
        var (_, store, _) = await BuildAsync(temp);

        await store.SaveAsync(TaskWithApprovals("waiting", AgentTaskStatus.WaitingForUser, 0));

        var queue = await store.ListReviewQueueAsync();
        Assert.Contains(queue, item => item.TaskId == "waiting");
    }

    [Fact]
    public async Task A_blocked_task_is_in_the_review_queue()
    {
        using var temp = new TempDir();
        var (_, store, _) = await BuildAsync(temp);

        await store.SaveAsync(TaskWithApprovals("blocked", AgentTaskStatus.Blocked, 1));

        var queue = await store.ListReviewQueueAsync();
        Assert.Contains(queue, item => item.TaskId == "blocked");
    }

    [Fact]
    public async Task Approving_a_pending_action_removes_the_task_from_the_queue()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp);

        var created = await agent.CreateTaskAsync("Change a file", options);
        var stepped = await agent.RunStepAsync(created.TaskId, options);
        Assert.NotNull(stepped.State.PendingToolAction);
        Assert.Contains(await store.ListReviewQueueAsync(), item => item.TaskId == created.TaskId);

        var fingerprint = AgentApprovalFingerprint.Resolve(stepped.State.PendingToolAction);
        var result = await agent.AppendApprovalAsync(created.TaskId, "review_queue", approved: true, fingerprint, options);

        Assert.True(result.Applied);
        Assert.DoesNotContain(await store.ListReviewQueueAsync(), item => item.TaskId == created.TaskId);
    }

    [Fact]
    public async Task Approving_a_task_with_nothing_pending_changes_nothing()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp);

        await store.SaveAsync(TaskWithApprovals("finished", AgentTaskStatus.Complete, 1));

        var result = await agent.AppendApprovalAsync("finished", "review_queue", approved: true, string.Empty, options);

        Assert.False(result.Applied);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));

        var reloaded = await store.LoadAsync("finished");
        Assert.NotNull(reloaded);
        Assert.Equal(AgentTaskStatus.Complete, reloaded!.Status);
        Assert.Single(reloaded.ApprovalHistory);
    }

    [Fact]
    public async Task Rejecting_a_task_with_nothing_pending_changes_nothing()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp);

        await store.SaveAsync(TaskWithApprovals("finished", AgentTaskStatus.Complete, 1));

        var result = await agent.AppendApprovalAsync("finished", "review_queue", approved: false, string.Empty, options);

        Assert.False(result.Applied);

        var reloaded = await store.LoadAsync("finished");
        Assert.NotNull(reloaded);
        Assert.Equal(AgentTaskStatus.Complete, reloaded!.Status);
        Assert.Single(reloaded.ApprovalHistory);
    }

    [Fact]
    public async Task Approving_a_task_that_has_a_pending_action_still_executes_it()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp);

        var created = await agent.CreateTaskAsync("Change a file", options);
        var stepped = await agent.RunStepAsync(created.TaskId, options);
        var fingerprint = AgentApprovalFingerprint.Resolve(stepped.State.PendingToolAction);

        var result = await agent.AppendApprovalAsync(created.TaskId, "review_queue", approved: true, fingerprint, options);

        Assert.True(result.Applied);
        var reloaded = await store.LoadAsync(created.TaskId);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.PendingToolAction);
        Assert.Single(reloaded.ApprovalHistory);
        Assert.Equal(AgentTaskStatus.Running, reloaded.Status);
    }

    [Fact]
    public async Task Rejecting_a_pending_action_clears_it_and_records_the_decision()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp);

        var created = await agent.CreateTaskAsync("Change a file", options);
        var stepped = await agent.RunStepAsync(created.TaskId, options);
        var fingerprint = AgentApprovalFingerprint.Resolve(stepped.State.PendingToolAction);

        var result = await agent.AppendApprovalAsync(created.TaskId, "review_queue", approved: false, fingerprint, options);

        Assert.True(result.Applied);
        var reloaded = await store.LoadAsync(created.TaskId);
        Assert.Null(reloaded!.PendingToolAction);
        Assert.Equal(AgentTaskStatus.WaitingForUser, reloaded.Status);
        Assert.Single(reloaded.ApprovalHistory);
    }

    private static AgentReviewQueueItemViewModel Row(AgentTaskStatus status, AgentPendingToolAction? pending) =>
        new(new AgentReviewQueueItem(
            "task", "goal", status, DateTime.UtcNow, "step", "summary",
            ApprovalCount: 0, LastApprovalAction: null, LastApprovalApproved: null, LastApprovalAt: null,
            PendingToolAction: pending));

    [Fact]
    public void A_row_with_a_pending_action_offers_only_a_decision()
    {
        var row = Row(AgentTaskStatus.WaitingForUser, new AgentPendingToolAction
        {
            ToolName = "run_command",
            RiskLevel = AgentRiskLevel.Medium,
            Reason = "Template-family command execution always requires approval."
        });

        Assert.True(row.HasPendingAction);
        Assert.False(row.NeedsReply);
        Assert.False(row.NeedsInstruction);
        Assert.Equal(string.Empty, row.NoDecisionLabel);
    }

    [Fact]
    public void A_waiting_row_with_nothing_pending_asks_for_a_reply()
    {
        var row = Row(AgentTaskStatus.WaitingForUser, null);

        Assert.False(row.HasPendingAction);
        Assert.True(row.NeedsReply);
        Assert.False(row.NeedsInstruction);
        Assert.Contains("question", row.NoDecisionLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_blocked_row_asks_for_an_instruction()
    {
        var row = Row(AgentTaskStatus.Blocked, null);

        Assert.False(row.HasPendingAction);
        Assert.False(row.NeedsReply);
        Assert.True(row.NeedsInstruction);
        Assert.Contains("instruction", row.NoDecisionLabel, StringComparison.OrdinalIgnoreCase);
    }
}
