namespace Hermaeus.Agent.Services;

/// <summary>
/// r29 doc 03: the shared constants for steering a running task.
///
/// The boundary, stated once and enforced everywhere it matters: an injected
/// instruction is user text and nothing else. It never carries an approval, it
/// never sets <c>requires_approval</c>, and it never changes a risk
/// classification. <c>AgentSafetyGate</c> is not involved in this feature at
/// all, and is not edited by it.
/// </summary>
public static class AgentSteering
{
    /// <summary>
    /// The <see cref="Hermaeus.Agent.Models.AgentDecision.Decision"/> value that
    /// marks a drained steering instruction, so the context builder can find
    /// them again without a second list on the task state.
    /// </summary>
    public const string DecisionKey = "User steering instruction";

    /// <summary>
    /// How many instructions may be queued at once. Matches the context
    /// builder's Constraints.Take(8). A full queue refuses with a message
    /// rather than silently dropping: an instruction that vanishes is worse
    /// than one that is refused.
    /// </summary>
    public const int MaxPending = 8;
}
