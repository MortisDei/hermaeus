using System.Text.Json;
using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

/// <summary>
/// Renders what a pending run_command approval will actually execute, so
/// approval is informed rather than nominal (r6 03-platform-cleanup.md 3.2).
/// Read-only: never executes anything, only describes.
/// </summary>
public static class AgentApprovalPreview
{
    public static string Describe(AgentPendingToolAction pending, AgentWorkspaceOptions options)
    {
        if (string.Equals(pending.ToolName, "plan_subtasks", StringComparison.OrdinalIgnoreCase))
            return DescribePlanSubtasks(pending.Arguments);

        if (!string.Equals(pending.ToolName, "run_command", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var command = AgentToolExecutor.Arg(pending.Arguments, "command");
        var family = WorkspaceCommandRecipes.ExtractFamily(command);
        if (family is null)
            return string.Empty;

        if (family == "npm run")
        {
            string root;
            try { root = AgentWorkspaceTools.ResolveWorkspaceRoot(options.WorkspaceRoot); }
            catch { return string.Empty; }

            var match = WorkspaceCommandRecipes.TryMatch(command, root);
            var scriptName = match?.Args is { Count: >= 2 } args ? args[1] : null;
            var body = scriptName is null ? null : TryReadNpmScriptBody(root, scriptName);
            return string.IsNullOrWhiteSpace(body)
                ? "Runs a package.json script."
                : $"Runs: {body}";
        }

        return "Runs workspace-defined build or test logic (MSBuild targets, build.rs, conftest, or a package.json test script).";
    }

    /// <summary>
    /// Renders the actual proposed sub-task plan so the user sees exactly
    /// what they are authorizing (r15 02-orchestration-ui.md 2.1): one line
    /// per sub-task with its profile and goal, plus the count. A malformed
    /// payload degrades to a clear message instead of throwing - approval-time
    /// validation (AgentService.TryParsePlanSubtasks) is what actually
    /// rejects an invalid plan; this is display only.
    /// </summary>
    private static string DescribePlanSubtasks(Dictionary<string, object?> arguments)
    {
        if (!arguments.TryGetValue("subtasks", out var raw) || raw is not JsonElement { ValueKind: JsonValueKind.Array } array)
            return "Could not parse the proposed plan.";

        var lines = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var goal = item.TryGetProperty("goal", out var g) ? g.GetString() ?? string.Empty : string.Empty;
            var profile = item.TryGetProperty("profile", out var p) ? p.GetString() ?? string.Empty : string.Empty;
            var modelId = item.TryGetProperty("model_id", out var m) ? m.GetString() ?? string.Empty : string.Empty;
            if (goal.Length == 0) continue;
            var model = string.IsNullOrWhiteSpace(modelId) ? "inherit parent" : modelId;
            lines.Add($"[{(profile.Length == 0 ? "general" : profile)}, model {model}] {goal}");
        }

        if (lines.Count == 0)
            return "Could not parse the proposed plan.";

        return $"{lines.Count} sub-task(s):\n" + string.Join("\n", lines);
    }

    private static string? TryReadNpmScriptBody(string root, string scriptName)
    {
        try
        {
            var packageJsonPath = Path.Combine(root, "package.json");
            if (!File.Exists(packageJsonPath))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            if (!doc.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
                return null;
            if (!scripts.TryGetProperty(scriptName, out var body) || body.ValueKind != JsonValueKind.String)
                return null;

            return body.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
