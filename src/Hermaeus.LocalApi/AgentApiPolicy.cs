using Hermaeus.Core.Models;

namespace Hermaeus.LocalApi;

public sealed record AgentApiAuthorizationContext(
    LocalApiAgentOperation Operation,
    string WorkspaceProfileId = "",
    bool WorkspaceProfileExists = true,
    string ModelId = "",
    bool ModelIsVisible = true,
    string ProjectId = "",
    bool ProjectExists = true,
    bool TaskExists = true,
    string ResourceOwnerTokenId = "",
    bool HasPendingDecision = false,
    bool IsWaitingForUserAnswer = false,
    int ActiveRunsForToken = 0,
    int TextLength = 0);

public sealed record AgentApiPolicyDecision(bool Allowed, int StatusCode, string Code, string Reason)
{
    public static AgentApiPolicyDecision Permit() => new(true, 200, "allowed", string.Empty);
    public static AgentApiPolicyDecision Deny(int statusCode, string code, string reason) =>
        new(false, statusCode, code, reason);
}

/// <summary>
/// Pure, side-effect-free authorization for the conditional Agent API. It
/// grants no approval authority and resolves no paths itself. A future route
/// adapter must first resolve saved workspace/project/model identifiers, then
/// pass only those verified facts here.
/// </summary>
public static class AgentApiPolicy
{
    public const int MaxGoalCharacters = 16_384;
    public const int MaxInstructionCharacters = 8_192;
    public const int MaxAllowedConcurrentRuns = 4;

    private static readonly HashSet<LocalApiAgentOperation> ReadOperations =
    [
        LocalApiAgentOperation.ReadTask,
        LocalApiAgentOperation.ReadRun,
        LocalApiAgentOperation.ReadOutput,
        LocalApiAgentOperation.ReadDecisions
    ];

    public static AgentApiPolicyDecision Evaluate(LocalApiTokenEntry token, AgentApiAuthorizationContext context)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(context);
        var scope = token.AgentScope;

        if (scope is null)
            return Deny(403, "agent_scope_disabled", "Agent access is disabled for this token.");
        if (scope.SchemaVersion != LocalApiAgentScope.CurrentSchemaVersion)
            return Deny(403, "unsupported_scope", "The token's Agent scope version is not supported.");
        if (!scope.Enabled)
            return Deny(403, "agent_scope_disabled", "Agent access is disabled for this token.");
        if (scope.AllowedOperations is null || !scope.AllowedOperations.Contains(context.Operation))
            return Deny(403, "operation_not_allowed", "This Agent operation is not allowed for the token.");
        if (scope.MaxConcurrentRuns is < 1 or > MaxAllowedConcurrentRuns)
            return Deny(403, "invalid_scope", "The token's Agent concurrency limit is invalid.");

        if (context.Operation == LocalApiAgentOperation.CreateTask)
        {
            if (context.TextLength is < 1 or > MaxGoalCharacters)
                return Deny(400, "invalid_goal", $"The goal must contain between 1 and {MaxGoalCharacters} characters.");
            if (string.IsNullOrWhiteSpace(context.WorkspaceProfileId)
                || !context.WorkspaceProfileExists
                || scope.AllowedWorkspaceProfileIds is null
                || !Contains(scope.AllowedWorkspaceProfileIds, context.WorkspaceProfileId))
                return Deny(403, "workspace_not_allowed", "The saved workspace profile is not allowed or unavailable.");
            if (string.IsNullOrWhiteSpace(context.ModelId))
                return Deny(403, "model_not_allowed", "The selected model is not allowed or unavailable.");
        }

        if (!string.IsNullOrWhiteSpace(context.ModelId)
            && (!context.ModelIsVisible
                || (scope.AllowedModelIds is { Count: > 0 } && !Contains(scope.AllowedModelIds, context.ModelId))))
            return Deny(403, "model_not_allowed", "The selected model is not allowed or unavailable.");

        if (!string.IsNullOrWhiteSpace(context.ProjectId)
            && (!context.ProjectExists
                || scope.AllowedProjectIds is null
                || !Contains(scope.AllowedProjectIds, context.ProjectId)))
            return Deny(403, "project_not_allowed", "The selected Project is not allowed or unavailable.");

        if (context.Operation != LocalApiAgentOperation.CreateTask)
        {
            if (!context.TaskExists)
                return Deny(404, "task_not_found", "The Agent task was not found.");
            var ownsResource = string.Equals(context.ResourceOwnerTokenId, token.Id, StringComparison.Ordinal);
            if (!ownsResource && !(ReadOperations.Contains(context.Operation) && scope.AllowReadOtherOwnedTasks))
                return Deny(404, "task_not_found", "The Agent task was not found.");
        }

        if (context.Operation == LocalApiAgentOperation.StartTask
            && context.ActiveRunsForToken >= scope.MaxConcurrentRuns)
            return Deny(429, "concurrency_limit", "The token's Agent run concurrency limit has been reached.");

        if (context.Operation is LocalApiAgentOperation.StartTask or LocalApiAgentOperation.ContinueTask)
        {
            if (context.HasPendingDecision)
                return Deny(409, "desktop_decision_required", "A pending decision must be reviewed in Desktop.");
            if (context.IsWaitingForUserAnswer)
                return Deny(409, "user_answer_required", "The task is waiting for a user answer and cannot be continued implicitly.");
        }

        if (context.Operation == LocalApiAgentOperation.SteerTask
            && context.TextLength is < 1 or > MaxInstructionCharacters)
            return Deny(400, "invalid_instruction", $"The instruction must contain between 1 and {MaxInstructionCharacters} characters.");
        if (context.Operation == LocalApiAgentOperation.ContinueTask
            && context.TextLength > MaxInstructionCharacters)
            return Deny(400, "invalid_instruction", $"The instruction cannot exceed {MaxInstructionCharacters} characters.");

        return AgentApiPolicyDecision.Permit();
    }

    private static bool Contains(IEnumerable<string> values, string candidate) =>
        values.Any(value => string.Equals(value, candidate, StringComparison.Ordinal));

    private static AgentApiPolicyDecision Deny(int statusCode, string code, string reason) =>
        AgentApiPolicyDecision.Deny(statusCode, code, reason);
}
