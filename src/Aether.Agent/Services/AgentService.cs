using System.Text;
using System.Text.Json;
using Aether.Agent.Models;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Agent.Services;

public sealed class AgentService : IAgentService
{
    private const string AgentSystemPrompt = """
        You are Aether Agent, a local-first semi-autonomous task assistant.
        Use explicit task state and retrieved context. Be practical and concise.
        You may run several steps in a row without the user clicking anything;
        keep working until the goal is done, you need to ask the user
        something, or an action needs approval.
        You may request supported local tools: list_files (optional
        subdirectory, max_depth), search_files (optional regex, context_lines),
        glob_files (pattern), read_file (optional line_offset, line_limit),
        summarize_file, inspect_git_diff, draft_patch, apply_draft_patch,
        edit_file, create_file, set_plan, and run_command. Read-only tools
        (list_files, search_files, glob_files, read_file, summarize_file,
        inspect_git_diff, set_plan) execute immediately. Prefer edit_file
        (relative_path, old_string, new_string) for changing part of an
        existing file over draft_patch/apply_draft_patch, which rewrite the
        whole file; old_string must match the file's current content exactly
        once. Use create_file (relative_path, content) only for new files; it
        refuses to overwrite an existing one. edit_file, create_file,
        apply_draft_patch, and run_command always require approval. Use
        set_plan (steps: array of {description, status: pending|in_progress|done})
        to keep a visible checklist for multi-step goals; it replaces the
        whole plan each time. run_command only accepts one of the workspace's
        own pre-declared safe recipes (for example "dotnet build" or "dotnet
        test") passed verbatim as the "command" argument; it cannot run
        arbitrary shell text. Network access, installs, commits, pushes,
        uploads, downloads, and history rewrites remain blocked.
        A Lessons section in your context lists things already learned about
        this workspace or machine, each with a confidence and evidence
        count; weigh higher-confidence lessons more, but they never change
        what you are allowed to do. If you notice something worth
        remembering for next time that is not already covered there, say so
        with a [LESSON: <short observation>] marker anywhere in
        thought_summary or user_message; it will not be shown to the user
        verbatim.
        Return only valid JSON matching:
        {
          "thought_summary": "brief user-visible reasoning summary",
          "current_step": "current step",
          "next_action": {
            "type": "tool | ask_user | final",
            "tool_name": null,
            "arguments": {},
            "requires_approval": false,
            "risk_level": "none | low | medium | high"
          },
          "state_update": {
            "completed": [],
            "pending": [],
            "new_facts": [],
            "blockers": []
          },
          "user_message": "message for the user"
        }
        """;

    /// <summary>
    /// Native tool declarations for the fixed workspace tool set, offered on
    /// every step via <see cref="LlmChatOptions.Tools"/>. MCP-bridged
    /// (<c>mcp:</c>) tools are not declared natively; a model reaches those
    /// only through the JSON "next_action" protocol described in the system
    /// prompt, same as before this feature existed.
    /// </summary>
    private static readonly IReadOnlyList<LlmToolDefinition> FixedToolDefinitions = BuildFixedToolDefinitions();

    private static List<LlmToolDefinition> BuildFixedToolDefinitions()
    {
        static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

        return
        [
            new("list_files", "List workspace files, optionally scoped to a subdirectory and depth.",
                Schema("""{"type":"object","properties":{"subdirectory":{"type":"string"},"max_depth":{"type":"integer"}}}""")),
            new("search_files", "Search workspace files for a literal string or, with regex=true, a regular expression.",
                Schema("""{"type":"object","properties":{"query":{"type":"string"},"regex":{"type":"boolean"},"context_lines":{"type":"integer"}},"required":["query"]}""")),
            new("glob_files", "Match workspace files against a glob pattern (supports * and **).",
                Schema("""{"type":"object","properties":{"pattern":{"type":"string"}},"required":["pattern"]}""")),
            new("read_file", "Read a workspace file, optionally a bounded line range via line_offset/line_limit.",
                Schema("""{"type":"object","properties":{"relative_path":{"type":"string"},"line_offset":{"type":"integer"},"line_limit":{"type":"integer"}},"required":["relative_path"]}""")),
            new("summarize_file", "Summarize a workspace file's readable content.",
                Schema("""{"type":"object","properties":{"relative_path":{"type":"string"}},"required":["relative_path"]}""")),
            new("inspect_git_diff", "Show git status for the workspace root.",
                Schema("""{"type":"object","properties":{}}""")),
            new("draft_patch", "Draft a whole-file patch proposal for later review (does not write anything).",
                Schema("""{"type":"object","properties":{"relative_path":{"type":"string"},"rationale":{"type":"string"},"proposed_content":{"type":"string"}},"required":["relative_path","proposed_content"]}""")),
            new("apply_draft_patch", "Apply a whole-file rewrite. Requires user approval.",
                Schema("""{"type":"object","properties":{"relative_path":{"type":"string"},"proposed_content":{"type":"string"}},"required":["relative_path","proposed_content"]}""")),
            new("edit_file", "Replace a unique old_string with new_string in an existing file. Requires user approval.",
                Schema("""{"type":"object","properties":{"relative_path":{"type":"string"},"old_string":{"type":"string"},"new_string":{"type":"string"}},"required":["relative_path","old_string","new_string"]}""")),
            new("create_file", "Create a new file; refuses to overwrite an existing one. Requires user approval.",
                Schema("""{"type":"object","properties":{"relative_path":{"type":"string"},"content":{"type":"string"}},"required":["relative_path","content"]}""")),
            new("set_plan", "Replace the task's visible plan checklist. Executes immediately, never requires approval.",
                Schema("""{"type":"object","properties":{"steps":{"type":"array","items":{"type":"object","properties":{"description":{"type":"string"},"status":{"type":"string","enum":["pending","in_progress","done"]}},"required":["description"]}}},"required":["steps"]}""")),
            new("run_command", "Run one of the workspace's own pre-declared safe recipes (e.g. \"dotnet build\"). Requires user approval.",
                Schema("""{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}"""))
        ];
    }

