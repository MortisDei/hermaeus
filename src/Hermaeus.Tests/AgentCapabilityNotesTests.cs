using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// The workbench's capability text is derived from the tool set, the
/// workspace's declared command recipes and its policy, so it cannot drift
/// away from what the agent can actually do. The five sentences it replaced
/// were hardcoded and had drifted from every source they described
/// (docs/review/archived/r26 doc 03 3.1).
/// </summary>
public sealed class AgentCapabilityNotesTests
{
    private static AgentCapabilityContext Context(
        bool hasWorkspace = true,
        IReadOnlyList<string>? recipes = null,
        string policy = "",
        bool mcp = false) =>
        new(hasWorkspace, recipes ?? [], policy, mcp);

    [Fact]
    public void Every_executable_tool_is_classified_by_exactly_one_line()
    {
        var classified = AgentCapabilityNotes.ToolCategories.Keys.ToHashSet(StringComparer.Ordinal);
        var executable = AgentToolExecutor.KnownTools.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(executable.Count, AgentCapabilityNotes.ToolCategories.Count);
        Assert.True(classified.SetEquals(executable),
            "Tools the executor accepts but the capability text does not classify: "
            + string.Join(", ", executable.Except(classified))
            + "; classified but not executable: "
            + string.Join(", ", classified.Except(executable)));
    }

    [Fact]
    public void Each_classification_category_is_spoken_for_by_the_rendered_lines()
    {
        var lines = AgentCapabilityNotes.Describe(Context(recipes: ["dotnet test"]));

        Assert.Contains(lines, line => line.Contains("Reads this workspace", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Proposes file changes", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("dotnet test", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("sub-tasks", StringComparison.Ordinal));
    }

    [Fact]
    public void With_no_recipes_the_command_line_says_commands_cannot_run_and_names_the_manifest()
    {
        var lines = AgentCapabilityNotes.Describe(Context());

        var commandLine = Assert.Single(lines, line => line.Contains("command", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("no command recipes", commandLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".hermaeus/workspace.json", commandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void With_recipes_declared_they_are_named()
    {
        var lines = AgentCapabilityNotes.Describe(Context(recipes: ["dotnet build", "dotnet test"]));

        var commandLine = Assert.Single(lines, line => line.Contains("dotnet build", StringComparison.Ordinal));
        Assert.Contains("dotnet test", commandLine, StringComparison.Ordinal);
        Assert.Contains("approval", commandLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_and_blank_recipes_are_not_repeated_in_the_text()
    {
        var lines = AgentCapabilityNotes.Describe(Context(recipes: ["dotnet test", " dotnet test ", "   "]));

        var commandLine = Assert.Single(lines, line => line.Contains("dotnet test", StringComparison.Ordinal));
        Assert.Equal(1, commandLine.Split("dotnet test").Length - 1);
    }

    [Fact]
    public void Without_an_mcp_bridge_the_text_says_the_agent_cannot_reach_outside_the_workspace()
    {
        var lines = AgentCapabilityNotes.Describe(Context());

        Assert.Contains(lines, line => line.Contains("Cannot reach outside this folder", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("MCP", StringComparison.Ordinal));
    }

    [Fact]
    public void With_an_mcp_bridge_the_text_says_calls_go_through_configured_servers_and_are_gated()
    {
        var lines = AgentCapabilityNotes.Describe(Context(mcp: true));

        var mcpLine = Assert.Single(lines, line => line.Contains("MCP", StringComparison.Ordinal));
        Assert.Contains("gated", mcpLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(lines, line => line.Contains("Cannot reach outside", StringComparison.Ordinal));
    }

    [Fact]
    public void The_workspace_policy_summary_is_included_when_there_is_one()
    {
        var withPolicy = AgentCapabilityNotes.Describe(Context(policy: "Workspace policy: reads unrestricted, writes limited to 2 rules, 1 path off limits."));
        var withoutPolicy = AgentCapabilityNotes.Describe(Context());

        Assert.Contains(withPolicy, line => line.StartsWith("Workspace policy:", StringComparison.Ordinal));
        Assert.DoesNotContain(withoutPolicy, line => line.StartsWith("Workspace policy:", StringComparison.Ordinal));
    }

    [Fact]
    public void With_no_workspace_the_text_says_the_answer_depends_on_the_workspace_and_that_nothing_runs_unapproved()
    {
        var lines = AgentCapabilityNotes.Describe(Context(hasWorkspace: false));

        Assert.Equal(2, lines.Count);
        Assert.Contains("depends on the workspace", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approval", lines[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_text_stays_short_enough_to_read()
    {
        var lines = AgentCapabilityNotes.Describe(Context(
            recipes: ["dotnet build", "dotnet test"],
            policy: "Workspace policy: reads unrestricted, writes unrestricted, 0 paths off limits.",
            mcp: true));

        Assert.InRange(lines.Count, 4, 6);
    }
}
