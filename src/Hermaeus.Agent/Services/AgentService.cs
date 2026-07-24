using System.Text;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Agent.Services;

public sealed class AgentService : IAgentService
{
    private const string AgentSystemPrompt = """
        You are Hermaeus Agent, a local-first semi-autonomous task assistant.
        Use explicit task state and retrieved context. Be practical and concise.
        You may run several steps in a row without the user clicking anything;
        keep working until the goal is done, you need to ask the user
        something, or an action needs approval.
        You may request supported local tools: list_files (optional
        subdirectory, max_depth), search_files (optional regex, context_lines),
        glob_files (pattern), read_file (optional line_offset, line_limit),
        summarize_file, inspect_git_diff, draft_patch, apply_draft_patch,
        edit_file, create_file, set_plan, plan_subtasks, and run_command.
        Read-only tools
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
        whole plan each time. Use plan_subtasks (subtasks: array of 2 to 6
        {goal, profile, success_criteria}, profile one of general|correctness|
        security|tests|performance|docs) only for a broad, multi-domain goal
        that should be split into focused sub-tasks each run through this
        same loop; never for a goal that fits a normal plan (set_plan is the
        right tool there). plan_subtasks always requires approval, and a
        sub-task can never itself request plan_subtasks. run_command only accepts one of the workspace's
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
        verbatim. Approval policy is never a valid lesson subject - a lesson
        claiming the user approves, pre-approves, or does not need to review
        something is rejected outright, not stored.
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
            new("plan_subtasks", "Propose splitting the current goal into 2 to 6 focused sub-tasks, each with a goal, a profile from the fixed list (general, correctness, security, tests, performance, docs), and success criteria. Always requires approval; only useful for broad, multi-domain goals, never for goals that fit a normal plan (use set_plan for that).",
                Schema("""{"type":"object","properties":{"subtasks":{"type":"array","minItems":2,"maxItems":6,"items":{"type":"object","properties":{"goal":{"type":"string"},"profile":{"type":"string","enum":["general","correctness","security","tests","performance","docs"]},"success_criteria":{"type":"string"}},"required":["goal","profile","success_criteria"]}}},"required":["subtasks"]}""")),
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

    private static readonly IReadOnlyList<string> BaseTaskConstraints =
    [
        "local-first",
        "do not write files without approval",
        "do not run commands without approval",
        "use retrieved context before inference",
        "keep task state explicit"
    ];

    public async Task<AgentTaskState> CreateTaskAsync(
        string goal,
        AgentWorkspaceOptions options,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(goal))
            throw new InvalidOperationException("Agent goal is required.");

        var root = AgentWorkspaceTools.ResolveWorkspaceRoot(options.WorkspaceRoot);
        var state = NewTaskState(goal, BaseTaskConstraints, parentTaskId: null);
        state.WorkspaceRoot = root;

        await _store.SaveAsync(state, ct);
        await _store.AppendLogAsync(state.TaskId, $"created task: {state.Goal}", ct);
        return state;
    }

    /// <summary>
    /// Creates a child task for one approved sub-task spec (r15
    /// 01-subtask-orchestration.md 1.4): same base constraints as an
    /// ordinary task, plus the specialist profile's focus constraints, goal
    /// composed with the spec's success criteria, and <see cref="AgentTaskState.ParentTaskId"/>
    /// set so the depth-1 check in <see cref="RunStepAsync"/> can block a
    /// child from requesting plan_subtasks itself.
    /// </summary>
    private async Task<AgentTaskState> CreateChildTaskAsync(
        AgentTaskState parent,
        AgentSubTaskSpec spec,
        AgentWorkspaceOptions options,
        CancellationToken ct)
    {
        var goal = string.IsNullOrWhiteSpace(spec.SuccessCriteria)
            ? spec.Goal
            : $"{spec.Goal}\nSuccess criteria: {spec.SuccessCriteria}";
        var constraints = BaseTaskConstraints.Concat(AgentSpecialistProfiles.Resolve(spec.ProfileName)).ToList();
        var state = NewTaskState(goal, constraints, parent.TaskId);
        state.WorkspaceRoot = parent.WorkspaceRoot;

        await _store.SaveAsync(state, ct);
        await _store.AppendLogAsync(state.TaskId, $"created sub-task (parent {parent.TaskId}): {state.Goal}", ct);
        return state;
    }

    private static AgentTaskState NewTaskState(string goal, IEnumerable<string> constraints, string? parentTaskId) => new()
    {
        Goal = goal.Trim(),
        ParentTaskId = parentTaskId,
        Status = AgentTaskStatus.New,
        ActiveStep = "Build an initial plan from the goal and retrieved context.",
        Constraints = constraints.ToList(),
        PendingSteps = ["Inspect workspace context", "Build plan", "Report next action"],
        Summary = "Task created. No tool actions have run yet."
    };

    public async Task<AgentStepResult> RunStepAsync(
        string taskId,
        AgentWorkspaceOptions options,
        CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(taskId, ct)
            ?? throw new InvalidOperationException("Agent task was not found.");
        if (state.Status is AgentTaskStatus.Complete or AgentTaskStatus.Failed)
            throw new InvalidOperationException("Agent task is already finished.");
        if (state.SubTaskPlan.Any(s => s.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running))
        {
            // A parent with an approved, unfinished SubTaskPlan never runs a
            // bare parent model step (r15 01-subtask-orchestration.md 1.4);
            // that would let the parent answer "final" with children unrun,
            // or re-propose plan_subtasks and silently discard the in-flight
            // plan (r16 01-orchestration-hardening.md 1.2). Callers must go
            // through RunAsync, which routes to RunOrchestrationAsync
            // instead. RunSynthesisAsync's own direct RunStepAsync call is
            // unaffected: it only runs once every spec is terminal.
            throw new InvalidOperationException(
                "This task has an unfinished sub-task plan; call RunAsync to advance orchestration instead of stepping the parent directly.");
        }

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
            var manifest = _manifests is null ? null : await _manifests.LoadAsync(options.WorkspaceRoot, ct);
            // Carries the workspace policy (r23 3.1) and a per-task read
            // budget seeded from persisted state into every tool call this
            // step makes - both the Allowed-tool direct-execute path below
            // and, via AppendApprovalAsync, the later approved-write path.
            var toolOptions = options with
            {
                Policy = manifest?.Policy,
                ReadBudget = new AgentReadBudget { MaxReads = manifest?.Policy?.MaxFileReadsPerTask ?? 0, UsedReads = state.FileReadCount }
            };

            AgentToolPolicyDecision decision;
            if (string.Equals(nextTool, "plan_subtasks", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(state.ParentTaskId))
            {
                // Depth limit enforced in code, not prompt text (r15
                // 01-subtask-orchestration.md 1.2 step 2): a sub-task can
                // never itself propose sub-tasks, regardless of what the
                // model set in requires_approval. This check runs before the
                // gate call and takes precedence over it.
                decision = new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "Sub-tasks cannot create sub-tasks (depth limit 1).");
            }
            else if (string.Equals(nextTool, "plan_subtasks", StringComparison.OrdinalIgnoreCase) && state.SubTaskPlan.Count > 0)
            {
                // Defense in depth alongside RunStepAsync's own guard above
                // (r16 01-orchestration-hardening.md 1.3): the one path that
                // still reaches this gate with a non-empty SubTaskPlan is the
                // synthesis step itself (every spec terminal by then), and a
                // model that proposes plan_subtasks there must not be allowed
                // to replace the plan it was just asked to summarize.
                decision = new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, "This task already has a sub-task plan.");
            }
            else if (string.Equals(nextTool, "run_command", StringComparison.OrdinalIgnoreCase))
            {
                var requestedCommand = AgentToolExecutor.Arg(response.NextAction.Arguments, "command");
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
            else if (nextTool is "edit_file" or "create_file" or "apply_draft_patch")
            {
                // A policy-denied write is classified Blocked before it ever
                // becomes an approvable pending action (r23 3.2); the same
                // rule is re-checked at actual execution time inside
                // AgentWorkspaceTools, for the draft-patch queue and Rewind
                // paths that do not go through this classification step.
                var targetPath = AgentToolExecutor.Arg(response.NextAction.Arguments, "relative_path", "path");
                var writeVerdict = WorkspacePolicyEvaluator.EvaluateWrite(manifest?.Policy, targetPath);
                decision = writeVerdict.Allowed
                    ? _safetyGate.Evaluate(nextTool, response.NextAction.RequiresApproval)
                    : new AgentToolPolicyDecision(AgentToolDisposition.Blocked, AgentRiskLevel.High, $"write blocked by workspace policy: {writeVerdict.Reason}");
            }
            else
            {
                decision = _safetyGate.Evaluate(nextTool, response.NextAction.RequiresApproval);
            }
            response.NextAction.RiskLevel = decision.RiskLevel;
            response.NextAction.RequiresApproval = decision.Disposition != AgentToolDisposition.Allowed;
            if (decision.Disposition == AgentToolDisposition.RequiresApproval)
            {
                if (_toolExecutor.CanExecute(nextTool))
                {
                    state.Status = AgentTaskStatus.WaitingForUser;
                    state.PendingToolAction = new AgentPendingToolAction
                    {
                        ToolName = nextTool,
                        Arguments = response.NextAction.Arguments,
                        RiskLevel = decision.RiskLevel,
                        Reason = decision.Reason,
                        Fingerprint = AgentApprovalFingerprint.Compute(nextTool, response.NextAction.Arguments)
                    };
                }
                else
                {
                    // A gated action with no registered executor has nothing
                    // to approve; leaving it WaitingForUser with no
                    // PendingToolAction strands the task (AppendUserReplyAsync
                    // is the only way out, which the user has no reason to
                    // guess). Treat it like the allowed-but-unexecutable case
                    // below: Blocked, with an explanatory result (r15
                    // 03-scenarios-and-hardening.md 3.2).
                    state.Status = AgentTaskStatus.Blocked;
                    state.ToolResults.Add(new AgentToolResult
                    {
                        Tool = nextTool,
                        Arguments = response.NextAction.Arguments,
                        ResultSummary = "The tool required approval, but no local executor is registered for it."
                    });
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
                        toolResult = await _toolExecutor.ExecuteAsync(nextTool, response.NextAction.Arguments, toolOptions, ct);
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

                    // Persists the read budget's usage so it survives a
                    // restart (r23 3.1); a policy-denied or budget-exhausted
                    // attempt returns gracefully without incrementing, so it
                    // never counts against the budget it was refused by.
                    state.FileReadCount = toolOptions.ReadBudget!.UsedReads;
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

            // Deterministic precedence for a step that both reports a
            // blocker and requests a tool: a tool that went on to execute
            // successfully this step wins (progress wins) and the blocker
            // is left recorded in Decisions only; any other outcome (gated,
            // blocked, or unexecutable) makes the blocker the visible
            // status instead of a plain WaitingForUser (r15
            // 03-scenarios-and-hardening.md 3.3).
            if (response.StateUpdate.Blockers.Count > 0 && executedToolResult is null)
            {
                state.Status = AgentTaskStatus.Blocked;
                // Blocked never coexists with something pending approval
                // elsewhere in this loop (the gate-Blocked and 3.2
                // unexecutable-gated paths above never set one either); a
                // blocker overriding WaitingForUser must not leave a stale
                // approval behind it.
                state.PendingToolAction = null;
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
        var loaded = await _store.LoadAsync(taskId, ct) ?? throw new InvalidOperationException("Agent task was not found.");
        if (loaded.SubTaskPlan.Count > 0)
            return await RunOrchestrationAsync(loaded, options, onStep, ct);

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

    /// <summary>
    /// Sequential sub-task orchestration (r15 01-subtask-orchestration.md 1.4):
    /// a parent with an approved, unfinished <see cref="AgentTaskState.SubTaskPlan"/>
    /// never runs a parent model step itself while sub-tasks remain; instead
    /// this advances one child at a time through the ordinary single-task
    /// <see cref="RunAsync(string,AgentWorkspaceOptions,Action{AgentStepResult}?,CancellationToken)"/>
    /// loop, then synthesizes once every spec is terminal. Cancellation
    /// propagates out of the current child's step exactly as it would for a
    /// plain task; remaining specs are left Pending for the next resume.
    /// </summary>
    private async Task<AgentStepResult> RunOrchestrationAsync(
        AgentTaskState parent,
        AgentWorkspaceOptions options,
        Action<AgentStepResult>? onStep,
        CancellationToken ct)
    {
        if (parent.Status is AgentTaskStatus.Complete or AgentTaskStatus.Failed)
            throw new InvalidOperationException("Agent task is already finished.");

        var maxOrchestrationSteps = Math.Max(_settings?.Settings.Agent.MaxOrchestrationSteps ?? 60, 1);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            parent = await _store.LoadAsync(parent.TaskId, ct) ?? throw new InvalidOperationException("Agent task was not found.");
            await ReconcileSubTaskPlanAsync(parent, ct);

            var next = parent.SubTaskPlan.FirstOrDefault(s => s.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running);
            if (next is null)
                return await RunSynthesisAsync(parent, options, budgetTruncated: false, ct);

            if (parent.OrchestrationStepsUsed >= maxOrchestrationSteps)
            {
                foreach (var spec in parent.SubTaskPlan.Where(s => s.Status == AgentSubTaskStatus.Pending))
                    spec.Status = AgentSubTaskStatus.Skipped;
                parent.Decisions.Add(new AgentDecision(
                    "Orchestration step budget exhausted",
                    $"MaxOrchestrationSteps ({maxOrchestrationSteps}) reached; remaining sub-tasks were skipped.",
                    DateTime.UtcNow));
                await _store.SaveAsync(parent, ct);
                await _store.AppendLogAsync(parent.TaskId, "orchestration step budget exhausted; remaining sub-tasks skipped", ct);
                return await RunSynthesisAsync(parent, options, budgetTruncated: true, ct);
            }

            if (next.Status == AgentSubTaskStatus.Pending)
            {
                var child = await CreateChildTaskAsync(parent, next, options, ct);
                next.TaskId = child.TaskId;
                next.Status = AgentSubTaskStatus.Running;
                await _store.SaveAsync(parent, ct);
            }

            var childTaskId = next.TaskId!;
            var childStepsUsed = 0;
            var childResult = await RunAsync(childTaskId, options, onStep: r =>
            {
                childStepsUsed++;
                onStep?.Invoke(r);
            }, ct);

            parent = await _store.LoadAsync(parent.TaskId, ct) ?? parent;
            parent.OrchestrationStepsUsed += childStepsUsed;

            if (childResult.State.Status is AgentTaskStatus.Complete or AgentTaskStatus.Failed)
            {
                var spec = parent.SubTaskPlan.First(s => s.TaskId == childTaskId);
                spec.Status = childResult.State.Status == AgentTaskStatus.Complete ? AgentSubTaskStatus.Complete : AgentSubTaskStatus.Failed;
                var combined = string.Join(" ", new[] { childResult.State.Summary, childResult.PlannerResponse.UserMessage }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                spec.ResultSummary = combined.Length > 1200 ? combined[..1200] + "..." : combined;
                await _store.SaveAsync(parent, ct);
                continue;
            }

            // Child paused (WaitingForUser or Blocked): the user acts on the
            // CHILD task's own approval queue entry, but the PARENT must say
            // so truthfully too (r16 01-orchestration-hardening.md 1.6) -
            // otherwise it sits "Running" forever in any list that shows it
            // even though nothing is actually happening until the user acts.
            parent.Status = childResult.State.Status;
            var childIndex = parent.SubTaskPlan.FindIndex(s => s.TaskId == childTaskId);
            parent.ActiveStep = $"Waiting on sub-task {childIndex + 1}/{parent.SubTaskPlan.Count}: {next.Goal}";
            await _store.SaveAsync(parent, ct);
            return childResult;
        }
    }

    /// <summary>
    /// Self-healing reconcile (r16 01-orchestration-hardening.md 1.1): a
    /// child can reach a terminal state OUTSIDE this loop entirely - opened
    /// directly in the workbench and stepped/run to completion, or resumed
    /// via the review queue while it (not the parent) was the open task.
    /// Without this, the parent's own <see cref="AgentSubTaskSpec"/> stays
    /// stuck on <see cref="AgentSubTaskStatus.Running"/> forever, and every
    /// later parent run throws "already finished" trying to <see cref="RunAsync"/>
    /// a terminal child. Mirrors the terminal-copyback done inline below in
    /// this class, just triggered by the child's own persisted state
    /// instead of a <see cref="RunAsync"/> call this loop itself made.
    /// </summary>
    private async Task ReconcileSubTaskPlanAsync(AgentTaskState parent, CancellationToken ct)
    {
        var changed = false;
        foreach (var spec in parent.SubTaskPlan.Where(s => s.Status == AgentSubTaskStatus.Running && s.TaskId is not null))
        {
            var child = await _store.LoadAsync(spec.TaskId!, ct);
            if (child is null || child.Status is not (AgentTaskStatus.Complete or AgentTaskStatus.Failed))
                continue;

            spec.Status = child.Status == AgentTaskStatus.Complete ? AgentSubTaskStatus.Complete : AgentSubTaskStatus.Failed;
            spec.ResultSummary = child.Summary.Length > 1200 ? child.Summary[..1200] + "..." : child.Summary;
            changed = true;
        }

        if (changed)
            await _store.SaveAsync(parent, ct);
    }

    /// <summary>
    /// One final ordinary model step on the parent once every sub-task is
    /// terminal (r15 01-subtask-orchestration.md 1.6): the model is expected
    /// to answer "final" with the consolidated report as user_message. A
    /// model call failure or an unparseable response falls back to a
    /// deterministic report built from the spec entries themselves - the
    /// sub-task work already happened, so a flaky synthesis step must not
    /// fail the whole run.
    /// </summary>
    private async Task<AgentStepResult> RunSynthesisAsync(AgentTaskState parent, AgentWorkspaceOptions options, bool budgetTruncated, CancellationToken ct)
    {
        parent.ActiveStep = "All sub-tasks are finished. Respond with next_action.type=\"final\" and a consolidated report covering every sub-task's outcome as user_message.";
        await _store.SaveAsync(parent, ct);

        AgentStepResult? stepResult;
        try
        {
            stepResult = await RunStepAsync(parent.TaskId, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stepResult = null;
        }

        var state = stepResult?.State ?? await _store.LoadAsync(parent.TaskId, ct) ?? parent;
        var synthesisSucceeded = stepResult is not null
            && state.Status == AgentTaskStatus.Complete
            && !string.IsNullOrWhiteSpace(stepResult.PlannerResponse.UserMessage);

        var report = synthesisSucceeded ? stepResult!.PlannerResponse.UserMessage : BuildFallbackSynthesisReport(state);
        if (budgetTruncated && !report.Contains("budget", StringComparison.OrdinalIgnoreCase))
            report += "\n\nNote: this run was truncated by the orchestration step budget; some sub-tasks were skipped.";

        state.Status = AgentTaskStatus.Complete;
        state.PendingToolAction = null;
        state.Summary = report;
        await _store.SaveAsync(state, ct);
        await _store.AppendTranscriptEntryAsync(state.TaskId, new AgentTranscriptEntry(
            state.StepCount, "assistant", null, report, DateTime.UtcNow), ct);
        await WriteReportFileAsync(state, report, ct);

        if (!synthesisSucceeded)
        {
            // RunStepAsync's own terminal-lesson capture only fires when it
            // sees the task end Complete/Failed; a fallback that forces
            // Complete after the fact never went through that path.
            await RecordTaskTerminalLessonAsync(state, options, ct);
        }

        return stepResult is not null && synthesisSucceeded
            ? stepResult with { State = state }
            : new AgentStepResult(state, stepResult?.ContextPack ?? new AgentContextPack(), stepResult?.PlannerResponse ?? new AgentPlannerResponse { UserMessage = report }, report);
    }

    private static string BuildFallbackSynthesisReport(AgentTaskState state)
    {
        var lines = new List<string>
        {
            $"Sub-task run for \"{state.Goal}\" finished. Synthesis could not be generated automatically; here is a summary of each sub-task:"
        };
        foreach (var spec in state.SubTaskPlan)
        {
            var summary = string.IsNullOrWhiteSpace(spec.ResultSummary) ? "no result recorded." : spec.ResultSummary;
            lines.Add($"- [{spec.Status}] ({spec.ProfileName}) {spec.Goal}: {summary}");
        }

        return string.Join("\n", lines);
    }

    private async Task WriteReportFileAsync(AgentTaskState state, string report, CancellationToken ct)
    {
        var dir = _store.GetTaskDirectory(state.TaskId);
        Directory.CreateDirectory(dir);
        await AtomicFileWriter.WriteAllTextAsync(Path.Combine(dir, "report.md"), report, ct);
    }

    public Task<IReadOnlyList<AgentTaskListItem>> LoadRecentTasksAsync(CancellationToken ct = default) =>
        _store.ListRecentAsync(25, ct);

    public async Task<AgentApprovalResult> AppendApprovalAsync(string taskId, string action, bool approved, string expectedFingerprint, AgentWorkspaceOptions? options = null, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(taskId, ct)
            ?? throw new InvalidOperationException("Agent task was not found.");

        // Binds the approval to the pending action as it exists right now,
        // not as it was when the UI rendered it (r23 4.1). A concurrent step,
        // a crash-restore race, or a tampered task_state.json could otherwise
        // let the user approve one thing and execute another. A pre-r23
        // pending action has no stored Fingerprint; recompute it from
        // ToolName/Arguments so old tasks keep working without a migration -
        // the UI does the same recompute when it renders a legacy task.
        var pendingAtStart = state.PendingToolAction;
        var actualFingerprint = AgentApprovalFingerprint.Resolve(pendingAtStart);
        var fingerprintMismatch = pendingAtStart is not null
            && !string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal);
        if (fingerprintMismatch)
        {
            await _store.AppendTraceAsync(taskId, new
            {
                task_id = taskId,
                type = "approval_fingerprint_mismatch",
                tool = pendingAtStart!.ToolName,
                expected_fingerprint = expectedFingerprint,
                actual_fingerprint = actualFingerprint,
                approved,
                logged_at = DateTime.UtcNow
            }, ct);
        }

        if (approved && fingerprintMismatch)
        {
            // Refuse execution: the pending action stays pending and the task
            // stays waiting_for_review. Rejections do not need this refusal
            // (rejecting the wrong thing executes nothing) - only the trace
            // event above, already written.
            await _store.AppendLogAsync(taskId, $"approval refused: pending action changed since it was displayed ({pendingAtStart!.ToolName})", ct);
            return new AgentApprovalResult(false, "The pending action changed since it was displayed. Review it again.");
        }

        state.ApprovalHistory.Add(new AgentApprovalRecord(action, approved, DateTime.UtcNow));
        if (approved && state.PendingToolAction is not null && string.Equals(state.PendingToolAction.ToolName, "plan_subtasks", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyPlanSubtasksApprovalAsync(state, state.PendingToolAction, ct);
        }
        else if (approved && state.PendingToolAction is not null)
        {
            // Executes against the task's OWN stored workspace root, not
            // whatever workspace the workbench currently has active - the
            // review queue lists tasks across every workspace, so the
            // caller-supplied options can point somewhere else entirely
            // (r16 01-orchestration-hardening.md 1.4). A pre-r16 state with
            // no stored root falls back to the caller's options exactly as
            // before; if neither is available there is nothing safe to
            // execute against (1.5), so this throws instead of silently
            // stranding the task Running with a stale pending action.
            var effectiveOptions = state.WorkspaceRoot is { Length: > 0 }
                ? options is not null ? options with { WorkspaceRoot = state.WorkspaceRoot } : new AgentWorkspaceOptions(state.WorkspaceRoot)
                : options ?? throw new InvalidOperationException("Workspace options are required to execute the pending action.");
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
                try { preImage = await _workspaceTools.ReadFileForRevertAsync(effectiveOptions, relativePath, ct); }
                catch { mutatesFile = false; /* best effort; skip the revert record, still execute the tool */ }
            }
            else
            {
                mutatesFile = false;
            }

            AgentToolResult result;
            try
            {
                result = await _toolExecutor.ExecuteAsync(pending.ToolName, pending.Arguments, effectiveOptions, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await RecordLessonEvidenceForToolAsync(state, effectiveOptions, pending.ToolName, pending.Arguments, ex.Message, success: false, ct);
                throw;
            }

            state.ToolResults.Add(result);
            RememberCommandApprovalIfApplicable(state, pending);
            await RecordLessonEvidenceForToolAsync(state, effectiveOptions, pending.ToolName, pending.Arguments, result.ResultSummary, success: true, ct, result.ExitCode, result.TimedOut);
            await RecordApprovalApprovedCounterEvidenceAsync(state, effectiveOptions, pending.ToolName, ct);
            // The approved tool's result is what the model most needs to see
            // next (it is why the step paused), so it belongs in the
            // transcript alongside every other executed tool result, not
            // just in ToolResults' last-five window.
            await _store.AppendTranscriptEntryAsync(taskId, new AgentTranscriptEntry(
                state.StepCount, "tool", result.Tool, result.ResultSummary, DateTime.UtcNow), ct);

            if (mutatesFile && _workspaceTools is not null)
            {
                var postContent = await _workspaceTools.ReadFileForRevertAsync(effectiveOptions, relativePath, ct) ?? string.Empty;
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
        return new AgentApprovalResult(true, string.Empty);
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

    private const string DefaultContinueInstruction = "Continue with the remaining pending steps.";

    public async Task<AgentTaskState> ContinueTaskAsync(string taskId, string instruction, AgentWorkspaceOptions options, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(taskId, ct)
            ?? throw new InvalidOperationException("Agent task was not found.");

        if (!string.IsNullOrWhiteSpace(state.ParentTaskId))
            throw new InvalidOperationException("This task is a sub-task; continue the parent instead.");
        if (state.Status == AgentTaskStatus.Running)
            throw new InvalidOperationException("This task is already running.");
        if (state.PendingToolAction is not null)
            throw new InvalidOperationException("A tool approval is pending; approve or reject it from the review queue instead of continuing.");

        var trimmedInstruction = string.IsNullOrWhiteSpace(instruction) ? DefaultContinueInstruction : instruction.Trim();

        await _store.AppendTranscriptEntryAsync(taskId, new AgentTranscriptEntry(
            state.StepCount, "user", null, $"continue: {trimmedInstruction}", DateTime.UtcNow), ct);

        // Reconcile child statuses first (r16 01-orchestration-hardening.md
        // 1.1) so a resumed orchestration parent advances the next PENDING
        // sub-task rather than re-running one that already finished outside
        // this loop.
        if (state.SubTaskPlan.Count > 0)
        {
            await ReconcileSubTaskPlanAsync(state, ct);
            state = await _store.LoadAsync(taskId, ct) ?? state;
        }

        state.Status = AgentTaskStatus.Running;
        state.ConsecutiveStepErrors = 0;
        await _store.SaveAsync(state, ct);
        await _store.AppendLogAsync(taskId, $"continued: {trimmedInstruction}", ct);
        return state;
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
    /// Deterministic (no LLM), case-insensitive tokens that mark a stated
    /// lesson as an approval-policy claim (r23 4.2, "Stated-lesson
    /// gate-claim filter"). The safety gate never reads the lesson store, so
    /// a poisoned lesson could not widen execution today - but it would
    /// still sit in every future context pack and the Lessons panel as
    /// persistent social engineering. Precision matters more than recall
    /// here; add tokens as new phrasings turn up rather than trying to be
    /// exhaustive up front.
    /// </summary>
    private static readonly string[] ApprovalClaimTokens =
    [
        "approv", "no confirmation", "without asking", "without review",
        "skip review", "skip the gate", "always allow", "allow all",
        "trusted to run", "does not need permission"
    ];

    private static string? MatchedApprovalClaimToken(string claim)
    {
        foreach (var token in ApprovalClaimTokens)
        {
            if (claim.Contains(token, StringComparison.OrdinalIgnoreCase))
                return token;
        }
        return null;
    }

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

                    var matchedToken = MatchedApprovalClaimToken(claim);
                    if (matchedToken is not null)
                    {
                        // Rejected outright, not stored at any confidence
                        // (r23 4.2): the safety gate never reads the lesson
                        // store, but a stored claim like this would still be
                        // persistent social engineering in every future
                        // context pack and the Lessons panel.
                        await _store.AppendTraceAsync(state.TaskId, new
                        {
                            task_id = state.TaskId,
                            type = "lesson_rejected",
                            claim,
                            matched_token = matchedToken,
                            logged_at = DateTime.UtcNow
                        }, ct);
                        continue;
                    }

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

        // Recorded unconditionally, regardless of what the status ends up
        // being this step - a blocker that gets superseded by a successful
        // tool execution (progress wins; see RunStepAsync) must still leave
        // a trace instead of vanishing (r15 03-scenarios-and-hardening.md 3.3).
        foreach (var blocker in response.StateUpdate.Blockers.Where(s => !string.IsNullOrWhiteSpace(s)))
            state.Decisions.Add(new AgentDecision(blocker, "model-reported blocker", DateTime.UtcNow));
    }

    /// <summary>
    /// Validates and materializes an approved plan_subtasks action onto the
    /// parent (r15 01-subtask-orchestration.md 1.2 step 3, 1.4). No child
    /// task is created here - only the plan itself. An invalid plan (wrong
    /// entry count, an empty goal, or an unknown profile) rejects instead of
    /// executing: the pending action is cleared and the task returns to
    /// WaitingForUser with an explanatory result, exactly as if the tool had
    /// failed to run.
    /// </summary>
    private async Task ApplyPlanSubtasksApprovalAsync(AgentTaskState state, AgentPendingToolAction pending, CancellationToken ct)
    {
        var argumentsCopy = new Dictionary<string, object?>(pending.Arguments, StringComparer.OrdinalIgnoreCase);
        state.PendingToolAction = null;

        if (state.SubTaskPlan.Count > 0)
        {
            // A stale queued approval must not clobber a plan that already
            // exists by the time it is actually approved (r16
            // 01-orchestration-hardening.md 1.3) - the gate check in
            // RunStepAsync closes the common case, this closes the race.
            const string duplicatePlanError = "This task already has a sub-task plan; the proposed plan was rejected.";
            state.Status = AgentTaskStatus.WaitingForUser;
            state.ToolResults.Add(new AgentToolResult { Tool = "plan_subtasks", Arguments = argumentsCopy, ResultSummary = duplicatePlanError });
            await _store.AppendTranscriptEntryAsync(state.TaskId, new AgentTranscriptEntry(
                state.StepCount, "tool", "plan_subtasks", duplicatePlanError, DateTime.UtcNow), ct);
            return;
        }

        if (!TryParsePlanSubtasks(pending.Arguments, out var specs, out var error))
        {
            state.Status = AgentTaskStatus.WaitingForUser;
            state.ToolResults.Add(new AgentToolResult { Tool = "plan_subtasks", Arguments = argumentsCopy, ResultSummary = error });
            await _store.AppendTranscriptEntryAsync(state.TaskId, new AgentTranscriptEntry(
                state.StepCount, "tool", "plan_subtasks", error, DateTime.UtcNow), ct);
            return;
        }

        state.SubTaskPlan = specs;
        state.Status = AgentTaskStatus.Running;
        var summary = $"Approved sub-task plan ({specs.Count}): " + string.Join("; ", specs.Select(s => $"[{s.ProfileName}] {s.Goal}"));
        state.ToolResults.Add(new AgentToolResult { Tool = "plan_subtasks", Arguments = argumentsCopy, ResultSummary = summary });
        await _store.AppendTranscriptEntryAsync(state.TaskId, new AgentTranscriptEntry(
            state.StepCount, "tool", "plan_subtasks", summary, DateTime.UtcNow), ct);
    }

    /// <summary>
    /// Parses and validates a plan_subtasks action's "subtasks" argument:
    /// 2 to 6 entries, each with a non-empty goal and a known specialist
    /// profile name (r15 01-subtask-orchestration.md 1.2 step 3). An
    /// unparseable, too-short, too-long, empty-goal, or unknown-profile plan
    /// fails validation rather than falling back to a default - the
    /// approving user must see exactly what they authorized.
    /// </summary>
    private static bool TryParsePlanSubtasks(Dictionary<string, object?> arguments, out List<AgentSubTaskSpec> specs, out string error)
    {
        specs = [];
        error = string.Empty;

        if (!arguments.TryGetValue("subtasks", out var raw) || raw is not JsonElement { ValueKind: JsonValueKind.Array } array)
        {
            error = "Could not parse the proposed sub-task plan: \"subtasks\" was missing or not an array.";
            return false;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var goal = item.TryGetProperty("goal", out var g) ? g.GetString() ?? string.Empty : string.Empty;
            var profile = item.TryGetProperty("profile", out var p) ? p.GetString() ?? string.Empty : string.Empty;
            var successCriteria = item.TryGetProperty("success_criteria", out var s) ? s.GetString() ?? string.Empty : string.Empty;
            specs.Add(new AgentSubTaskSpec { Goal = goal.Trim(), ProfileName = profile.Trim(), SuccessCriteria = successCriteria.Trim() });
        }

        if (specs.Count is < 2 or > 6)
        {
            error = $"Proposed sub-task plan has {specs.Count} entr{(specs.Count == 1 ? "y" : "ies")}; must be between 2 and 6.";
            return false;
        }

        if (specs.Any(s => s.Goal.Length == 0))
        {
            error = "Proposed sub-task plan has an entry with an empty goal.";
            return false;
        }

        var unknown = specs.Select(s => s.ProfileName).FirstOrDefault(p => !AgentSpecialistProfiles.IsKnown(p));
        if (unknown is not null)
        {
            error = $"Proposed sub-task plan references an unknown profile '{unknown}'.";
            return false;
        }

        return true;
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
