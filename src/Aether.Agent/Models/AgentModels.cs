using Aether.Core.Models;

namespace Aether.Agent.Models;

public enum AgentTaskStatus
{
    New,
    Running,
    WaitingForUser,
    Blocked,
    Complete,
    Failed
}

public enum AgentRiskLevel
{
    None,
    Low,
    Medium,
    High
}

public enum AgentActionKind
{
    None,
    Tool,
    AskUser,
    Final
}

public enum AgentToolDisposition
{
    Allowed,
    RequiresApproval,
    Blocked
}

public enum AgentDraftPatchStatus
{
    Pending,
    Applied,
    Approved,
    Rejected,
    Blocked
}

public sealed class AgentTaskState
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
    public string Goal { get; set; } = string.Empty;
    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.New;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string ActiveStep { get; set; } = string.Empty;
    public List<string> Constraints { get; set; } = [];
    public List<string> CompletedSteps { get; set; } = [];
    public List<string> PendingSteps { get; set; } = [];
    public List<AgentDecision> Decisions { get; set; } = [];
    public List<AgentToolResult> ToolResults { get; set; } = [];
    public List<AgentApprovalRecord> ApprovalHistory { get; set; } = [];
    public List<AgentDraftPatch> DraftPatches { get; set; } = [];
    public AgentPendingToolAction? PendingToolAction { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public sealed record AgentReviewQueueItem(
    string TaskId,
    string Goal,
    AgentTaskStatus Status,
    DateTime UpdatedAt,
    string ActiveStep,
    string Summary,
    int ApprovalCount,
    string? LastApprovalAction,
    bool? LastApprovalApproved,
    DateTime? LastApprovalAt);

public sealed class AgentWorkspaceMemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record AgentDecision(
    string Decision,
    string Reason,
    DateTime Timestamp);

public sealed record AgentApprovalRecord(
    string Action,
    bool Approved,
    DateTime Timestamp);

public sealed class AgentDraftPatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RelativePath { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public string ProposedContent { get; set; } = string.Empty;
    public AgentDraftPatchStatus Status { get; set; } = AgentDraftPatchStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? BlockedAt { get; set; }
    public string? BlockedBy { get; set; }
    public string BlockReason { get; set; } = string.Empty;
}

public sealed class AgentContextPack
{
    public string CurrentGoal { get; set; } = string.Empty;
    public string ActiveStep { get; set; } = string.Empty;
    public List<string> Constraints { get; set; } = [];
    public List<string> RecentUserMessages { get; set; } = [];
    public string TaskStateSummary { get; set; } = string.Empty;
    public List<AgentRetrievedItem> RetrievedMemory { get; set; } = [];
    public List<AgentRetrievedItem> RetrievedFiles { get; set; } = [];
    public List<AgentRetrievedItem> ProjectInstructions { get; set; } = [];
    public List<AgentToolResult> ToolResults { get; set; } = [];
    public List<string> KnownRisks { get; set; } = [];
    public string RequiredOutputFormat { get; set; } =
        "Return JSON with thought_summary, current_step, next_action, state_update, and user_message.";
}

public sealed record AgentRetrievedItem(
    string Source,
    string Title,
    string Content,
    double Score,
    DateTime? Timestamp = null,
    string? Locator = null);

public sealed class AgentToolResult
{
    public string Tool { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = [];
    public string ResultSummary { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Where this result's evidence actually came from (a file path, a
    /// dataset/chunk, an MCP server:tool pair), when the tool has one clear
    /// locator. Left null for tools with no single source (e.g. run_command).
    /// </summary>
    public SourceReference? Source { get; set; }
}

public sealed class AgentPendingToolAction
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = [];
    public AgentRiskLevel RiskLevel { get; set; } = AgentRiskLevel.Medium;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentNextAction
{
    public AgentActionKind Type { get; set; } = AgentActionKind.None;
    public string? ToolName { get; set; }
    public Dictionary<string, object?> Arguments { get; set; } = [];
    public bool RequiresApproval { get; set; }
    public AgentRiskLevel RiskLevel { get; set; } = AgentRiskLevel.None;
}

public sealed class AgentPlannerResponse
{
    public string ThoughtSummary { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public AgentNextAction NextAction { get; set; } = new();
    public AgentStateUpdate StateUpdate { get; set; } = new();
    public string UserMessage { get; set; } = string.Empty;
}

public sealed class AgentStateUpdate
{
    public List<string> Completed { get; set; } = [];
    public List<string> Pending { get; set; } = [];
    public List<string> NewFacts { get; set; } = [];
    public List<string> Blockers { get; set; } = [];
}

public sealed record AgentToolPolicyDecision(
    AgentToolDisposition Disposition,
    AgentRiskLevel RiskLevel,
    string Reason);

public sealed record AgentWorkspaceOptions(
    string WorkspaceRoot,
    string? RagDatasetId = null,
    string ModelId = "",
    int MaxFileBytes = 128 * 1024,
    int MaxSearchResults = 20,
    int MaxContextItems = 6);

public sealed record AgentStepResult(
    AgentTaskState State,
    AgentContextPack ContextPack,
    AgentPlannerResponse PlannerResponse,
    string LogEntry);

public sealed record AgentTaskListItem(
    string TaskId,
    string Goal,
    AgentTaskStatus Status,
    DateTime UpdatedAt);

public sealed record AgentFileSearchResult(
    string RelativePath,
    string Snippet,
    DateTime ModifiedUtc);

public sealed record AgentFileReadResult(
    string RelativePath,
    string Content,
    bool Truncated);

public sealed record AgentFileSummaryResult(
    string RelativePath,
    string Summary,
    bool Truncated);

public sealed class WorkspaceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string PreferredModelId { get; set; } = string.Empty;
    public string PreferredEmbeddingModelId { get; set; } = string.Empty;
    public string? LinkedRagDatasetId { get; set; }
    public int WorkspaceMemoryCount { get; set; }
    public int RecentChatCount { get; set; }
    public int BenchmarkRunCount { get; set; }
    public string TrustStatus { get; set; } = "unknown";
    public string LastSummary { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record ProjectInstructionFile(
    string RelativePath,
    string Summary,
    string Content,
    bool IsPrimary);

public sealed record WorkspaceCommandRecipe(
    string Command,
    string Why,
    AgentRiskLevel RiskLevel);

public sealed class WorkspaceManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string PreferredModelId { get; set; } = string.Empty;
    public string PreferredEmbeddingModelId { get; set; } = string.Empty;
    public string? LinkedRagDatasetId { get; set; }
    public List<string> InstructionPaths { get; set; } = [];
    public List<WorkspaceCommandRecipe> AllowedCommands { get; set; } = [];
}

public sealed record WorkspaceActivation(
    string? PreferredModelId,
    string? PreferredEmbeddingModelId,
    string? LinkedRagDatasetId,
    IReadOnlyList<string> InstructionPaths,
    bool FromManifest);

public sealed class WorkspaceAnalysisReport
{
    public WorkspaceProfile Profile { get; set; } = new();
    public string RepoType { get; set; } = "unknown";
    public List<string> Languages { get; set; } = [];
    public List<string> Frameworks { get; set; } = [];
    public List<string> ImportantFiles { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<ProjectInstructionFile> Instructions { get; set; } = [];
    public List<string> InstructionWarnings { get; set; } = [];
    public List<WorkspaceCommandRecipe> CommandRecipes { get; set; } = [];
    public string SuggestedAgentsMd { get; set; } = string.Empty;
    public string RagIngestPlan { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
