using Hermaeus.Core.Models;

namespace Hermaeus.Agent.Models;

public enum AgentTaskStatus
{
    New,
    Running,
    WaitingForUser,
    Blocked,
    Complete,
    Failed,

    /// <summary>
    /// The user abandoned the task instead of answering it. Terminal, and the
    /// only status the user assigns directly (every other one is the run's own
    /// account of itself). Appended last deliberately: status is persisted by
    /// name, but an appended member keeps any numeric reading stable too.
    ///
    /// Exists because a paused task had no way out. Rejecting a pending action
    /// returned the task to WaitingForUser, so a run the user had finished
    /// with sat in the review queue forever with no action that could clear it.
    /// </summary>
    Cancelled
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
    Blocked,
    Reverted
}

/// <summary>Lifecycle of one entry in a parent task's <see cref="AgentTaskState.SubTaskPlan"/>.</summary>
public enum AgentSubTaskStatus
{
    Pending,
    Running,
    Complete,
    Failed,
    Skipped
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

    /// <summary>
    /// Instructions the user sent while the task was running, not yet folded
    /// into the planner context. Drained at the next step boundary, in order.
    /// User text: carries no approval and no risk decision (r29 doc 03).
    /// Empty for pre-existing task files (JSON-additive).
    /// </summary>
    public List<AgentSteeringNote> PendingInstructions { get; set; } = [];
    public List<AgentDraftPatch> DraftPatches { get; set; } = [];
    public AgentPendingToolAction? PendingToolAction { get; set; }
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// The model's own last message to the user (the planner's
    /// <c>user_message</c>), which is what an <c>ask_user</c> step's question
    /// actually is. It used to go only to the log and the transcript, so a
    /// task paused on a question showed a reply box with no question next to
    /// it and the user had no way to see what they were being asked. Empty
    /// for pre-existing task files (JSON-additive).
    /// </summary>
    public string LastUserMessage { get; set; } = string.Empty;

