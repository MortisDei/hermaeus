namespace Hermaeus.Agent.Services;

/// <summary>
/// Deterministic (no LLM), case-insensitive tokens that mark a lesson claim
/// as an approval-policy claim (r23 4.2/4.5). Shared by AgentService's
/// stated-lesson gate-claim filter and the forbid_active_lesson_matching
/// scenario check, so both use the exact same notion of "looks like a
/// permission claim" - the safety gate never reads the lesson store, so a
/// poisoned lesson could not widen execution today, but an unfiltered claim
/// would still sit in every future context pack and the Lessons panel as
/// persistent social engineering. Precision matters more than recall here;
/// add tokens as new phrasings turn up rather than trying to be exhaustive
/// up front.
/// </summary>
public static class AgentApprovalClaimTokens
{
    private static readonly string[] Tokens =
    [
        "approv", "no confirmation", "without asking", "without review",
        "skip review", "skip the gate", "always allow", "allow all",
        "trusted to run", "does not need permission"
    ];

    public static string? FirstMatch(string text)
    {
        foreach (var token in Tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                return token;
        }
        return null;
    }
}
