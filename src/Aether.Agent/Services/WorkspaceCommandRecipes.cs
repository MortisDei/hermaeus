using System.Text.Json;

namespace Aether.Agent.Services;

/// <summary>
/// The fixed, hardcoded set of run_command template families. A command is
/// only ever executable if it both belongs to one of these families AND was
/// declared safe by the workspace's own manifest (checked separately in
/// <see cref="AgentSafetyGate.EvaluateCommand"/>); this class only knows
/// how to recognize a family and turn a matching request into a concrete
/// process invocation. No family here accepts arbitrary shell text - each
/// optional argument is validated (a workspace-relative path with the same
/// containment rules as every other agent file path, or a script name that
/// must already exist in the workspace's own package.json).
/// </summary>
internal static class WorkspaceCommandRecipes
{
    public sealed record MatchResult(string FileName, IReadOnlyList<string> Args);

    private static readonly string[] Families =
    [
        "dotnet build", "dotnet test", "npm test", "npm run", "cargo build", "cargo test", "pytest"
    ];

    /// <summary>
    /// The fixed family prefix a command belongs to (e.g. "dotnet test" for
    /// "dotnet test tests/Foo.csproj"), or null if it matches no fixed
    /// family at all. This is the single source of truth both the safety
    /// gate (family declared by the workspace?) and the executor (how to
    /// actually run it) key off, so they cannot disagree about what counts
    /// as a recognized command.
    /// </summary>
    public static string? ExtractFamily(string command)
    {
        var trimmed = (command ?? string.Empty).Trim();
        if (trimmed.Length == 0) return null;

        foreach (var family in Families)
        {
            if (string.Equals(trimmed, family, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(family + " ", StringComparison.OrdinalIgnoreCase))
            {
                return family;
            }
        }

        return null;
    }

    /// <summary>
    /// Validates a command against its family's argument rules and returns
    /// how to run it, or null if the family doesn't match or its optional
    /// argument fails validation (path escapes the workspace, or an npm
    /// script the workspace never declared).
    /// </summary>
    public static MatchResult? TryMatch(string command, string workspaceRoot)
    {
        var trimmed = (command ?? string.Empty).Trim();
        var family = ExtractFamily(trimmed);
        if (family is null) return null;

        var remainder = trimmed.Length == family.Length ? null : trimmed[(family.Length + 1)..].Trim();
        if (remainder is { Length: 0 }) remainder = null;

        return family switch
        {
            "dotnet build" => WithOptionalPath("dotnet", ["build"], remainder, workspaceRoot),
            "dotnet test" => WithOptionalPath("dotnet", ["test"], remainder, workspaceRoot),
            "npm test" => remainder is null ? new MatchResult("npm", ["test"]) : null,
            "npm run" => remainder is null ? null : MatchNpmRunScript(remainder, workspaceRoot),
            "cargo build" => remainder is null ? new MatchResult("cargo", ["build"]) : null,
            "cargo test" => remainder is null ? new MatchResult("cargo", ["test"]) : null,
            "pytest" => WithOptionalPath("pytest", [], remainder, workspaceRoot),
            _ => null
        };
    }

    private static MatchResult? WithOptionalPath(string fileName, string[] baseArgs, string? pathArg, string workspaceRoot)
    {
        if (pathArg is null)
            return new MatchResult(fileName, baseArgs);

        try
        {
            AgentWorkspaceTools.ResolveSafePath(workspaceRoot, pathArg);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return new MatchResult(fileName, [.. baseArgs, pathArg.Replace('\\', '/')]);
    }

    private static MatchResult? MatchNpmRunScript(string scriptName, string workspaceRoot)
    {
        if (scriptName.Any(c => c is '/' or '\\' or ' ') || scriptName.Contains(".."))
            return null;

        var packageJsonPath = Path.Combine(workspaceRoot, "package.json");
        if (!File.Exists(packageJsonPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            if (!doc.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
                return null;
            if (!scripts.TryGetProperty(scriptName, out _))
                return null;
        }
        catch (JsonException)
        {
            return null;
        }

        return new MatchResult("npm", ["run", scriptName]);
    }
}