    /// <summary>
    /// True when an autonomous run stopped because it reached MaxAutoSteps.
    /// This is distinct from an ask_user wait, so the workbench cannot render
    /// a reply box for a pause that has no question. JSON-additive for older
    /// task files.
    /// </summary>
    public bool StepBudgetExhausted { get; set; }

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
    /// Whether the most recent planner call was sent with the protocol schema
    /// enforced by the provider's sampler (r28 doc 05 5.6). Read beside
    /// <see cref="TotalStepErrors"/>: it is the only way to tell a run whose
    /// shape was guaranteed from one that was asked nicely, without running a
    /// benchmark. Not a metric panel, not a comparison view.
    /// </summary>
    public bool PlannerConstrained { get; set; }
    /// <summary>
    /// Ids of every lesson that has appeared in this task's context pack
    /// across all steps. On a successful completion, each is confirmed
    /// (evidence bumped) via <see cref="Hermaeus.Agent.Services.ILessonStore"/> -
    /// the compounding half of the self-learning loop.
    /// </summary>
    public List<string> InjectedLessonIds { get; set; } = [];
    /// <summary>
    /// Ids of lessons actually created (not merely reinforced or confirmed)
    /// by this task, in creation order. Drives the "new lessons" review
    /// strip so a freshly captured lesson is seen once by a human before it
    /// can influence future prompts (r6 03-platform-cleanup.md 3.3).
    /// </summary>
    public List<string> NewLessonIds { get; set; } = [];
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
    /// <summary>
    /// Non-null only on a child task created by an approved <c>plan_subtasks</c>
    /// action (r15 01-subtask-orchestration.md 1.1). A child can never itself
    /// gain a <see cref="SubTaskPlan"/> - depth is limited to one level, and
    /// that limit is enforced in code (AgentService.RunStepAsync), not by the
    /// model.
    /// </summary>
    public string? ParentTaskId { get; set; }
    /// <summary>
    /// The workspace root this task was created against, captured once at
    /// creation time (r16 01-orchestration-hardening.md 1.4). Empty for
    /// pre-r16 task state files (JSON-additive); a pending approval on such
    /// a task falls back to the caller-supplied options as before. Approval
    /// execution and resumed steps use this instead of whatever workspace
    /// happens to be active in the workbench, so an action always lands
    /// where the user actually approved it.
    /// </summary>
    public string WorkspaceRoot { get; set; } = string.Empty;
    /// <summary>
    /// r24 doc 01: the project this task was created under, if any, captured
    /// once at creation time. Switching the active project never changes it,
    /// including while the task is running (doc 01 1.3). Empty for pre-r24
    /// task state files (JSON-additive).
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;
    /// <summary>The model frozen for this task at its first planner boundary.
    /// Empty on legacy tasks until they next run.</summary>
    public string ModelId { get; set; } = string.Empty;
    public string ModelDisplayName { get; set; } = string.Empty;
    /// <summary>
    /// Populated only on a parent task, only by an approved <c>plan_subtasks</c>
    /// action. Empty for every ordinary task and for every child task.
    /// </summary>
    public List<AgentSubTaskSpec> SubTaskPlan { get; set; } = [];
    /// <summary>
    /// Total model steps spent across every child plus synthesis under this
    /// parent's orchestration loop, checked against
    /// <see cref="Hermaeus.Core.Models.AgentSettings.MaxOrchestrationSteps"/>.
    /// Always 0 for a task that is not an orchestration parent.
    /// </summary>
    public int OrchestrationStepsUsed { get; set; }
    /// <summary>
    /// Successful read_file/summarize_file executions so far this task,
    /// checked against the workspace policy's MaxFileReadsPerTask (r23 3.1).
    /// Persisted so a restart does not reset the budget.
    /// </summary>
    public int FileReadCount { get; set; }
    /// <summary>
    /// Set true the first time the plan-approval checkpoint pauses this task
    /// (r23 2.1, AgentSettings.RequirePlanApproval) and never cleared
    /// afterward, so the checkpoint fires at most once per task even across
    /// a restart. See <see cref="PlanApprovalPending"/> for whether the pause
    /// is currently active.
    /// </summary>
    public bool PlanApprovalCheckpointFired { get; set; }
    /// <summary>
    /// True only while the task is actively paused at the plan-approval
    /// checkpoint (r23 2.1): distinguishes that pause from an ordinary
    /// ask_user reply-wait, since both otherwise look identical (WaitingForUser
    /// with no PendingToolAction). Cleared as soon as the run resumes.
    /// </summary>
    public bool PlanApprovalPending { get; set; }
    /// <summary>Step count at which set_plan last replaced a non-empty plan (r23 2.2); null until the first revision.</summary>
    public int? PlanRevisedAtStep { get; set; }
    /// <summary>
    /// Set from the model's final answer when non-empty (r23 2.3): things it
    /// looked for and could not verify or finish. Status stays Complete;
    /// this is presentation ("Completed with reservations") plus persisted
    /// metadata, not a new state machine value. Empty means nothing to show.
    /// </summary>
    public List<string> Reservations { get; set; } = [];
}

/// <summary>
/// One planned sub-task from an approved <c>plan_subtasks</c> action. See
/// docs/review/01-subtask-orchestration.md 1.1.
/// </summary>
public sealed class AgentSubTaskSpec
{
    public string Goal { get; set; } = string.Empty;
    /// <summary>Key into the fixed <see cref="Hermaeus.Agent.Services.AgentSpecialistProfiles"/> catalog.</summary>
    public string ProfileName { get; set; } = string.Empty;
    public string SuccessCriteria { get; set; } = string.Empty;
    /// <summary>Explicit proposed/user-selected child model. Empty means inherit parent.</summary>
    public string ModelId { get; set; } = string.Empty;
    /// <summary>Actual model resolved when the plan is approved.</summary>
    public string ResolvedModelId { get; set; } = string.Empty;
    public string ModelDisplayName { get; set; } = string.Empty;
    public string ModelLabel => string.IsNullOrWhiteSpace(ResolvedModelId)
        ? "inherit parent (legacy unresolved)"
        : string.IsNullOrWhiteSpace(ModelId)
            ? $"inherit {(string.IsNullOrWhiteSpace(ModelDisplayName) ? ResolvedModelId : ModelDisplayName)} ({ResolvedModelId})"
            : $"{(string.IsNullOrWhiteSpace(ModelDisplayName) ? ResolvedModelId : ModelDisplayName)} ({ResolvedModelId})";
    public AgentSubTaskStatus Status { get; set; } = AgentSubTaskStatus.Pending;
    /// <summary>Set when the child task is actually created (lazily, at execution time - not at plan-approval time).</summary>
    public string? TaskId { get; set; }
    /// <summary>Bounded copy of the child's outcome (its Summary plus final user message, truncated), written at child terminal state.</summary>
    public string ResultSummary { get; set; } = string.Empty;
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
    DateTime? LastApprovalAt,
    /// <summary>
    /// The gated action actually waiting on this task, if any - populated
    /// from a full task load (the index table this queue is built from does
    /// not carry it), so the queue can show risk and reason instead of a
    /// bare status (r6 01-first-five-minutes.md 1.7).
    /// </summary>
    AgentPendingToolAction? PendingToolAction = null,
    /// <summary>The parent task's goal, populated only when this entry is a child task (r15 02-orchestration-ui.md 2.3), so the review queue can show "for: &lt;parent goal&gt;".</summary>
    string? ParentGoal = null,
    /// <summary>
    /// The parent task's id, populated only when this entry is a child task
    /// (r16 01-orchestration-hardening.md 1.1). Approving a child's pending
    /// action resumes this id instead of the child's, so orchestration
    /// advances even when the child (not the parent) is the task open in
    /// the workbench.
    /// </summary>
    string? ParentTaskId = null,
    /// <summary>
    /// The task's stored workspace root (r16 01-orchestration-hardening.md
    /// 1.4), populated from a full task load for items that carry a pending
    /// action. Empty for pre-r16 tasks. Lets the review queue show a task's
    /// workspace folder when it differs from the currently active one.
    /// </summary>
    string? WorkspaceRoot = null);

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

