using Hermaeus.Core.Models;

namespace Hermaeus.Agent.Services;

internal enum AgentToolOutcomeSignal
{
    Completed,
    Empty,
    NoEffect,
    Partial,
    Unavailable,
    PolicyBlocked,
    ApprovalRequired,
    UserDenied,
    Failed,
    Cancelled,
    TimedOut,
    StructuredSuccess,
    StructuredFailure,
    Unclassified
}

internal sealed record AgentToolOutcomeEvidence(
    AgentToolOutcomeSignal Signal,
    int? ExitCode = null,
    string Detail = "");

/// <summary>
/// One deterministic registry keyed by the executor/tool family. Outcome is
/// derived only from structured signals supplied by the executor or approval
/// path. Model-authored result text is never inspected.
/// </summary>
internal static class AgentToolOutcomeNormalizer
{
    private enum ToolFamily
    {
        RunCommand,
        Collection,
        Read,
        Draft,
        Mutation,
        Plan,
        SafetyGate,
        Approval,
        Mcp
    }

    private static readonly IReadOnlyDictionary<string, ToolFamily> Registry =
        new Dictionary<string, ToolFamily>(StringComparer.OrdinalIgnoreCase)
        {
            ["run_command"] = ToolFamily.RunCommand,
            ["list_files"] = ToolFamily.Collection,
            ["search_files"] = ToolFamily.Collection,
            ["glob_files"] = ToolFamily.Collection,
            ["inspect_git_diff"] = ToolFamily.Collection,
            ["read_file"] = ToolFamily.Read,
            ["summarize_file"] = ToolFamily.Read,
            ["draft_patch"] = ToolFamily.Draft,
            ["apply_draft_patch"] = ToolFamily.Mutation,
            ["edit_file"] = ToolFamily.Mutation,
            ["create_file"] = ToolFamily.Mutation,
            ["plan_subtasks"] = ToolFamily.Plan,
            ["set_plan"] = ToolFamily.Plan,
            ["safety_gate"] = ToolFamily.SafetyGate,
            ["approval"] = ToolFamily.Approval
        };

    public static NormalizedToolOutcome Normalize(string toolName, AgentToolOutcomeEvidence evidence)
    {
        var trimmed = toolName.Trim();
        var family = trimmed.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase)
            ? ToolFamily.Mcp
            : Registry.TryGetValue(trimmed, out var registered)
                ? registered
                : (ToolFamily?)null;

