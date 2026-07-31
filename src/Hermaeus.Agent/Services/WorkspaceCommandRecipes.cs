using System.Text.Json;

namespace Hermaeus.Agent.Services;

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
public static class WorkspaceCommandRecipes
{
    public sealed record MatchResult(string FileName, IReadOnlyList<string> Args);

    // The verbs a developer runs to check their own work: build it, test it,
    // inspect it. Everything here is bounded to the workspace and produces a
    // result rather than a change.
    //
    // Deliberately absent, each for a reason rather than an oversight:
    //   - installers (npm install, dotnet restore, pip install): they reach the
    //     network and pull in third-party code, which is out of scope entirely.
    //   - formatters and fixers (cargo fmt, dotnet format): they rewrite source
    //     files, which has to go through the patch queue where the user can see
    //     the diff, not through a command that edits the tree invisibly.
    //   - long-running processes (dotnet run, npm start): not a verification
    //     step, and nothing here supervises a server.
    //   - make, mvn, gradle: a target can be arbitrary shell and there is no
    //     cheap declared-target check equivalent to package.json's scripts.
    //     Worth revisiting with real validation rather than waving through.
    private static readonly string[] Families =
    [
        "dotnet build", "dotnet test",
        "npm test", "npm run",
        "pnpm test", "pnpm run",
        "yarn test", "yarn run",
        "cargo build", "cargo test", "cargo check", "cargo clippy",
        "go build", "go test", "go vet",
        "pytest", "python -m pytest"
    ];

    /// <summary>
    /// Every command family the agent can ever run, which is the complete set
    /// a workspace may declare. Public so the workbench can offer them as a
    /// pick list instead of expecting the user to know them: a recipe typed
    /// from memory that is not one of these is refused by the safety gate at
    /// run time, which is a bad moment to find out.
    /// </summary>
    public static IReadOnlyList<string> KnownFamilies => Families;

    /// <summary>One line on what a family is for, for the workbench's recipe picker.</summary>
    public static string DescribeFamily(string family) => family switch
    {
        "dotnet build" => "Compile a .NET solution or project.",
        "dotnet test" => "Run a .NET test project.",
        "npm test" => "Run the package's test script.",
        "npm run" => "Run a script declared in package.json.",
        "pnpm test" => "Run the package's test script with pnpm.",
        "pnpm run" => "Run a package.json script with pnpm.",
        "yarn test" => "Run the package's test script with yarn.",
        "yarn run" => "Run a package.json script with yarn.",
        "cargo build" => "Compile a Rust crate.",
        "cargo test" => "Run a Rust crate's tests.",
        "cargo check" => "Type-check a Rust crate without building it.",
        "cargo clippy" => "Lint a Rust crate.",
        "go build" => "Compile Go packages.",
        "go test" => "Run Go tests.",
        "go vet" => "Report suspicious constructs in Go code.",
        "pytest" => "Run a Python test suite.",
        "python -m pytest" => "Run a Python test suite through the interpreter.",
        _ => string.Empty
    };

    /// <summary>
    /// The fixed family prefix a command belongs to (e.g. "dotnet test" for
    /// "dotnet test tests/Foo.csproj"), or null if it matches no fixed
    /// family at all. This is the single source of truth both the safety
    /// gate (family declared by the workspace?) and the executor (how to
    /// actually run it) key off, so they cannot disagree about what counts
    /// as a recognized command.
    /// </summary>
    /// <summary>
    /// Characters that only ever mean chaining, redirection or substitution.
    /// Nothing runs through a shell here (every command is launched with an
    /// explicit ArgumentList), so these could not have been interpreted, but a
    /// string like "dotnet test &amp;&amp; rm -rf /" still matched the family prefix
    /// and was therefore offered to the user as an approvable action. It would
    /// have been refused at execution time; it should never reach an approval
    /// prompt looking legitimate.
    /// </summary>
    private static readonly char[] ShellMetaCharacters = ['&', '|', ';', '`', '$', '>', '<', '\n', '\r'];

    public static string? ExtractFamily(string command)
    {
        var trimmed = (command ?? string.Empty).Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.IndexOfAny(ShellMetaCharacters) >= 0) return null;

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
            "npm run" => remainder is null ? null : MatchPackageScript("npm", remainder, workspaceRoot),
            "pnpm test" => remainder is null ? new MatchResult("pnpm", ["test"]) : null,
            "pnpm run" => remainder is null ? null : MatchPackageScript("pnpm", remainder, workspaceRoot),
            "yarn test" => remainder is null ? new MatchResult("yarn", ["test"]) : null,
            "yarn run" => remainder is null ? null : MatchPackageScript("yarn", remainder, workspaceRoot),
            "cargo build" => remainder is null ? new MatchResult("cargo", ["build"]) : null,
            "cargo test" => remainder is null ? new MatchResult("cargo", ["test"]) : null,
            "cargo check" => remainder is null ? new MatchResult("cargo", ["check"]) : null,
            "cargo clippy" => remainder is null ? new MatchResult("cargo", ["clippy"]) : null,
            "go build" => WithGoTarget("build", remainder, workspaceRoot),
            "go test" => WithGoTarget("test", remainder, workspaceRoot),
            "go vet" => WithGoTarget("vet", remainder, workspaceRoot),
            "pytest" => WithOptionalPath("pytest", [], remainder, workspaceRoot),
            "python -m pytest" => WithOptionalPath("python", ["-m", "pytest"], remainder, workspaceRoot),
            _ => null
        };
    }

    /// <summary>
    /// Go's package pattern "./..." is how every real Go command is written and
    /// is not a filesystem path, so it is allowed literally; anything else has
    /// to be a contained workspace path like every other family's argument.
    /// </summary>
    private static MatchResult? WithGoTarget(string verb, string? remainder, string workspaceRoot) =>
        remainder is "./..."
            ? new MatchResult("go", [verb, "./..."])
            : WithOptionalPath("go", [verb], remainder, workspaceRoot);

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

    /// <summary>
    /// A package-manager script must already be declared in the workspace's own
    /// package.json. The script body is arbitrary, so what keeps this bounded is
    /// that the workspace author wrote it, not the model.
    /// </summary>
    private static MatchResult? MatchPackageScript(string fileName, string scriptName, string workspaceRoot)
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

        return new MatchResult(fileName, ["run", scriptName]);
    }
}
