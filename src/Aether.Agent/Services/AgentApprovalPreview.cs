using System.Text.Json;
using Aether.Agent.Models;

namespace Aether.Agent.Services;

/// <summary>
/// Renders what a pending run_command approval will actually execute, so
/// approval is informed rather than nominal (r6 03-platform-cleanup.md 3.2).
/// Read-only: never executes anything, only describes.
/// </summary>
public static class AgentApprovalPreview
{
    public static string Describe(AgentPendingToolAction pending, AgentWorkspaceOptions options)
    {
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