/// <summary>
/// r29 doc 03: an instruction the user sent while the task was running, waiting
/// to be folded into the planner context at the next step boundary.
///
/// This is USER TEXT and nothing else, exactly as untrusted as the goal the
/// task was created with. It carries no approval, sets no risk classification,
/// and never marks a tool pre-approved.
/// </summary>
/// <param name="Text">What the user typed.</param>
/// <param name="ReceivedAt">When it was accepted, UTC.</param>
/// <param name="StepCount">The task's step count when it arrived.</param>
public sealed record AgentSteeringNote(
    string Text,
    DateTime ReceivedAt,
    int StepCount);

/// <summary>Outcome of <c>AgentService.SteerTaskAsync</c>.</summary>
/// <param name="Accepted">False when the instruction was refused.</param>
/// <param name="Message">The reason, when refused; empty when accepted.</param>
public sealed record AgentSteeringResult(bool Accepted, string Message)
{
    public static AgentSteeringResult Ok() => new(true, string.Empty);
    public static AgentSteeringResult Refused(string message) => new(false, message);
}

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

    /// <summary>
    /// File content immediately before this patch was applied, captured at
    /// apply time; null means the file did not exist (revert deletes it).
    /// JSON-additive, so pre-r6 applied patches simply have no revert
    /// capability (r6 01-first-five-minutes.md 1.8).
    /// </summary>
    public string? PreImageContent { get; set; }
    public bool PreImageExisted { get; set; }

    /// <summary>
    /// Exact file content on disk immediately after this patch was applied.
    /// Revert compares the file's current content against this before
    /// restoring the pre-image, so a later edit is never silently clobbered.
    /// </summary>
    public string AppliedContent { get; set; } = string.Empty;

    public DateTime? RevertedAt { get; set; }
    public string? RevertedBy { get; set; }
}

/// <summary>Outcome of attempting to revert an applied patch.</summary>
public sealed record AgentRevertResult(bool Reverted, string Message)
{
    public NormalizedToolOutcome NormalizedOutcome { get; init; } = new();
}

/// <summary>One file's outcome within a whole-run Task Rewind (r23 1.3).</summary>
public sealed record AgentTaskRevertFileOutcome(string RelativePath, bool Reverted, string Message)
{
    public NormalizedToolOutcome NormalizedOutcome { get; init; } = new();
}

/// <summary>
/// Outcome of AgentPatchReviewService.RevertTaskAsync: a truthful partial-success
/// report. Reverting a run never overwrites content changed after the run, so
/// some files can be skipped while others succeed (r23 1.3).
/// </summary>
public sealed record AgentTaskRevertResult(IReadOnlyList<AgentTaskRevertFileOutcome> Files, string Summary)
{
    public int RevertedCount => Files.Count(f => f.Reverted);
    public int TotalCount => Files.Count;
    public NormalizedToolOutcome NormalizedOutcome { get; init; } = new();
}

/// <summary>
/// Outcome of an approval decision (AgentService.AppendApprovalAsync).
/// <see cref="Applied"/> is false only when the approval fingerprint did not
/// match the currently pending action (r23 4.1); the pending action stays
/// pending and nothing executed.
/// </summary>
public sealed record AgentApprovalResult(bool Applied, string Message);

