using System.Text;
using Aether.Agent.Models;

namespace Aether.Agent.Services;

public sealed class WorkspaceAnalysisService : IWorkspaceAnalysisService
{
    private static readonly string[] InstructionPaths =
    [
        "AGENTS.md",
        "CLAUDE.md",
        "GEMINI.md",
        ".codex/instructions.md",
        ".github/copilot-instructions.md",
        "README.md",
        "CONTRIBUTING.md"
    ];

    private static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".fs"] = "F#",
        [".xaml"] = "XAML",
        [".axaml"] = "Avalonia XAML",
        [".js"] = "JavaScript",
        [".ts"] = "TypeScript",
        [".tsx"] = "TypeScript",
        [".py"] = "Python",
        [".rs"] = "Rust",
        [".go"] = "Go",
        [".java"] = "Java",
        [".md"] = "Markdown",
        [".json"] = "JSON",
        [".yml"] = "YAML",
        [".yaml"] = "YAML"
    };

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", "dist", "build", ".venv", "venv"
    };

    private readonly IWorkspaceProfileStore _profiles;
    private readonly IAgentWorkspaceMemoryStore _memory;

    public WorkspaceAnalysisService(IWorkspaceProfileStore profiles, IAgentWorkspaceMemoryStore memory)
    {
        _profiles = profiles;
        _memory = memory;
    }

    public async Task<WorkspaceAnalysisReport> AnalyzeAsync(AgentWorkspaceOptions options, CancellationToken ct = default)
    {
        var root = AgentWorkspaceTools.ResolveWorkspaceRoot(options.WorkspaceRoot);
        var files = EnumerateFiles(root, options.MaxFileBytes).Take(5000).ToList();
        var profile = await _profiles.LoadAsync(root, ct) ?? new WorkspaceProfile { WorkspaceRoot = root };
        profile.LinkedRagDatasetId = options.RagDatasetId;
        profile.PreferredModelId = options.ModelId;
        profile.WorkspaceMemoryCount = (await _memory.ListAsync(root, ct)).Count;
        profile.TrustStatus = "local scan complete";

        var report = new WorkspaceAnalysisReport
        {
            Profile = profile,
            RepoType = DetectRepoType(root, files),
            Languages = DetectLanguages(files).ToList(),
            Frameworks = DetectFrameworks(root, files).ToList(),
            ImportantFiles = DetectImportantFiles(root, files).ToList(),
            Instructions = await DetectInstructionsAsync(root, ct),
            CommandRecipes = DetectCommandRecipes(root, files).ToList()
        };

        report.InstructionWarnings = DetectInstructionWarnings(report.Instructions).ToList();
        report.Risks = DetectRisks(root, files, report).ToList();
        report.SuggestedAgentsMd = BuildSuggestedAgentsMd(report);
        report.RagIngestPlan = BuildRagIngestPlan(report);
        report.Summary = BuildSummary(report);
        report.Profile.LastSummary = report.Summary;
        await _profiles.SaveAsync(report.Profile, ct);
        return report;
    }

    private static IEnumerable<string> EnumerateFiles(string root, int maxFileBytes)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(dir); }
            catch { continue; }

            foreach (var child in dirs)
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)))
                    pending.Push(child);
            }

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { continue; }

            foreach (var file in files)
            {
                FileInfo info;
                try { info = new FileInfo(file); }
                catch { continue; }

                if (info.Exists && info.Length <= maxFileBytes)
                    yield return file;
            }
        }
    }

    private static string DetectRepoType(string root, IReadOnlyCollection<string> files)
    {
        if (Directory.Exists(Path.Combine(root, ".git"))) return "git repository";
        if (files.Any(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))) return ".NET solution";
        if (File.Exists(Path.Combine(root, "package.json"))) return "Node workspace";
        if (File.Exists(Path.Combine(root, "Cargo.toml"))) return "Rust crate";
        if (File.Exists(Path.Combine(root, "pyproject.toml"))) return "Python project";
        return "folder";
    }

    private static IEnumerable<string> DetectLanguages(IEnumerable<string> files) =>
        files.Select(path => Path.GetExtension(path))
            .Where(LanguageByExtension.ContainsKey)
            .GroupBy(ext => LanguageByExtension[ext])
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(8)
            .Select(group => $"{group.Key} ({group.Count()})");

    private static IEnumerable<string> DetectFrameworks(string root, IEnumerable<string> files)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var text = TryRead(file, 32_000);
            if (text.Contains("Avalonia", StringComparison.OrdinalIgnoreCase)) found.Add("Avalonia");
            if (text.Contains("Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase)) found.Add(".NET SDK");
            if (text.Contains("xunit", StringComparison.OrdinalIgnoreCase)) found.Add("xUnit");
        }

        var packageJson = Path.Combine(root, "package.json");
        if (File.Exists(packageJson))
        {
            var text = TryRead(packageJson, 32_000);
            if (text.Contains("\"react\"", StringComparison.OrdinalIgnoreCase)) found.Add("React");
            if (text.Contains("\"vite\"", StringComparison.OrdinalIgnoreCase)) found.Add("Vite");
        }

        if (File.Exists(Path.Combine(root, "Cargo.toml"))) found.Add("Cargo");
        if (File.Exists(Path.Combine(root, "pyproject.toml"))) found.Add("Python packaging");
        return found;
    }

    private static IEnumerable<string> DetectImportantFiles(string root, IEnumerable<string> files)
    {
        var known = new[]
        {
            "AGENTS.md", "README.md", "CONTRIBUTING.md", "CHANGELOG.md", "Directory.Build.props",
            "global.json", "package.json", "Cargo.toml", "pyproject.toml"
        };
        var relatives = files.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return known.Where(relatives.Contains).Concat(relatives.Where(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)).Take(3));
    }

    private static async Task<List<ProjectInstructionFile>> DetectInstructionsAsync(string root, CancellationToken ct)
    {
        var result = new List<ProjectInstructionFile>();
        foreach (var relative in InstructionPaths)
        {
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                continue;

            var content = await ReadLimitedAsync(full, 24_000, ct);
            result.Add(new ProjectInstructionFile(relative, SummarizeInstruction(content), content, result.Count == 0));
        }

        return result;
    }

    private static IEnumerable<string> DetectInstructionWarnings(IReadOnlyList<ProjectInstructionFile> instructions)
    {
        if (instructions.Count == 0)
        {
            yield return "No project instruction file found.";
            yield break;
        }

        if (instructions.Count > 1)
            yield return "Multiple instruction files were found. Review precedence before injecting all of them.";

        if (instructions.SelectMany(x => x.Content.Split('\n')).Any(line => line.Contains("ignore", StringComparison.OrdinalIgnoreCase)
            && line.Contains("instruction", StringComparison.OrdinalIgnoreCase)))
        {
            yield return "Instruction text contains override language. Review before injecting into model context.";
        }
    }

    private static IEnumerable<string> DetectRisks(string root, IReadOnlyCollection<string> files, WorkspaceAnalysisReport report)
    {
        if (!report.Instructions.Any(file => file.RelativePath.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase)))
            yield return "No AGENTS.md found for local agent guidance.";
        if (!files.Any(path => path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase)))
            yield return "No obvious test project or tests folder found.";
        if (!File.Exists(Path.Combine(root, "README.md")))
            yield return "No README.md found.";
    }

    private static IEnumerable<WorkspaceCommandRecipe> DetectCommandRecipes(string root, IEnumerable<string> files)
    {
        var list = files.ToList();
        if (list.Any(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new WorkspaceCommandRecipe("dotnet build", "Validate the .NET solution after changes.", AgentRiskLevel.Low);
            yield return new WorkspaceCommandRecipe("dotnet test", "Run the solution test suite when available.", AgentRiskLevel.Low);
        }
        else if (list.Any(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new WorkspaceCommandRecipe("dotnet build <project>.csproj", "Validate the selected .NET project.", AgentRiskLevel.Low);
        }

        if (File.Exists(Path.Combine(root, "package.json")))
            yield return new WorkspaceCommandRecipe("npm test", "Run the Node package test script if configured.", AgentRiskLevel.Low);
        if (File.Exists(Path.Combine(root, "Cargo.toml")))
            yield return new WorkspaceCommandRecipe("cargo test", "Run Rust tests for this crate or workspace.", AgentRiskLevel.Low);
        if (File.Exists(Path.Combine(root, "pyproject.toml")))
            yield return new WorkspaceCommandRecipe("pytest", "Run Python tests if pytest is configured.", AgentRiskLevel.Low);
    }

    private static string BuildSuggestedAgentsMd(WorkspaceAnalysisReport report)
    {
        var commands = report.CommandRecipes.Count == 0
            ? "- Add project-specific build and test commands."
            : string.Join('\n', report.CommandRecipes.Select(recipe => $"- `{recipe.Command}` - {recipe.Why}"));
        return $"""
        # AGENTS.md

        Project type: {report.RepoType}

        Guidance:
        - Work locally and keep edits scoped to the selected workspace.
        - Read existing conventions before changing code.
        - Update docs when behavior or user-facing workflow changes.

        Useful commands:
        {commands}
        """;
    }

    private static string BuildRagIngestPlan(WorkspaceAnalysisReport report)
    {
        var include = report.ImportantFiles.Count == 0
            ? "README, docs, source files, and configuration files"
            : string.Join(", ", report.ImportantFiles.Take(8));
        return $"Create or link a dataset for this workspace, ingest {include}, skip build outputs and dependency folders, then reindex when the embedding model changes.";
    }

    private static string BuildSummary(WorkspaceAnalysisReport report)
    {
        var languages = report.Languages.Count == 0 ? "unknown languages" : string.Join(", ", report.Languages.Take(4));
        var frameworks = report.Frameworks.Count == 0 ? "no framework markers" : string.Join(", ", report.Frameworks);
        return $"{report.RepoType}; {languages}; {frameworks}; {report.Instructions.Count} instruction file(s); {report.Risks.Count} risk note(s).";
    }

    private static string SummarizeInstruction(string content)
    {
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .Take(6);
        var summary = string.Join(" ", lines);
        return summary.Length > 320 ? summary[..317] + "..." : summary;
    }

    private static async Task<string> ReadLimitedAsync(string path, int maxChars, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        return text.Length > maxChars ? text[..maxChars] : text;
    }

    private static string TryRead(string path, int maxChars)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.Length > maxChars ? text[..maxChars] : text;
        }
        catch
        {
            return string.Empty;
        }
    }
}
