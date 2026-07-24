namespace Hermaeus.Core.Models;

/// <summary>
/// Configuration for the Agent workbench's autonomous loop and transcript.
/// </summary>
public class AgentSettings
{
    /// <summary>
    /// Token budget for the per-task step transcript sent to the model on
    /// each step, in addition to the freshly-built context pack.
    /// </summary>
    public int TranscriptTokenBudget { get; set; } = 12000;

    /// <summary>
    /// Maximum number of steps an autonomous run (Start/Run) executes before
    /// pausing, even if nothing required approval. A safety valve against
    /// runaway loops, not a normal stopping condition.
    /// </summary>
    public int MaxAutoSteps { get; set; } = 20;

    /// <summary>
    /// Total model steps a single orchestrated run (parent plus every child
    /// plus synthesis) may spend before remaining sub-tasks are marked
    /// Skipped and synthesis runs early. Per-child runs still respect
    /// <see cref="MaxAutoSteps"/> individually; this is the outer ceiling
    /// across the whole orchestration.
    /// </summary>
    public int MaxOrchestrationSteps { get; set; } = 60;

    /// <summary>
    /// When true, a fresh task's first autonomous run pauses after the
    /// model's first successful <c>set_plan</c> so the user can review the
    /// plan before it continues (r23 2.1). Off by default: it adds a click
    /// to every run, and some users trust plans more than opaque momentum,
    /// but that is their choice to opt into.
    /// </summary>
    public bool RequirePlanApproval { get; set; }
}