public enum AgentLedgerFileKind { Created, Edited }

/// <summary>
/// A file entry's current status in the ledger. The builder
/// (AgentRunLedgerBuilder) only ever produces Applied or Reverted, derived
/// from persisted patch state; Conflicted is set by a caller that has
/// workspace access, after comparing <see cref="AgentLedgerFileEntry.LatestAppliedContent"/>
/// against the file's live content (r23 1.1 - the builder never touches disk).
/// </summary>
public enum AgentLedgerFileStatus { Applied, Reverted, Conflicted }

/// <summary>One distinct file a run touched, folded across every applied/reverted patch for that path.</summary>
public sealed record AgentLedgerFileEntry(
    string RelativePath,
    AgentLedgerFileKind Kind,
    int AppliedPatchCount,
    AgentLedgerFileStatus Status,
    int LineDelta,
    /// <summary>Content before the run first touched the file; null for a created file. The "before" half of the ledger UI's before/after preview.</summary>
    string? EarliestPreImageContent,
    /// <summary>The most recent applied patch's content: the "after" half of the preview, and what a caller diffs against the live file to detect Conflicted.</summary>
    string LatestAppliedContent,
    /// <summary>The task this entry came from: the run's own id, or a child's when folded in from a sub-task (r23 1.1).</summary>
    string TaskId);

/// <summary>One run_command execution the ledger surfaces (r23 1.1).</summary>
public sealed record AgentLedgerCommandEntry(
    string Command,
    int? ExitCode,
    bool TimedOut,
    DateTime Timestamp,
    string TaskId);

/// <summary>One approval decision the ledger surfaces (r23 1.1).</summary>
public sealed record AgentLedgerApprovalEntry(
    string Action,
    bool Approved,
    DateTime Timestamp,
    string TaskId);

/// <summary>One sub-task line for an orchestration parent's ledger (r23 1.1).</summary>
public sealed record AgentLedgerSubTaskEntry(
    string Goal,
    AgentSubTaskStatus Status,
    string? TaskId,
    string ModelId = "",
    string ModelDisplayName = "");

/// <summary>A run's total footprint: every file, command, and approval it produced (r23 1.1, the Run Ledger).</summary>
public sealed record AgentRunLedger(
    IReadOnlyList<AgentLedgerFileEntry> Files,
    IReadOnlyList<AgentLedgerCommandEntry> Commands,
    IReadOnlyList<AgentLedgerApprovalEntry> Approvals,
    IReadOnlyList<AgentLedgerSubTaskEntry> SubTasks)
{
    public bool IsEmpty => Files.Count == 0 && Commands.Count == 0 && Approvals.Count == 0;
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
    /// <summary>Accepted, bounded, user-owned Project State. Empty when the task
    /// is not project-bound or the project has no accepted state.</summary>
    public List<AgentRetrievedItem> ProjectState { get; set; } = [];
    /// <summary>The frozen model producing this planner step.</summary>
    public List<AgentRetrievedItem> ModelIdentity { get; set; } = [];
    /// <summary>Bounded visible models a parent may name in a plan_subtasks proposal.</summary>
    public List<AgentRetrievedItem> EligibleModels { get; set; } = [];
    public List<AgentToolResult> ToolResults { get; set; } = [];
    /// <summary>
    /// Budgeted replay of the task's persisted step transcript (assistant
    /// thoughts and tool results across all prior steps, not just the last
    /// five), most recent steps prioritized when the budget can't fit all of
    /// it. See transcript.jsonl and AgentContextBuilder.
    /// </summary>
    public List<AgentRetrievedItem> TranscriptHistory { get; set; } = [];
    /// <summary>
    /// Informational notices produced while compacting transcript replay. They
    /// never affect tool approval, task status, or loop execution.
    /// </summary>
    public List<string> TranscriptDiagnostics { get; set; } = [];
    /// <summary>
    /// Deterministic, evidence-backed lessons relevant to this task's
    /// workspace (plus any global ones), most confident first. Content
    /// includes the confidence and evidence count so the model can weigh
    /// them; see AgentService's system prompt and SqliteLessonStore.
    /// </summary>
    public List<AgentRetrievedItem> Lessons { get; set; } = [];
    /// <summary>
    /// Populated only for an orchestration parent (see <see cref="AgentTaskState.SubTaskPlan"/>):
    /// one item per sub-task spec (goal, profile, status, ResultSummary),
    /// most recent children favored if the budget cannot fit all of them.
    /// Empty, and omitted from the context receipt, for every other task.
    /// </summary>
    public List<AgentRetrievedItem> SubTaskReports { get; set; } = [];
    /// <summary>
    /// r29 doc 03 3.3: instructions the user sent mid-run, delivered to the
    /// model as the user's own words arriving late. Deliberately not in the
    /// system prompt: these are as untrusted as the goal, they carry no
    /// authority, and nothing here can approve a tool or change a risk level.
    /// </summary>
    public List<string> SteeringInstructions { get; set; } = [];
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

    /// <summary>
    /// Deterministic semantic interpretation of the raw fields above. Missing
    /// pre-R31 JSON keeps the legacy Unknown default and is never guessed from
    /// ResultSummary text.
    /// </summary>
    public NormalizedToolOutcome NormalizedOutcome { get; set; } = new();

}

