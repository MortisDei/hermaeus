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

/// <summary>Where a lesson applies: every task in every workspace, or one specific workspace.</summary>
public enum AgentLessonScope
{
    Global,
    Workspace
}

/// <summary>What kind of evidence produced this lesson.</summary>
public enum AgentLessonKind
{
    Command,
    Patch,
    Approval,
    Task,
    Stated
}

public enum AgentLessonOutcome
{
    Worked,
    Failed,
    UserRejected,
    Observation
}

public enum AgentLessonStatus
{
    Active,
    Retired
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
    public int StepCount { get; set; }
    /// <summary>
    /// Consecutive steps in a row whose model response could not be parsed
    /// as valid JSON. Reset to 0 on any step that parses successfully; at 3
    /// the task moves to <see cref="AgentTaskStatus.Failed"/> instead of
    /// looping forever in <see cref="AgentTaskStatus.WaitingForUser"/>.
    /// </summary>
    public int ConsecutiveStepErrors { get; set; }
    /// <summary>
    /// Total parse failures across the task's whole lifetime; unlike
    /// <see cref="ConsecutiveStepErrors"/> this never resets, so a
    /// terminal-state lesson can tell an uneventful success apart from one
    /// that recovered from trouble along the way.
    /// </summary>
    public int TotalStepErrors { get; set; }
    /// <summary>
    /// Ids of every lesson that has appeared in this task's context pack
    /// across all steps. On a successful completion, each is confirmed
    /// (evidence bumped) via <see cref="Aether.Agent.Services.ILessonStore"/> -
    /// the compounding half of the self-learning loop.
    /// </summary>
    public List<string> InjectedLessonIds { get; set; } = [];
    /// <summary>
    /// The model's current plan, replaced atomically by the set_plan tool.
    /// Purely informational task-state, like <see cref="CompletedSteps"/>/
    /// <see cref="PendingSteps"/>; it cannot authorize or bypass anything.
    /// </summary>
    public List<AgentPlanStep> Plan { get; set; } = [];
    /// <summary>
    /// Exact run_command strings the user has already approved once in this
    /// task; a later request for the identical command may auto-execute
    /// instead of pausing for approval again. Scoped to the task (never
    /// persists beyond it) and to the exact command string (never a
    /// different command, even in the same family). See AgentSafetyGate and
    /// AgentService.AppendApprovalAsync.
    /// </summary>
    public List<string> RememberedCommandApprovals { get; set; } = [];
}

public enum AgentPlanStepStatus
{
    Pending,
    InProgress,
    Done
}

public sealed class AgentPlanStep
{
    public string Description { get; set; } = string.Empty;
    public AgentPlanStepStatus Status { get; set; } = AgentPlanStepStatus.Pending;
}

/// <summary>
/// A deterministic, evidence-backed observation about what works or fails on
/// this machine/workspace: "dotnet test fails in this workspace with
/// CS0246 unless X". Confidence grows with repeated matching evidence and
/// falls (eventually retiring the lesson) when new evidence contradicts it.
/// Never authorizes anything on its own - the safety gate never reads this.
/// </summary>
public sealed class AgentLesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AgentLessonScope Scope { get; set; } = AgentLessonScope.Global;
    /// <summary>Normalized workspace root for Workspace scope; empty for Global.</summary>
    public string ScopeId { get; set; } = string.Empty;
    public AgentLessonKind Kind { get; set; } = AgentLessonKind.Task;
    /// <summary>Dedupe key, e.g. "command:dotnet test:exit!=0:CS0246".</summary>
    public string Signature { get; set; } = string.Empty;
    public string Claim { get; set; } = string.Empty;
    public string Guidance { get; set; } = string.Empty;
    public AgentLessonOutcome Outcome { get; set; } = AgentLessonOutcome.Observation;
    public double Confidence { get; set; } = 0.3;
    public int EvidenceCount { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastConfirmedAt { get; set; } = DateTime.UtcNow;
    public AgentLessonStatus Status { get; set; } = AgentLessonStatus.Active;
    public bool IsPinned { get; set; }
    /// <summary>Task id(s) this lesson's evidence came from, most recent first, bounded.</summary>
    public List<string> SourceTaskIds { get; set; } = [];
}

/// <summary>One piece of evidence to record against a lesson, identified by its signature.</summary>
public sealed record AgentLessonEvidence(
    AgentLessonScope Scope,
    string ScopeId,
    AgentLessonKind Kind,
    string Signature,
    string Claim,
    string Guidance,
    AgentLessonOutcome Outcome,
    string? SourceTaskId = null,
    /// <summary>
    /// When true and no lesson with this signature exists yet, the store
    /// writes nothing instead of creating one. For evidence that should
    /// only ever counter an existing claim (e.g. an approval confirming a
    /// tool the user previously rejected), never originate one on its own.
    /// </summary>
    bool CounterOnly = false);

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
    /// <summary>
    /// Budgeted replay of the task's persisted step transcript (assistant
    /// thoughts and tool results across all prior steps, not just the last
    /// five), most recent steps prioritized when the budget can't fit all of
    /// it. See transcript.jsonl and AgentContextBuilder.
    /// </summary>
    public List<AgentRetrievedItem> TranscriptHistory { get; set; } = [];
    /// <summary>
    /// Deterministic, evidence-backed lessons relevant to this task's
    /// workspace (plus any global ones), most confident first. Content
    /// includes the confidence and evidence count so the model can weigh
    /// them; see AgentService's system prompt and SqliteLessonStore.
    /// </summary>
    public List<AgentRetrievedItem> Lessons { get; set; } = [];
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

    /// <summary>run_command's process exit code; null for every other tool and for a timed-out command.</summary>
    public int? ExitCode { get; set; }
    /// <summary>True only when run_command hit its 5-minute timeout and was killed; ExitCode is meaningless then.</summary>
    public bool TimedOut { get; set; }
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

/// <summary>
/// One entry in a task's persisted step transcript (transcript.jsonl). Unlike
/// the free-form agent.trace.jsonl audit log, this is what gets replayed back
/// into the model's context on the next step, so its shape and size matter.
/// </summary>
public sealed record AgentTranscriptEntry(
    int Step,
    string Role, // "assistant" | "tool" | "user"
    string? ToolName,
    string Content,
    DateTime Timestamp);

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

/// <summary>
/// Resolves a <see cref="WorkspaceActivation"/>'s preferred-id fields against
/// a ViewModel's already-loaded candidate list. Both Chat and Agent used to
/// duplicate this id-to-object lookup inline.
/// </summary>
public static class WorkspaceActivationSelection
{
    public static T? ResolvePreferredModel<T>(this WorkspaceActivation activation, IEnumerable<T> candidates, Func<T, string> id) =>
        string.IsNullOrWhiteSpace(activation.PreferredModelId)
            ? default
            : candidates.FirstOrDefault(c => id(c) == activation.PreferredModelId);

    public static T? ResolveLinkedDataset<T>(this WorkspaceActivation activation, IEnumerable<T> candidates, Func<T, string> id) =>
        string.IsNullOrWhiteSpace(activation.LinkedRagDatasetId)
            ? default
            : candidates.FirstOrDefault(c => id(c) == activation.LinkedRagDatasetId);
}

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
