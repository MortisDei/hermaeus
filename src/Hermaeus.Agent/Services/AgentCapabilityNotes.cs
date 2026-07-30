namespace Hermaeus.Agent.Services;

/// <summary>
/// What the agent can do in a given workspace, in the workspace's own terms
/// (r26 03 3.1). This replaces five hardcoded sentences that had drifted from
/// every source of truth they described. Pure: reads the tool set, the
/// workspace's declared command recipes, its policy summary and whether an
/// MCP bridge is configured, and returns lines to render. It executes
/// nothing, loads nothing and calls no service.
/// </summary>
public static class AgentCapabilityNotes
{
    /// <summary>Category a capability line speaks for.</summary>
    public const string InspectCategory = "inspect";
    public const string ChangeCategory = "change";
    public const string CommandCategory = "command";
    public const string DelegateCategory = "delegate";

    /// <summary>
    /// Every tool in <see cref="AgentToolExecutor.KnownTools"/> mapped to the
    /// line that speaks for it. The drift guard is a test asserting these keys
    /// are exactly the executor's tool set: add a tool without classifying it
    /// here and the suite fails, which is what the old hardcoded text had no
    /// way to do.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ToolCategories { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["list_files"] = InspectCategory,
        ["glob_files"] = InspectCategory,
        ["search_files"] = InspectCategory,
        ["read_file"] = InspectCategory,
        ["summarize_file"] = InspectCategory,
        ["inspect_git_diff"] = InspectCategory,
        ["draft_patch"] = ChangeCategory,
        ["apply_draft_patch"] = ChangeCategory,
        ["edit_file"] = ChangeCategory,
        ["create_file"] = ChangeCategory,
        ["run_command"] = CommandCategory,
        ["plan_subtasks"] = DelegateCategory
    };

    public static IReadOnlyList<string> Describe(AgentCapabilityContext context)
    {
        if (!context.HasWorkspace)
        {
            return
            [
                "What the agent can do depends on the workspace. Choose one and this list says exactly what applies there.",
                "True in every workspace: nothing is changed and no command is run without an approval you give."
            ];
        }

        var lines = new List<string>
        {
            "Reads this workspace: list, glob, search, read and summarise files, and inspect the git diff. No approval needed for reading.",
            "Proposes file changes: draft, create, edit, and apply a patch. Each one waits for your approval before anything is written."
        };

        var recipes = context.CommandRecipes
            .Select(recipe => recipe.Trim())
            .Where(recipe => recipe.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        lines.Add(recipes.Count == 0
            ? "Runs no commands here: this workspace declares no command recipes. Declare them in .hermaeus/workspace.json to allow any."
            : $"Runs only these declared commands, and only after approval: {string.Join(", ", recipes)}.");

        lines.Add("Can propose splitting the goal into sub-tasks. The split itself needs approval before any of it runs.");

        lines.Add(context.HasMcpBridge
            ? "Reaches outside this folder only through the MCP servers you configured, and every one of those calls is gated."
            : "Cannot reach outside this folder: there is no network, install, commit, push or remote-control tool.");

        if (!string.IsNullOrWhiteSpace(context.WorkspacePolicySummary))
            lines.Add(context.WorkspacePolicySummary.Trim());

        return lines;
    }
}

/// <summary>Inputs to <see cref="AgentCapabilityNotes.Describe"/>; all already held by the workbench.</summary>
public sealed record AgentCapabilityContext(
    bool HasWorkspace,
    IReadOnlyList<string> CommandRecipes,
    string WorkspacePolicySummary,
    bool HasMcpBridge);