public sealed class AgentPendingToolAction
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = [];
    public AgentRiskLevel RiskLevel { get; set; } = AgentRiskLevel.Medium;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Why the safety gate gated this action, from
    /// <see cref="AgentToolPolicyDecision"/>.Reason - shown in the approval
    /// UI so risk is never a bare, unexplained label (r6 01-first-five-minutes.md 1.7).
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// SHA256 (hex) over the tool name and canonicalized arguments, computed
    /// once when this action is created (AgentApprovalFingerprint.Compute).
    /// AppendApprovalAsync refuses to execute an approval whose caller-supplied
    /// expected fingerprint does not match this, binding what was displayed to
    /// what actually executes (r23 4.1). Empty on pre-r23 persisted state; the
    /// approval path recomputes from ToolName/Arguments in that case.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;
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
    /// <summary>
    /// Optional, model-provided (r23 2.3): things it looked for and could
    /// not verify or finish, on a final answer. Never required and never
    /// nagged for; an empty or absent list means nothing is shown. Not a
    /// confidence score - each entry is a specific, honest statement, not a
    /// number dressing a vibe as measurement.
    /// </summary>
    public List<string> Reservations { get; set; } = [];
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
    /// <summary>
    /// Cap for a directory listing, which is a different job from a search hit
    /// list and needs a far bigger budget. Sharing MaxSearchResults meant
    /// list_files returned 20 entries for an entire workspace and said nothing
    /// about the rest, so a real run concluded a top-level folder did not exist
    /// when the listing had simply stopped before reaching it.
    /// </summary>
    int MaxListResults = 500,
    int MaxContextItems = 6,
    /// <summary>The active workspace's manifest policy, if any (r23 3.1); null means unrestricted. Set by the caller from a loaded WorkspaceManifest, never by AgentWorkspaceTools itself.</summary>
    WorkspacePolicy? Policy = null,
    /// <summary>Mutable read-count tracker shared by reference across a step's tool calls, so AgentWorkspaceTools can enforce and increment MaxFileReadsPerTask in one place (r23 3.1). Null disables the cap regardless of policy.</summary>
    AgentReadBudget? ReadBudget = null);

/// <summary>
/// Tracks read_file/summarize_file executions against a workspace policy's
/// MaxFileReadsPerTask for one agent step (r23 3.1). Seeded from
/// AgentTaskState.FileReadCount by the caller and read back afterward so the
/// budget persists across steps and restarts.
/// </summary>
public sealed class AgentReadBudget
{
    public int MaxReads { get; init; }
    public int UsedReads { get; set; }
    public bool IsExhausted => MaxReads > 0 && UsedReads >= MaxReads;
}

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
    DateTime Timestamp,
    string? ArgumentsCanonical = null,
    bool? ReplaySafe = null,
    NormalizedToolOutcome? NormalizedOutcome = null,
    string ModelId = "");

public sealed record AgentStepResult(
    AgentTaskState State,
    AgentContextPack ContextPack,
    AgentPlannerResponse PlannerResponse,
    string LogEntry);

public sealed record AgentTaskListItem(
    string TaskId,
    string Goal,
    AgentTaskStatus Status,
    DateTime UpdatedAt,
    /// <summary>Non-null when this task is a sub-task child, so recent-task lists can mark it as such.</summary>
    string? ParentTaskId = null,
    /// <summary>r19 3.3: PendingSteps.Count as of the last save, so the recent-tasks list can flag a
    /// terminal task that declared victory with its own plan still open, without loading the full state.</summary>
    int PendingStepCount = 0,
    /// <summary>r23 2.3: true when the task completed with a non-empty Reservations list, so the recent-tasks list can show "Completed with reservations" without loading the full state.</summary>
    bool HasReservations = false,
    /// <summary>r24 doc 01: the project this task was created under, if any.</summary>
    string ProjectId = "",
    string ModelId = "",
    string ModelDisplayName = "");

