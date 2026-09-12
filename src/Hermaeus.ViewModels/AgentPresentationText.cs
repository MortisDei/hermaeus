using Hermaeus.Agent.Models;

namespace Hermaeus.ViewModels;

/// <summary>
/// Keeps internal lifecycle enum names out of the primary Agent workbench
/// path. Raw values remain available in the expandable state and log panels.
/// </summary>
public static class AgentPresentationText
{
    public static string TaskStatus(AgentTaskStatus status) => status switch
    {
        AgentTaskStatus.New => "Ready to run",
        AgentTaskStatus.Running => "Running",
        AgentTaskStatus.WaitingForUser => "Waiting for you",
        AgentTaskStatus.Blocked => "Blocked",
        AgentTaskStatus.Complete => "Complete",
        AgentTaskStatus.Failed => "Failed",
        AgentTaskStatus.Cancelled => "Cancelled",
        AgentTaskStatus.Interrupted => "Recovered after interruption",
        _ => status.ToString()
    };

    public static string SubTaskStatus(AgentSubTaskStatus status) => status switch
    {
        AgentSubTaskStatus.Pending => "Queued",
        AgentSubTaskStatus.Running => "Running",
        AgentSubTaskStatus.Complete => "Complete",
        AgentSubTaskStatus.Failed => "Failed",
        AgentSubTaskStatus.Skipped => "Skipped",
        AgentSubTaskStatus.Interrupted => "Interrupted",
        _ => status.ToString()
    };

    public static string PlanStatus(AgentPlanStepStatus status) => status switch
    {
        AgentPlanStepStatus.Pending => "Next",
        AgentPlanStepStatus.InProgress => "In progress",
        AgentPlanStepStatus.Done => "Done",
        _ => status.ToString()
    };

    public static string PatchStatus(AgentDraftPatchStatus status) => status switch
    {
        AgentDraftPatchStatus.Pending => "Needs review",
        AgentDraftPatchStatus.Approved => "Approved, not applied",
        AgentDraftPatchStatus.Applied => "Applied and verified",
        AgentDraftPatchStatus.AlreadySatisfied => "Already satisfied and verified",
        AgentDraftPatchStatus.Rejected => "Rejected",
        AgentDraftPatchStatus.Blocked => "Blocked",
        AgentDraftPatchStatus.Reverted => "Reverted",
        _ => status.ToString()
    };
}
