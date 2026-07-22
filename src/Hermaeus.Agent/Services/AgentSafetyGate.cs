using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

public sealed class AgentSafetyGate : IAgentSafetyGate
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "list_files",
        "search_files",
        "glob_files",
        "read_file",
        "summarize_file",
        "draft_patch",
        "inspect_git_diff",
        "set_plan"
    };

    private static readonly HashSet<string> HighRiskTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete_file",
        "run_command",
        "install_package",
        "network_access",
        "upload",
        "download",
        "modify_system_config",
        "commit",
        "push",
        "change_git_history"
    };

    public AgentToolPolicyDecision Evaluate(string toolName, bool wouldMutate = false)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "Missing tool name.");

        if (wouldMutate)
            return new AgentToolPolicyDecision(AgentToolDisposition.RequiresApproval, AgentRiskLevel.Medium, "Local write actions require approval.");

        // Explicit case, not the "apply/create/edit" substring heuristics
        // below: plan_subtasks never mutates anything itself, but approving
        // it changes how much autonomous work will run, so it always
        // requires approval regardless of what the model set in
        // requires_approval (r15 01-subtask-orchestration.md 1.2).
        if (string.Equals(toolName, "plan_subtasks", StringComparison.OrdinalIgnoreCase))
            return new AgentToolPolicyDecision(AgentToolDisposition.RequiresApproval, AgentRiskLevel.Medium, "Delegating the goal to sub-tasks changes how much autonomous work will run and requires approval.");

        if (toolName.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
            return new AgentToolPolicyDecision(AgentToolDisposition.RequiresApproval, AgentRiskLevel.Medium, "MCP tool calls always require approval, regardless of what the server claims about itself.");

        if (ReadOnlyTools.Contains(toolName))
            return new AgentToolPolicyDecision(AgentToolDisposition.Allowed, AgentRiskLevel.Low, "Read-only local operation.");

        if (HighRiskTools.Contains(toolName))
            return new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "High-risk or external action is blocked.");

        if (toolName.Contains("write", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("apply", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("create", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("update", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("rename", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("edit", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentToolPolicyDecision(AgentToolDisposition.RequiresApproval, AgentRiskLevel.Medium, "Potentially mutating tool requires approval.");
        }

        return new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "Unknown tool is blocked.");
    }

    public AgentToolPolicyDecision EvaluateCommand(string? requestedCommand, IReadOnlyList<WorkspaceCommandRecipe> allowedCommands)
    {
        var command = requestedCommand?.Trim() ?? string.Empty;
        if (command.Length == 0)
            return new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "No command specified.");

        var family = WorkspaceCommandRecipes.ExtractFamily(command);
        if (family is null)
            return new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "Command is not one of the fixed, safe executable template families.");

        // A declared recipe may itself carry an example argument (or none);
        // what has to match is the family, not the exact string, so a
        // workspace that declares bare "dotnet test" also covers "dotnet
        // test tests/Foo.csproj" - the optional argument's safety is
        // enforced separately when the command actually runs.
        var declared = allowedCommands.Any(recipe =>
            string.Equals(WorkspaceCommandRecipes.ExtractFamily(recipe.Command) ?? recipe.Command.Trim(), family, StringComparison.OrdinalIgnoreCase));
        if (!declared)
            return new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "Command family was not declared as a safe recipe for this workspace.");

        return new AgentToolPolicyDecision(AgentToolDisposition.RequiresApproval, AgentRiskLevel.Medium, "Template-family command execution always requires approval.");
    }
}
