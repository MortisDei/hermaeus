using System.Text;
using System.Text.Json;
using Aether.Agent.Models;
using Aether.Core.Services;

namespace Aether.Agent.Services;

public sealed class AgentService : IAgentService
{
    private const string AgentSystemPrompt = """
        You are Aether Agent, a local-first semi-autonomous task assistant.
        Use explicit task state and retrieved context. Be practical and concise.
        Do not claim risky actions were executed. Writes, commands, network access,
        commits, and pushes require approval and are not executable in this alpha.
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

    private readonly IAgentTaskStateStore _store;
    private readonly IAgentContextBuilder _contextBuilder;
    private readonly IAgentSafetyGate _safetyGate;
    private readonly ILlmService _llm;

    public AgentService(
        IAgentTaskStateStore store,
        IAgentContextBuilder contextBuilder,
        IAgentSafetyGate safetyGate,
        ILlmService llm)
    {
        _store = store;
        _contextBuilder = contextBuilder;
        _safetyGate = safetyGate;
        _llm = llm;
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
        var prompt = BuildPrompt(context);
        var modelId = options.ModelId;
        var raw = new StringBuilder();
        await foreach (var token in _llm.StreamChatAsync(
            modelId,
            [new ChatMessage("user", prompt)],
            AgentSystemPrompt,
            temperature: 0.2,
            ct))
        {
            raw.Append(token);
        }

        var response = ParseResponse(raw.ToString());
        ApplyResponse(state, response);
        var nextTool = response.NextAction.ToolName ?? string.Empty;
        if (response.NextAction.Type == AgentActionKind.Tool)
        {
            var decision = _safetyGate.Evaluate(nextTool, response.NextAction.RequiresApproval);
            response.NextAction.RiskLevel = decision.RiskLevel;
            response.NextAction.RequiresApproval = decision.Disposition != AgentToolDisposition.Allowed;
            if (decision.Disposition == AgentToolDisposition.RequiresApproval)
                state.Status = AgentTaskStatus.WaitingForUser;
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
        }

        if (response.NextAction.Type == AgentActionKind.AskUser)
            state.Status = AgentTaskStatus.WaitingForUser;
        if (response.NextAction.Type == AgentActionKind.Final)
            state.Status = AgentTaskStatus.Complete;

        state.ActiveStep = string.IsNullOrWhiteSpace(response.CurrentStep)
            ? state.ActiveStep
            : response.CurrentStep;
        state.Summary = BuildSummary(state, response);
        await _store.SaveAsync(state, ct);

        var logEntry = string.IsNullOrWhiteSpace(response.UserMessage)
            ? response.ThoughtSummary
            : response.UserMessage;
        await _store.AppendLogAsync(taskId, logEntry, ct);
        await _store.AppendTraceAsync(taskId, new
        {
            task_id = taskId,
            state.Status,
            context,
            response,
            logged_at = DateTime.UtcNow
        }, ct);

        return new AgentStepResult(state, context, response, logEntry);
    }

    public Task<IReadOnlyList<AgentTaskListItem>> LoadRecentTasksAsync(CancellationToken ct = default) =>
        _store.ListRecentAsync(25, ct);

    public async Task AppendApprovalAsync(string taskId, string action, bool approved, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(taskId, ct)
            ?? throw new InvalidOperationException("Agent task was not found.");
        state.ApprovalHistory.Add(new AgentApprovalRecord(action, approved, DateTime.UtcNow));
        state.Status = approved ? AgentTaskStatus.Running : AgentTaskStatus.WaitingForUser;
        await _store.SaveAsync(state, ct);
        await _store.AppendLogAsync(taskId, $"approval recorded: {action} approved={approved}", ct);
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

        if (response.StateUpdate.Blockers.Count > 0)
            state.Status = AgentTaskStatus.Blocked;
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
