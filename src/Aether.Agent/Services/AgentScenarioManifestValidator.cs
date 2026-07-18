using Aether.Agent.Models;

namespace Aether.Agent.Services;

/// <summary>
/// Non-fatal sanity checks for a loaded scenario manifest: unknown tool
/// names, unknown status/risk-level spellings, and the standing rule that
/// no scenario may auto-approve run_command. Warnings never block a
/// scenario from loading (a typo in an expectation just means that
/// expectation always fails, which is self-diagnosing); they exist so
/// <see cref="IAgentScenarioStore"/> can surface them and so the shipped
/// library has an automated guard against drift.
/// </summary>
public static class AgentScenarioManifestValidator
{
    private static readonly HashSet<string> KnownTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "list_files", "search_files", "glob_files", "read_file", "summarize_file", "draft_patch",
        "inspect_git_diff", "apply_draft_patch", "edit_file", "create_file", "run_command", "set_plan",
        "plan_subtasks", "delete_file", "install_package", "network_access", "upload", "download",
        "modify_system_config", "commit", "push", "change_git_history"
    };

    public static IReadOnlyList<string> Validate(AgentScenarioManifest manifest, string scenarioLabel)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.Goal))
            warnings.Add($"{scenarioLabel}: goal is empty.");

        foreach (var tool in manifest.AutoApprove)
        {
            if (!KnownTools.Contains(tool))
                warnings.Add($"{scenarioLabel}: auto_approve references unknown tool '{tool}'.");
            if (string.Equals(tool, "run_command", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"{scenarioLabel}: auto_approve must never include 'run_command' - scenario runs may never actually execute a command.");
        }

        foreach (var tool in manifest.Expect.RequireApprovalFor
            .Concat(manifest.Expect.ForbidExecutionOf)
            .Concat(manifest.Expect.ExpectBlocked))
        {
            if (!KnownTools.Contains(tool))
                warnings.Add($"{scenarioLabel}: expectation references unknown tool '{tool}'.");
        }

        foreach (var status in manifest.Expect.FinalStatusAnyOf)
        {
            if (!IsKnownStatusName(status))
                warnings.Add($"{scenarioLabel}: final_status_any_of has unknown status '{status}'.");
        }

        foreach (var status in manifest.Expect.ExpectSubtaskStatuses)
        {
            if (!Enum.TryParse<AgentSubTaskStatus>(status, ignoreCase: true, out _))
                warnings.Add($"{scenarioLabel}: expect_subtask_statuses has unknown status '{status}'.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Expect.PendingRiskAtLeast)
            && !Enum.TryParse<AgentRiskLevel>(manifest.Expect.PendingRiskAtLeast, ignoreCase: true, out _))
        {
            warnings.Add($"{scenarioLabel}: pending_risk_at_least has unknown risk level '{manifest.Expect.PendingRiskAtLeast}'.");
        }

        foreach (var seed in manifest.SeedLessons)
        {
            if (!Enum.TryParse<AgentLessonOutcome>(seed.Outcome, ignoreCase: true, out _))
                warnings.Add($"{scenarioLabel}: seed_lessons outcome '{seed.Outcome}' is unknown.");
        }

        return warnings;
    }

    private static bool IsKnownStatusName(string value)
    {
        var stripped = value.Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.GetNames<AgentTaskStatus>().Any(n => string.Equals(n, stripped, StringComparison.OrdinalIgnoreCase));
    }
}
