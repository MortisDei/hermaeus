using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Rag;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r12 03-runtime-vm-correctness.md 3.4 (stale SelectedModel/SelectedDataset
/// after a refresh) and 3.5 (the agent's default workspace used to be the
/// whole user profile, analyzed at every startup).
/// </summary>
public sealed class AgentViewModelWorkspaceTests
{
    private static async Task<(AgentViewModel vm, ScriptedModelsLlm llm, FileAgentTaskStateStore store)> NewViewModelAsync(
        TempDir temp,
        ScriptedModelsLlm llm,
        AgentScenarioSuiteViewModel? scenarioSuite = null)
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

        var vm = new AgentViewModel(
            agentService,
            store,
            memoryStore,
            tools,
            llm,
            rag,
            logs,
            analysis,
            activation,
            manifests,
            settings,
            scenarioSuite: scenarioSuite);
        return (vm, llm, store);
    }

    private static LlmModel Model(string id) => new() { Id = id, Name = id, Provider = "Test" };

    private sealed class SingleScenarioStore : IAgentScenarioStore
    {
        public Task<IReadOnlyList<AgentScenario>> LoadAllAsync(ICollection<string>? warnings = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentScenario>>(
            [new AgentScenario(new AgentScenarioManifest { Id = "s1", Title = "One", Goal = "goal" }, "unused", "unused", true)]);
    }

    private sealed class NoOpScenarioRunner : IAgentScenarioRunner
    {
        public Task<AgentScenarioRunResult> RunScenarioAsync(AgentScenario scenario, string modelId, IProgress<string>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(new AgentScenarioRunResult(scenario.Manifest.Id, scenario.Manifest.Title, true, [], 1, 1, "Complete", null));

        public Task<AgentScenarioSuiteResult> RunSuiteAsync(IReadOnlyList<AgentScenario> scenarios, string modelId, IProgress<string>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(new AgentScenarioSuiteResult
            {
                ModelId = modelId,
                Results = scenarios.Select(s => new AgentScenarioRunResult(s.Manifest.Id, s.Manifest.Title, true, [], 1, 1, "Complete", null)).ToList()
            });
    }

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

    [Fact]
    public async Task Agent_load_propagates_its_selected_model_to_the_real_scenario_suite()
    {
        using var temp = new TempDir();
        var suite = new AgentScenarioSuiteViewModel(new SingleScenarioStore(), new NoOpScenarioRunner(), new FakeToasts());
        var (vm, _, _) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]), suite);

        await vm.LoadAsync();

        Assert.Equal("a", vm.SelectedModel?.Id);
        Assert.Equal("a", suite.ModelId);
        Assert.Single(suite.Scenarios);
        Assert.True(suite.RunSuiteCommand.CanExecute(null));
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

    // ── r24: a workspace root renamed/deleted out from under the app must never crash it ──

    [Fact]
    public async Task RefreshWorkspaceFiles_against_a_missing_workspace_root_reports_an_error_instead_of_throwing()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));

        var missingRoot = temp.PathFor("was-a-workspace-but-got-renamed");
        Directory.CreateDirectory(missingRoot);
        vm.WorkspaceRoot = missingRoot;
        Directory.Delete(missingRoot);

        // This is the exact call shape AgentView.axaml's refresh button uses
        // (a direct command binding, not a wrapping try/catch elsewhere) - it
        // must complete, not throw, regardless of how RefreshWorkspaceFilesAsync
        // is invoked.
        await vm.RefreshWorkspaceFilesCommand.ExecuteAsync(null);

        Assert.True(vm.IsError);
        Assert.Empty(vm.WorkspaceFiles);
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

    [Fact]
    public async Task Start_command_is_disabled_while_an_existing_task_is_open()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));
        await vm.LoadAsync();
        vm.WorkspaceRoot = temp.PathFor("workspace");
        vm.GoalText = "start another task";
        vm.CurrentTask = new AgentTaskState
        {
            TaskId = "existing",
            Goal = "already open",
            Status = AgentTaskStatus.WaitingForUser
        };

        Assert.False(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Suggested_agents_can_be_queued_without_an_open_task_and_never_write_directly()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        vm.WorkspaceRoot = workspace;
        vm.SuggestedAgentsMd = "# AGENTS.md\n\nKeep changes local and reviewed.";

        DraftPatchPreviewRequest? preview = null;
        vm.RequestDraftPatchPreview = request =>
        {
            preview = request;
            return Task.FromResult(true);
        };

        Assert.True(vm.CanReviewSuggestedAgents);
        await vm.ReviewSuggestedAgentsCommand.ExecuteAsync(null);

        Assert.NotNull(preview);
        Assert.Equal("AGENTS.md", preview!.RelativePath);
        Assert.Equal(vm.SuggestedAgentsMd, preview.NewContent);
        Assert.NotNull(vm.CurrentTask);
        Assert.Equal("Create workspace AGENTS.md", vm.CurrentTask!.Goal);
        var patch = Assert.Single(vm.CurrentTask.DraftPatches);
        Assert.Equal("AGENTS.md", patch.RelativePath);
        Assert.Equal(AgentDraftPatchStatus.Pending, patch.Status);
        Assert.False(File.Exists(Path.Combine(workspace, "AGENTS.md")));
        Assert.Equal(AgentViewModel.ChangesTabIndex, vm.SelectedTabIndex);
    }

    [Fact]
    public async Task Per_patch_approve_command_uses_the_authoritative_prepared_mutation()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "note.txt"), "old");
        var (vm, _, store) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));
        var state = await SavePreparedPatchTaskAsync(store, workspace, "new");

        vm.WorkspaceRoot = workspace;
        vm.RequestDraftPatchPreview = _ => Task.FromResult(true);
        await vm.LoadTaskCommand.ExecuteAsync(state.TaskId);

        await vm.ApprovePatchCommand.ExecuteAsync(new AgentDraftPatchViewModel(state.DraftPatches[0]));

        var saved = await store.LoadAsync(state.TaskId);
        Assert.False(vm.IsError, vm.StatusMessage);
        Assert.Equal("new", File.ReadAllText(Path.Combine(workspace, "note.txt")));
        Assert.Null(saved!.PendingToolAction);
        Assert.Equal(AgentDraftPatchStatus.Applied, Assert.Single(saved.DraftPatches).Status);
        Assert.Contains(saved.MutationReceipts, receipt => receipt.Verified && receipt.Outcome == AgentMutationOutcome.Applied);
    }

    [Fact]
    public async Task Per_patch_reject_command_records_the_same_authoritative_review_transition()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "note.txt"), "old");
        var (vm, _, store) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));
        var state = await SavePreparedPatchTaskAsync(store, workspace, "new");

        vm.WorkspaceRoot = workspace;
        await vm.LoadTaskCommand.ExecuteAsync(state.TaskId);

        await vm.RejectPatchCommand.ExecuteAsync(new AgentDraftPatchViewModel(state.DraftPatches[0]));

        var saved = await store.LoadAsync(state.TaskId);
        Assert.False(vm.IsError, vm.StatusMessage);
        Assert.Equal("old", File.ReadAllText(Path.Combine(workspace, "note.txt")));
        Assert.Null(saved!.PendingToolAction);
        Assert.Equal(AgentDraftPatchStatus.Rejected, Assert.Single(saved.DraftPatches).Status);
        Assert.False(Assert.Single(saved.ApprovalHistory).Approved);
    }

    [Fact]
    public async Task Per_patch_block_command_preserves_the_blocked_owner_action_state()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "note.txt"), "old");
        var (vm, _, store) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));
        var state = await SavePreparedPatchTaskAsync(store, workspace, "new");

        vm.WorkspaceRoot = workspace;
        await vm.LoadTaskCommand.ExecuteAsync(state.TaskId);

        await vm.BlockPatchCommand.ExecuteAsync(new AgentDraftPatchViewModel(state.DraftPatches[0]));

        var saved = await store.LoadAsync(state.TaskId);
        Assert.False(vm.IsError, vm.StatusMessage);
        Assert.Equal("old", File.ReadAllText(Path.Combine(workspace, "note.txt")));
        Assert.Null(saved!.PendingToolAction);
        Assert.Equal(AgentTaskStatus.Blocked, saved.Status);
        Assert.Equal(AgentDraftPatchStatus.Blocked, Assert.Single(saved.DraftPatches).Status);
        Assert.Contains("blocked", saved.LastUserMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<AgentTaskState> SavePreparedPatchTaskAsync(
        FileAgentTaskStateStore store,
        string workspace,
        string proposedContent)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["relative_path"] = "note.txt",
            ["proposed_content"] = proposedContent
        };
        var preparation = await AgentMutationPreparation.PrepareAsync(
            "apply_draft_patch",
            arguments,
            new AgentWorkspaceOptions(workspace),
            policy: null,
            workspaceTools: new AgentWorkspaceTools());
        Assert.True(preparation.IsValid, preparation.Error);
        var pending = preparation.Pending!;
        var patch = new AgentDraftPatch
        {
            RelativePath = pending.RelativePath,
            Rationale = "Update the note.",
            ProposedContent = pending.ProposedContent,
            Status = AgentDraftPatchStatus.Pending,
            CreatedAt = pending.PreparedAt,
            ProposalId = pending.ProposalId,
            ProposalRevision = pending.ProposalRevision,
            WorkspaceRoot = pending.WorkspaceRoot,
            ExpectedPreImageSha256 = pending.ExpectedPreImageSha256,
            ExpectedPreImageExisted = pending.ExpectedPreImageExisted,
            ProposedContentSha256 = pending.ProposedContentSha256,
            PolicyFingerprint = pending.PolicyFingerprint,
            PreparedAt = pending.PreparedAt,
            ApprovalFingerprint = AgentApprovalFingerprint.Resolve(pending)
        };
        var state = new AgentTaskState
        {
            Goal = "Review a prepared patch",
            Status = AgentTaskStatus.WaitingForUser,
            WorkspaceRoot = workspace,
            PendingToolAction = pending,
            LastUserMessage = "Review the prepared note patch.",
            DraftPatches = [patch]
        };
        await store.SaveAsync(state);
        return state;
    }

    [Fact]
    public void Historical_goal_preview_is_bounded_but_full_goal_is_retained()
    {
        var goal = string.Join(' ', Enumerable.Repeat("long-goal-word", 40));
        var item = new AgentTaskListItem("task", goal, AgentTaskStatus.Complete, DateTime.UtcNow);

        var viewModel = new AgentTaskListItemViewModel(item);

        Assert.Equal(goal, viewModel.Goal);
        Assert.Equal(180, viewModel.GoalPreview.Length);
        Assert.EndsWith("...", viewModel.GoalPreview, StringComparison.Ordinal);
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
