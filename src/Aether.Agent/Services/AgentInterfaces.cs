using Aether.Agent.Models;

namespace Aether.Agent.Services;

public interface IAgentTaskStateStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task SaveAsync(AgentTaskState state, CancellationToken ct = default);
    Task<AgentTaskState?> LoadAsync(string taskId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentTaskListItem>> ListRecentAsync(int limit = 25, CancellationToken ct = default);
    Task<IReadOnlyList<AgentReviewQueueItem>> ListReviewQueueAsync(int limit = 25, CancellationToken ct = default);
    Task AppendLogAsync(string taskId, string line, CancellationToken ct = default);
    Task AppendTraceAsync(string taskId, object trace, CancellationToken ct = default);
    Task AppendTranscriptEntryAsync(string taskId, AgentTranscriptEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AgentTranscriptEntry>> LoadTranscriptAsync(string taskId, CancellationToken ct = default);
    string GetTaskDirectory(string taskId);
}

public interface IAgentWorkspaceMemoryStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AgentWorkspaceMemoryEntry>> ListAsync(string workspaceRoot, CancellationToken ct = default);
    Task<AgentWorkspaceMemoryEntry> UpsertAsync(AgentWorkspaceMemoryEntry entry, CancellationToken ct = default);
    Task DeleteAsync(string workspaceRoot, string id, CancellationToken ct = default);
    string GetWorkspaceDirectory(string workspaceRoot);
}

public interface IAgentWorkspaceTools
{
    /// <summary>
    /// <paramref name="subdirectory"/> scopes the listing to one folder
    /// (workspace-relative); <paramref name="maxDepth"/> bounds how many
    /// path segments deep it recurses. Both optional so this can act as a
    /// flat listing (default) or a bounded tree view.
    /// </summary>
    IReadOnlyList<string> ListFiles(AgentWorkspaceOptions options, string? subdirectory = null, int? maxDepth = null);
    /// <summary>
    /// <paramref name="regex"/> switches <paramref name="query"/> from a
    /// literal substring match to a regular expression; <paramref name="contextLines"/>
    /// includes that many lines of surrounding context per match.
    /// </summary>
    IReadOnlyList<AgentFileSearchResult> SearchFiles(AgentWorkspaceOptions options, string query, bool regex = false, int contextLines = 0);
    /// <summary>Bounded glob match (`*`, `**`, `?`) over the workspace's safe file list.</summary>
    IReadOnlyList<string> GlobFiles(AgentWorkspaceOptions options, string pattern);
    /// <summary>
    /// <paramref name="lineOffset"/>/<paramref name="lineLimit"/> page a
    /// large file by line instead of relying on the byte-size truncation
    /// that applies when they are left null.
    /// </summary>
    AgentFileReadResult ReadFile(AgentWorkspaceOptions options, string relativePath, int? lineOffset = null, int? lineLimit = null);
    AgentFileSummaryResult SummarizeFile(AgentWorkspaceOptions options, string relativePath);
    Task<AgentFileReadResult> ApplyDraftPatchAsync(AgentWorkspaceOptions options, string relativePath, string proposedContent, CancellationToken ct = default);
    string DraftPatch(string relativePath, string rationale, string proposedContent);
    /// <summary>
    /// Applies a surgical text edit: <paramref name="oldString"/> must match
    /// the target file's content exactly once, or the edit is refused (0
    /// matches: nothing to replace; more than 1: ambiguous, needs more
    /// surrounding context). This is the primary write tool for touching one
    /// part of a file without rewriting it whole.
    /// </summary>
    Task<AgentFileReadResult> EditFileAsync(AgentWorkspaceOptions options, string relativePath, string oldString, string newString, CancellationToken ct = default);
    /// <summary>Creates a new file; refuses to overwrite an existing one (use edit_file for that).</summary>
    Task<AgentFileReadResult> CreateFileAsync(AgentWorkspaceOptions options, string relativePath, string content, CancellationToken ct = default);
    /// <summary>Raw content of a workspace file, or null if it does not exist. Used to capture pre-images before a mutating tool runs (r6 1.8).</summary>
    Task<string?> ReadFileForRevertAsync(AgentWorkspaceOptions options, string relativePath, CancellationToken ct = default);
    /// <summary>
    /// Restores <paramref name="preImageContent"/> (or deletes the file when
    /// null, meaning it did not exist before the patch), but only if the
    /// file's current content still matches <paramref name="expectedCurrentContent"/>
    /// exactly - a later edit is left alone rather than overwritten.
    /// </summary>
    Task<AgentRevertResult> RevertAppliedPatchAsync(AgentWorkspaceOptions options, string relativePath, string? preImageContent, string expectedCurrentContent, CancellationToken ct = default);
}

public interface IAgentSafetyGate
{
    AgentToolPolicyDecision Evaluate(string toolName, bool wouldMutate = false);
    AgentToolPolicyDecision EvaluateCommand(string? requestedCommand, IReadOnlyList<WorkspaceCommandRecipe> allowedCommands);
}

public interface IAgentToolExecutor
{
    bool CanExecute(string toolName);
    Task<AgentToolResult> ExecuteAsync(string toolName, Dictionary<string, object?> arguments, AgentWorkspaceOptions options, CancellationToken ct = default);
}

public interface IAgentContextBuilder
{
    Task<AgentContextPack> BuildAsync(AgentTaskState state, AgentWorkspaceOptions options, CancellationToken ct = default);
}

