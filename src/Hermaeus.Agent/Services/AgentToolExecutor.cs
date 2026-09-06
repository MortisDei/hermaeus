using System.Text.Json;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections;
using Hermaeus.Agent.Models;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Agent.Services;

public sealed class AgentToolExecutor : IAgentToolExecutor
{
    private readonly IAgentWorkspaceTools _workspaceTools;
    private readonly IMcpToolBridge? _mcpBridge;
    private readonly IRuntimeLogService? _logs;

    public AgentToolExecutor(IAgentWorkspaceTools workspaceTools, IMcpToolBridge? mcpBridge = null, IRuntimeLogService? logs = null)
    {
        _workspaceTools = workspaceTools;
        _mcpBridge = mcpBridge;
        _logs = logs;
    }

    /// <summary>
    /// Every built-in tool this executor accepts, in one enumerable place so
    /// that text describing the agent's capabilities can be derived from the
    /// tool set instead of restating it (r26 03 3.1). MCP tools are not here:
    /// they are named by the configured bridge, not by this list.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownTools =
    [
        "list_files", "search_files", "read_file", "summarize_file", "draft_patch", "inspect_git_diff",
        "apply_draft_patch", "run_command", "edit_file", "create_file", "glob_files", "plan_subtasks"
    ];

    public bool CanExecute(string toolName)
    {
        var trimmed = toolName.Trim();
        if (trimmed.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
            return _mcpBridge?.CanExecute(trimmed) == true;

        return KnownTools.Contains(Normalize(toolName), StringComparer.Ordinal);
    }

    public async Task<AgentToolResult> ExecuteAsync(
        string toolName,
        Dictionary<string, object?> arguments,
        AgentWorkspaceOptions options,
        CancellationToken ct = default,
        string? operationId = null)
    {
        operationId ??= OperationCorrelation.NewId();
        var timer = Stopwatch.StartNew();
        _logs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Debug,
            RuntimeLogCategory.Agent,
            $"Agent tool started: tool={NormalizeToolForLog(toolName)}, arg_count={arguments.Count}, arg_keys={FormatArgumentKeys(arguments)}.",
            operationId));
        try
        {
            var result = await ExecuteCoreAsync(toolName, arguments, options, ct);
            _logs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Debug,
                RuntimeLogCategory.Agent,
                $"Agent tool completed: tool={NormalizeToolForLog(result.Tool)}, outcome={result.NormalizedOutcome.Outcome}, exit_code={result.ExitCode?.ToString() ?? "Unknown"}, timed_out={result.TimedOut}, duration_ms={timer.ElapsedMilliseconds}.",
                operationId));
            return result;
        }
        catch (OperationCanceledException)
        {
            _logs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Agent,
                $"Agent tool cancelled: tool={NormalizeToolForLog(toolName)}, duration_ms={timer.ElapsedMilliseconds}.",
                operationId));
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Error,
                RuntimeLogCategory.Agent,
                $"Agent tool failed: tool={NormalizeToolForLog(toolName)}, exception={ex.GetType().Name}, duration_ms={timer.ElapsedMilliseconds}.",
                operationId));
            throw;
        }
    }

    private async Task<AgentToolResult> ExecuteCoreAsync(
        string toolName,
        Dictionary<string, object?> arguments,
        AgentWorkspaceOptions options,
        CancellationToken ct)
    {
        var trimmedToolName = toolName.Trim();
        if (ct.IsCancellationRequested)
            return Result(trimmedToolName, arguments, "The operation was cancelled before execution.",
                AgentToolOutcomeSignal.Cancelled);

        if (trimmedToolName.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            if (_mcpBridge is null || !_mcpBridge.CanExecute(trimmedToolName))
                return Result(trimmedToolName, arguments, $"No MCP bridge is configured to execute '{toolName}'.",
                    AgentToolOutcomeSignal.Unavailable);

            McpToolExecutionResult mcpOutput;
            try
            {
                mcpOutput = await _mcpBridge.ExecuteAsync(trimmedToolName, arguments, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Result(trimmedToolName, arguments, "The MCP call was cancelled.", AgentToolOutcomeSignal.Cancelled);
            }
            catch (Exception ex)
            {
                return Result(trimmedToolName, arguments, ex.Message, AgentToolOutcomeSignal.Failed);
            }

            return new AgentToolResult
            {
                Tool = trimmedToolName,
                Arguments = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase),
                ResultSummary = Summarize(mcpOutput.Content, trimmedToolName),
                Source = new SourceReference(ProvenanceKind.AgentTool, trimmedToolName, Locator: trimmedToolName),
                NormalizedOutcome = AgentToolOutcomeNormalizer.Normalize(trimmedToolName, new AgentToolOutcomeEvidence(
                    mcpOutput.IsError switch
                    {
                        false => AgentToolOutcomeSignal.StructuredSuccess,
                        true => AgentToolOutcomeSignal.StructuredFailure,
                        null => AgentToolOutcomeSignal.Unclassified
                    }, Detail: mcpOutput.IsError is null
                        ? "The MCP response did not include a structured completion status."
                        : "The MCP response included structured completion status."))
            };
        }

        var normalized = Normalize(toolName);
        if (!KnownTools.Contains(normalized, StringComparer.Ordinal))
            return Result(normalized, arguments, $"Unsupported agent tool: {toolName}", AgentToolOutcomeSignal.Unavailable);

        if (normalized == "run_command")
        {
            CommandExecutionResult commandResult;
            try
            {
                commandResult = await RunCommandAsync(options, Arg(arguments, "command"), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Result(normalized, arguments, "The command was cancelled by the caller.", AgentToolOutcomeSignal.Cancelled);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
            {
                return Result(normalized, arguments, "The command executable was not found.", AgentToolOutcomeSignal.Unavailable);
            }
            catch (InvalidOperationException ex)
            {
                return Result(normalized, arguments, ex.Message, AgentToolOutcomeSignal.PolicyBlocked);
            }
            catch (Exception ex)
            {
                return Result(normalized, arguments, ex.Message, AgentToolOutcomeSignal.Failed);
            }

            return new AgentToolResult
            {
                Tool = normalized,
                Arguments = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase),
                ResultSummary = Summarize(commandResult.Summary, normalized),
                ExitCode = commandResult.ExitCode,
                TimedOut = commandResult.TimedOut,
                Source = BuildSource(normalized, arguments),
                NormalizedOutcome = AgentToolOutcomeNormalizer.Normalize(normalized, new AgentToolOutcomeEvidence(
                    commandResult.TimedOut ? AgentToolOutcomeSignal.TimedOut : AgentToolOutcomeSignal.Completed,
                    commandResult.ExitCode,
                    commandResult.TimedOut ? "The configured command deadline elapsed." : $"The process exited with code {commandResult.ExitCode}."))
            };
        }

        object result;
        try
        {
            result = normalized switch
            {
                "list_files" => _workspaceTools.ListFiles(options, ArgOrNull(arguments, "subdirectory"), ArgIntOrNull(arguments, "max_depth")),
                "search_files" => _workspaceTools.SearchFiles(
                    options,
                    Arg(arguments, "query"),
                    ArgBool(arguments, "regex"),
                    ArgInt(arguments, "context_lines")),
                "glob_files" => _workspaceTools.GlobFiles(options, Arg(arguments, "pattern")),
                "read_file" => _workspaceTools.ReadFile(
                    options,
                    Arg(arguments, "relative_path", "path"),
                    ArgIntOrNull(arguments, "line_offset"),
                    ArgIntOrNull(arguments, "line_limit")),
                "summarize_file" => _workspaceTools.SummarizeFile(options, Arg(arguments, "relative_path", "path")),
                "draft_patch" => _workspaceTools.DraftPatch(
                    Arg(arguments, "relative_path", "path"),
                    Arg(arguments, "rationale"),
                    Arg(arguments, "proposed_content", "content")),
                "inspect_git_diff" => await InspectGitDiffAsync(options, ct),
                "apply_draft_patch" => await _workspaceTools.ApplyDraftPatchAsync(
                    options,
                    Arg(arguments, "relative_path", "path"),
                    Arg(arguments, "proposed_content", "content"),
                    ct),
                "edit_file" => await _workspaceTools.EditFileAsync(
                    options,
                    Arg(arguments, "relative_path", "path"),
                    Arg(arguments, "old_string"),
                    Arg(arguments, "new_string"),
                    ct),
                "create_file" => await _workspaceTools.CreateFileAsync(
                    options,
                    Arg(arguments, "relative_path", "path"),
                    Arg(arguments, "content"),
                    ct),
                _ => throw new InvalidOperationException($"Unsupported agent tool: {toolName}")
            };
        }
        catch (AgentWorkspacePolicyDeniedException ex)
        {
            // A structured refusal, not a crash (r23 3.2): the step
            // completes normally with the denial in the transcript so the
            // model sees it and can route around it, instead of aborting
            // the whole step the way a containment or not-found failure does.
            return new AgentToolResult
            {
                Tool = normalized,
                Arguments = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase),
                ResultSummary = ex.Message,
                NormalizedOutcome = AgentToolOutcomeNormalizer.Normalize(normalized,
                    new AgentToolOutcomeEvidence(AgentToolOutcomeSignal.PolicyBlocked,
                        Detail: "Workspace policy or the configured read budget refused the operation."))
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result(normalized, arguments, "The operation was cancelled by the caller.", AgentToolOutcomeSignal.Cancelled);
        }
        catch (FileNotFoundException ex)
        {
            return Result(normalized, arguments, ex.Message, AgentToolOutcomeSignal.Unavailable);
        }
        catch (DirectoryNotFoundException ex)
        {
            return Result(normalized, arguments, ex.Message, AgentToolOutcomeSignal.Unavailable);
        }
        catch (InvalidOperationException ex)
        {
            return Result(normalized, arguments, ex.Message, AgentToolOutcomeSignal.PolicyBlocked);
        }
        catch (Exception ex)
        {
            return Result(normalized, arguments, ex.Message, AgentToolOutcomeSignal.Failed);
        }

        var summary = result is GitDiffInspectionResult inspection
            ? inspection.Summary
            : Summarize(result, normalized);
        var signal = result switch
        {
            GitDiffInspectionResult gitDiff => gitDiff.Signal,
            ICollection { Count: 0 } => AgentToolOutcomeSignal.Empty,
            AgentFileReadResult { Changed: false } when normalized is "apply_draft_patch" or "edit_file" or "create_file"
                => AgentToolOutcomeSignal.NoEffect,
            _ => AgentToolOutcomeSignal.Completed
        };

        return new AgentToolResult
        {
            Tool = normalized,
            Arguments = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase),
            ResultSummary = summary,
            Source = BuildSource(normalized, arguments),
            NormalizedOutcome = AgentToolOutcomeNormalizer.Normalize(normalized,
                new AgentToolOutcomeEvidence(signal, Detail: signal == AgentToolOutcomeSignal.Empty
                    ? "The operation completed with a valid empty result."
                    : "The operation completed using structured executor evidence."))
        };
    }

    private static AgentToolResult Result(
        string tool,
        Dictionary<string, object?> arguments,
        string summary,
        AgentToolOutcomeSignal signal) => new()
        {
            Tool = tool,
            Arguments = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase),
            ResultSummary = Summarize(summary, tool),
            NormalizedOutcome = AgentToolOutcomeNormalizer.Normalize(tool,
                new AgentToolOutcomeEvidence(signal, Detail: signal switch
                {
                    AgentToolOutcomeSignal.Unavailable => "The requested executor, dependency, or target was proven unavailable.",
                    AgentToolOutcomeSignal.PolicyBlocked => "A deterministic validation or policy guard refused the operation.",
                    AgentToolOutcomeSignal.Failed => "The operation was attempted and failed.",
                    AgentToolOutcomeSignal.Cancelled => "The caller cancelled the operation.",
                    _ => "The retained executor evidence could not establish a stronger outcome."
                }))
        };

    private static SourceReference? BuildSource(string normalizedTool, Dictionary<string, object?> arguments)
    {
        var relativePath = Arg(arguments, "relative_path", "path");
        return normalizedTool switch
        {
            "read_file" or "summarize_file" or "draft_patch" or "apply_draft_patch" or "edit_file" or "create_file"
                when !string.IsNullOrWhiteSpace(relativePath) =>
                new SourceReference(ProvenanceKind.Workspace, relativePath, Locator: relativePath),
            _ => null
        };
    }

    /// <summary>Structured outcome of a run_command execution, used both for the model-visible summary text and for lesson-store capture (see AgentService.RecordLessonEvidenceForToolAsync).</summary>
    private sealed record CommandExecutionResult(string Summary, int? ExitCode, bool TimedOut);

    private static async Task<CommandExecutionResult> RunCommandAsync(AgentWorkspaceOptions options, string command, CancellationToken ct)
    {
        var root = AgentWorkspaceTools.ResolveWorkspaceRoot(options.WorkspaceRoot);
        var recipe = WorkspaceCommandRecipes.TryMatch(command, root)
            ?? throw new InvalidOperationException($"'{command}' is not one of the fixed, safe executable template families, or its argument failed validation.");

        var psi = new ProcessStartInfo
        {
            FileName = recipe.FileName,
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in recipe.Args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start '{command}'.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            return new CommandExecutionResult(
                $"Command '{command}' timed out after 5 minutes and was terminated.", ExitCode: null, TimedOut: true);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        // The model needs to see the actual compiler/test error to fix it,
        // so keep a generous tail instead of the old unbounded dump (which
        // then just got hard-truncated mid-line by Summarize downstream).
        var summary = $"Exit code {process.ExitCode}\n\nstdout:\n{LastLines(stdout, 200)}\n\nstderr:\n{LastLines(stderr, 200)}";
        return new CommandExecutionResult(summary, process.ExitCode, TimedOut: false);
    }

    private static string LastLines(string text, int maxLines)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return trimmed;

        var lines = trimmed.Split('\n');
        if (lines.Length <= maxLines) return trimmed;

        var omitted = lines.Length - maxLines;
        return $"[{omitted} earlier line(s) omitted]\n" + string.Join('\n', lines[^maxLines..]);
    }

    private sealed record GitDiffInspectionResult(string Summary, AgentToolOutcomeSignal Signal);

    private static async Task<GitDiffInspectionResult> InspectGitDiffAsync(AgentWorkspaceOptions options, CancellationToken ct)
    {
        var root = AgentWorkspaceTools.ResolveWorkspaceRoot(options.WorkspaceRoot);
        var git = Path.Combine(root, ".git");
        if (!Directory.Exists(git))
            return new GitDiffInspectionResult("Workspace is not a Git repository.", AgentToolOutcomeSignal.Unavailable);

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("status");
        psi.ArgumentList.Add("--short");
        using var process = Process.Start(psi);
        if (process is null)
            return new GitDiffInspectionResult("Could not start git status.", AgentToolOutcomeSignal.Failed);

        // Read stdout/stderr concurrently with waiting: a large working tree
        // can produce more output than the OS pipe buffer holds, and reading
        // only after WaitForExit deadlocks against git blocking on a full
        // pipe, which used to surface as a false "timed out" result
        // (docs/review/01-code-audit.md P2-4).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            return new GitDiffInspectionResult("git status timed out.", AgentToolOutcomeSignal.TimedOut);
        }

        var output = await stdoutTask;
        var error = await stderrTask;
        if (process.ExitCode != 0)
            return new GitDiffInspectionResult(
                string.IsNullOrWhiteSpace(error) ? "git status failed." : error.Trim(),
                AgentToolOutcomeSignal.Failed);
        return string.IsNullOrWhiteSpace(output)
            ? new GitDiffInspectionResult("No working tree changes.", AgentToolOutcomeSignal.Empty)
            : new GitDiffInspectionResult(output.Trim(), AgentToolOutcomeSignal.Completed);
    }

    internal static string Arg(Dictionary<string, object?> args, params string[] names)
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

    private static string? ArgOrNull(Dictionary<string, object?> args, params string[] names)
    {
        var value = Arg(args, names);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int ArgInt(Dictionary<string, object?> args, params string[] names) =>
        ArgIntOrNull(args, names) ?? 0;

    private static int? ArgIntOrNull(Dictionary<string, object?> args, params string[] names)
    {
        foreach (var name in names)
        {
            if (!args.TryGetValue(name, out var value) || value is null)
                continue;
            if (value is JsonElement { ValueKind: JsonValueKind.Number } element && element.TryGetInt32(out var i))
                return i;
            if (value is int direct)
                return direct;
            if (value is string text && int.TryParse(text, out var parsed))
                return parsed;
        }

        return null;
    }

    private static bool ArgBool(Dictionary<string, object?> args, params string[] names)
    {
        foreach (var name in names)
        {
            if (!args.TryGetValue(name, out var value) || value is null)
                continue;
            if (value is JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } element)
                return element.GetBoolean();
            if (value is bool direct)
                return direct;
            if (value is string text && bool.TryParse(text, out var parsed))
                return parsed;
        }

        return false;
    }

    // Content-heavy read tools deserve a much larger budget than a directory
    // listing or a run_command exit summary; a blanket 4000-char JSON slice
    // used to cut read_file results off mid-content after only a few dozen
    // lines.
    private static readonly HashSet<string> ContentHeavyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file", "summarize_file", "search_files", "inspect_git_diff", "run_command"
    };

    private static string Summarize(object result, string normalizedTool)
    {
        var json = JsonSerializer.Serialize(result, AgentJson.Options);
        var cap = ContentHeavyTools.Contains(normalizedTool) ? 12000 : 4000;
        if (json.Length <= cap)
            return json;

        if (result is AgentFileReadResult fileRead)
            return SummarizeTruncatedFileRead(fileRead, cap);

        // Says what to do about it, not just that it happened. A real run read
        // this as "the tool cannot return the entire file content in one go"
        // and abandoned the file, when reading it in slices was available all
        // along.
        var advice = normalizedTool switch
        {
            "read_file" => " Read it in slices: call read_file again with line_offset and line_limit"
                + " (for example line_offset=0, line_limit=400, then line_offset=400).",
            "search_files" => " Narrow the query, or use glob_files to find candidate files first.",
            _ => string.Empty
        };
        return SummarizeTruncatedResult(result is string text ? text : json, cap, advice);
    }

    private static string NormalizeToolForLog(string toolName) =>
        string.IsNullOrWhiteSpace(toolName) ? "Unknown" : toolName.Trim()[..Math.Min(toolName.Trim().Length, 80)];

    private static string FormatArgumentKeys(Dictionary<string, object?> arguments) =>
        arguments.Count == 0
            ? "none"
            : string.Join(',', arguments.Keys
                .Select(key => key.Trim().ToLowerInvariant())
                .Where(key => key.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal));

    private static string SummarizeTruncatedResult(string text, int cap, string advice)
    {
        // Keep every capped result valid JSON. A raw prefix used to be appended
        // to the serialized value, so large command/MCP/list results could no
        // longer be parsed and the planner could mistake the cut for a complete
        // result. The preview is intentionally a string, so it remains valid
        // even when the source itself was structured JSON.
        var keep = Math.Min(text.Length, Math.Max(128, cap - 900));
        while (keep >= 128)
        {
            var preview = text[..keep];
            var clipped = JsonSerializer.Serialize(new
            {
                truncated = true,
                result_preview = preview,
                omitted_characters = text.Length - keep,
                continuation_hint = $"The executor output cap clipped this result. Narrow the request or repeat the tool with a more focused scope.{advice}"
            }, AgentJson.Options);
            if (clipped.Length <= cap)
                return clipped;
            keep -= Math.Max(64, clipped.Length - cap);
        }

        return JsonSerializer.Serialize(new
        {
            truncated = true,
            result_preview = string.Empty,
            omitted_characters = text.Length,
            continuation_hint = $"The executor output cap clipped this result. Narrow the request or repeat the tool with a more focused scope.{advice}"
        }, AgentJson.Options);
    }

    private static string SummarizeTruncatedFileRead(AgentFileReadResult read, int cap)
    {
        // Keep the result parseable. The old raw JSON prefix could end inside
        // the content string, which made a complete read look like a one-line
        // or otherwise unusable result to the planner.
        var content = read.Content;
        var lineStart = read.LineOffset ?? 0;
        var keep = Math.Min(content.Length, Math.Max(256, cap - 1200));
        while (keep > 256)
        {
            var candidate = content[..keep];
            var lastNewline = candidate.LastIndexOf('\n');
            if (lastNewline >= 0)
            {
                candidate = candidate[..(lastNewline + 1)].TrimEnd('\n');
                keep = candidate.Length;
            }

            var linesShown = candidate.Length == 0 ? 0 : candidate.Count(c => c == '\n') + 1;
            var clipped = new
            {
                relative_path = read.RelativePath,
                content = candidate,
                truncated = true,
                total_lines = read.TotalLines,
                line_offset = read.LineOffset,
                line_count = linesShown,
                continuation_hint = $"The executor output cap clipped this result. Call read_file again with line_offset={lineStart + linesShown} and line_limit=400 to continue."
            };
            var clippedJson = JsonSerializer.Serialize(clipped, AgentJson.Options);
            if (clippedJson.Length <= cap)
                return clippedJson;
            keep -= Math.Max(128, clippedJson.Length - cap);
        }

        return JsonSerializer.Serialize(new
        {
            relative_path = read.RelativePath,
            content = string.Empty,
            truncated = true,
            continuation_hint = "The executor output cap clipped this result. Call read_file with line_offset=0 and line_limit=400 to continue."
        }, AgentJson.Options);
    }

    private static string Normalize(string toolName) => toolName.Trim().Replace('-', '_').ToLowerInvariant();
}