public sealed record AgentFileSearchResult(
    string RelativePath,
    string Snippet,
    DateTime ModifiedUtc);

public sealed record AgentFileReadResult(
    string RelativePath,
    string Content,
    bool Truncated,
    /// <summary>Total lines in the file, when known (a line-ranged read always knows).</summary>
    int? TotalLines = null,
    /// <summary>First line index this result covers, 0-based.</summary>
    int? LineOffset = null,
    /// <summary>
    /// Lines returned in this result. With <see cref="LineOffset"/> this is
    /// exactly what the model needs to ask for the next slice.
    /// </summary>
    int? LineCount = null,
    /// <summary>Meaningful only for mutation tools; reads leave the default.</summary>
    bool Changed = true)
{
    /// <summary>
    /// What to do about a truncated read, in the result itself. A bare
    /// "truncated": true told the model only that content was missing, and a
    /// real run concluded "the tool cannot return the entire file content in
    /// one go" and gave up on the file entirely. It can: this says how.
    /// </summary>
    public string ContinuationHint => !Truncated
        ? string.Empty
        : LineOffset is { } offset && LineCount is { } count
            ? $"Truncated. This is lines {offset + 1} to {offset + count}"
                + (TotalLines is { } total ? $" of {total}" : string.Empty)
                + $". Call read_file again with line_offset={offset + count} to continue from where this stopped."
            : "Truncated: the file was too large to return whole. Call read_file again with line_offset and "
                + "line_limit (for example line_offset=0, line_limit=400, then line_offset=400) to read it in "
                + "slices, or use search_files to find a symbol without reading the whole file.";
}

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

/// <summary>
/// Deterministic read/write/never glob policy, narrowing what the agent's
/// tools may touch inside a workspace (r23 3.1). Policy can only ever
/// narrow: absent or empty allow lists mean "allow all" (backwards
/// compatible with a workspace that has no policy), and <see cref="Never"/>
/// beats both allow lists for both reads and writes.
/// </summary>
public sealed class WorkspacePolicy
{
    public List<string> ReadAllow { get; set; } = [];
    public List<string> WriteAllow { get; set; } = [];
    public List<string> Never { get; set; } = [];
    /// <summary>0 or absent means unlimited; counted on <see cref="AgentTaskState.FileReadCount"/>, persisted so a restart does not reset it.</summary>
    public int MaxFileReadsPerTask { get; set; }
}

public sealed class WorkspaceManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string PreferredModelId { get; set; } = string.Empty;
    public string PreferredEmbeddingModelId { get; set; } = string.Empty;
    public string? LinkedRagDatasetId { get; set; }
    public List<string> InstructionPaths { get; set; } = [];
    public List<WorkspaceCommandRecipe> AllowedCommands { get; set; } = [];
    /// <summary>
    /// Optional named <c>TtsSettings.Profiles</c> entry to narrate this
    /// workspace's agent milestones with, letting different projects have a
    /// recognizably different narrator. Unknown or blank falls back to the
    /// Agent channel's configured profile.
    /// </summary>
    public string VoiceProfileName { get; set; } = string.Empty;
    /// <summary>Null when absent (unrestricted) or when a present policy was malformed and rejected as a whole (r23 3.1); see <see cref="PolicyRejectionWarning"/>.</summary>
    public WorkspacePolicy? Policy { get; set; }
    /// <summary>
    /// Set only by WorkspaceManifestService.LoadAsync when a present
    /// <c>policy</c> object failed validation and was rejected as a whole
    /// (r23 3.1). Transient - never persisted back to workspace.json - so a
    /// re-save from a form that never touched policy cannot silently erase a
    /// user's hand-edited (if malformed) policy block.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PolicyRejectionWarning { get; set; }
}

public sealed record WorkspaceActivation(
    string? PreferredModelId,
    string? PreferredEmbeddingModelId,
    string? LinkedRagDatasetId,
    IReadOnlyList<string> InstructionPaths,
    bool FromManifest,
    string? VoiceProfileName = null);

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
