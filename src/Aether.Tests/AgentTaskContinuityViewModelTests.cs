using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// r19 3.2 (New task affordance) and 3.3 (premature-complete honesty note),
/// VM-level. Mirrors AgentOrchestrationViewModelTests' wiring.
/// </summary>
public sealed class AgentTaskContinuityViewModelTests
{
    private static string FinalResponse(string message) => $$"""
        {
          "thought_summary": "Done.",
          "current_step": "Done.",
          "next_action": { "type": "final", "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "{{message}}"
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
            GoalText = "Investigate the bug"
        };

        return (vm, workspace, store);
    }

    [Fact]
    public async Task NewTask_clears_the_composer_and_leaves_the_persisted_task_untouched()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlm([FinalResponse("Finished.")]);
        var (vm, _, store) = await BuildAsync(temp, llm);

        await vm.StartCommand.ExecuteAsync(null);
        var taskId = vm.CurrentTask!.TaskId;
        Assert.Equal(AgentTaskStatus.Complete, vm.CurrentTask!.Status);
        Assert.True(vm.CanShowNewTaskButton);

        vm.NewTaskCommand.Execute(null);

        Assert.Null(vm.CurrentTask);
        Assert.Equal(string.Empty, vm.GoalText);
        Assert.Equal(string.Empty, vm.ReplyText);
        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.False(vm.IsError);

        var stillPersisted = await store.LoadAsync(taskId);
        Assert.NotNull(stillPersisted);
        Assert.Equal(AgentTaskStatus.Complete, stillPersisted!.Status);
    }

    [Fact]
    public async Task Starting_a_fresh_goal_after_NewTask_creates_a_task_with_a_new_id()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlm([FinalResponse("First done."), FinalResponse("Second done.")]);
        var (vm, _, _) = await BuildAsync(temp, llm);

        await vm.StartCommand.ExecuteAsync(null);
        var firstId = vm.CurrentTask!.TaskId;

        vm.NewTaskCommand.Execute(null);
        vm.GoalText = "A brand new goal";
        await vm.StartCommand.ExecuteAsync(null);

        Assert.NotEqual(firstId, vm.CurrentTask!.TaskId);
        Assert.Equal("A brand new goal", vm.CurrentTask!.Goal);
    }

    [Fact]
    public async Task A_terminal_task_with_pending_steps_shows_the_premature_complete_note()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlm([]);
        var (vm, _, store) = await BuildAsync(temp, llm);

        var state = new AgentTaskState
        {
            TaskId = "half-done",
            Goal = "Do a big thing",
            Status = AgentTaskStatus.Complete,
            PendingSteps = ["step a", "step b", "step c"]
        };
        await store.SaveAsync(state);

        await vm.LoadTaskCommand.ExecuteAsync(state.TaskId);

        Assert.True(vm.HasPrematureCompleteNote);
        Assert.Equal("Finished with 3 planned steps not run.", vm.PrematureCompleteNote);
    }

    [Fact]
    public async Task A_terminal_task_with_no_pending_steps_shows_no_note()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlm([]);
        var (vm, _, store) = await BuildAsync(temp, llm);

        var state = new AgentTaskState
        {
            TaskId = "fully-done",
            Goal = "Do a small thing",
            Status = AgentTaskStatus.Complete,
            PendingSteps = []
        };
        await store.SaveAsync(state);

        await vm.LoadTaskCommand.ExecuteAsync(state.TaskId);

        Assert.False(vm.HasPrematureCompleteNote);
        Assert.Equal(string.Empty, vm.PrematureCompleteNote);
    }
}