        return family switch
        {
            ToolFamily.RunCommand => NormalizeRunCommand(evidence),
            ToolFamily.Collection => NormalizeCollection(evidence),
            ToolFamily.Read or ToolFamily.Draft => NormalizeCompletedOperation(evidence),
            ToolFamily.Mutation => NormalizeMutation(evidence),
            ToolFamily.Plan => NormalizePlan(evidence),
            ToolFamily.SafetyGate => NormalizeSafetyGate(evidence),
            ToolFamily.Approval => NormalizeApproval(evidence),
            ToolFamily.Mcp => NormalizeMcp(evidence),
            _ when evidence.Signal == AgentToolOutcomeSignal.Unavailable =>
                Create(NormalizedOutcome.Unavailable, "tool-executor-unavailable", evidence.Detail),
            _ => Create(NormalizedOutcome.Unknown, "unregistered-outcome-normalizer",
                "No deterministic outcome normalizer is registered for this tool.")
        };
    }

    private static NormalizedToolOutcome NormalizeRunCommand(AgentToolOutcomeEvidence evidence) => evidence.Signal switch
    {
        AgentToolOutcomeSignal.TimedOut => Create(NormalizedOutcome.TimedOut, "process-timeout", evidence.Detail),
        AgentToolOutcomeSignal.Cancelled => Create(NormalizedOutcome.Cancelled, "caller-cancelled", evidence.Detail),
        AgentToolOutcomeSignal.Unavailable => Create(NormalizedOutcome.Unavailable, "process-executable-missing", evidence.Detail),
        AgentToolOutcomeSignal.PolicyBlocked => Create(NormalizedOutcome.Blocked, "command-validation-blocked", evidence.Detail),
        AgentToolOutcomeSignal.Failed when evidence.ExitCode is null => Create(NormalizedOutcome.Failed, "process-start-failed", evidence.Detail),
        AgentToolOutcomeSignal.Completed when evidence.ExitCode == 0 => Create(NormalizedOutcome.Succeeded, "process-exit-zero", evidence.Detail),
        AgentToolOutcomeSignal.Completed when evidence.ExitCode is not null => Create(NormalizedOutcome.Failed, "process-exit-nonzero", evidence.Detail),
        _ => Create(NormalizedOutcome.Unknown, "process-outcome-unknown", evidence.Detail)
    };

    private static NormalizedToolOutcome NormalizeCollection(AgentToolOutcomeEvidence evidence) => evidence.Signal switch
    {
        AgentToolOutcomeSignal.Empty or AgentToolOutcomeSignal.NoEffect => Create(NormalizedOutcome.NoEffect, "workspace-result-empty", evidence.Detail),
        AgentToolOutcomeSignal.Completed => Create(NormalizedOutcome.Succeeded, "workspace-operation-completed", evidence.Detail),
        _ => NormalizeCommon(evidence)
    };

    private static NormalizedToolOutcome NormalizeCompletedOperation(AgentToolOutcomeEvidence evidence) => evidence.Signal switch
    {
        AgentToolOutcomeSignal.Completed => Create(NormalizedOutcome.Succeeded, "workspace-operation-completed", evidence.Detail),
        AgentToolOutcomeSignal.NoEffect => Create(NormalizedOutcome.NoEffect, "workspace-no-effect", evidence.Detail),
        _ => NormalizeCommon(evidence)
    };

    private static NormalizedToolOutcome NormalizeMutation(AgentToolOutcomeEvidence evidence) => evidence.Signal switch
    {
        AgentToolOutcomeSignal.Completed => Create(NormalizedOutcome.Succeeded, "workspace-mutation-completed", evidence.Detail),
        AgentToolOutcomeSignal.NoEffect => Create(NormalizedOutcome.NoEffect, "workspace-content-already-matched", evidence.Detail),
        AgentToolOutcomeSignal.Partial => Create(NormalizedOutcome.PartiallySucceeded, "workspace-mutation-partial", evidence.Detail),
        _ => NormalizeCommon(evidence)
    };

    private static NormalizedToolOutcome NormalizePlan(AgentToolOutcomeEvidence evidence) => evidence.Signal switch
    {
        AgentToolOutcomeSignal.Completed => Create(NormalizedOutcome.Succeeded, "plan-state-updated", evidence.Detail),
        AgentToolOutcomeSignal.NoEffect => Create(NormalizedOutcome.NoEffect, "plan-state-unchanged", evidence.Detail),
        AgentToolOutcomeSignal.PolicyBlocked => Create(NormalizedOutcome.Blocked, "plan-validation-blocked", evidence.Detail),
        _ => NormalizeCommon(evidence)
    };

    private static NormalizedToolOutcome NormalizeSafetyGate(AgentToolOutcomeEvidence evidence) => evidence.Signal switch
    {
        AgentToolOutcomeSignal.Completed => Create(NormalizedOutcome.Succeeded, "safety-gate-allowed", evidence.Detail),
        AgentToolOutcomeSignal.ApprovalRequired => Create(NormalizedOutcome.Blocked, "approval-required", evidence.Detail),
        AgentToolOutcomeSignal.PolicyBlocked => Create(NormalizedOutcome.Blocked, "safety-gate-blocked", evidence.Detail),
        _ => NormalizeCommon(evidence)
    };

    private static NormalizedToolOutcome NormalizeApproval(AgentToolOutcomeEvidence evidence) => evidence.Signal switch
    {
        AgentToolOutcomeSignal.Completed => Create(NormalizedOutcome.Succeeded, "approval-recorded", evidence.Detail),
        AgentToolOutcomeSignal.UserDenied => Create(NormalizedOutcome.Denied, "user-denied", evidence.Detail),
        AgentToolOutcomeSignal.PolicyBlocked => Create(NormalizedOutcome.Blocked, "approval-fingerprint-blocked", evidence.Detail),
        _ => NormalizeCommon(evidence)
    };

    private static NormalizedToolOutcome NormalizeMcp(AgentToolOutcomeEvidence evidence) => evidence.Signal switch
    {
        AgentToolOutcomeSignal.StructuredSuccess => Create(NormalizedOutcome.Succeeded, "mcp-structured-success", evidence.Detail),
        AgentToolOutcomeSignal.StructuredFailure => Create(NormalizedOutcome.Failed, "mcp-structured-error", evidence.Detail),
        AgentToolOutcomeSignal.Cancelled => Create(NormalizedOutcome.Cancelled, "caller-cancelled", evidence.Detail),
        AgentToolOutcomeSignal.Unavailable => Create(NormalizedOutcome.Unavailable, "mcp-tool-unavailable", evidence.Detail),
        AgentToolOutcomeSignal.Failed => Create(NormalizedOutcome.Failed, "mcp-call-failed", evidence.Detail),
        _ => Create(NormalizedOutcome.Unknown, "mcp-no-result-contract", evidence.Detail)
    };

    private static NormalizedToolOutcome NormalizeCommon(AgentToolOutcomeEvidence evidence) => evidence.Signal switch
    {
        AgentToolOutcomeSignal.Unavailable => Create(NormalizedOutcome.Unavailable, "workspace-target-unavailable", evidence.Detail),
        AgentToolOutcomeSignal.PolicyBlocked => Create(NormalizedOutcome.Blocked, "workspace-policy-blocked", evidence.Detail),
        AgentToolOutcomeSignal.Cancelled => Create(NormalizedOutcome.Cancelled, "caller-cancelled", evidence.Detail),
        AgentToolOutcomeSignal.TimedOut => Create(NormalizedOutcome.TimedOut, "operation-timeout", evidence.Detail),
        AgentToolOutcomeSignal.Failed => Create(NormalizedOutcome.Failed, "tool-execution-failed", evidence.Detail),
        AgentToolOutcomeSignal.Partial => Create(NormalizedOutcome.PartiallySucceeded, "operation-partial", evidence.Detail),
        _ => Create(NormalizedOutcome.Unknown, "tool-outcome-unknown", evidence.Detail)
    };

    private static NormalizedToolOutcome Create(NormalizedOutcome outcome, string code, string detail) =>
        NormalizedToolOutcome.Create(outcome, code, string.IsNullOrWhiteSpace(detail) ? code : detail);
}