public interface IAgentService
{
    Task<AgentTaskState> CreateTaskAsync(string goal, AgentWorkspaceOptions options, CancellationToken ct = default);
    Task<AgentStepResult> RunStepAsync(string taskId, AgentWorkspaceOptions options, CancellationToken ct = default);
    /// <summary>
    /// Runs steps back to back without waiting for a manual "run step" click,
    /// stopping when the model reaches a final answer, asks the user a
    /// question, needs approval for a gated action, gets blocked, or hits
    /// <see cref="Aether.Core.Models.AgentSettings.MaxAutoSteps"/>. Never
    /// auto-approves a gated action; it only avoids making the user drive
    /// every intermediate read-only step by hand.
    /// </summary>
    Task<AgentStepResult> RunAsync(string taskId, AgentWorkspaceOptions options, Action<AgentStepResult>? onStep = null, CancellationToken ct = default);
    Task<IReadOnlyList<AgentTaskListItem>> LoadRecentTasksAsync(CancellationToken ct = default);
    Task AppendApprovalAsync(string taskId, string action, bool approved, AgentWorkspaceOptions? options = null, CancellationToken ct = default);
    /// <summary>
    /// Answers a task's <c>ask_user</c> question: appends the reply to the
    /// task's transcript so the next step sees it, and resumes the task to
    /// <see cref="Aether.Agent.Models.AgentTaskStatus.Running"/>. Refuses
    /// (no state change) when a tool approval is pending - a reply is never
    /// a substitute for an explicit approval decision.
    /// </summary>
    Task AppendUserReplyAsync(string taskId, string reply, CancellationToken ct = default);

    /// <summary>
    /// r19 3.1: reopens a terminal or stalled task with a user instruction,
    /// so a task that finished (prematurely or not), failed, or got blocked
    /// can keep going without the user retyping the whole goal as a brand
    /// new task. Never auto-approves anything - a reopened task that
    /// proposes a gated action still goes through the review queue exactly
    /// like a fresh one. Refuses (no state change) when the task is
    /// actively <see cref="Aether.Agent.Models.AgentTaskStatus.Running"/> or
    /// has a pending tool approval (the review queue is for that), and when
    /// the task is a sub-task child (continue the parent instead).
    /// </summary>
    Task<AgentTaskState> ContinueTaskAsync(string taskId, string instruction, AgentWorkspaceOptions options, CancellationToken ct = default);
}

public interface IWorkspaceProfileStore
{
    Task<WorkspaceProfile?> LoadAsync(string workspaceRoot, CancellationToken ct = default);
    Task<WorkspaceProfile> SaveAsync(WorkspaceProfile profile, CancellationToken ct = default);
}

public interface IWorkspaceAnalysisService
{
    Task<WorkspaceAnalysisReport> AnalyzeAsync(AgentWorkspaceOptions options, CancellationToken ct = default);
}

public interface IWorkspaceManifestStore
{
    Task<WorkspaceManifest?> LoadAsync(string workspaceRoot, CancellationToken ct = default);
    Task SaveAsync(string workspaceRoot, WorkspaceManifest manifest, CancellationToken ct = default);
}

public interface IWorkspaceActivationService
{
    Task<WorkspaceActivation> ActivateAsync(string workspaceRoot, CancellationToken ct = default);
}

/// <summary>
/// The agent self-learning store: deterministic, evidence-backed lessons
/// about what works or fails, keyed by a dedupe signature so repeated
/// evidence reinforces one row instead of creating duplicates, and
/// contradicting evidence decays confidence toward automatic retirement.
/// </summary>
public interface ILessonStore
{
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Records one piece of evidence. If a lesson with the same signature
    /// (scope-qualified) already exists: matching outcome reinforces it
    /// (evidence count up, confidence up, never re-inserted); a different
    /// outcome for the same signature is treated as a contradiction
    /// (confidence down, retiring the lesson below a floor). Otherwise
    /// creates a new lesson.
    /// </summary>
    Task<AgentLesson> RecordEvidenceAsync(AgentLessonEvidence evidence, CancellationToken ct = default);

    /// <summary>Active lessons in scope (Global lessons plus, if scopeId is given, that workspace's), most confident first.</summary>
    Task<IReadOnlyList<AgentLesson>> ListRelevantAsync(string? workspaceScopeId, bool includeRetired, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<AgentLesson>> ListAllAsync(bool includeRetired, CancellationToken ct = default);
    Task<AgentLesson?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Bumps evidence (and confidence, via the same curve as
    /// <see cref="RecordEvidenceAsync"/>) on each Active, non-pinned lesson
    /// in <paramref name="lessonIds"/> - the lessons that were actually
    /// injected into a task that just completed successfully. This is the
    /// compounding half of the self-learning loop: a lesson that helped
    /// gets more confident, not just one that was independently re-observed.
    /// Unknown, retired, or pinned ids are silently skipped.
    /// </summary>
    Task ConfirmAsync(IReadOnlyList<string> lessonIds, string sourceTaskId, CancellationToken ct = default);

    /// <summary>Manual edit: overwrites claim/guidance and locks confidence at its current value (pinning also locks it).</summary>
    Task UpdateAsync(string id, string claim, string guidance, CancellationToken ct = default);
    Task SetPinnedAsync(string id, bool pinned, CancellationToken ct = default);
    Task SetStatusAsync(string id, AgentLessonStatus status, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