    private readonly IAgentTaskStateStore _store;
    private readonly IAgentContextBuilder _contextBuilder;
    private readonly IAgentSafetyGate _safetyGate;
    private readonly IAgentToolExecutor _toolExecutor;
    private readonly ILlmService _llm;
    private readonly ITraceStore? _traces;
    private readonly IWorkspaceManifestStore? _manifests;
    private readonly ISettingsService? _settings;
    private readonly ILessonStore? _lessons;
    private readonly IAgentWorkspaceTools? _workspaceTools;

    public AgentService(
        IAgentTaskStateStore store,
        IAgentContextBuilder contextBuilder,
        IAgentSafetyGate safetyGate,
        IAgentToolExecutor toolExecutor,
        ILlmService llm,
        ITraceStore? traces = null,
        IWorkspaceManifestStore? manifests = null,
        ISettingsService? settings = null,
        ILessonStore? lessons = null,
        IAgentWorkspaceTools? workspaceTools = null)
    {
        _store = store;
        _contextBuilder = contextBuilder;
        _safetyGate = safetyGate;
        _toolExecutor = toolExecutor;
        _llm = llm;
        _traces = traces;
        _manifests = manifests;
        _lessons = lessons;
        _settings = settings;
        _workspaceTools = workspaceTools;
    }

    public async Task<AgentTaskState> CreateTaskAsync(
        string goal,
        AgentWorkspaceOptions options,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(goal))
            throw new InvalidOperationException("Agent goal is required.");

        AgentWorkspaceTools.ResolveWorkspaceRoot(options.WorkspaceRoot);
        var state = new AgentTaskState
        {
            Goal = goal.Trim(),
            Status = AgentTaskStatus.New,
            ActiveStep = "Build an initial plan from the goal and retrieved context.",
            Constraints =
            [
                "local-first",
                "do not write files without approval",
                "do not run commands without approval",
                "use retrieved context before inference",
                "keep task state explicit"
            ],
            PendingSteps = ["Inspect workspace context", "Build plan", "Report next action"],
            Summary = "Task created. No tool actions have run yet."
        };

