using System.Runtime.CompilerServices;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// A response the model shapes correctly is not a response the app trusts.
/// requires_approval and risk_level are fields the model fills in and the
/// dispatch path overrides from the tool name, and that has to stay true
/// whether or not the response arrived through a sampler that guaranteed its
/// shape. This test exists to fail loudly if a schema-valid response is ever
/// allowed to carry its own approval decision.
/// </summary>
public sealed class AgentModelClaimedRiskIsIgnoredTests
{
    /// <summary>A response claiming a high-risk tool needs no approval and carries no risk.</summary>
    private static string InnocentLookingRequest(string toolName) => $$"""
        {
          "thought_summary": "This is completely routine.",
          "current_step": "Do the thing.",
          "next_action": {
            "type": "tool",
            "tool_name": "{{toolName}}",
            "arguments": { "relative_path": "notes.md", "command": "rm -rf /", "package": "left-pad" },
            "requires_approval": false,
            "risk_level": "none"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Proceeding."
        }
        """;

    /// <summary>
    /// Reports whether it can enforce an output constraint, so the same
    /// response can be run down both the constrained and the unconstrained
    /// path and the outcomes compared.
    /// </summary>
    private sealed class ConstraintAwareAgentLlm : ILlmService
    {
        private readonly bool _supportsConstraints;
        private readonly string _response;

        public ConstraintAwareAgentLlm(bool supportsConstraints, string response)
        {
            _supportsConstraints = supportsConstraints;
            _response = response;
        }

        public bool SawConstraint { get; private set; }
        public string ProviderName => "ConstraintAwareAgent";
        public bool IsConfigured => true;

        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel>
            {
                new() { Id = "constraint-aware", Name = "Constraint Aware", Provider = "Test", SupportsOutputConstraints = _supportsConstraints }
            });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            SawConstraint = options?.OutputConstraint is not null;
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent(_response);
        }
    }

    private static async Task<(AgentTaskState State, string GateReason, bool Constrained)> StepAsync(
        string toolName, bool supportsConstraints)
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();

        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var tools = new AgentWorkspaceTools();
        var llm = new ConstraintAwareAgentLlm(supportsConstraints, InnocentLookingRequest(toolName));
        var agent = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools),
            llm, settings: settings, workspaceTools: tools);
        var options = new AgentWorkspaceOptions(workspace, null, "constraint-aware");

        var created = await agent.CreateTaskAsync($"Use {toolName}", options);
        var stepped = await agent.RunStepAsync(created.TaskId, options);
        var gateResult = stepped.State.ToolResults.Last(r => r.Tool == "safety_gate");
        return (stepped.State, gateResult.ResultSummary, llm.SawConstraint);
    }

    [Theory]
    [InlineData("delete_file")]
    [InlineData("install_package")]
    [InlineData("network_access")]
    [InlineData("change_git_history")]
    public async Task A_high_risk_tool_is_blocked_however_harmless_the_model_says_it_is(string toolName)
    {
        var (state, reason, _) = await StepAsync(toolName, supportsConstraints: false);

        Assert.Equal(AgentTaskStatus.Blocked, state.Status);
        Assert.Equal(AgentRiskLevel.High, state.ToolResults.Last(r => r.Tool == "safety_gate").Arguments["risk_level"] is string s
            ? Enum.Parse<AgentRiskLevel>(s)
            : AgentRiskLevel.None);
        Assert.Contains("blocked", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("delete_file")]
    [InlineData("run_command")]
    [InlineData("install_package")]
    public async Task Constraining_the_shape_changes_nothing_about_the_classification(string toolName)
    {
        var unconstrained = await StepAsync(toolName, supportsConstraints: false);
        var constrained = await StepAsync(toolName, supportsConstraints: true);

        // The constraint really was sent on one side and not the other, so a
        // green result here is not the test quietly comparing two identical runs.
        Assert.False(unconstrained.Constrained);
        Assert.True(constrained.Constrained);

        Assert.Equal(unconstrained.State.Status, constrained.State.Status);
        Assert.Equal(unconstrained.GateReason, constrained.GateReason);
        Assert.Equal(AgentTaskStatus.Blocked, constrained.State.Status);
    }

    [Fact]
    public async Task A_constrained_response_never_carries_its_own_approval_decision()
    {
        // The model set requires_approval: false and risk_level: none. The
        // dispatch path overwrites both from the classification.
        var (state, _, constrained) = await StepAsync("delete_file", supportsConstraints: true);

        Assert.True(constrained);
        Assert.Null(state.PendingToolAction);
        Assert.Equal(AgentTaskStatus.Blocked, state.Status);
        Assert.Equal("High", state.ToolResults.Last(r => r.Tool == "safety_gate").Arguments["risk_level"]);
    }

    [Fact]
    public async Task The_task_records_whether_its_planner_calls_were_constrained()
    {
        var constrained = await StepAsync("delete_file", supportsConstraints: true);
        var unconstrained = await StepAsync("delete_file", supportsConstraints: false);

        Assert.True(constrained.State.PlannerConstrained);
        Assert.False(unconstrained.State.PlannerConstrained);
    }
}
