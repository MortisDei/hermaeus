using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

/// <summary>
/// Thrown by AgentWorkspaceTools when a workspace policy denies a single-target
/// read or write, immediately after the existing containment/symlink checks
/// (r23 3.2). Caught by AgentToolExecutor and converted into a normal,
/// non-crashing AgentToolResult for reads; write callers that need a graceful
/// (non-throwing) refusal - AgentPatchReviewService's revert path - check the
/// policy directly instead of relying on this exception.
/// </summary>
public sealed class AgentWorkspacePolicyDeniedException(string message) : InvalidOperationException(message);

/// <summary>
/// Evaluates a workspace's optional read/write/never glob policy (r23 3.1,
/// docs/review/03-workspace-policy.md). Policy only ever narrows: null means
/// unrestricted, and an empty or absent allow list means "allow all" for that
/// direction. <c>never</c> beats both allow lists. Reuses
/// <see cref="AgentWorkspaceTools.GlobToRegex"/> so policy matching can never
/// diverge from what <c>glob_files</c> itself matches.
/// </summary>
public static class WorkspacePolicyEvaluator
{
    public readonly record struct Verdict(bool Allowed, string Reason)
    {
        public static readonly Verdict Ok = new(true, string.Empty);
    }

    public static Verdict EvaluateRead(WorkspacePolicy? policy, string relativePath) =>
        Evaluate(policy, relativePath, policy?.ReadAllow);

    public static Verdict EvaluateWrite(WorkspacePolicy? policy, string relativePath) =>
        Evaluate(policy, relativePath, policy?.WriteAllow);

    private static Verdict Evaluate(WorkspacePolicy? policy, string relativePath, List<string>? allowList)
    {
        if (policy is null)
            return Verdict.Ok;

        var normalized = relativePath.Replace('\\', '/');
        var neverMatch = FirstMatch(policy.Never, normalized);
        if (neverMatch is not null)
            return new Verdict(false, neverMatch);

        if (allowList is not { Count: > 0 })
            return Verdict.Ok;

        return FirstMatch(allowList, normalized) is not null
            ? Verdict.Ok
            : new Verdict(false, $"{normalized} does not match any allowed path");
    }

    private static string? FirstMatch(List<string>? patterns, string relativePath)
    {
        if (patterns is not { Count: > 0 })
            return null;

        foreach (var pattern in patterns)
        {
            if (MatchesGlob(pattern, relativePath))
                return pattern;
        }

        return null;
    }

    private static bool MatchesGlob(string pattern, string relativePath)
    {
        try { return AgentWorkspaceTools.GlobToRegex(pattern).IsMatch(relativePath); }
        catch { return false; }
    }
}
