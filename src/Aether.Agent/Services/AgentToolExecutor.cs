using System.Text.Json;
using System.Diagnostics;
using Aether.Agent.Models;

namespace Aether.Agent.Services;

public sealed class AgentToolExecutor : IAgentToolExecutor
{
    private readonly IAgentWorkspaceTools _workspaceTools;

    public AgentToolExecutor(IAgentWorkspaceTools workspaceTools)
    {
        _workspaceTools = workspaceTools;
    }

    public bool CanExecute(string toolName) => Normalize(toolName) is
        "list_files" or "search_files" or "read_file" or "summarize_file" or "draft_patch" or "inspect_git_diff" or "apply_draft_patch";

    public Task<AgentToolResult> ExecuteAsync(
        string toolName,
        Dictionary<string, object?> arguments,
        AgentWorkspaceOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = Normalize(toolName);
        object result = normalized switch
        {
            "list_files" => _workspaceTools.ListFiles(options),
            "search_files" => _workspaceTools.SearchFiles(options, Arg(arguments, "query")),
            "read_file" => _workspaceTools.ReadFile(options, Arg(arguments, "relative_path", "path")),
            "summarize_file" => _workspaceTools.SummarizeFile(options, Arg(arguments, "relative_path", "path")),
            "draft_patch" => _workspaceTools.DraftPatch(
                Arg(arguments, "relative_path", "path"),
                Arg(arguments, "rationale"),
                Arg(arguments, "proposed_content", "content")),
            "inspect_git_diff" => InspectGitDiff(options),
            "apply_draft_patch" => _workspaceTools.ApplyDraftPatch(
                options,
                Arg(arguments, "relative_path", "path"),
                Arg(arguments, "proposed_content", "content")),
            _ => throw new InvalidOperationException($"Unsupported agent tool: {toolName}")
        };

        return Task.FromResult(new AgentToolResult
        {
            Tool = normalized,
            Arguments = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase),
            ResultSummary = Summarize(result)
        });
    }

    private static string InspectGitDiff(AgentWorkspaceOptions options)
    {
        var root = AgentWorkspaceTools.ResolveWorkspaceRoot(options.WorkspaceRoot);
        var git = Path.Combine(root, ".git");
        if (!Directory.Exists(git))
            return "Workspace is not a Git repository.";

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("status");
        psi.ArgumentList.Add("--short");
        using var process = Process.Start(psi);
        if (process is null)
            return "Could not start git status.";

        if (!process.WaitForExit(3000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            return "git status timed out.";
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
            return string.IsNullOrWhiteSpace(error) ? "git status failed." : error.Trim();
        return string.IsNullOrWhiteSpace(output) ? "No working tree changes." : output.Trim();
    }

    private static string Arg(Dictionary<string, object?> args, params string[] names)
    {
        foreach (var name in names)
        {
            if (!args.TryGetValue(name, out var value) || value is null)
                continue;
            if (value is string text)
                return text;
            if (value is JsonElement element)
                return element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString();
            return value.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string Summarize(object result)
    {
        var json = JsonSerializer.Serialize(result, AgentJson.Options);
        return json.Length > 4000 ? json[..4000] + "\n[truncated]" : json;
    }

    private static string Normalize(string toolName) => toolName.Trim().Replace('-', '_').ToLowerInvariant();
}
