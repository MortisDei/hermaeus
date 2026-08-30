using System.Runtime.CompilerServices;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class AgentSubtaskModelSelectionTests
{
    private sealed record Rig(
        AgentService Agent,
        FileAgentTaskStateStore Store,
        MultiModelAgentLlm Llm,
        AgentWorkspaceOptions Options,
        string Workspace);

    private static async Task<Rig> NewRigAsync(TempDir temp, string plan = DefaultPlan)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings); await store.InitializeAsync();
        var workspace = temp.PathFor("workspace"); Directory.CreateDirectory(workspace);
        var llm = new MultiModelAgentLlm(plan);
        var tools = new AgentWorkspaceTools();
        var agent = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(),
            new AgentToolExecutor(tools), llm, settings: settings, workspaceTools: tools);
        return new Rig(agent, store, llm, new AgentWorkspaceOptions(workspace, ModelId: "parent"), workspace);
    }

    private static async Task<AgentTaskState> ProposeAsync(Rig rig, string projectId = "")
    {
        var task = await rig.Agent.CreateTaskAsync("Coordinate work", rig.Options, projectId: projectId);
        return (await rig.Agent.RunStepAsync(task.TaskId, rig.Options)).State;
    }

    private static async Task<AgentTaskState> ApproveAsync(Rig rig, AgentTaskState proposed)
    {
        var pending = Assert.IsType<AgentPendingToolAction>(proposed.PendingToolAction);
        var result = await rig.Agent.AppendApprovalAsync(proposed.TaskId, "test", true,
            AgentApprovalFingerprint.Resolve(pending), rig.Options);
        Assert.True(result.Applied);
        return (await rig.Store.LoadAsync(proposed.TaskId))!;
    }

    private const string DefaultPlan = """
        [
          {"goal":"Check correctness","profile":"correctness","success_criteria":"Report findings","model_id":"child-a"},
          {"goal":"Write tests","profile":"tests","success_criteria":"Report coverage"}
        ]
        """;

    [Fact]
    public async Task First_planner_step_freezes_root_model_and_display_name()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var state = await ProposeAsync(rig);
        Assert.Equal("parent", state.ModelId); Assert.Contains("Parent", state.ModelDisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Planner_context_lists_only_visible_eligible_models_and_task_identity()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        await ProposeAsync(rig);
        var prompt = Assert.Single(rig.Llm.Calls).Prompt;
        Assert.Contains("child-a", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", prompt, StringComparison.Ordinal);
        Assert.Contains("task-model", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approved_plan_persists_explicit_and_inherited_resolved_models()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var state = await ApproveAsync(rig, await ProposeAsync(rig));
        Assert.Equal("child-a", state.SubTaskPlan[0].ModelId);
        Assert.Equal("child-a", state.SubTaskPlan[0].ResolvedModelId);
        Assert.Equal(string.Empty, state.SubTaskPlan[1].ModelId);
        Assert.Equal("parent", state.SubTaskPlan[1].ResolvedModelId);
        Assert.Contains("inherit", state.SubTaskPlan[1].ModelLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_explicit_model_is_rejected_before_children_materialize()
    {
        const string plan = """[{"goal":"A","profile":"general","success_criteria":"A","model_id":"missing"},{"goal":"B","profile":"tests","success_criteria":"B"}]""";
        using var temp = new TempDir(); var rig = await NewRigAsync(temp, plan);
        var state = await ApproveAsync(rig, await ProposeAsync(rig));
        Assert.Empty(state.SubTaskPlan);
        Assert.Contains("unavailable model", state.ToolResults.Last().ResultSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hidden_explicit_model_is_rejected_before_children_materialize()
    {
        const string plan = """[{"goal":"A","profile":"general","success_criteria":"A","model_id":"hidden"},{"goal":"B","profile":"tests","success_criteria":"B"}]""";
        using var temp = new TempDir(); var rig = await NewRigAsync(temp, plan);
        var state = await ApproveAsync(rig, await ProposeAsync(rig));
        Assert.Empty(state.SubTaskPlan);
        Assert.Contains("hidden", state.ToolResults.Last().ResultSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mixed_model_children_run_on_their_resolved_models_and_parent_synthesizes_on_parent()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var approved = await ApproveAsync(rig, await ProposeAsync(rig));
        var finished = await rig.Agent.RunAsync(approved.TaskId, rig.Options);
        Assert.Equal(AgentTaskStatus.Complete, finished.State.Status);
        Assert.Equal(["parent", "child-a", "parent", "parent"], rig.Llm.Calls.Select(call => call.ModelId));
    }

    [Fact]
    public async Task Child_state_persists_resolved_model_and_parent_project()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var approved = await ApproveAsync(rig, await ProposeAsync(rig, "project-7"));
        await rig.Agent.RunAsync(approved.TaskId, rig.Options);
        var parent = (await rig.Store.LoadAsync(approved.TaskId))!;
        var first = (await rig.Store.LoadAsync(parent.SubTaskPlan[0].TaskId!))!;
        Assert.Equal("child-a", first.ModelId); Assert.Equal("project-7", first.ProjectId); Assert.Equal(parent.TaskId, first.ParentTaskId);
    }

    [Fact]
    public async Task Transcript_records_the_model_that_produced_each_response()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var state = await ProposeAsync(rig);
        var transcript = await rig.Store.LoadTranscriptAsync(state.TaskId);
        Assert.Contains(transcript, entry => entry.Role == "assistant" && entry.ModelId == "parent");
    }

    [Fact]
    public async Task Recent_task_index_round_trips_model_identity()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        await ProposeAsync(rig);
        var recent = Assert.Single(await rig.Store.ListRecentAsync());
        Assert.Equal("parent", recent.ModelId); Assert.Contains("Parent", recent.ModelDisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_task_index_v5_migrates_model_identity_columns_additively()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var agentDirectory = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "agent");
        Directory.CreateDirectory(agentDirectory);
        var indexPath = Path.Combine(agentDirectory, "task_index.db");

        await using (var connection = new SqliteConnection($"Data Source={indexPath}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE agent_task_index (
                    task_id TEXT PRIMARY KEY, goal TEXT NOT NULL, status TEXT NOT NULL,
                    updated_at TEXT NOT NULL, active_step TEXT NOT NULL, summary TEXT NOT NULL,
                    approval_count INTEGER NOT NULL DEFAULT 0, last_approval_action TEXT,
                    last_approval_approved INTEGER, last_approval_at TEXT, parent_task_id TEXT,
                    pending_step_count INTEGER NOT NULL DEFAULT 0,
                    has_reservations INTEGER NOT NULL DEFAULT 0,
                    project_id TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE hermaeus_schema_versions (
                    scope TEXT PRIMARY KEY, version INTEGER NOT NULL,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO hermaeus_schema_versions (scope, version, updated_at)
                VALUES ('agent_task_index', 5, '2026-08-25T00:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();

        await using var migrated = new SqliteConnection($"Data Source={indexPath}");
        await migrated.OpenAsync();
        var columns = migrated.CreateCommand();
        columns.CommandText = "SELECT name FROM pragma_table_info('agent_task_index') WHERE name LIKE 'model_%' ORDER BY name";
        var names = new List<string>();
        await using var reader = await columns.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        Assert.Equal(["model_display_name", "model_id"], names);
    }

    [Fact]
    public async Task Frozen_model_ignores_a_different_caller_picker_on_resume()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var proposed = await ProposeAsync(rig);
        await rig.Agent.AppendApprovalAsync(proposed.TaskId, "reject", false,
            AgentApprovalFingerprint.Resolve(proposed.PendingToolAction), rig.Options);
        var resumed = await rig.Store.LoadAsync(proposed.TaskId); resumed!.Status = AgentTaskStatus.New; resumed.PendingToolAction = null; await rig.Store.SaveAsync(resumed);
        await rig.Agent.RunStepAsync(resumed.TaskId, rig.Options with { ModelId = "child-b" });
        Assert.Equal("parent", rig.Llm.Calls.Last().ModelId);
    }

    [Fact]
    public async Task Missing_frozen_model_blocks_without_calling_or_falling_back()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var task = await rig.Agent.CreateTaskAsync("Resume", rig.Options);
        task.ModelId = "removed"; task.ModelDisplayName = "Removed"; await rig.Store.SaveAsync(task);
        var calls = rig.Llm.Calls.Count;
        var result = await rig.Agent.RunStepAsync(task.TaskId, rig.Options with { ModelId = "child-b" });
        Assert.Equal(AgentTaskStatus.Blocked, result.State.Status); Assert.Equal(calls, rig.Llm.Calls.Count);
        Assert.Contains("did not fall back", result.PlannerResponse.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explicit_user_model_change_updates_a_paused_task_and_is_audited()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var task = await rig.Agent.CreateTaskAsync("Resume", rig.Options); task.ModelId = "removed"; task.Status = AgentTaskStatus.Blocked; task.Decisions.Add(new("Selected model unavailable", "gone", DateTime.UtcNow)); await rig.Store.SaveAsync(task);
        var changed = await rig.Agent.ChangeTaskModelAsync(task.TaskId, "child-b");
        Assert.Equal("child-b", changed.ModelId); Assert.Equal(AgentTaskStatus.WaitingForUser, changed.Status);
        Assert.Contains((await rig.Store.LoadTranscriptAsync(task.TaskId)), entry => entry.ModelId == "child-b" && entry.Content.Contains("changed task model", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Explicit_user_model_change_rejects_an_unavailable_target()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var task = await rig.Agent.CreateTaskAsync("Resume", rig.Options); task.Status = AgentTaskStatus.Blocked; await rig.Store.SaveAsync(task);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Agent.ChangeTaskModelAsync(task.TaskId, "missing"));
        Assert.Equal(string.Empty, (await rig.Store.LoadAsync(task.TaskId))!.ModelId);
    }

    [Fact]
    public async Task Legacy_task_without_model_uses_caller_model_then_freezes_it()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var legacy = new AgentTaskState { TaskId = "legacy", Goal = "Legacy", WorkspaceRoot = rig.Workspace };
        await rig.Store.SaveAsync(legacy);
        var result = await rig.Agent.RunStepAsync(legacy.TaskId, rig.Options);
        Assert.Equal("parent", result.State.ModelId); Assert.Equal("parent", rig.Llm.Calls.Single().ModelId);
    }

    [Fact]
    public void Plan_review_exposes_inherit_visible_and_unavailable_model_choices()
    {
        var pending = new AgentPendingToolAction
        {
            ToolName = "plan_subtasks",
            Arguments = { ["subtasks"] = JsonSerializer.SerializeToElement(JsonDocument.Parse(DefaultPlan).RootElement) }
        };
        var row = new AgentReviewQueueItem("t", "g", AgentTaskStatus.WaitingForUser, DateTime.UtcNow, "", "", 0, null, null, null, pending);
        var vm = new AgentReviewQueueItemViewModel(row, availableModels:
            [new LlmModel { Id = "child-a", Name = "A" }], fullState: new AgentTaskState { ModelId = "parent", ModelDisplayName = "Parent" });
        Assert.Equal(2, vm.SubTaskModelChoices.Count);
        Assert.Contains(vm.SubTaskModelChoices[1].Options, option => option.ModelId == string.Empty && option.Label.Contains("Parent", StringComparison.Ordinal));
        Assert.Equal("child-a", vm.SubTaskModelChoices[0].SelectedOption!.ModelId);
    }

    [Fact]
    public async Task Synthesis_report_retains_parent_and_child_model_identity()
    {
        using var temp = new TempDir(); var rig = await NewRigAsync(temp);
        var approved = await ApproveAsync(rig, await ProposeAsync(rig));
        await rig.Agent.RunAsync(approved.TaskId, rig.Options);
        var report = await File.ReadAllTextAsync(Path.Combine(rig.Store.GetTaskDirectory(approved.TaskId), "report.md"));
        Assert.Contains("child-a", report, StringComparison.Ordinal); Assert.Contains("parent", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_choice_does_not_change_safety_gate_classification()
    {
        var first = new AgentSafetyGate().Evaluate("edit_file", wouldMutate: true);
        var second = new AgentSafetyGate().Evaluate("edit_file", wouldMutate: true);
        Assert.Equal(first.Disposition, second.Disposition); Assert.Equal(first.RiskLevel, second.RiskLevel);
    }

    private sealed class MultiModelAgentLlm(string plan) : ILlmService
    {
        private int _parentCalls;
        public List<(string ModelId, string Prompt)> Calls { get; } = [];
        public List<LlmModel> Models { get; } =
        [
            new() { Id = "parent", Name = "Parent", Provider = "Test" },
            new() { Id = "child-a", Name = "Child A", Provider = "Test" },
            new() { Id = "child-b", Name = "Child B", Provider = "Test" },
            new() { Id = "hidden", Name = "Hidden", Provider = "Test", IsVisible = false }
        ];
        public string ProviderName => "Multi";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) => Task.FromResult(Models.ToList());

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls.Add((modelId, messages.Single().Content));
            await Task.Yield();
            if (modelId == "parent" && _parentCalls++ == 0)
                yield return new LlmStreamEvent(PlanResponse(plan));
            else
                yield return new LlmStreamEvent(FinalResponse(modelId));
        }

        private static string PlanResponse(string subtasks) => $$"""
            {
              "thought_summary":"Split work.","current_step":"Review child models.",
              "next_action":{"type":"tool","tool_name":"plan_subtasks","arguments":{"subtasks":{{subtasks}}},"requires_approval":true,"risk_level":"high"},
              "state_update":{"completed":[],"pending":[],"new_facts":[],"blockers":[]},"user_message":"Review plan."
            }
            """;

        private static string FinalResponse(string modelId) => $$"""
            {"thought_summary":"Done on {{modelId}}.","current_step":"Done.","next_action":{"type":"final","tool_name":null,"arguments":{},"requires_approval":false,"risk_level":"none"},"state_update":{"completed":[],"pending":[],"new_facts":[],"blockers":[]},"user_message":"Finished on {{modelId}}."}
            """;
    }
}
