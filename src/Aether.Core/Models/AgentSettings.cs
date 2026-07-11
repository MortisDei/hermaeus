namespace Aether.Core.Models;

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
}