        await _store.SaveAsync(state, ct);
        await _store.AppendLogAsync(state.TaskId, $"created task: {state.Goal}", ct);
        return state;
    }

    public async Task<AgentStepResult> RunStepAsync(
        string taskId,
        AgentWorkspaceOptions options,
        CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(taskId, ct)
            ?? throw new InvalidOperationException("Agent task was not found.");
        if (state.Status is AgentTaskStatus.Complete or AgentTaskStatus.Failed)
            throw new InvalidOperationException("Agent task is already finished.");

        state.Status = AgentTaskStatus.Running;
        await _store.SaveAsync(state, ct);

        var context = await _contextBuilder.BuildAsync(state, options, ct);
        // Tracked across the whole task so a successful completion can
        // confirm (bump evidence on) every lesson that actually informed
        // it, not just the ones from the final step.
        foreach (var lesson in context.Lessons)
        {
            if (lesson.Locator is { Length: > 0 } id && !state.InjectedLessonIds.Contains(id))
                state.InjectedLessonIds.Add(id);
        }
        var prompt = BuildPrompt(context);
        var modelId = options.ModelId;
        var raw = new StringBuilder();
        IReadOnlyList<LlmToolCallRequest>? nativeToolCalls = null;
        try
        {
            await foreach (var evt in _llm.StreamChatAsync(
                modelId,
                [new ChatMessage("user", prompt)],
                new LlmChatOptions { SystemPrompt = AgentSystemPrompt, Temperature = 0.2, Tools = FixedToolDefinitions },
                ct))
            {
                if (!string.IsNullOrEmpty(evt.ContentDelta))
                    raw.Append(evt.ContentDelta);
                if (evt.ToolCalls is { Count: > 0 })
                    nativeToolCalls = evt.ToolCalls;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The model call itself failed (provider unreachable, etc.), not
            // just an unparseable reply; state was already saved as Running
            // above, so this exception path must not leave it there.
            state.Status = AgentTaskStatus.WaitingForUser;
            await _store.SaveAsync(state, ct);
            throw;
        }

        // A model/provider that supports native tool calling ends the
        // response in structured tool_calls instead of prose; only then do
        // we skip the "return JSON matching this schema" text protocol.
        // Anything else (no tool calls, or a provider/model that ignores the
        // declared tools) falls straight back to parsing raw as JSON exactly
        // as before, so non-tool-calling local models keep working unchanged.
        AgentPlannerResponse response;
        var parseFailed = false;
        try
        {
            response = nativeToolCalls is { Count: > 0 }
                ? BuildResponseFromToolCall(nativeToolCalls, raw.ToString())
                : ParseResponse(raw.ToString());
            state.ConsecutiveStepErrors = 0;
        }
        catch (JsonException)
        {
            parseFailed = true;
            state.ConsecutiveStepErrors++;
            state.TotalStepErrors++;
            response = new AgentPlannerResponse
            {
                ThoughtSummary = "The model's response could not be parsed as valid JSON.",
                CurrentStep = state.ActiveStep,
                NextAction = new AgentNextAction
                {
                    Type = AgentActionKind.AskUser,
                    RequiresApproval = false,
                    RiskLevel = AgentRiskLevel.None
                },
                UserMessage = "The agent could not parse the model's response."
            };
        }
        ApplyResponse(state, response);
        await RecordStatedLessonsAsync(state, options, response, ct);
        var nextTool = response.NextAction.ToolName ?? string.Empty;
        AgentToolResult? executedToolResult = null;
        if (response.NextAction.Type == AgentActionKind.Tool)
        {
            AgentToolPolicyDecision decision;
            if (string.Equals(nextTool, "run_command", StringComparison.OrdinalIgnoreCase))
            {
                var requestedCommand = AgentToolExecutor.Arg(response.NextAction.Arguments, "command");
                var manifest = _manifests is null ? null : await _manifests.LoadAsync(options.WorkspaceRoot, ct);
                decision = _safetyGate.EvaluateCommand(requestedCommand, manifest?.AllowedCommands ?? []);
                if (decision.Disposition == AgentToolDisposition.RequiresApproval
                    && state.RememberedCommandApprovals.Any(c => string.Equals(c, requestedCommand.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    // Same exact command string was approved earlier in this
                    // task; skip asking again. A different command, even in
                    // the same template family, still requires a fresh
                    // approval - this never widens to "the family is now
                    // trusted".
                    decision = decision with { Disposition = AgentToolDisposition.Allowed, Reason = "Command was already approved once in this task." };
                }
            }
            else
            {
                decision = _safetyGate.Evaluate(nextTool, response.NextAction.RequiresApproval);
            }
            response.NextAction.RiskLevel = decision.RiskLevel;
            response.NextAction.RequiresApproval = decision.Disposition != AgentToolDisposition.Allowed;
            if (decision.Disposition == AgentToolDisposition.RequiresApproval)
            {
                state.Status = AgentTaskStatus.WaitingForUser;
                if (_toolExecutor.CanExecute(nextTool))
                {
                    state.PendingToolAction = new AgentPendingToolAction
                    {
                        ToolName = nextTool,
                        Arguments = response.NextAction.Arguments,
                        RiskLevel = decision.RiskLevel,
                        Reason = decision.Reason
                    };
                }
            }
            if (decision.Disposition == AgentToolDisposition.Blocked)
                state.Status = AgentTaskStatus.Blocked;

            state.ToolResults.Add(new AgentToolResult
            {
                Tool = "safety_gate",
                Arguments =
                {
                    ["tool_name"] = nextTool,
                    ["disposition"] = decision.Disposition.ToString(),
                    ["risk_level"] = decision.RiskLevel.ToString()
                },
                ResultSummary = decision.Reason
            });

            if (decision.Disposition == AgentToolDisposition.Allowed)
            {
                if (string.Equals(nextTool, "set_plan", StringComparison.OrdinalIgnoreCase))
                {
                    var planResult = ApplySetPlan(state, response.NextAction.Arguments);
                    state.ToolResults.Add(planResult);
                    executedToolResult = planResult;
                    state.Status = AgentTaskStatus.Running;
                    response.UserMessage = $"Updated plan ({state.Plan.Count} step(s)).";
                }
                else if (_toolExecutor.CanExecute(nextTool))
                {
                    AgentToolResult toolResult;
                    try
                    {
                        toolResult = await _toolExecutor.ExecuteAsync(nextTool, response.NextAction.Arguments, options, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        await RecordLessonEvidenceForToolAsync(state, options, nextTool, response.NextAction.Arguments, ex.Message, success: false, ct);
                        // State was already saved as Running at the top of
                        // this step; an unhandled tool exception must not
                        // leave the task stranded there with no path back.
                        state.Status = AgentTaskStatus.WaitingForUser;
                        await _store.SaveAsync(state, ct);
                        throw;
                    }

                    state.ToolResults.Add(toolResult);
                    executedToolResult = toolResult;
                    state.Status = AgentTaskStatus.Running;
                    response.UserMessage = $"Executed {nextTool}.";
                    await RecordLessonEvidenceForToolAsync(state, options, nextTool, response.NextAction.Arguments, toolResult.ResultSummary, success: true, ct, toolResult.ExitCode, toolResult.TimedOut);
                }
                else
                {
                    state.Status = AgentTaskStatus.Blocked;
                    state.ToolResults.Add(new AgentToolResult
                    {
                        Tool = nextTool,
                        Arguments = response.NextAction.Arguments,
                        ResultSummary = "Supported policy allowed the tool, but no local executor is registered for it."
                    });
                }
            }
        }

        if (response.NextAction.Type == AgentActionKind.AskUser)
            state.Status = AgentTaskStatus.WaitingForUser;
        if (response.NextAction.Type == AgentActionKind.Final)
            state.Status = AgentTaskStatus.Complete;

        if (parseFailed && state.ConsecutiveStepErrors >= 3)
        {
            state.Status = AgentTaskStatus.Failed;
            state.Decisions.Add(new AgentDecision(
                "Task failed", "model responses unparseable 3 times in a row", DateTime.UtcNow));
            response.UserMessage = "The agent could not parse the model's response 3 times in a row and has stopped.";
        }

        state.ActiveStep = string.IsNullOrWhiteSpace(response.CurrentStep)
            ? state.ActiveStep
            : response.CurrentStep;
        state.Summary = BuildSummary(state, response);

        var logEntry = string.IsNullOrWhiteSpace(response.UserMessage)
            ? response.ThoughtSummary
            : response.UserMessage;
        await _store.AppendLogAsync(taskId, logEntry, ct);

        state.StepCount++;
        if (state.StepCount == 1)
        {
            // Full context pack only on the first step of a task; later steps
            // trace a small delta instead of re-serializing the whole pack
            // every time, which used to make agent.trace.jsonl grow
            // quadratically with task length.
            await _store.AppendTraceAsync(taskId, new
            {
                task_id = taskId,
                step = state.StepCount,
                state.Status,
                context,
                response,
                logged_at = DateTime.UtcNow
            }, ct);
        }
        else
        {
            await _store.AppendTraceAsync(taskId, new
            {
                task_id = taskId,
                step = state.StepCount,
                state.Status,
                action = response.NextAction.Type.ToString(),
                tool = response.NextAction.ToolName,
                thought_summary = response.ThoughtSummary,
                message = logEntry,
                logged_at = DateTime.UtcNow
            }, ct);
        }

        await _store.AppendTranscriptEntryAsync(taskId, new AgentTranscriptEntry(
            state.StepCount,
            "assistant",
            null,
            string.IsNullOrWhiteSpace(response.ThoughtSummary) ? logEntry : response.ThoughtSummary,
            DateTime.UtcNow), ct);
        if (executedToolResult is not null)
        {
            await _store.AppendTranscriptEntryAsync(taskId, new AgentTranscriptEntry(
                state.StepCount,
                "tool",
                executedToolResult.Tool,
                executedToolResult.ResultSummary,
                DateTime.UtcNow), ct);
        }

        await _store.SaveAsync(state, ct);

        if (_traces is not null)
        {
            // The task-directory JSONL above is the reviewable workspace artifact
            // (schema-validated); the unified store row indexes the step for the
            // shared trace timeline.
            try
            {
                await _traces.AppendAsync(new TraceRecord
                {
                    Kind = TraceKind.Agent,
                    SourceId = taskId,
                    Operation = "agent-step",
                    DetailJson = JsonSerializer.Serialize(new
                    {
                        status = state.Status.ToString(),
                        action = response.NextAction.Type.ToString(),
                        tool = response.NextAction.ToolName,
                        step = state.ActiveStep,
                        message = logEntry
                    })
                }, ct);
            }
            catch
            {
                // The JSONL artifact above already recorded the step.
            }
        }

        await RecordTaskTerminalLessonAsync(state, options, ct);

        return new AgentStepResult(state, context, response, logEntry);
    }

    public async Task<AgentStepResult> RunAsync(
        string taskId,
        AgentWorkspaceOptions options,
        Action<AgentStepResult>? onStep = null,
        CancellationToken ct = default)
    {
        var maxSteps = Math.Max(_settings?.Settings.Agent.MaxAutoSteps ?? 20, 1);
        AgentStepResult result;
        var steps = 0;
        do
        {
            ct.ThrowIfCancellationRequested();
            result = await RunStepAsync(taskId, options, ct);
            steps++;
            onStep?.Invoke(result);
        }
        // Any status other than Running means the step ended in a final
        // answer, a question for the user, or something gated/blocked that
        // needs a human decision; the loop only continues while the model
        // is still working through allowed, read-only tool calls on its own.
        while (steps < maxSteps && result.State.Status == AgentTaskStatus.Running);

        if (steps >= maxSteps && result.State.Status == AgentTaskStatus.Running)
        {
            // Hitting the step cap while still Running must not look like an
            // active task to the workbench; hand it back to the user
            // explicitly instead of leaving it silently stalled.
            var note = $"step budget exhausted after {steps} step(s)";
            result.State.Status = AgentTaskStatus.WaitingForUser;
            await _store.AppendLogAsync(taskId, note, ct);
            await _store.AppendTranscriptEntryAsync(taskId, new AgentTranscriptEntry(
                result.State.StepCount, "assistant", null, note, DateTime.UtcNow), ct);
            await _store.SaveAsync(result.State, ct);
        }

        return result;
    }

    public Task<IReadOnlyList<AgentTaskListItem>> LoadRecentTasksAsync(CancellationToken ct = default) =>
        _store.ListRecentAsync(25, ct);

    public async Task AppendApprovalAsync(string taskId, string action, bool approved, AgentWorkspaceOptions? options = null, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(taskId, ct)
            ?? throw new InvalidOperationException("Agent task was not found.");
        state.ApprovalHistory.Add(new AgentApprovalRecord(action, approved, DateTime.UtcNow));
        if (approved && state.PendingToolAction is not null && options is not null)
        {
            var pending = state.PendingToolAction;

            // Mutating tools get a pre-image captured before they run, so a
            // later revert can restore exactly what was there
            // (r6 01-first-five-minutes.md 1.8). apply_draft_patch's own
            // manual review flow (AgentPatchReviewService.ApplyAsync)
            // already does this for the draft-patch queue; this covers the
            // direct-approval path for edit_file/create_file/apply_draft_patch.
            var mutatesFile = pending.ToolName is "edit_file" or "create_file" or "apply_draft_patch";
            var relativePath = mutatesFile ? AgentToolExecutor.Arg(pending.Arguments, "relative_path", "path") : string.Empty;
            string? preImage = null;
            if (mutatesFile && _workspaceTools is not null && !string.IsNullOrWhiteSpace(relativePath))
            {
                try { preImage = await _workspaceTools.ReadFileForRevertAsync(options, relativePath, ct); }
                catch { mutatesFile = false; /* best effort; skip the revert record, still execute the tool */ }
            }
            else
            {
                mutatesFile = false;
            }

            AgentToolResult result;
            try
            {
                result = await _toolExecutor.ExecuteAsync(pending.ToolName, pending.Arguments, options, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await RecordLessonEvidenceForToolAsync(state, options, pending.ToolName, pending.Arguments, ex.Message, success: false, ct);
                throw;
            }

            state.ToolResults.Add(result);
            RememberCommandApprovalIfApplicable(state, pending);
            await RecordLessonEvidenceForToolAsync(state, options, pending.ToolName, pending.Arguments, result.ResultSummary, success: true, ct, result.ExitCode, result.TimedOut);
            await RecordApprovalApprovedCounterEvidenceAsync(state, options, pending.ToolName, ct);
            // The approved tool's result is what the model most needs to see
            // next (it is why the step paused), so it belongs in the
            // transcript alongside every other executed tool result, not
            // just in ToolResults' last-five window.
            await _store.AppendTranscriptEntryAsync(taskId, new AgentTranscriptEntry(
                state.StepCount, "tool", result.Tool, result.ResultSummary, DateTime.UtcNow), ct);

            if (mutatesFile && _workspaceTools is not null)
            {
                var postContent = await _workspaceTools.ReadFileForRevertAsync(options, relativePath, ct) ?? string.Empty;
                state.DraftPatches.Add(new AgentDraftPatch
                {
                    RelativePath = relativePath,
                    Rationale = $"Applied via {pending.ToolName}.",
                    ProposedContent = postContent,
                    Status = AgentDraftPatchStatus.Applied,
                    ApprovedAt = DateTime.UtcNow,
                    ApprovedBy = "User",
                    PreImageContent = preImage,
                    PreImageExisted = preImage is not null,
                    AppliedContent = postContent
                });
            }

            state.PendingToolAction = null;
            state.Status = AgentTaskStatus.Running;
        }
        else
        {
            if (!approved && state.PendingToolAction is not null)
                await RecordApprovalRejectionLessonAsync(state, options, state.PendingToolAction.ToolName, ct);

            state.Status = approved ? AgentTaskStatus.Running : AgentTaskStatus.WaitingForUser;
            if (!approved)
                state.PendingToolAction = null;
        }
        await _store.SaveAsync(state, ct);
        await _store.AppendLogAsync(taskId, $"approval recorded: {action} approved={approved}", ct);
    }

    public async Task AppendUserReplyAsync(string taskId, string reply, CancellationToken ct = default)
    {
        var trimmed = reply?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A reply cannot be empty.");

        var state = await _store.LoadAsync(taskId, ct)
            ?? throw new InvalidOperationException("Agent task was not found.");
        if (state.Status != AgentTaskStatus.WaitingForUser)
            throw new InvalidOperationException("This task is not waiting for a reply.");
        if (state.PendingToolAction is not null)
            throw new InvalidOperationException("A tool approval is pending; approve or reject it instead of replying.");

        await _store.AppendTranscriptEntryAsync(taskId, new AgentTranscriptEntry(
            state.StepCount, "user", null, trimmed, DateTime.UtcNow), ct);
        state.Status = AgentTaskStatus.Running;
        await _store.SaveAsync(state, ct);
        await _store.AppendLogAsync(taskId, "user reply recorded", ct);
    }

    /// <summary>
    /// Deterministic, evidence-backed capture for the lesson store: a
    /// command's exit status, or a patch/edit/create tool's success or
    /// failure. Best-effort - a lesson-capture failure must never break the
    /// agent step or the approval flow. Approval-rejection evidence is
    /// recorded separately by <see cref="RecordApprovalRejectionLessonAsync"/>,
    /// stated evidence by <see cref="RecordStatedLessonsAsync"/>, and
    /// task-terminal evidence by <see cref="RecordTaskTerminalLessonAsync"/>.
    /// The signature identifies only the subject (command text, or
    /// tool+path); the outcome lives in <see cref="AgentLessonEvidence.Outcome"/>
    /// so that the store's contradiction/reinforcement logic actually has
    /// something to compare against instead of every outcome creating its
    /// own permanently-separate row.
    /// </summary>
    private async Task RecordLessonEvidenceForToolAsync(
        AgentTaskState state,
        AgentWorkspaceOptions options,
        string toolName,
        Dictionary<string, object?> arguments,
        string resultText,
        bool success,
        CancellationToken ct,
        int? exitCode = null,
        bool timedOut = false)
    {
        if (_lessons is null) return;
        try
        {
            var normalized = toolName.Trim().ToLowerInvariant();
            var scopeId = NormalizeWorkspaceScopeId(options.WorkspaceRoot);

            if (normalized == "run_command")
            {
                // A timeout says nothing about whether the command itself
                // works; capturing it as a failure would teach the wrong
                // lesson (docs/review/02-lessons-v2.md L3).
                if (timedOut) return;
                var command = AgentToolExecutor.Arg(arguments, "command").Trim();
                if (command.Length == 0) return;
                var ok = exitCode == 0;
                var errorToken = ok ? string.Empty : ExtractLessonErrorToken(resultText);
                var signature = $"command:{command.ToLowerInvariant()}";
                var claim = ok
                    ? $"'{command}' succeeds in this workspace."
                    : errorToken.Length == 0 || errorToken == "generic"
                        ? $"'{command}' fails in this workspace."
                        : $"'{command}' fails in this workspace with {errorToken}.";
                var guidance = ok
                    ? "Safe to re-run; a prior run in this task succeeded."
                    : "Read the command output before retrying; the same failure recurred.";
                await RecordEvidenceTrackingNewAsync(_lessons, state, new AgentLessonEvidence(
                    AgentLessonScope.Workspace, scopeId, AgentLessonKind.Command, signature, claim, guidance,
                    ok ? AgentLessonOutcome.Worked : AgentLessonOutcome.Failed, state.TaskId), ct);
            }
            else if (normalized is "apply_draft_patch" or "edit_file" or "create_file")
            {
                var path = AgentToolExecutor.Arg(arguments, "relative_path", "path").Trim();
                if (path.Length == 0) return;
                var signature = $"patch:{normalized}:{path.ToLowerInvariant()}";
                var claim = success
                    ? $"{normalized} on {path} applies cleanly."
                    : $"{normalized} on {path} keeps failing: {TruncateForLesson(resultText, 120)}";
                var guidance = success ? string.Empty : "Re-read the file before proposing another edit to this path.";
                await RecordEvidenceTrackingNewAsync(_lessons, state, new AgentLessonEvidence(
                    AgentLessonScope.Workspace, scopeId, AgentLessonKind.Patch, signature, claim, guidance,
                    success ? AgentLessonOutcome.Worked : AgentLessonOutcome.Failed, state.TaskId), ct);
            }
        }
        catch
        {
            // Lesson capture is best-effort; it must never break the agent step.
        }
    }

    private async Task RecordApprovalRejectionLessonAsync(AgentTaskState state, AgentWorkspaceOptions? options, string toolName, CancellationToken ct)
    {
        if (_lessons is null) return;
        try
        {
            var normalized = toolName.Trim().ToLowerInvariant();
            if (normalized.Length == 0) return;
            var scopeId = options is null ? string.Empty : NormalizeWorkspaceScopeId(options.WorkspaceRoot);
            var scope = string.IsNullOrEmpty(scopeId) ? AgentLessonScope.Global : AgentLessonScope.Workspace;
            var signature = $"approval:{normalized}";
            await RecordEvidenceTrackingNewAsync(_lessons, state, new AgentLessonEvidence(
                scope, scopeId, AgentLessonKind.Approval, signature,
                $"The user rejects {normalized} requests in this context.",
                "Avoid proposing this again without a materially different rationale.",
                AgentLessonOutcome.UserRejected, state.TaskId), ct);
        }
        catch
        {
            // Lesson capture is best-effort; it must never break the approval flow.
        }
    }

    /// <summary>
    /// Counter-evidence for a prior rejection lesson: approving a gated
    /// action on the same signature as an earlier rejection contradicts it,
    /// letting the store's normal contradiction/reinforcement logic weaken
    /// or eventually flip that lesson. Routine approvals must not spawn new
    /// "user approves X" rows on their own, so this only ever writes when a
    /// lesson with the signature already exists (see AgentLessonEvidence.CounterOnly).
    /// </summary>
    private async Task RecordApprovalApprovedCounterEvidenceAsync(AgentTaskState state, AgentWorkspaceOptions? options, string toolName, CancellationToken ct)
    {
        if (_lessons is null) return;
        try
        {
            var normalized = toolName.Trim().ToLowerInvariant();
            if (normalized.Length == 0) return;
            var scopeId = options is null ? string.Empty : NormalizeWorkspaceScopeId(options.WorkspaceRoot);
            var scope = string.IsNullOrEmpty(scopeId) ? AgentLessonScope.Global : AgentLessonScope.Workspace;
            var signature = $"approval:{normalized}";
            await RecordEvidenceTrackingNewAsync(_lessons, state, new AgentLessonEvidence(
                scope, scopeId, AgentLessonKind.Approval, signature,
                $"The user approves {normalized} requests in this context.",
                string.Empty,
                AgentLessonOutcome.Worked, state.TaskId, CounterOnly: true), ct);
        }
        catch
        {
            // Lesson capture is best-effort; it must never break the approval flow.
        }
    }

    /// <summary>
    /// The deterministic task-terminal capture deferred from r3
    /// (docs/review/archived/r3/README.md) and specified in
    /// docs/review/02-lessons-v2.md L4. Two signals, both goal-fingerprint
    /// keyed (no LLM): a Worked/Failed lesson on the goal itself, and - the
    /// compounding half of the loop - confirming every lesson that was
    /// actually injected into a task that completed successfully.
    /// </summary>
    private async Task RecordTaskTerminalLessonAsync(AgentTaskState state, AgentWorkspaceOptions options, CancellationToken ct)
    {
        if (_lessons is null) return;
        if (state.Status is not (AgentTaskStatus.Complete or AgentTaskStatus.Failed)) return;
        try
        {
            var scopeId = NormalizeWorkspaceScopeId(options.WorkspaceRoot);
            if (state.Status == AgentTaskStatus.Complete)
            {
                // An uneventful success teaches nothing; only worth a
                // lesson if the task actually recovered from trouble.
                if (TaskHadPriorFailure(state))
                {
                    var signature = $"task:{AgentLessonText.Fingerprint(state.Goal)}";
                    await RecordEvidenceTrackingNewAsync(_lessons, state, new AgentLessonEvidence(
                        AgentLessonScope.Workspace, scopeId, AgentLessonKind.Task, signature,
                        $"Goals like '{TruncateForLesson(state.Goal, 80)}' complete in this workspace.",
                        string.Empty, AgentLessonOutcome.Worked, state.TaskId), ct);
                }

                if (state.InjectedLessonIds.Count > 0)
                    await _lessons.ConfirmAsync(state.InjectedLessonIds, state.TaskId, ct);
            }
            else
            {
                var blocker = state.Decisions.LastOrDefault(d => d.Decision == "Task failed")?.Reason
                    ?? "no specific blocker was recorded.";
                var signature = $"task:{AgentLessonText.Fingerprint(state.Goal)}";
                await RecordEvidenceTrackingNewAsync(_lessons, state, new AgentLessonEvidence(
                    AgentLessonScope.Workspace, scopeId, AgentLessonKind.Task, signature,
                    $"Goals like '{TruncateForLesson(state.Goal, 80)}' have failed in this workspace: {blocker}",
                    "Check the blockers from the failed task before retrying this goal.",
                    AgentLessonOutcome.Failed, state.TaskId), ct);
            }
        }
        catch
        {
            // Lesson capture is best-effort; it must never break the agent step.
        }
    }

    /// <summary>
    /// Wraps every <see cref="ILessonStore.RecordEvidenceAsync"/> call so a
    /// genuinely new lesson (evidence count 1 right after the write - the
    /// store only ever assigns that on insert or on a full contradiction
    /// flip, never on reinforcement or a no-op counter) is tracked on the
    /// task for the "new lessons" review strip (r6 3.3).
    /// </summary>
    private static async Task<AgentLesson> RecordEvidenceTrackingNewAsync(
        ILessonStore lessons, AgentTaskState state, AgentLessonEvidence evidence, CancellationToken ct)
    {
        var lesson = await lessons.RecordEvidenceAsync(evidence, ct);
        if (lesson.EvidenceCount == 1 && !state.NewLessonIds.Contains(lesson.Id))
            state.NewLessonIds.Add(lesson.Id);
        return lesson;
    }

    private static bool TaskHadPriorFailure(AgentTaskState state) =>
        state.TotalStepErrors > 0
        || state.ToolResults.Any(t => string.Equals(t.Tool, "run_command", StringComparison.OrdinalIgnoreCase)
            && (t.TimedOut || (t.ExitCode.HasValue && t.ExitCode != 0)));

    private static readonly System.Text.RegularExpressions.Regex StatedLessonMarkerRegex = new(
        @"\[LESSON:\s*(.+?)\]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500));

    /// <summary>
    /// The only model-authored lesson source: a [LESSON: ...] marker in the
    /// model's own thought/user message, captured at low starting
    /// confidence (see SqliteLessonStore.InitialConfidence) and labelled
    /// AgentLessonKind.Stated so it is never confused with the
    /// deterministic command/patch/approval evidence sources. Stripped from
    /// both fields afterward so raw marker syntax never reaches the user.
    /// </summary>
    private async Task RecordStatedLessonsAsync(AgentTaskState state, AgentWorkspaceOptions options, AgentPlannerResponse response, CancellationToken ct)
    {
        var thoughtMatches = StatedLessonMarkerRegex.Matches(response.ThoughtSummary ?? string.Empty);
        var messageMatches = StatedLessonMarkerRegex.Matches(response.UserMessage ?? string.Empty);
        if (thoughtMatches.Count == 0 && messageMatches.Count == 0)
            return;

        if (_lessons is not null)
        {
            try
            {
                var scopeId = NormalizeWorkspaceScopeId(options.WorkspaceRoot);
                foreach (System.Text.RegularExpressions.Match match in thoughtMatches.Concat(messageMatches))
                {
                    var claim = match.Groups[1].Value.Trim();
                    if (claim.Length == 0) continue;

                    var signature = "stated:" + Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(claim.ToLowerInvariant())))[..16];
                    await RecordEvidenceTrackingNewAsync(_lessons, state, new AgentLessonEvidence(
                        AgentLessonScope.Workspace, scopeId, AgentLessonKind.Stated, signature,
                        claim, string.Empty, AgentLessonOutcome.Observation, state.TaskId), ct);
                }
            }
            catch
            {
                // Lesson capture is best-effort; it must never break the agent step.
            }
        }

        response.ThoughtSummary = StatedLessonMarkerRegex.Replace(response.ThoughtSummary ?? string.Empty, string.Empty).Trim();
        response.UserMessage = StatedLessonMarkerRegex.Replace(response.UserMessage ?? string.Empty, string.Empty).Trim();
    }

    private static readonly System.Text.RegularExpressions.Regex LessonErrorTokenRegex = new(
        @"\b([A-Z]{1,3}\d{3,5})\b",
        System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500));

    private static string ExtractLessonErrorToken(string text)
    {
        var match = LessonErrorTokenRegex.Match(text);
        return match.Success ? match.Value : "generic";
    }

    private static string TruncateForLesson(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";

    private static string NormalizeWorkspaceScopeId(string workspaceRoot) =>
        string.IsNullOrWhiteSpace(workspaceRoot) ? string.Empty : Path.GetFullPath(workspaceRoot);

    /// <summary>
    /// After a run_command approval executes, remember the exact command
    /// string for the rest of this task so an identical repeat can
    /// auto-execute (see the RunStepAsync run_command branch). Scoped to the
    /// task only; nothing here ever widens to the whole template family.
    /// </summary>
    private static void RememberCommandApprovalIfApplicable(AgentTaskState state, AgentPendingToolAction pending)
    {
        if (!string.Equals(pending.ToolName, "run_command", StringComparison.OrdinalIgnoreCase))
            return;

        var command = AgentToolExecutor.Arg(pending.Arguments, "command").Trim();
        if (command.Length == 0) return;
        if (!state.RememberedCommandApprovals.Any(c => string.Equals(c, command, StringComparison.OrdinalIgnoreCase)))
            state.RememberedCommandApprovals.Add(command);
    }

    private static string BuildPrompt(AgentContextPack context)
    {
        var json = JsonSerializer.Serialize(context, AgentJson.Options);
        return $"Use this compact context pack for the next decision.\n\n{json}";
    }

    private static AgentPlannerResponse ParseResponse(string raw)
    {
        var json = ExtractJson(raw);
        var response = JsonSerializer.Deserialize<AgentPlannerResponse>(json, AgentJson.Options);
        return response ?? new AgentPlannerResponse
        {
            ThoughtSummary = "The model returned an empty response.",
            CurrentStep = "Model response parsing failed.",
            NextAction = new AgentNextAction
            {
                Type = AgentActionKind.AskUser,
                RequiresApproval = false,
                RiskLevel = AgentRiskLevel.None
            },
            UserMessage = "The agent could not parse the model response."
        };
    }

    /// <summary>
    /// Builds the same <see cref="AgentPlannerResponse"/> shape the JSON
    /// protocol produces, but from native tool call(s) instead of parsing
    /// prose. Only the first tool call executes; a model that requests
    /// several tools in one turn gets the rest re-offered next step, which
    /// keeps this on the same one-action-per-step model as the JSON
    /// protocol rather than introducing a second, parallel execution path.
    /// Any prose the model streamed alongside the call(s) becomes the
    /// thought summary instead of a synthetic "Calling X." placeholder, so
    /// tool-calling providers get transcripts as informative as the JSON
    /// fallback's.
    /// </summary>
    private static AgentPlannerResponse BuildResponseFromToolCall(IReadOnlyList<LlmToolCallRequest> calls, string raw)
    {
        var call = calls[0];
        Dictionary<string, object?> arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(call.ArgumentsJson, AgentJson.CompactOptions) ?? [];
        }
        catch (JsonException)
        {
            arguments = [];
        }

        var thought = string.IsNullOrWhiteSpace(raw) ? $"Calling {call.Name}." : raw.Trim();
        if (calls.Count > 1)
        {
            var dropped = string.Join(", ", calls.Skip(1).Select(c => c.Name));
            thought += $" (also requested: {dropped}; one action per step, requesting the rest next.)";
        }

        return new AgentPlannerResponse
        {
            ThoughtSummary = thought,
            CurrentStep = $"Run {call.Name}.",
            NextAction = new AgentNextAction
            {
                Type = AgentActionKind.Tool,
                ToolName = call.Name,
                Arguments = arguments,
                RequiresApproval = false,
                RiskLevel = AgentRiskLevel.None
            },
            UserMessage = $"Requested tool {call.Name}."
        };
    }

    private static string ExtractJson(string raw)
    {
        var trimmed = raw.Trim();
        
        // First, try to extract JSON by removing markdown fence markers
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        // Try parsing using brace matching to handle nested structures
        var candidate = ExtractJsonObject(trimmed);
        if (!string.IsNullOrEmpty(candidate))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(candidate);
                return candidate;
            }
            catch { }
        }

        // Fallback: find first { and last } for malformed responses
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var extracted = trimmed[start..(end + 1)];
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(extracted);
                return extracted;
            }
            catch { }
        }

        throw new JsonException("Agent response did not contain valid JSON object.");
    }

    private static string ExtractJsonObject(string trimmed)
    {
        var start = trimmed.IndexOf('{');
        if (start < 0)
            return string.Empty;

        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return trimmed[start..(i + 1)];
                }
            }
        }

        return string.Empty;
    }

    private static void ApplyResponse(AgentTaskState state, AgentPlannerResponse response)
    {
        foreach (var step in response.StateUpdate.Completed.Where(s => !string.IsNullOrWhiteSpace(s)))
            if (!state.CompletedSteps.Contains(step))
                state.CompletedSteps.Add(step);

        foreach (var step in response.StateUpdate.Pending.Where(s => !string.IsNullOrWhiteSpace(s)))
            if (!state.PendingSteps.Contains(step))
                state.PendingSteps.Add(step);

        foreach (var fact in response.StateUpdate.NewFacts.Where(s => !string.IsNullOrWhiteSpace(s)))
            state.Decisions.Add(new AgentDecision(fact, "model state update", DateTime.UtcNow));

        // A step reported as completed should stop showing up as pending;
        // otherwise long tasks accumulate a PendingSteps list that
        // contradicts CompletedSteps and confuses the model about what is
        // actually left to do.
        state.PendingSteps.RemoveAll(p => state.CompletedSteps.Any(c =>
            string.Equals(c.Trim(), p.Trim(), StringComparison.OrdinalIgnoreCase)));

        if (response.StateUpdate.Blockers.Count > 0)
            state.Status = AgentTaskStatus.Blocked;
    }

    /// <summary>
    /// Replaces the task's plan atomically from a set_plan tool call's
    /// "steps" argument: a JSON array of {"description", "status"} objects,
    /// status one of pending/in_progress/done (case-insensitive; anything
    /// else defaults to pending). This only touches task state, never files
    /// or commands, which is why the safety gate allows it without approval.
    /// </summary>
    private static AgentToolResult ApplySetPlan(AgentTaskState state, Dictionary<string, object?> arguments)
    {
        var plan = new List<AgentPlanStep>();
        if (arguments.TryGetValue("steps", out var raw) && raw is JsonElement { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(description)) continue;
                var statusText = item.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                var status = statusText.Trim().ToLowerInvariant() switch
                {
                    "in_progress" or "in-progress" or "inprogress" => AgentPlanStepStatus.InProgress,
                    "done" or "complete" or "completed" => AgentPlanStepStatus.Done,
                    _ => AgentPlanStepStatus.Pending
                };
                plan.Add(new AgentPlanStep { Description = description.Trim(), Status = status });
            }
        }

        state.Plan = plan;
        return new AgentToolResult
        {
            Tool = "set_plan",
            Arguments = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase),
            ResultSummary = plan.Count == 0
                ? "Plan cleared."
                : string.Join("; ", plan.Select(p => $"[{p.Status}] {p.Description}"))
        };
    }

    private static string BuildSummary(AgentTaskState state, AgentPlannerResponse response)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(response.ThoughtSummary)) bits.Add(response.ThoughtSummary);
        if (state.CompletedSteps.Count > 0) bits.Add($"Completed: {string.Join("; ", state.CompletedSteps.TakeLast(3))}");
        if (state.PendingSteps.Count > 0) bits.Add($"Pending: {string.Join("; ", state.PendingSteps.Take(3))}");
        return string.Join(" ", bits).Trim();
    }
}
