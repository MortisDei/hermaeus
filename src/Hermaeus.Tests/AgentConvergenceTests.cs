using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using static Hermaeus.Tests.Helpers;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AgentConvergenceTests
{
    [Fact]
    public void Read_only_authority_ignores_a_model_approval_flag()
    {
        var decision = new AgentSafetyGate().Evaluate("read_file", wouldMutate: true);

        Assert.Equal(AgentToolDisposition.Allowed, decision.Disposition);
        Assert.Equal(AgentRiskLevel.Low, decision.RiskLevel);
    }

    [Fact]
    public void Equivalent_blocked_actions_stop_at_the_bounded_threshold()
    {
        var state = new AgentTaskState();
        var decision = new AgentToolPolicyDecision(
            AgentToolDisposition.Blocked, AgentRiskLevel.High, "This workspace has not declared 'dotnet build'.");

        var observations = Enumerable.Range(1, AgentConvergencePolicy.MaxEquivalentNonProgressSteps)
            .Select(_ => AgentConvergencePolicy.ObserveTool(state, "run_command",
                new Dictionary<string, object?> { ["command"] = "dotnet build" }, decision, null))
            .ToList();

        Assert.Equal(AgentConvergencePolicy.MaxEquivalentNonProgressSteps, state.ConsecutiveNonProgressCount);
        Assert.False(observations[0].ShouldStop);
        Assert.True(observations[^1].ShouldStop);
    }

    [Fact]
    public void Mixed_no_effect_and_blocked_actions_share_the_task_level_bound()
    {
        var state = new AgentTaskState();
        var allowed = new AgentToolPolicyDecision(AgentToolDisposition.Allowed, AgentRiskLevel.Low, "plan");
        var blocked = new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "model unavailable");
        var noEffect = new AgentToolResult
        {
            Tool = "set_plan",
            ResultSummary = "The submitted plan matched the persisted plan.",
            NormalizedOutcome = new NormalizedToolOutcome { Outcome = NormalizedOutcome.NoEffect }
        };

        var arguments = new Dictionary<string, object?>();
        var first = AgentConvergencePolicy.ObserveTool(state, "set_plan", arguments, allowed, noEffect);
        var second = AgentConvergencePolicy.ObserveTool(state, "plan_subtasks", arguments, blocked, null);
        var third = AgentConvergencePolicy.ObserveTool(state, "plan_subtasks", arguments, blocked, null);

        Assert.Equal(1, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(AgentConvergencePolicy.MaxEquivalentNonProgressSteps, third.Count);
        Assert.True(third.ShouldStop);
    }

    [Fact]
    public void A_changed_read_result_starts_a_new_non_progress_sequence()
    {
        var state = new AgentTaskState();
        var decision = new AgentToolPolicyDecision(AgentToolDisposition.Allowed, AgentRiskLevel.Low, "read");
        var args = new Dictionary<string, object?> { ["relative_path"] = "notes.md" };

        var first = AgentConvergencePolicy.ObserveTool(state, "read_file", args, decision, Result("before"));
        var repeated = AgentConvergencePolicy.ObserveTool(state, "read_file", args, decision, Result("before"));
        var changed = AgentConvergencePolicy.ObserveTool(state, "read_file", args, decision, Result("after"));

        Assert.Equal(1, first.Count);
        Assert.Equal(2, repeated.Count);
        Assert.Equal(1, changed.Count);
        Assert.False(changed.ShouldStop);
    }

    [Fact]
    public void An_exact_repeated_mutation_request_is_blocked_after_a_verified_write()
    {
        var arguments = new Dictionary<string, object?>
        {
            ["relative_path"] = "Calculator.java",
            ["old_string"] = "public class Calculator {",
            ["new_string"] = "public class Calculator {\n    public static void main(String[] args) {}\n}"
        };
        var requestFingerprint = AgentApprovalFingerprint.Compute("edit_file", arguments);
        var state = new AgentTaskState
        {
            MutationReceipts =
            [
                new AgentMutationReceipt
                {
                    ToolName = "edit_file",
                    RequestedActionFingerprint = requestFingerprint,
                    MutationKind = AgentMutationKind.Edit,
                    WorkspaceRoot = "C:\\workspace",
                    RelativePath = "Calculator.java",
                    Outcome = AgentMutationOutcome.Applied,
                    Changed = true,
                    Verified = true
                }
            ]
        };
        var pending = new AgentPendingToolAction
        {
            ToolName = "edit_file",
            Arguments = arguments,
            RequestedActionFingerprint = requestFingerprint,
            MutationKind = AgentMutationKind.Edit,
            WorkspaceRoot = "C:\\workspace",
            RelativePath = "Calculator.java"
        };

        var reason = AgentConvergencePolicy.FindEquivalentMutation(state, pending);

        Assert.NotNull(reason);
        Assert.Contains("identical", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_equivalent_verified_post_image_is_blocked_without_language_specific_rules()
    {
        const string contentHash = "post-image-hash";
        var state = new AgentTaskState
        {
            MutationReceipts =
            [
                new AgentMutationReceipt
                {
                    ToolName = "replace_file",
                    MutationKind = AgentMutationKind.Replace,
                    WorkspaceRoot = "C:\\workspace",
                    RelativePath = "notes.txt",
                    Outcome = AgentMutationOutcome.Applied,
                    Changed = true,
                    Verified = true,
                    ObservedPostImageExisted = true,
                    ObservedPostImageSha256 = contentHash
                }
            ]
        };
        var pending = new AgentPendingToolAction
        {
            ToolName = "replace_file",
            MutationKind = AgentMutationKind.Replace,
            WorkspaceRoot = "C:\\workspace",
            RelativePath = "notes.txt",
            ProposedContentSha256 = contentHash
        };

        Assert.Contains("already present", AgentConvergencePolicy.FindEquivalentMutation(state, pending), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_legitimate_iterative_same_file_edit_is_not_treated_as_a_duplicate()
    {
        var state = new AgentTaskState
        {
            MutationReceipts =
            [
                new AgentMutationReceipt
                {
                    ToolName = "edit_file",
                    RequestedActionFingerprint = "first-request",
                    MutationKind = AgentMutationKind.Edit,
                    WorkspaceRoot = "C:\\workspace",
                    RelativePath = "notes.txt",
                    Outcome = AgentMutationOutcome.Applied,
                    Changed = true,
                    Verified = true,
                    ObservedPostImageSha256 = "first-post-image"
                }
            ]
        };
        var pending = new AgentPendingToolAction
        {
            ToolName = "edit_file",
            RequestedActionFingerprint = "second-request",
            MutationKind = AgentMutationKind.Edit,
            WorkspaceRoot = "C:\\workspace",
            RelativePath = "notes.txt",
            ProposedContentSha256 = "second-post-image"
        };

        Assert.Null(AgentConvergencePolicy.FindEquivalentMutation(state, pending));
    }

    [Fact]
    public void A_verified_no_change_receipt_is_non_progress_but_a_verified_change_resets_it()
    {
        var state = new AgentTaskState();
        var noChange = new AgentMutationReceipt
        {
            ToolName = "edit_file",
            RequestedActionFingerprint = "same-request",
            RelativePath = "notes.txt",
            Outcome = AgentMutationOutcome.AlreadySatisfied,
            Verified = true,
            Changed = false
        };
        var changed = new AgentMutationReceipt
        {
            ToolName = "edit_file",
            RequestedActionFingerprint = "new-request",
            RelativePath = "notes.txt",
            Outcome = AgentMutationOutcome.Applied,
            Verified = true,
            Changed = true
        };

        var first = AgentConvergencePolicy.ObserveMutationReceipt(state, noChange);
        var reset = AgentConvergencePolicy.ObserveMutationReceipt(state, changed);

        Assert.Equal(1, first.Count);
        Assert.Equal(0, reset.Count);
        Assert.False(reset.ShouldStop);
    }

    [Fact]
    public void An_answered_question_is_still_bounded_if_the_model_repeats_it()
    {
        var state = new AgentTaskState { LastAnsweredQuestion = "Which file should I inspect?" };

        var first = AgentConvergencePolicy.ObserveAskUser(state, "Which file should I inspect?");
        var second = AgentConvergencePolicy.ObserveAskUser(state, "Which file should I inspect?");
        var third = AgentConvergencePolicy.ObserveAskUser(state, "Which file should I inspect?");

        Assert.Equal(1, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(AgentConvergencePolicy.MaxEquivalentNonProgressSteps, third.Count);
        Assert.True(third.ShouldStop);
        Assert.Contains("after it was answered", third.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_command_recipes_do_not_block_prepared_file_creation()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var preparation = await AgentMutationPreparation.PrepareAsync(
            "create_file",
            new Dictionary<string, object?>
            {
                ["relative_path"] = "new.md",
                ["content"] = "created"
            },
            new AgentWorkspaceOptions(workspace),
            policy: null,
            new AgentWorkspaceTools());

        Assert.True(preparation.IsValid, preparation.Error);
        Assert.Equal(AgentMutationKind.Create, preparation.Pending!.MutationKind);
    }

    [Fact]
    public async Task Trivial_writable_goal_reaches_prepared_parent_mutation_without_delegation()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var tools = new AgentWorkspaceTools();
        var agent = new AgentService(
            store,
            new FakeAgentContextBuilder(),
            new AgentSafetyGate(),
            new AgentToolExecutor(tools),
            new FakeSequencedAgentLlm([CreateFileResponse]),
            settings: settings,
            workspaceTools: tools);
        var options = new AgentWorkspaceOptions(workspace, ModelId: "fake-sequenced-agent");
        var task = await agent.CreateTaskAsync("Make a simple calculator in python.", options);

        var result = await agent.RunStepAsync(task.TaskId, options);

        Assert.Equal(AgentTaskStatus.WaitingForUser, result.State.Status);
        Assert.Equal("create_file", result.State.PendingToolAction?.ToolName);
        Assert.Empty(result.State.SubTaskPlan);
    }

    [Fact]
    public async Task Identical_empty_reads_stop_the_autonomous_loop_truthfully()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var responses = Enumerable.Repeat(ListFilesResponse, AgentConvergencePolicy.MaxEquivalentNonProgressSteps);
        var tools = new AgentWorkspaceTools();
        var agent = new AgentService(
            store,
            new FakeAgentContextBuilder(),
            new AgentSafetyGate(),
            new AgentToolExecutor(tools),
            new FakeSequencedAgentLlm(responses),
            settings: settings,
            workspaceTools: tools);
        var options = new AgentWorkspaceOptions(workspace, ModelId: "fake-sequenced-agent");
        var task = await agent.CreateTaskAsync("inspect the workspace", options);

        var result = await agent.RunAsync(task.TaskId, options);

        Assert.Equal(AgentTaskStatus.Blocked, result.State.Status);
        Assert.Equal(AgentConvergencePolicy.MaxEquivalentNonProgressSteps, result.State.StepCount);
        Assert.Contains("non-progressing actions", result.State.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_ask_user_after_answering_stops_without_another_question()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var responses = Enumerable.Repeat(AskUserResponse, AgentConvergencePolicy.MaxEquivalentNonProgressSteps);
        var tools = new AgentWorkspaceTools();
        var agent = new AgentService(
            store,
            new FakeAgentContextBuilder(),
            new AgentSafetyGate(),
            new AgentToolExecutor(tools),
            new FakeSequencedAgentLlm(responses),
            settings: settings,
            workspaceTools: tools);
        var options = new AgentWorkspaceOptions(workspace, ModelId: "fake-sequenced-agent");
        var task = await agent.CreateTaskAsync("ask me once", options);

        var first = await agent.RunAsync(task.TaskId, options);
        await agent.AppendUserReplyAsync(task.TaskId, "the answer");
        var second = await agent.RunAsync(task.TaskId, options);
        await agent.AppendUserReplyAsync(task.TaskId, "the answer again");
        var third = await agent.RunAsync(task.TaskId, options);

        Assert.Equal(AgentTaskStatus.WaitingForUser, first.State.Status);
        Assert.Equal(AgentTaskStatus.WaitingForUser, second.State.Status);
        Assert.Equal(AgentTaskStatus.Blocked, third.State.Status);
        Assert.Contains("after it was answered", third.State.Summary, StringComparison.Ordinal);
    }

    private static AgentToolResult Result(string content) => new()
    {
        Tool = "read_file",
        ResultSummary = content,
        NormalizedOutcome = new NormalizedToolOutcome { Outcome = NormalizedOutcome.Succeeded }
    };

    private const string CreateFileResponse = """
        {
          "thought_summary": "Create the requested calculator.",
          "current_step": "Prepare calculator file.",
          "next_action": { "type": "tool", "tool_name": "create_file", "arguments": { "relative_path": "calculator.py", "content": "print(\"calculator\")" }, "requires_approval": true, "risk_level": "medium" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Review the prepared calculator file."
        }
        """;

    private const string ListFilesResponse = """
        {
          "thought_summary": "Inspecting the workspace.",
          "current_step": "List the workspace.",
          "next_action": { "type": "tool", "tool_name": "list_files", "arguments": {}, "requires_approval": false, "risk_level": "low" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": ""
        }
        """;

    private const string AskUserResponse = """
        {
          "thought_summary": "I need the same answer.",
          "current_step": "Waiting for the file.",
          "next_action": { "type": "ask_user", "tool_name": null, "arguments": {}, "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Which file should I inspect?"
        }
        """;
}
