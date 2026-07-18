using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Models;
using Aether.Rag;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// r12 03-runtime-vm-correctness.md 3.4 (stale SelectedModel/SelectedDataset
/// after a refresh) and 3.5 (the agent's default workspace used to be the
/// whole user profile, analyzed at every startup).
/// </summary>
public sealed class AgentViewModelWorkspaceTests
{
    private static async Task<(AgentViewModel vm, ScriptedModelsLlm llm, FileAgentTaskStateStore store)> NewViewModelAsync(TempDir temp, ScriptedModelsLlm llm)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var memoryStore = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memoryStore.InitializeAsync();
        var tools = new AgentWorkspaceTools();
        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var agentService = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm());
        var logs = new RuntimeLogService(settings);
        var profiles = new FileWorkspaceProfileStore(settings);
        var analysis = new WorkspaceAnalysisService(profiles, memoryStore);
        var manifests = new WorkspaceManifestService();
        var activation = new WorkspaceActivationService(manifests, profiles);

        var vm = new AgentViewModel(agentService, store, memoryStore, tools, llm, rag, logs, analysis, activation, manifests, settings);
        return (vm, llm, store);
    }

    private static LlmModel Model(string id) => new() { Id = id, Name = id, Provider = "Test" };

    // ── 3.5: the agent no longer treats the user profile as an implicit workspace ──

    [Fact]
    public async Task Fresh_agent_defaults_to_no_workspace_and_never_auto_creates_a_workspace_profile_memory()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));

        Assert.Equal(string.Empty, vm.WorkspaceRoot);
        Assert.False(vm.HasWorkspace);

        await vm.LoadAsync();

        Assert.Empty(vm.WorkspaceMemory);
        Assert.False(vm.IsAnalyzingWorkspace);
    }

    // ── 3.4: LoadAsync must re-match SelectedModel/SelectedDataset by id, not keep a stale reference ──

    [Fact]
    public async Task LoadAsync_twice_with_fresh_model_instances_keeps_a_selection_whose_reference_is_current()
    {
        using var temp = new TempDir();
        var llm = new ScriptedModelsLlm(() => [Model("a"), Model("b")]);
        var (vm, _, _) = await NewViewModelAsync(temp, llm);

        await vm.LoadAsync();
        vm.SelectedModel = vm.AvailableModels.Single(m => m.Id == "b");

        await vm.LoadAsync();

        Assert.Equal("b", vm.SelectedModel?.Id);
        Assert.Same(vm.AvailableModels.Single(m => m.Id == "b"), vm.SelectedModel);
    }

    // ── 2.5: overlapping LoadAsync calls must not duplicate models ──

    [Fact]
    public async Task Concurrent_LoadAsync_calls_share_the_in_flight_load_and_never_duplicate_models()
    {
        using var temp = new TempDir();
        var gate = new TaskCompletionSource();
        var llm = new ScriptedModelsLlm(() => [Model("a")]) { DelayGate = gate };
        var (vm, _, _) = await NewViewModelAsync(temp, llm);

        var first = vm.LoadAsync();
        var second = vm.LoadAsync();
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Single(vm.AvailableModels);
        Assert.Equal(1, llm.GetModelsCallCount);
    }

    // ── r16 03-workbench-and-desktop.md 3.4: null-safe Sub-tasks chrome ──

    [Fact]
    public async Task HasSubTaskPlan_is_false_with_no_task_and_true_only_for_an_orchestration_parent()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));

        Assert.False(vm.HasSubTaskPlan, "a fresh workbench with no task loaded must not show sub-task chrome");

        vm.CurrentTask = new AgentTaskState { Goal = "Plain task", Status = AgentTaskStatus.Running };
        Assert.False(vm.HasSubTaskPlan, "a plain task with no sub-task plan should not show the chrome either");

        vm.CurrentTask = new AgentTaskState
        {
            Goal = "Broad task",
            Status = AgentTaskStatus.Running,
            SubTaskPlan = [new AgentSubTaskSpec { Goal = "child", ProfileName = "general" }]
        };
        Assert.True(vm.HasSubTaskPlan, "an orchestration parent with a materialized plan should show the chrome");
    }

    // ── r16 03-workbench-and-desktop.md 3.1: recent-tasks list / LoadTaskCommand ──

    [Fact]
    public async Task LoadTaskCommand_loads_by_bare_task_id_and_shows_parent_goal_for_a_child()
    {
        using var temp = new TempDir();
        var (vm, _, store) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));

        var parent = new AgentTaskState { Goal = "Parent goal", Status = AgentTaskStatus.Running };
        await store.SaveAsync(parent);
        var child = new AgentTaskState { Goal = "Child goal", Status = AgentTaskStatus.WaitingForUser, ParentTaskId = parent.TaskId };
        await store.SaveAsync(child);

        await vm.LoadTaskCommand.ExecuteAsync(child.TaskId);

        Assert.Equal(child.TaskId, vm.CurrentTask?.TaskId);
        Assert.True(vm.HasCurrentTaskParentGoal, "opening a child directly should surface its parent's goal");
        Assert.Equal("for: Parent goal", vm.CurrentTaskParentGoalLabel);

        await vm.LoadTaskCommand.ExecuteAsync(parent.TaskId);
        Assert.False(vm.HasCurrentTaskParentGoal, "opening a plain/parent task should clear the stale parent-goal label from the previous selection");
    }

    [Fact]
    public async Task LoadTaskCommand_is_a_no_op_for_a_null_or_blank_task_id()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));

        await vm.LoadTaskCommand.ExecuteAsync(null);
        await vm.LoadTaskCommand.ExecuteAsync("");

        Assert.Null(vm.CurrentTask);
    }
}
