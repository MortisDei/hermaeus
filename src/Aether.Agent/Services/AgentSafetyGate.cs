using Aether.Agent.Models;

namespace Aether.Agent.Services;

public sealed class AgentSafetyGate : IAgentSafetyGate
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "list_files",
        "search_files",
        "read_file",
        "summarize_file",
        "draft_patch",
        "inspect_git_diff"
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

        if (ReadOnlyTools.Contains(toolName))
            return new AgentToolPolicyDecision(AgentToolDisposition.Allowed, AgentRiskLevel.Low, "Read-only local operation.");

        if (HighRiskTools.Contains(toolName))
            return new AgentToolPolicyDecision(AgentToolDisposition.RequiresApproval, AgentRiskLevel.High, "High-risk or external action requires explicit approval.");

        if (toolName.Contains("write", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("apply", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("create", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("update", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("rename", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentToolPolicyDecision(AgentToolDisposition.RequiresApproval, AgentRiskLevel.Medium, "Potentially mutating tool requires approval.");
        }

        return new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "Unknown tool is blocked.");
    }
}
