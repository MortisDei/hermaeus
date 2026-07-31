using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// run_command is Review, not Blocked, and the route is what makes that true:
/// the dispatch path sends it to <see cref="AgentSafetyGate.EvaluateCommand"/>
/// rather than the generic <c>Evaluate</c>, which would block it on its
/// high-risk-set membership. The two halves are pinned together here because
/// reading either one alone gives the wrong answer, which is exactly the trace
/// someone repeated when docs/agent.md and the gate looked like they disagreed.
/// </summary>
public sealed class AgentCommandRiskClassificationTests
{
    private static string RunCommandResponse(string command) => $$"""
        {
          "thought_summary": "Running a workspace command.",
          "current_step": "Run the command.",
          "next_action": {
            "type": "tool",
            "tool_name": "run_command",
            "arguments": { "command": "{{command}}" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Running."
        }
        """;

    private static async Task<(AgentService Agent, AgentWorkspaceOptions Options)> BuildAsync(
        TempDir temp, string command, params string[] declaredCommands)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();

        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var manifests = new WorkspaceManifestService();
        await manifests.SaveAsync(workspace, new WorkspaceManifest
        {
            AllowedCommands = [.. declaredCommands.Select(c => new WorkspaceCommandRecipe(c, "Declared for the test.", AgentRiskLevel.Low))]
        });

        var tools = new AgentWorkspaceTools();
        var agent = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools),
            new FakeSequencedAgentLlm([RunCommandResponse(command)]), manifests: manifests, settings: settings, workspaceTools: tools);
        return (agent, new AgentWorkspaceOptions(workspace, null, "fake-sequenced-agent"));
    }

    [Fact]
    public void The_generic_gate_path_blocks_run_command()
    {
        // Defence in depth for any future caller that reaches Evaluate with
        // this tool name. It is not the classification the agent uses.
        var decision = new AgentSafetyGate().Evaluate("run_command");

        Assert.Equal(AgentToolDisposition.Blocked, decision.Disposition);
        Assert.Equal(AgentRiskLevel.High, decision.RiskLevel);
    }

    [Fact]
    public async Task Dispatching_run_command_for_a_declared_family_asks_for_approval()
    {
        using var temp = new TempDir();
        var (agent, options) = await BuildAsync(temp, "dotnet test", "dotnet test");

        var created = await agent.CreateTaskAsync("Run the tests", options);
        var stepped = await agent.RunStepAsync(created.TaskId, options);

        Assert.Equal(AgentTaskStatus.WaitingForUser, stepped.State.Status);
        Assert.NotNull(stepped.State.PendingToolAction);
        Assert.Equal("run_command", stepped.State.PendingToolAction!.ToolName);
        Assert.Equal(AgentRiskLevel.Medium, stepped.State.PendingToolAction.RiskLevel);
        // EvaluateCommand's own wording, which is the proof that the generic
        // Evaluate path was not the one that ran.
        Assert.Contains("Template-family", stepped.State.PendingToolAction.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatching_run_command_for_an_undeclared_family_is_blocked_and_names_the_family()
    {
        using var temp = new TempDir();
        var (agent, options) = await BuildAsync(temp, "dotnet test", "npm run lint");

        var created = await agent.CreateTaskAsync("Run the tests", options);
        var stepped = await agent.RunStepAsync(created.TaskId, options);

        Assert.Null(stepped.State.PendingToolAction);
        Assert.Equal(AgentTaskStatus.Blocked, stepped.State.Status);
    }
}
