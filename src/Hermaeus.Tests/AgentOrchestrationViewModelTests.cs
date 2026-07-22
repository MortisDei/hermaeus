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
/// VM-level coverage for r15 02-orchestration-ui.md: CurrentTask stays
/// pointed at the opened parent while children run, step/status text is
/// labeled with the active child's sub-task position, and a child's pending
/// approval surfaces in the shared review queue with its parent's goal.
/// </summary>
public sealed class AgentOrchestrationViewModelTests
{
    private const string PlanTwoSubtasksResponse = """
        {
          "thought_summary": "Splitting into sub-tasks.",
          "current_step": "Propose sub-tasks.",
          "next_action": {
            "type": "tool",
            "tool_name": "plan_subtasks",
            "arguments": { "subtasks": [
              { "goal": "Fix the bug", "profile": "correctness", "success_criteria": "the bug is fixed" },
              { "goal": "Add a regression test", "profile": "tests", "success_criteria": "a test covers the bug" }
            ] },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Proposing a sub-task plan."
        }
        """;

    private static string FinalResponse(string message) => $$"""
        {
          "thought_summary": "Done.",
          "current_step": "Done.",
          "next_action": { "type": "final", "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "{{message}}"
        }
        """;

    private const string EditRequestResponse = """
        {
          "thought_summary": "Editing notes.md",
          "current_step": "Apply the edit",
          "next_action": {
            "type": "tool",
            "tool_name": "edit_file",
            "arguments": { "relative_path": "notes.md", "old_string": "status: draft", "new_string": "status: final" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Requesting edit."
        }
        """;

    private static async Task<(AgentViewModel Vm, string Workspace, FileAgentTaskStateStore Store)> BuildAsync(TempDir temp, ILlmService llm)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var memoryStore = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memoryStore.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var tools = new AgentWorkspaceTools();
        var agentService = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
        var logs = new RuntimeLogService(settings);
        var profiles = new FileWorkspaceProfileStore(settings);
        var analysis = new WorkspaceAnalysisService(profiles, memoryStore);
        var manifests = new WorkspaceManifestService();
        var activation = new WorkspaceActivationService(manifests, profiles);
        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());

        var vm = new AgentViewModel(agentService, store, memoryStore, tools, new FakeLlm(), rag, logs, analysis, activation, manifests, settings)
        {
            WorkspaceRoot = workspace,
            SelectedModel = new LlmModel { Id = "fake-sequenced-agent", Name = "Fake", Provider = "Test" },
            GoalText = "Fix the bug and add coverage"
        };

        return (vm, workspace, store);
    }

    [Fact]
    public async Task CurrentTask_stays_on_the_opened_parent_and_synthesis_produces_a_report()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlm([
            PlanTwoSubtasksResponse,
            FinalResponse("Fixed the bug."),
            FinalResponse("Added a test."),
            FinalResponse("Both sub-tasks completed successfully.")
        ]);
        var (vm, _, _) = await BuildAsync(temp, llm);

        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(AgentTaskStatus.WaitingForUser, vm.CurrentTask?.Status);
        var parentId = vm.CurrentTask!.TaskId;

        await vm.RefreshReviewQueueCommand.ExecuteAsync(null);
        await vm.ApproveReviewCommand.ExecuteAsync(vm.ReviewQueue.Single(i => i.TaskId == parentId));

        Assert.Equal(parentId, vm.CurrentTask?.TaskId);
        Assert.Equal(AgentTaskStatus.Complete, vm.CurrentTask?.Status);
        Assert.Equal(2, vm.CurrentTask?.SubTaskPlan.Count);
        Assert.True(vm.CurrentTask!.SubTaskPlan.All(s => s.Status == AgentSubTaskStatus.Complete));
        Assert.True(vm.HasReport, "synthesis should have written report.md, and HasReport should reflect it");
        Assert.Contains("sub-task 2/2", vm.CurrentStepCountLabel);
    }

    [Fact]
    public async Task Child_approval_shows_the_parent_goal_and_resumes_the_orchestrated_run()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(temp.PathFor("workspace"));
        File.WriteAllText(Path.Combine(temp.PathFor("workspace"), "notes.md"), "status: draft");

        var llm = new FakeSequencedAgentLlm([
            PlanTwoSubtasksResponse,
            EditRequestResponse,
            FinalResponse("Edit applied."),
            FinalResponse("Second sub-task done."),
            FinalResponse("Synthesis report.")
        ]);
        var (vm, workspace, _) = await BuildAsync(temp, llm);

        await vm.StartCommand.ExecuteAsync(null);
        var parentId = vm.CurrentTask!.TaskId;
        await vm.RefreshReviewQueueCommand.ExecuteAsync(null);
        await vm.ApproveReviewCommand.ExecuteAsync(vm.ReviewQueue.Single(i => i.TaskId == parentId));

        // The run should now be paused on the CHILD's edit_file approval, while
        // CurrentTask (the parent) is still open and unaffected.
        Assert.Equal(parentId, vm.CurrentTask?.TaskId);
        await vm.RefreshReviewQueueCommand.ExecuteAsync(null);
        var childEntry = vm.ReviewQueue.Single(i => i.TaskId != parentId);
        Assert.True(childEntry.IsSubTask, "a sub-task's review queue entry should be marked as such");
        Assert.Contains("Fix the bug and add coverage", childEntry.ParentGoalLabel);
        Assert.Equal("edit_file", childEntry.PendingToolName);

        await vm.ApproveReviewCommand.ExecuteAsync(childEntry);

        Assert.Equal(parentId, vm.CurrentTask?.TaskId);
        Assert.Equal(AgentTaskStatus.Complete, vm.CurrentTask?.Status);
        Assert.Equal("status: final", File.ReadAllText(Path.Combine(workspace, "notes.md")));
    }
}
