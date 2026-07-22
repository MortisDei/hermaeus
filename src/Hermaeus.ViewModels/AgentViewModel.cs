using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public sealed class AgentContextItemViewModel
{
    public string Source { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string ScoreDisplay { get; init; } = string.Empty;
    public string Locator { get; init; } = string.Empty;
    public bool HasLocator => !string.IsNullOrWhiteSpace(Locator);
}

/// <summary>One row of the per-step context receipt: "why were these files selected" (r6 1.5).</summary>
public sealed class AgentContextReceiptSectionViewModel
{
    public AgentContextReceiptSectionViewModel(AgentContextReceiptSection section)
    {
        SectionLabel = section.SectionLabel;
        ItemCount = section.ItemCount;
        EstimatedTokens = section.EstimatedTokens;
        Identifiers = string.Join(", ", section.ItemIdentifiers);
    }

    public string SectionLabel { get; }
    public int ItemCount { get; }
    public int EstimatedTokens { get; }
    public string Identifiers { get; }
    public string Summary => $"{SectionLabel}: {ItemCount} item{(ItemCount == 1 ? "" : "s")}, ~{EstimatedTokens} tokens";
}

public sealed class AgentReviewQueueItemViewModel
{
    public AgentReviewQueueItemViewModel(AgentReviewQueueItem item, string recipePreview = "", string activeWorkspaceRoot = "")
    {
        TaskId = item.TaskId;
        Goal = item.Goal;
        Status = item.Status;
        UpdatedAt = item.UpdatedAt;
        ActiveStep = item.ActiveStep;
        Summary = item.Summary;
        ApprovalCount = item.ApprovalCount;
        LastApprovalAction = item.LastApprovalAction ?? string.Empty;
        LastApprovalApproved = item.LastApprovalApproved;
        LastApprovalAt = item.LastApprovalAt;
        PendingToolName = item.PendingToolAction?.ToolName ?? string.Empty;
        PendingRiskLevel = item.PendingToolAction?.RiskLevel;
        PendingReason = item.PendingToolAction?.Reason ?? string.Empty;
        RecipePreview = recipePreview;
        ParentGoal = item.ParentGoal ?? string.Empty;
        ParentTaskId = item.ParentTaskId;
        WorkspaceRoot = item.WorkspaceRoot ?? string.Empty;
        DifferentWorkspaceLabel = WorkspaceRoot.Length > 0
            && !string.Equals(WorkspaceRoot, activeWorkspaceRoot, StringComparison.OrdinalIgnoreCase)
                ? $"workspace: {Path.GetFileName(WorkspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}"
                : string.Empty;
    }

    public string TaskId { get; }
    public string Goal { get; }
    /// <summary>The parent task's goal, set only when this entry is a sub-task child (r15 02-orchestration-ui.md 2.3).</summary>
    public string ParentGoal { get; }
    /// <summary>The parent task's id, set only when this entry is a sub-task child (r16 01-orchestration-hardening.md 1.1).</summary>
    public string? ParentTaskId { get; }
    public bool IsSubTask => !string.IsNullOrWhiteSpace(ParentGoal);
    public string ParentGoalLabel => IsSubTask ? $"for: {ParentGoal}" : string.Empty;
    /// <summary>The task's own workspace root (r16 01-orchestration-hardening.md 1.4); empty for pre-r16 tasks.</summary>
    public string WorkspaceRoot { get; }
    /// <summary>Non-empty only when this task's workspace differs from the currently active workbench workspace.</summary>
    public string DifferentWorkspaceLabel { get; }
    public bool HasDifferentWorkspace => DifferentWorkspaceLabel.Length > 0;
    public AgentTaskStatus Status { get; }
    public DateTime UpdatedAt { get; }
    public string ActiveStep { get; }
    public string Summary { get; }
    public int ApprovalCount { get; }
    public string LastApprovalAction { get; }
    public bool? LastApprovalApproved { get; }
    public DateTime? LastApprovalAt { get; }
    public string StatusLabel => Status.ToString();
    public string ApprovalLabel => ApprovalCount == 0
        ? "No approvals"
        : $"{ApprovalCount} approval(s), last {LastApprovalAction}={(LastApprovalApproved == true ? "yes" : "no")}";

    public string LatestApprovalLabel => LastApprovalAt is null
        ? string.Empty
        : $"Last review {LastApprovalAt:yyyy-MM-dd HH:mm} UTC";

    /// <summary>Name of the gated tool waiting on approval, e.g. "run_command"; empty if this queue entry has none (r6 1.7).</summary>
    public string PendingToolName { get; }
    public AgentRiskLevel? PendingRiskLevel { get; }
    public string PendingRiskLabel => PendingRiskLevel?.ToString() ?? string.Empty;
    /// <summary>Why the safety gate gated this action (AgentToolPolicyDecision.Reason).</summary>
    public string PendingReason { get; }
    public bool HasPendingAction => !string.IsNullOrEmpty(PendingToolName);
    /// <summary>What a pending run_command approval will actually execute (r6 3.2); empty for non-command tools.</summary>
    public string RecipePreview { get; }
    public bool HasRecipePreview => !string.IsNullOrWhiteSpace(RecipePreview);
}

/// <summary>
/// One row of the recent-tasks list (r16 03-workbench-and-desktop.md 3.1) -
/// the data layer (<see cref="AgentTaskListItem"/>, ParentTaskId column)
/// shipped in r15 with no view to bind it; this is that view's item shape.
/// </summary>
public sealed class AgentTaskListItemViewModel
{
    public AgentTaskListItemViewModel(AgentTaskListItem item)
    {
        TaskId = item.TaskId;
        Goal = item.Goal;
        Status = item.Status;
        UpdatedAt = item.UpdatedAt;
        ParentTaskId = item.ParentTaskId;
        PendingStepCount = item.PendingStepCount;
    }

    public string TaskId { get; }
    public string Goal { get; }
    public AgentTaskStatus Status { get; }
    public DateTime UpdatedAt { get; }
    public string? ParentTaskId { get; }
    public int PendingStepCount { get; }
    public bool IsSubTask => !string.IsNullOrWhiteSpace(ParentTaskId);
    public string StatusLabel => Status.ToString();

    /// <summary>r19 3.3: a terminal task (Complete/Failed/Blocked) whose own plan still lists
    /// pending steps declared victory prematurely; flag it at a glance in the recent-tasks list.</summary>
    public bool IsPrematureComplete =>
        PendingStepCount > 0 && Status is AgentTaskStatus.Complete or AgentTaskStatus.Failed or AgentTaskStatus.Blocked;
    public string PrematureCompleteNote => IsPrematureComplete
        ? $"Finished with {PendingStepCount} planned step{(PendingStepCount == 1 ? "" : "s")} not run."
        : string.Empty;

    public string RelativeTimeLabel
    {
        get
        {
            var elapsed = DateTime.UtcNow - UpdatedAt;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return elapsed switch
            {
                { TotalSeconds: < 60 } => "just now",
                { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes}m ago",
                { TotalHours: < 24 } => $"{(int)elapsed.TotalHours}h ago",
                { TotalDays: < 7 } => $"{(int)elapsed.TotalDays}d ago",
                _ => UpdatedAt.ToLocalTime().ToString("d MMM")
            };
        }
    }
}

public sealed class AgentWorkspaceMemoryEntryViewModel
{
    public AgentWorkspaceMemoryEntryViewModel(AgentWorkspaceMemoryEntry entry)
    {
        Id = entry.Id;
        WorkspaceRoot = entry.WorkspaceRoot;
        Title = entry.Title;
        Body = entry.Body;
        Tags = string.Join(", ", entry.Tags);
        CreatedAt = entry.CreatedAt;
        UpdatedAt = entry.UpdatedAt;
    }

    public string Id { get; }
    public string WorkspaceRoot { get; }
    public string Title { get; }
    public string Body { get; }
    public string Tags { get; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; }
}

/// <summary>
/// UI-editable view of one <see cref="AgentLesson"/>: the self-learning
/// panel's row. Claim/Guidance are mutable here so a manual edit can be
/// staged before <c>UpdateLessonCommand</c> persists it.
/// </summary>
public sealed partial class AgentLessonViewModel : ObservableObject
{
    public AgentLessonViewModel(AgentLesson lesson)
    {
        Id = lesson.Id;
        Scope = lesson.Scope;
        ScopeId = lesson.ScopeId;
        Kind = lesson.Kind;
        Signature = lesson.Signature;
        Outcome = lesson.Outcome;
        Confidence = lesson.Confidence;
        EvidenceCount = lesson.EvidenceCount;
        UpdatedAt = lesson.UpdatedAt;
        Status = lesson.Status;
        _isPinned = lesson.IsPinned;
        _claim = lesson.Claim;
        _guidance = lesson.Guidance;
    }

    public string Id { get; }
    public AgentLessonScope Scope { get; }
    public string ScopeId { get; }
    public AgentLessonKind Kind { get; }
    public string Signature { get; }
    public AgentLessonOutcome Outcome { get; }
    public double Confidence { get; }
    public int EvidenceCount { get; }
    public DateTime UpdatedAt { get; }
    public AgentLessonStatus Status { get; }

    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private string _claim;
    [ObservableProperty] private string _guidance;

    public bool IsRetired => Status == AgentLessonStatus.Retired;
    public string ScopeLabel => Scope == AgentLessonScope.Global ? "Global" : "This workspace";
    public string ConfidenceLabel => $"{Confidence:P0} confidence, seen {EvidenceCount}x";
    public string StatusLabel => IsRetired ? "Retired" : "Active";
    public bool HasGuidance => !string.IsNullOrWhiteSpace(Guidance);
}

public sealed class AgentWorkspaceFileViewModel
{
    public AgentWorkspaceFileViewModel(string relativePath, string snippet, DateTime modifiedUtc)
    {
        RelativePath = relativePath;
        Snippet = snippet;
        ModifiedUtc = modifiedUtc;
    }

    public string RelativePath { get; }
    public string Snippet { get; }
    public DateTime ModifiedUtc { get; }
    public string ModifiedLabel => $"{ModifiedUtc:yyyy-MM-dd HH:mm} UTC";
}

public sealed class ProjectInstructionFileViewModel
{
    public ProjectInstructionFileViewModel(ProjectInstructionFile file)
    {
        RelativePath = file.RelativePath;
        Summary = file.Summary;
        Content = file.Content;
        IsPrimary = file.IsPrimary;
    }

    public string RelativePath { get; }
    public string Summary { get; }
    public string Content { get; }
    public bool IsPrimary { get; }
    public string PriorityLabel => IsPrimary ? "Primary" : "Secondary";
}

public sealed class WorkspaceCommandRecipeViewModel
{
    public WorkspaceCommandRecipeViewModel(WorkspaceCommandRecipe recipe)
    {
        Command = recipe.Command;
        Why = recipe.Why;
        RiskLevel = recipe.RiskLevel;
    }

    public string Command { get; }
    public string Why { get; }
    public AgentRiskLevel RiskLevel { get; }
    public string RiskLabel => RiskLevel.ToString();
    public bool IsElevatedRisk => RiskLevel is AgentRiskLevel.Medium or AgentRiskLevel.High;
}

public sealed class AgentDraftPatchViewModel
{
    public AgentDraftPatchViewModel(AgentDraftPatch patch)
    {
        Id = patch.Id;
        RelativePath = patch.RelativePath;
        Rationale = patch.Rationale;
        ProposedContent = patch.ProposedContent;
        Status = patch.Status;
        CreatedAt = patch.CreatedAt;
        ApprovedAt = patch.ApprovedAt;
        ApprovedBy = patch.ApprovedBy;
        BlockedAt = patch.BlockedAt;
        BlockedBy = patch.BlockedBy;
        BlockReason = patch.BlockReason;
        RevertedAt = patch.RevertedAt;
        RevertedBy = patch.RevertedBy;
    }

    public string Id { get; }
    public string RelativePath { get; }
    public string Rationale { get; }
    public string ProposedContent { get; }
    public AgentDraftPatchStatus Status { get; }
    public DateTime CreatedAt { get; }
    public DateTime? ApprovedAt { get; }
    public string? ApprovedBy { get; }
    public DateTime? BlockedAt { get; }
    public string? BlockedBy { get; }
    public string BlockReason { get; }
    public DateTime? RevertedAt { get; }
    public string? RevertedBy { get; }
    public string StatusLabel => Status.ToString();
    public string CreatedLabel => $"Created {CreatedAt:yyyy-MM-dd HH:mm} UTC";
    public bool CanReview => Status != AgentDraftPatchStatus.Applied && Status != AgentDraftPatchStatus.Reverted;
    /// <summary>Only an applied patch that came with a captured pre-image can be reverted (r6 1.8); pre-r6 applied patches have none.</summary>
    public bool CanRevert => Status == AgentDraftPatchStatus.Applied;
    public string OutcomeLabel => Status switch
    {
        AgentDraftPatchStatus.Pending => "Pending review",
        AgentDraftPatchStatus.Applied => $"Applied {ApprovedAt:yyyy-MM-dd HH:mm} by {ApprovedBy}",
        AgentDraftPatchStatus.Approved => $"Approved {ApprovedAt:yyyy-MM-dd HH:mm} by {ApprovedBy}",
        AgentDraftPatchStatus.Rejected => $"Rejected {BlockedAt:yyyy-MM-dd HH:mm} by {BlockedBy}",
        AgentDraftPatchStatus.Blocked => string.IsNullOrWhiteSpace(BlockReason)
            ? $"Blocked {BlockedAt:yyyy-MM-dd HH:mm} by {BlockedBy}"
            : $"Blocked {BlockedAt:yyyy-MM-dd HH:mm} by {BlockedBy}: {BlockReason}",
        AgentDraftPatchStatus.Reverted => $"Reverted {RevertedAt:yyyy-MM-dd HH:mm} by {RevertedBy}",
        _ => Status.ToString()
    };
}

public sealed record DraftPatchPreviewRequest(string PatchId, string RelativePath, string OldContent, string NewContent);

public partial class AgentViewModel : ViewModelBase
{
    public Func<DraftPatchPreviewRequest, Task<bool>>? RequestDraftPatchPreview { get; set; }
    private readonly IAgentService _agent;
    private readonly IAgentTaskStateStore _store;
    private readonly IAgentWorkspaceMemoryStore _workspaceMemory;
    private readonly IAgentWorkspaceTools _workspaceTools;
    private readonly ILlmService _llm;
    private readonly RagQueryService _rag;
    private readonly IRuntimeLogService _logs;
    private readonly IWorkspaceAnalysisService _workspaceAnalysis;
    private readonly IWorkspaceActivationService _workspaceActivation;
    private readonly IWorkspaceManifestStore _workspaceManifests;
    private readonly AgentPatchReviewService _patchReview;
    private readonly ISettingsService? _settings;
    private readonly ILessonStore? _lessons;
    private readonly IVoiceOrchestrator? _voice;
    private CancellationTokenSource? _cts;
    /// <summary>
    /// The task id the user actually opened (Start or LoadTask), independent
    /// of which task an in-flight orchestrated run's steps currently belong
    /// to. During orchestration, onStep fires with the running CHILD's step
    /// results (r15 02-orchestration-ui.md 2.2); CurrentTask must keep
    /// pointing at this id rather than flip to whichever child is active.
    /// </summary>
    private string? _openedTaskId;
    private string _activeWorkspaceVoiceProfile = string.Empty;
    private Task? _loadTask;
    private CancellationTokenSource? _workspaceFileQueryCts;
    private int _workspaceFileSelectionGeneration;

    public UiBoundCollection<LlmModel> AvailableModels { get; } = [];
    public UiBoundCollection<RagDataset> Datasets { get; } = [];
    public UiBoundCollection<AgentTaskListItemViewModel> RecentTasks { get; } = [];
    public UiBoundCollection<AgentReviewQueueItemViewModel> ReviewQueue { get; } = [];
    public UiBoundCollection<AgentWorkspaceMemoryEntryViewModel> WorkspaceMemory { get; } = [];
    public UiBoundCollection<AgentLessonViewModel> Lessons { get; } = [];
    /// <summary>Lessons newly created (not merely reinforced) by the open task, for the "new lessons" review strip (r6 3.3).</summary>
    public UiBoundCollection<AgentLessonViewModel> NewLessons { get; } = [];
    public UiBoundCollection<AgentWorkspaceFileViewModel> WorkspaceFiles { get; } = [];
    public UiBoundCollection<AgentContextItemViewModel> RetrievedContext { get; } = [];
    /// <summary>Per-section counts/token estimates for the most recent step's context pack (r6 1.5).</summary>
    public UiBoundCollection<AgentContextReceiptSectionViewModel> ContextReceipt { get; } = [];
    public UiBoundCollection<AgentDraftPatchViewModel> QueuedPatches { get; } = [];
    public UiBoundCollection<ProjectInstructionFileViewModel> ProjectInstructions { get; } = [];
    public UiBoundCollection<WorkspaceCommandRecipeViewModel> CommandRecipes { get; } = [];
    public UiBoundCollection<string> WorkspaceRisks { get; } = [];
    public UiBoundCollection<string> InstructionWarnings { get; } = [];

    public IReadOnlyList<string> CapabilityNotes { get; } =
    [
        "Read-first workspace inspection: list, search, read, and summarise local files.",
        "Approval-gated patch drafting: propose content, queue it for review, and apply only after approval.",
        "Approval-gated command execution: only the workspace's own declared build/test recipes can run, never freeform shell text.",
        "No network, install, commit, push, or remote-control actions in this slice.",
        "Workspace memory and review queues remain local and explicit."
    ];

    public string CapabilityLabel => "Current agent scope";

    /// <summary>
    /// Drives the workbench's Scenario Evals panel (r7): deterministic
    /// behaviour checks run against isolated sandbox copies of small
    /// scenario workspaces. Null only when a caller constructs
    /// <see cref="AgentViewModel"/> without it (existing tests that do not
    /// exercise this panel); real DI wiring always supplies one.
    /// </summary>
    public AgentScenarioSuiteViewModel? ScenarioSuite { get; }

    public Action? RequestWorkspaceRootPicker { get; set; }
    /// <summary>Opens a path with the OS default handler (same shape as LogsViewModel.RequestOpenFolder); used here for the synthesis report.md (r15 02-orchestration-ui.md 2.4).</summary>
    public Action<string>? RequestOpenFolder { get; set; }

    /// <summary>Drives the "no workspace selected" empty state (r8 02-onboarding-and-usability.md 2.6).</summary>
    public bool HasWorkspace => !string.IsNullOrWhiteSpace(WorkspaceRoot) && Directory.Exists(WorkspaceRoot);

    /// <summary>An orchestration parent whose synthesis has written report.md (r15 02-orchestration-ui.md 2.4).</summary>
    public bool HasReport => CurrentTask is { SubTaskPlan.Count: > 0 } && File.Exists(ReportPath);
    private string ReportPath => CurrentTask is null ? string.Empty : Path.Combine(_store.GetTaskDirectory(CurrentTask.TaskId), "report.md");

    /// <summary>
    /// Null-safe replacement for the Sub-tasks border's old
    /// <c>!!CurrentTask.SubTaskPlan.Count</c> binding (r16
    /// 03-workbench-and-desktop.md 3.4): a compiled binding with a null
    /// <c>CurrentTask</c> intermediate produces UnsetValue, which leaves
    /// IsVisible at its default true - the empty "Sub-tasks" chrome showed
    /// on a fresh workbench before any task was loaded.
    /// </summary>
    public bool HasSubTaskPlan => CurrentTask is { SubTaskPlan.Count: > 0 };

    [RelayCommand]
    private void OpenReport()
    {
        if (HasReport)
            RequestOpenFolder?.Invoke(ReportPath);
    }

    [ObservableProperty] private string _goalText = string.Empty;
    [ObservableProperty] private string _workspaceRoot = string.Empty;
    [ObservableProperty] private LlmModel? _selectedModel;
    [ObservableProperty] private RagDataset? _selectedDataset;
    [ObservableProperty] private AgentTaskState? _currentTask;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _currentStep = string.Empty;
    [ObservableProperty] private string _taskStatePreview = string.Empty;
    [ObservableProperty] private string _nextActionPreview = string.Empty;
    [ObservableProperty] private string _logPreview = string.Empty;
    [ObservableProperty] private string _workspaceFileQuery = string.Empty;
    [ObservableProperty] private AgentWorkspaceFileViewModel? _selectedWorkspaceFile;
    [ObservableProperty] private string _workspaceFilePreview = string.Empty;
    [ObservableProperty] private string _draftRationale = string.Empty;
    [ObservableProperty] private string _draftProposedContent = string.Empty;
    [ObservableProperty] private string _draftPreview = string.Empty;
    [ObservableProperty] private string _workspaceFileSummary = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isAnalyzingWorkspace;
    [ObservableProperty] private string _workspaceProfileSummary = string.Empty;
    [ObservableProperty] private string _workspaceRepoType = string.Empty;
    [ObservableProperty] private string _workspaceLanguages = string.Empty;
    [ObservableProperty] private string _workspaceFrameworks = string.Empty;
    [ObservableProperty] private string _workspaceImportantFiles = string.Empty;
    [ObservableProperty] private string _workspaceRagIngestPlan = string.Empty;
    [ObservableProperty] private string _suggestedAgentsMd = string.Empty;
    [ObservableProperty] private string _replyText = string.Empty;
    [ObservableProperty] private string _workspaceVoiceProfileName = string.Empty;

    public string CurrentTaskStatusLabel => CurrentTask is null ? "No active task" : CurrentTask.Status.ToString();
    /// <summary>
    /// Plain "step N/max" for an ordinary task; "sub-task X/N, step Y" for
    /// an orchestration parent, sourced from SubTaskPlan and
    /// OrchestrationStepsUsed rather than the parent's own (low) StepCount,
    /// which only counts the parent's own propose/synthesis steps
    /// (r15 02-orchestration-ui.md 2.2).
    /// </summary>
    public string CurrentStepCountLabel
    {
        get
        {
            if (CurrentTask is null) return string.Empty;
            if (CurrentTask.SubTaskPlan.Count == 0)
                return $"step {CurrentTask.StepCount}/{Math.Max(_settings?.Settings.Agent.MaxAutoSteps ?? 20, 1)}";

            var specs = CurrentTask.SubTaskPlan;
            var firstUnfinished = specs.FindIndex(s => s.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running);
            var progress = firstUnfinished < 0 ? specs.Count : firstUnfinished + 1;
            return $"sub-task {progress}/{specs.Count}, step {CurrentTask.OrchestrationStepsUsed}";
        }
    }
    public string CurrentTaskGoalLabel => CurrentTask is null || string.IsNullOrWhiteSpace(CurrentTask.Goal) ? "No goal loaded" : CurrentTask.Goal;
    public string CurrentTaskSummaryLabel => CurrentTask is null || string.IsNullOrWhiteSpace(CurrentTask.Summary) ? "No summary yet" : CurrentTask.Summary;

    /// <summary>r19 3.1/3.3: a task that will not resume its own loop without user action - the
    /// Continue affordance and New task button both key off this.</summary>
    public bool IsTaskTerminal => CurrentTask?.Status is AgentTaskStatus.Complete or AgentTaskStatus.Failed or AgentTaskStatus.Blocked;

    /// <summary>r19 3.3: presentation only - a terminal task whose own plan still lists pending
    /// steps declared victory prematurely; pairs with the Continue box (3.1) to answer
    /// "it stopped halfway, now what".</summary>
    public string PrematureCompleteNote => CurrentTask is { } t && IsTaskTerminal && t.PendingSteps.Count > 0
        ? $"Finished with {t.PendingSteps.Count} planned step{(t.PendingSteps.Count == 1 ? "" : "s")} not run."
        : string.Empty;
    public bool HasPrematureCompleteNote => !string.IsNullOrEmpty(PrematureCompleteNote);
    /// <summary>True when the task is asking a question, not waiting on a tool approval; only then does the reply box apply.</summary>
    public bool IsWaitingForReply => CurrentTask is { Status: AgentTaskStatus.WaitingForUser, PendingToolAction: null };
    public int RecentTaskCount => RecentTasks.Count;
    public int ReviewQueueCount => ReviewQueue.Count;
    public int WorkspaceMemoryCount => WorkspaceMemory.Count;
    public int RetrievedContextCount => RetrievedContext.Count;
    public int QueuedPatchCount => QueuedPatches.Count;
    public int PendingPatchCount => QueuedPatches.Count(patch => patch.Status == AgentDraftPatchStatus.Pending);
    public int AppliedPatchCount => QueuedPatches.Count(patch => patch.Status == AgentDraftPatchStatus.Applied);
    public int RejectedPatchCount => QueuedPatches.Count(patch => patch.Status == AgentDraftPatchStatus.Rejected);
    public int BlockedPatchCount => QueuedPatches.Count(patch => patch.Status == AgentDraftPatchStatus.Blocked);
    public bool HasQueuedPatches => QueuedPatchCount > 0;

    public AgentViewModel(
        IAgentService agent,
        IAgentTaskStateStore store,
        IAgentWorkspaceMemoryStore workspaceMemory,
        IAgentWorkspaceTools workspaceTools,
        ILlmService llm,
        RagQueryService rag,
        IRuntimeLogService logs,
        IWorkspaceAnalysisService workspaceAnalysis,
        IWorkspaceActivationService workspaceActivation,
        IWorkspaceManifestStore workspaceManifests,
        ISettingsService? settings = null,
        ILessonStore? lessons = null,
        IVoiceOrchestrator? voice = null,
        AgentScenarioSuiteViewModel? scenarioSuite = null)
    {
        _agent = agent;
        _store = store;
        _workspaceMemory = workspaceMemory;
        _workspaceTools = workspaceTools;
        _llm = llm;
        _rag = rag;
        _logs = logs;
        _workspaceAnalysis = workspaceAnalysis;
        _workspaceActivation = workspaceActivation;
        _workspaceManifests = workspaceManifests;
        _settings = settings;
        _lessons = lessons;
        _voice = voice;
        ScenarioSuite = scenarioSuite;
        _patchReview = new AgentPatchReviewService(workspaceTools, store, agent);
        // r12 03-runtime-vm-correctness.md 3.5: defaulting to the whole user
        // profile meant every startup (and every Agent panel navigation)
        // silently enumerated and analyzed it, writing a "Workspace profile"
        // workspace-memory entry for a folder the user never chose. The
        // existing HasWorkspace empty-state UI (r8 2.6) already handles "no
        // workspace selected"; LoadAsync's own guards skip file listing and
        // analysis while it is empty.

        RecentTasks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RecentTaskCount));
            OnPropertyChanged(nameof(HasTaskHistory));
        };

        ReviewQueue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ReviewQueueCount));
            OnPropertyChanged(nameof(HasReviewQueue));
        };

        WorkspaceMemory.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WorkspaceMemoryCount));
            OnPropertyChanged(nameof(HasWorkspaceMemory));
        };

        WorkspaceFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WorkspaceFileCount));
            OnPropertyChanged(nameof(HasWorkspaceFiles));
        };

        RetrievedContext.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RetrievedContextCount));
            OnPropertyChanged(nameof(HasRetrievedContext));
        };

        QueuedPatches.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(QueuedPatchCount));
            OnPropertyChanged(nameof(PendingPatchCount));
            OnPropertyChanged(nameof(AppliedPatchCount));
            OnPropertyChanged(nameof(RejectedPatchCount));
            OnPropertyChanged(nameof(BlockedPatchCount));
            OnPropertyChanged(nameof(HasQueuedPatches));
        };

        ProjectInstructions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ProjectInstructionCount));
        CommandRecipes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CommandRecipeCount));
        WorkspaceRisks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(WorkspaceRiskCount));
        NewLessons.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNewLessons));
    }

    public bool HasTaskHistory => RecentTaskCount > 0;
    public bool HasReviewQueue => ReviewQueueCount > 0;
    public bool HasNewLessons => NewLessons.Count > 0;
    public bool HasWorkspaceMemory => WorkspaceMemoryCount > 0;
    public bool HasWorkspaceFiles => WorkspaceFileCount > 0;
    public bool HasRetrievedContext => RetrievedContextCount > 0;
    public int WorkspaceFileCount => WorkspaceFiles.Count;
    public int ProjectInstructionCount => ProjectInstructions.Count;
    public int CommandRecipeCount => CommandRecipes.Count;
    public int WorkspaceRiskCount => WorkspaceRisks.Count;

    /// <summary>
    /// Re-entrancy-safe (r12 02-async-and-threading.md 2.5): overlapping
    /// callers (startup, Agent panel navigation) share the one in-flight
    /// load instead of each running their own Clear/re-add pass.
    /// </summary>
    [RelayCommand]
    public Task LoadAsync()
    {
        if (_loadTask is { IsCompleted: false } inFlight)
            return inFlight;

        var task = LoadCoreAsync();
        _loadTask = task;
        return task;
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            // r12 03-runtime-vm-correctness.md 3.4: GetModelsAsync/GetDatasetsAsync
            // always materialize fresh instances, so a stale-reference
            // ??= kept the ComboBox pointing at an object no longer in
            // AvailableModels/Datasets (renders blank) while CanStart still
            // passed on the stale id. Re-match by id like Chat does.
            var previousModelId = SelectedModel?.Id;
            var previousDatasetId = SelectedDataset?.Id;

            var models = await _llm.GetModelsAsync();
            AvailableModels.Clear();
            foreach (var model in models.Where(m => m.IsVisible)) AvailableModels.Add(model);
            SelectedModel = AvailableModels.FirstOrDefault(m => m.Id == previousModelId) ?? AvailableModels.FirstOrDefault();

            var datasets = await _rag.GetDatasetsAsync();
            Datasets.Clear();
            foreach (var dataset in datasets) Datasets.Add(dataset);
            SelectedDataset = previousDatasetId is null ? null : Datasets.FirstOrDefault(d => d.Id == previousDatasetId);

            await RefreshRecentAsync();
            await RefreshReviewQueueAsync();
            await RefreshWorkspaceMemoryAsync();
            await RefreshLessonsAsync();
            await RefreshWorkspaceFilesAsync();
            await ExplainWorkspaceAsync();
            if (ScenarioSuite is not null)
            {
                ScenarioSuite.ModelId = SelectedModel?.Id ?? string.Empty;
                await ScenarioSuite.LoadScenariosAsync();
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        IsError = false;
        StatusMessage = string.Empty;
        _cts = new CancellationTokenSource();
        try
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Agent,
                $"Agent started: {GoalText}"));
            CurrentTask = await _agent.CreateTaskAsync(GoalText, BuildOptions(), _cts.Token);
            _openedTaskId = CurrentTask.TaskId;
            _currentTaskParentGoal = string.Empty;
            Narrate("Agent task started.", VoicePriority.Normal, $"{CurrentTask.TaskId}:started");
            await RunAgentLoopAsync();
            await RefreshRecentAsync();
        }
        catch (OperationCanceledException) { StatusMessage = "Agent stopped."; }
        catch (Exception ex) { SetError(ex.Message); }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            StartCommand.NotifyCanExecuteChanged();
            RunStepCommand.NotifyCanExecuteChanged();
            SendReplyCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunStep))]
    private async Task RunStepAsync()
    {
        IsRunning = true;
        IsError = false;
        _cts = new CancellationTokenSource();
        try
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Agent,
                $"Agent step started for task {CurrentTask?.TaskId}"));
            await RunCurrentStepAsync();
            await RefreshRecentAsync();
        }
        catch (OperationCanceledException) { StatusMessage = "Agent stopped."; }
        catch (Exception ex) { SetError(ex.Message); }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            StartCommand.NotifyCanExecuteChanged();
            RunStepCommand.NotifyCanExecuteChanged();
            SendReplyCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    /// <summary>
    /// r19 3.2: once a task is loaded the workbench is visually and mentally
    /// owned by it and nothing said "you can just type a new goal" - the
    /// owner read that as impossible. Clears composer/preview state only;
    /// never touches the persisted task (Recent tasks still resumes it
    /// exactly as before) and never clears the review queue.
    /// </summary>
    public bool CanShowNewTaskButton => !IsRunning && CurrentTask is not null;
    private bool CanNewTask() => CanShowNewTaskButton;

    [RelayCommand(CanExecute = nameof(CanNewTask))]
    private void NewTask()
    {
        CurrentTask = null;
        _openedTaskId = null;
        _currentTaskParentGoal = string.Empty;
        GoalText = string.Empty;
        ReplyText = string.Empty;
        StatusMessage = string.Empty;
        IsError = false;
        RefreshTaskPreview();
    }

    /// <summary>
    /// Takes a bare task id, not an item view model (r16
    /// 03-workbench-and-desktop.md 3.1), so both the recent-tasks list
    /// (<see cref="AgentTaskListItemViewModel.TaskId"/>) and the review
    /// queue's Open button (<see cref="AgentReviewQueueItemViewModel.TaskId"/>)
    /// can invoke this same command.
    /// </summary>
    [RelayCommand]
    private async Task LoadTaskAsync(string? taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return;
        CurrentTask = await _store.LoadAsync(taskId);
        _openedTaskId = CurrentTask?.TaskId;

        // Opening a child directly (recent-tasks list, review queue Open) has
        // no other cue that it belongs to a parent orchestration run (r16
        // 03-workbench-and-desktop.md 3.1's "clicking a child shows its
        // parent context" acceptance) - the review queue's own ParentGoal
        // column only covers items still in that queue.
        _currentTaskParentGoal = CurrentTask?.ParentTaskId is { Length: > 0 } parentId
            ? (await _store.LoadAsync(parentId))?.Goal ?? string.Empty
            : string.Empty;

        RefreshTaskPreview();
        await RefreshQueuedPatchesAsync();
        await RefreshNewLessonsAsync();
        RunStepCommand.NotifyCanExecuteChanged();
    }

    private string _currentTaskParentGoal = string.Empty;
    /// <summary>"for: &lt;parent goal&gt;" when the open task is a sub-task child, empty otherwise.</summary>
    public string CurrentTaskParentGoalLabel => _currentTaskParentGoal.Length > 0 ? $"for: {_currentTaskParentGoal}" : string.Empty;
    public bool HasCurrentTaskParentGoal => _currentTaskParentGoal.Length > 0;

    [RelayCommand]
    private async Task RefreshReviewQueueAsync()
    {
        ReviewQueue.Clear();
        var options = BuildOptions();
        foreach (var item in await _store.ListReviewQueueAsync())
        {
            // The recipe preview describes what a pending run_command would
            // execute; it must reflect the TASK's own workspace, not the
            // workbench's active one (r16 01-orchestration-hardening.md 1.4).
            var previewOptions = item.WorkspaceRoot is { Length: > 0 } root ? options with { WorkspaceRoot = root } : options;
            var preview = item.PendingToolAction is null ? string.Empty : AgentApprovalPreview.Describe(item.PendingToolAction, previewOptions);
            ReviewQueue.Add(new AgentReviewQueueItemViewModel(item, preview, WorkspaceRoot));
        }
    }

    [RelayCommand]
    private async Task ApproveReviewAsync(AgentReviewQueueItemViewModel? item)
    {
        if (item is null) return;
        await _agent.AppendApprovalAsync(item.TaskId, "review_queue", approved: true, BuildOptions());
        await RefreshReviewQueueAsync();
        await LoadTaskIfOpenAsync(item.TaskId);

        // Approve-and-continue: a single approval both unblocks the gated
        // action and resumes the autonomous loop, instead of leaving the
        // user to click Run Step repeatedly. The approval itself already
        // happened above; this only continues a task that approval just
        // returned to Running, it never bypasses a gate on its own.
        //
        // A child's approval must resume the PARENT's task id, not the
        // child's - orchestration only advances through AgentService.RunAsync
        // called with the parent id (r15 01-subtask-orchestration.md 1.4
        // step 4; 02-orchestration-ui.md 2.3). Read directly off the queue
        // item's own ParentTaskId rather than inferring parenthood from
        // whatever task happens to be open in the workbench - that
        // inference silently failed whenever the child itself was the open
        // task (r16 01-orchestration-hardening.md 1.1).
        await ResumeAgentLoopIfRunnableAsync(item.ParentTaskId ?? item.TaskId);
    }

    [RelayCommand(CanExecute = nameof(CanSendReply))]
    private async Task SendReplyAsync()
    {
        if (CurrentTask is null || string.IsNullOrWhiteSpace(ReplyText)) return;
        var taskId = CurrentTask.TaskId;
        try
        {
            await _agent.AppendUserReplyAsync(taskId, ReplyText);
            ReplyText = string.Empty;
            await LoadTaskIfOpenAsync(taskId);
            // Same approve-and-continue shape as ApproveReviewAsync: a
            // reply that unblocked the task should resume the loop instead
            // of requiring a separate manual step.
            await ResumeAgentLoopIfRunnableAsync(taskId);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private bool CanSendReply() => !IsRunning && IsWaitingForReply && !string.IsNullOrWhiteSpace(ReplyText);

    /// <summary>Bound to the "Continue task" instruction box (r19 3.1); placeholder text
    /// covers the empty-input default, so this stays literally empty until the user types.</summary>
    [ObservableProperty] private string _continueInstructionText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanContinueTask))]
    private async Task ContinueTaskAsync()
    {
        if (CurrentTask is null) return;
        var taskId = CurrentTask.TaskId;
        try
        {
            var options = CurrentTask.WorkspaceRoot is { Length: > 0 } root ? BuildOptions() with { WorkspaceRoot = root } : BuildOptions();
            await _agent.ContinueTaskAsync(taskId, ContinueInstructionText, options);
            ContinueInstructionText = string.Empty;
            await LoadTaskIfOpenAsync(taskId);
            // Same approve-and-continue shape as ApproveReviewAsync/SendReplyAsync:
            // a reopened task that AgentService just returned to Running should
            // resume the loop instead of requiring a separate manual step.
            await ResumeAgentLoopIfRunnableAsync(taskId);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private bool CanContinueTask() => !IsRunning && IsTaskTerminal;

    /// <summary>
    /// Shared by <see cref="ApproveReviewAsync"/> and <see cref="SendReplyAsync"/>:
    /// resumes the autonomous loop for a task that some other action (an
    /// approval, a reply) already returned to <see cref="AgentTaskStatus.Running"/>.
    /// A no-op if the loop is already running or there is nothing left for it
    /// to do. Unlike before r16, <paramref name="taskId"/> need not be the
    /// currently open task: approving a CHILD's pending action while the
    /// child itself is open must still resume the PARENT's orchestration
    /// loop (r16 01-orchestration-hardening.md 1.1), so this loads the
    /// target task's own state when it differs from <see cref="CurrentTask"/>.
    /// An orchestration parent can be resumable while its OWN status reads
    /// WaitingForUser/Blocked (it now truthfully mirrors its paused child's
    /// status - 1.6), so a parent with any unfinished sub-task spec is
    /// treated as runnable too; the orchestration loop itself decides
    /// whether the next spec is actually ready to advance.
    /// </summary>
    private async Task ResumeAgentLoopIfRunnableAsync(string taskId)
    {
        if (IsRunning) return;

        var target = CurrentTask?.TaskId == taskId ? CurrentTask : await _store.LoadAsync(taskId);
        var hasUnfinishedSubTasks = target?.SubTaskPlan.Any(s => s.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running) ?? false;
        if (target is null || (target.Status != AgentTaskStatus.Running && !hasUnfinishedSubTasks))
            return;

        IsRunning = true;
        IsError = false;
        _cts = new CancellationTokenSource();
        try
        {
            var options = target.WorkspaceRoot is { Length: > 0 } root ? BuildOptions() with { WorkspaceRoot = root } : BuildOptions();
            await RunAgentLoopAsync(taskId, options);
            await RefreshRecentAsync();
        }
        catch (OperationCanceledException) { StatusMessage = "Agent stopped."; }
        catch (Exception ex) { SetError(ex.Message); }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            StartCommand.NotifyCanExecuteChanged();
            RunStepCommand.NotifyCanExecuteChanged();
            SendReplyCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task RejectReviewAsync(AgentReviewQueueItemViewModel? item)
    {
        if (item is null) return;
        await _agent.AppendApprovalAsync(item.TaskId, "review_queue", approved: false, BuildOptions());
        await RefreshReviewQueueAsync();
        await LoadTaskIfOpenAsync(item.TaskId);
    }

    [RelayCommand]
    private async Task RefreshWorkspaceMemoryAsync()
    {
        WorkspaceMemory.Clear();
        if (string.IsNullOrWhiteSpace(WorkspaceRoot))
            return;

        foreach (var item in await _workspaceMemory.ListAsync(WorkspaceRoot))
            WorkspaceMemory.Add(new AgentWorkspaceMemoryEntryViewModel(item));
    }

    [RelayCommand]
    private async Task RefreshLessonsAsync()
    {
        Lessons.Clear();
        if (_lessons is null) return;

        var scopeId = string.IsNullOrWhiteSpace(WorkspaceRoot) ? null : WorkspaceRoot;
        foreach (var lesson in await _lessons.ListRelevantAsync(scopeId, includeRetired: true, limit: 200))
            Lessons.Add(new AgentLessonViewModel(lesson));
    }

    [RelayCommand]
    private async Task PinLessonAsync(AgentLessonViewModel? item)
    {
        if (item is null || _lessons is null) return;
        await _lessons.SetPinnedAsync(item.Id, !item.IsPinned);
        await RefreshLessonsAsync();
    }

    /// <summary>
    /// Looks up the full lesson for each id in <see cref="AgentTaskState.NewLessonIds"/>
    /// on the open task, so a lesson captured this task is seen once before
    /// it can influence future prompts (r6 03-platform-cleanup.md 3.3).
    /// Independent of <see cref="Lessons"/>'s workspace-scoped list: a
    /// direct id lookup, not a scope filter.
    /// </summary>
    private async Task RefreshNewLessonsAsync()
    {
        NewLessons.Clear();
        if (_lessons is null || CurrentTask is null) return;

        foreach (var id in CurrentTask.NewLessonIds)
        {
            var lesson = await _lessons.GetByIdAsync(id);
            if (lesson is not null && lesson.Status != AgentLessonStatus.Retired)
                NewLessons.Add(new AgentLessonViewModel(lesson));
        }
    }

    /// <summary>Acknowledges a newly captured lesson without changing it - the default action, kept active (r6 3.3).</summary>
    [RelayCommand]
    private void DismissNewLesson(AgentLessonViewModel? item)
    {
        if (item is not null)
            NewLessons.Remove(item);
    }

    [RelayCommand]
    private async Task RetireLessonAsync(AgentLessonViewModel? item)
    {
        if (item is null || _lessons is null) return;
        await _lessons.SetStatusAsync(item.Id, AgentLessonStatus.Retired);
        await RefreshLessonsAsync();
        await RefreshNewLessonsAsync();
    }

    [RelayCommand]
    private async Task ReactivateLessonAsync(AgentLessonViewModel? item)
    {
        if (item is null || _lessons is null) return;
        await _lessons.SetStatusAsync(item.Id, AgentLessonStatus.Active);
        await RefreshLessonsAsync();
    }

    [RelayCommand]
    private async Task DeleteLessonAsync(AgentLessonViewModel? item)
    {
        if (item is null || _lessons is null) return;
        await _lessons.DeleteAsync(item.Id);
        Lessons.Remove(item);
    }

    [RelayCommand]
    private async Task UpdateLessonAsync(AgentLessonViewModel? item)
    {
        if (item is null || _lessons is null) return;
        await _lessons.UpdateAsync(item.Id, item.Claim, item.Guidance);
        await RefreshLessonsAsync();
    }

    [RelayCommand]
    private Task RefreshQueuedPatchesAsync()
    {
        QueuedPatches.Clear();
        if (CurrentTask is null)
            return Task.CompletedTask;

        foreach (var patch in CurrentTask.DraftPatches
                     .OrderBy(patch => patch.Status == AgentDraftPatchStatus.Pending ? 0 : 1)
                     .ThenByDescending(patch => patch.CreatedAt))
            QueuedPatches.Add(new AgentDraftPatchViewModel(patch));

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshWorkspaceFilesAsync()
    {
        WorkspaceFiles.Clear();
        WorkspaceFilePreview = string.Empty;
        WorkspaceFileSummary = string.Empty;
        SelectedWorkspaceFile = null;

        if (string.IsNullOrWhiteSpace(WorkspaceRoot))
            return;

        var files = await Task.Run(() =>
        {
            var options = BuildOptions();
            return string.IsNullOrWhiteSpace(WorkspaceFileQuery)
                ? _workspaceTools.ListFiles(options)
                    .Select(path => new AgentWorkspaceFileViewModel(path, string.Empty, DateTime.MinValue))
                    .ToList()
                : _workspaceTools.SearchFiles(options, WorkspaceFileQuery)
                    .Select(result => new AgentWorkspaceFileViewModel(result.RelativePath, result.Snippet, result.ModifiedUtc))
                    .ToList();
        });

        foreach (var file in files)
            WorkspaceFiles.Add(file);

        OnPropertyChanged(nameof(WorkspaceFileCount));
        OnPropertyChanged(nameof(HasWorkspaceFiles));
    }

    /// <summary>
    /// r12 02-async-and-threading.md 2.3: <paramref name="generation"/> is a
    /// snapshot of <see cref="_workspaceFileSelectionGeneration"/> taken when
    /// this selection was made; a slower older read/summarize completing
    /// after a newer selection was made must not overwrite that newer
    /// selection's preview.
    /// </summary>
    private async Task LoadSelectedWorkspaceFileAsync(AgentWorkspaceFileViewModel? file, int generation)
    {
        if (file is null || string.IsNullOrWhiteSpace(WorkspaceRoot))
        {
            if (generation != _workspaceFileSelectionGeneration) return;
            WorkspaceFilePreview = string.Empty;
            WorkspaceFileSummary = string.Empty;
            DraftProposedContent = string.Empty;
            return;
        }

        var options = BuildOptions();
        var preview = await Task.Run(() => _workspaceTools.ReadFile(options, file.RelativePath));
        var summary = await Task.Run(() => _workspaceTools.SummarizeFile(options, file.RelativePath));
        if (generation != _workspaceFileSelectionGeneration)
            return;

        WorkspaceFilePreview = preview.Content;
        WorkspaceFileSummary = summary.Summary;
        // Populate draft proposed content with the current file preview by default
        DraftProposedContent = WorkspaceFilePreview;
    }

    [RelayCommand]
    private async Task GenerateDraftPatchAsync()
    {
        if (SelectedWorkspaceFile is null) return;
        try
        {
            var relative = SelectedWorkspaceFile.RelativePath;
            if (string.IsNullOrWhiteSpace(DraftProposedContent))
                DraftProposedContent = WorkspaceFilePreview;

            var result = await Task.Run(() => _workspaceTools.DraftPatch(relative, DraftRationale ?? string.Empty, DraftProposedContent ?? string.Empty));
            DraftPreview = result ?? string.Empty;
            var approved = RequestDraftPatchPreview is not null
                && await RequestDraftPatchPreview(new DraftPatchPreviewRequest(
                    Guid.NewGuid().ToString(),
                    relative,
                    WorkspaceFilePreview ?? string.Empty,
                    DraftProposedContent ?? string.Empty));
            if (approved)
                await QueueDraftPatchAsync();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task QueueDraftPatchAsync()
    {
        if (CurrentTask is null || SelectedWorkspaceFile is null) return;
        try
        {
            var patch = new AgentDraftPatch
            {
                RelativePath = SelectedWorkspaceFile.RelativePath,
                Rationale = DraftRationale ?? string.Empty,
                ProposedContent = DraftProposedContent ?? WorkspaceFilePreview
            };
            CurrentTask.DraftPatches.Add(patch);
            await _store.SaveAsync(CurrentTask);
            QueuedPatches.Add(new AgentDraftPatchViewModel(patch));
            DraftRationale = string.Empty;
            DraftProposedContent = string.Empty;
            DraftPreview = string.Empty;
            StatusMessage = $"Patch for {SelectedWorkspaceFile.RelativePath} queued for review.";
            RefreshTaskPreview();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ApprovePatchAsync(AgentDraftPatchViewModel? patch)
    {
        if (CurrentTask is null || patch is null) return;
        try
        {
            var found = CurrentTask.DraftPatches.FirstOrDefault(p => p.Id == patch.Id);
            if (found is not null)
            {
                var filePreview = _workspaceTools.ReadFile(BuildOptions(), found.RelativePath)?.Content ?? string.Empty;
                var approved = RequestDraftPatchPreview is not null
                    && await RequestDraftPatchPreview(new DraftPatchPreviewRequest(
                        found.Id,
                        found.RelativePath,
                        filePreview,
                        found.ProposedContent));
                if (approved)
                    await HandlePreviewDecisionAsync(found.Id, apply: true);
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private async Task HandlePreviewDecisionAsync(string patchId, bool apply)
    {
        if (!apply || CurrentTask is null) return;
        var found = CurrentTask.DraftPatches.FirstOrDefault(p => p.Id == patchId);
        if (found is null) return;
        try
        {
            var selectedPath = SelectedWorkspaceFile?.RelativePath;
            await _patchReview.ApplyAsync(CurrentTask, found, BuildOptions());
            StatusMessage = $"Patch for {found.RelativePath} applied.";
            await RefreshWorkspaceFilesAsync();
            if (!string.IsNullOrWhiteSpace(selectedPath))
                SelectedWorkspaceFile = WorkspaceFiles.FirstOrDefault(file => file.RelativePath == selectedPath);
            await LoadTaskIfOpenAsync(CurrentTask.TaskId);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task RejectPatchAsync(AgentDraftPatchViewModel? patch)
    {
        if (CurrentTask is null || patch is null) return;
        try
        {
            var found = CurrentTask.DraftPatches.FirstOrDefault(p => p.Id == patch.Id);
            if (found is not null)
            {
                await _patchReview.RejectAsync(CurrentTask, found, BuildOptions());
                StatusMessage = $"Patch for {patch.RelativePath} rejected.";
                await LoadTaskIfOpenAsync(CurrentTask.TaskId);
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task BlockPatchAsync(AgentDraftPatchViewModel? patch)
    {
        if (CurrentTask is null || patch is null) return;
        try
        {
            var found = CurrentTask.DraftPatches.FirstOrDefault(p => p.Id == patch.Id);
            if (found is not null)
            {
                await _patchReview.BlockAsync(CurrentTask, found, BuildOptions());
                StatusMessage = $"Patch for {patch.RelativePath} blocked.";
                await LoadTaskIfOpenAsync(CurrentTask.TaskId);
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task RevertPatchAsync(AgentDraftPatchViewModel? patch)
    {
        if (CurrentTask is null || patch is null) return;
        try
        {
            var found = CurrentTask.DraftPatches.FirstOrDefault(p => p.Id == patch.Id);
            if (found is not null)
            {
                var error = await _patchReview.RevertAsync(CurrentTask, found, BuildOptions());
                StatusMessage = string.IsNullOrEmpty(error)
                    ? $"Patch for {patch.RelativePath} reverted."
                    : error;
                await RefreshWorkspaceFilesAsync();
                await LoadTaskIfOpenAsync(CurrentTask.TaskId);
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveWorkspaceMemoryAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || string.IsNullOrWhiteSpace(GoalText))
            return;

        var entry = new AgentWorkspaceMemoryEntry
        {
            WorkspaceRoot = WorkspaceRoot,
            Title = GoalText.Trim(),
            Body = CurrentTask?.Summary ?? GoalText.Trim(),
            Tags = ["agent", "workspace"]
        };

        await _workspaceMemory.UpsertAsync(entry);
        await RefreshWorkspaceMemoryAsync();
    }

    [RelayCommand]
    private async Task DeleteWorkspaceMemoryAsync(AgentWorkspaceMemoryEntryViewModel? entry)
    {
        if (entry is null) return;
        await _workspaceMemory.DeleteAsync(WorkspaceRoot, entry.Id);
        await RefreshWorkspaceMemoryAsync();
    }

    [RelayCommand]
    private void BrowseWorkspaceRoot() => RequestWorkspaceRootPicker?.Invoke();

    [RelayCommand]
    private async Task ExplainWorkspaceAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || !Directory.Exists(WorkspaceRoot))
            return;

        IsAnalyzingWorkspace = true;
        IsError = false;
        try
        {
            var report = await _workspaceAnalysis.AnalyzeAsync(BuildOptions());
            WorkspaceProfileSummary = report.Summary;
            WorkspaceRepoType = report.RepoType;
            WorkspaceLanguages = string.Join(", ", report.Languages);
            WorkspaceFrameworks = string.Join(", ", report.Frameworks);
            WorkspaceImportantFiles = string.Join(", ", report.ImportantFiles);
            WorkspaceRagIngestPlan = report.RagIngestPlan;
            SuggestedAgentsMd = report.SuggestedAgentsMd;

            ProjectInstructions.Clear();
            foreach (var instruction in report.Instructions)
                ProjectInstructions.Add(new ProjectInstructionFileViewModel(instruction));

            InstructionWarnings.Clear();
            foreach (var warning in report.InstructionWarnings)
                InstructionWarnings.Add(warning);

            WorkspaceRisks.Clear();
            foreach (var risk in report.Risks)
                WorkspaceRisks.Add(risk);

            CommandRecipes.Clear();
            foreach (var recipe in report.CommandRecipes)
                CommandRecipes.Add(new WorkspaceCommandRecipeViewModel(recipe));

            await _workspaceMemory.UpsertAsync(new AgentWorkspaceMemoryEntry
            {
                WorkspaceRoot = WorkspaceRoot,
                Title = "Workspace profile",
                Body = $"{report.Summary}\n\nRAG ingest plan: {report.RagIngestPlan}",
                Tags = ["workspace", "profile", "auto"]
            });
            await RefreshWorkspaceMemoryAsync();
            await ActivateWorkspaceAsync();
            StatusMessage = "Workspace profile updated.";
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            IsAnalyzingWorkspace = false;
        }
    }

    private async Task ActivateWorkspaceAsync()
    {
        var activation = await _workspaceActivation.ActivateAsync(WorkspaceRoot);
        var model = activation.ResolvePreferredModel(AvailableModels, m => m.Id);
        if (model is not null) SelectedModel = model;

        var dataset = activation.ResolveLinkedDataset(Datasets, d => d.Id);
        if (dataset is not null) SelectedDataset = dataset;

        _activeWorkspaceVoiceProfile = activation.VoiceProfileName ?? string.Empty;
        WorkspaceVoiceProfileName = _activeWorkspaceVoiceProfile;
    }

    [RelayCommand]
    private async Task SaveWorkspaceManifestAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || !Directory.Exists(WorkspaceRoot))
            return;

        var manifest = await _workspaceManifests.LoadAsync(WorkspaceRoot) ?? new WorkspaceManifest();
        manifest.PreferredModelId = SelectedModel?.Id ?? string.Empty;
        manifest.LinkedRagDatasetId = SelectedDataset?.Id;
        manifest.InstructionPaths = ProjectInstructions.Select(i => i.RelativePath).ToList();
        manifest.AllowedCommands = CommandRecipes
            .Select(r => new WorkspaceCommandRecipe(r.Command, r.Why, r.RiskLevel))
            .ToList();
        manifest.VoiceProfileName = WorkspaceVoiceProfileName.Trim();
        await _workspaceManifests.SaveAsync(WorkspaceRoot, manifest);
        _activeWorkspaceVoiceProfile = manifest.VoiceProfileName;
        StatusMessage = "Saved workspace defaults to .hermaeus/workspace.json.";
    }

    private async Task LoadTaskIfOpenAsync(string taskId)
    {
        if (CurrentTask?.TaskId != taskId)
            return;

        CurrentTask = await _store.LoadAsync(taskId);
        RefreshTaskPreview();
        await RefreshRecentAsync();
        await RefreshNewLessonsAsync();
    }

    private async Task RunCurrentStepAsync()
    {
        if (CurrentTask is null) return;

        // A parent with unfinished sub-tasks can never take a bare parent
        // model step - AgentService.RunStepAsync throws for it (r16
        // 01-orchestration-hardening.md 1.2). The Run Step button advances
        // orchestration instead: one child pause boundary at a time, via
        // the same loop Start uses.
        if (CurrentTask.SubTaskPlan.Any(s => s.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running))
        {
            await RunAgentLoopAsync();
            return;
        }

        StatusMessage = "Building context and asking the agent...";
        var result = await _agent.RunStepAsync(CurrentTask.TaskId, BuildOptions(), _cts?.Token ?? CancellationToken.None);
        ApplyStepResult(result);
        await RefreshLogAsync();
        await RefreshLessonsAsync();
        await RefreshNewLessonsAsync();
        _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Agent,
            $"Agent step complete: {result.State.ActiveStep}"));
    }

    /// <summary>
    /// Runs the currently-open task's autonomous loop (Start, or resuming
    /// after an approval on the open task itself).
    /// </summary>
    private async Task RunAgentLoopAsync()
    {
        if (CurrentTask is null) return;
        await RunAgentLoopAsync(CurrentTask.TaskId, BuildOptions());
    }

    /// <summary>
    /// Core of the autonomous loop, parameterized by task id (r16
    /// 01-orchestration-hardening.md 1.1) so <see cref="ResumeAgentLoopIfRunnableAsync"/>
    /// can resume a task other than the one currently open - approving a
    /// CHILD's pending action while the child itself is open must resume
    /// the PARENT's orchestration, not silently switch the workbench away
    /// from the child the user is looking at. Each intermediate step still
    /// updates the visible task/context state via <see cref="ApplyStepResult"/>
    /// (which already no-ops for a child step while a different task is
    /// open, via its own isChildStep check); <see cref="CurrentTask"/> is
    /// only reassigned at the end if it still points at the resumed task.
    /// </summary>
    private async Task RunAgentLoopAsync(string taskId, AgentWorkspaceOptions options)
    {
        var openedTaskId = _openedTaskId = taskId;
        var viewedTaskId = CurrentTask?.TaskId;
        StatusMessage = "Running agent...";
        var result = await _agent.RunAsync(
            openedTaskId,
            options,
            onStep: ApplyStepResult,
            ct: _cts?.Token ?? CancellationToken.None);

        // The returned result can describe a paused CHILD task rather than
        // the parent that was resumed, and even the parent's own final
        // synthesis step never flows through ApplyStepResult's onStep
        // (RunSynthesisAsync calls RunStepAsync directly - r15
        // 01-subtask-orchestration.md 1.6). Re-fetch the resumed task itself
        // so CurrentTask (and its SubTaskPlan) always reflect the latest
        // persisted state after the run settles - but only when the
        // workbench was already showing that task (or nothing) before this
        // call; otherwise the view stays on whatever the user had open.
        if (viewedTaskId == openedTaskId || viewedTaskId is null)
        {
            CurrentTask = await _store.LoadAsync(openedTaskId) ?? result.State;
            RefreshTaskPreview();
        }

        await RefreshLogAsync();
        await RefreshLessonsAsync();
        await RefreshNewLessonsAsync();
        _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Agent,
            $"Agent run paused: {result.State.ActiveStep} (status {result.State.Status})"));
    }

    /// <summary>
    /// The per-step UI update shared by a single manual step and every step
    /// of an autonomous run, called synchronously on the UI thread (async
    /// continuations here resume on the same context that awaited the
    /// agent call, as with the rest of this ViewModel). During an
    /// orchestrated run, a step can belong to a running CHILD task rather
    /// than the parent the user opened; CurrentTask stays pointed at the
    /// opened task and the step's text is labeled with the child's position
    /// instead (r15 02-orchestration-ui.md 2.2).
    /// </summary>
    private void ApplyStepResult(AgentStepResult result)
    {
        var isChildStep = _openedTaskId is not null && result.State.TaskId != _openedTaskId;
        var label = isChildStep ? BuildSubTaskLabel(result.State.TaskId) : string.Empty;

        var previousStatus = CurrentTask?.Status;
        if (!isChildStep)
            CurrentTask = result.State;
        NarrateStatusTransition(previousStatus, result.State);
        CurrentStep = label + result.State.ActiveStep;
        StatusMessage = label + result.LogEntry;
        NextActionPreview = JsonSerializer.Serialize(result.PlannerResponse.NextAction, new JsonSerializerOptions { WriteIndented = true });
        RetrievedContext.Clear();
        foreach (var item in result.ContextPack.RetrievedMemory.Concat(result.ContextPack.RetrievedFiles).Concat(result.ContextPack.ProjectInstructions).Concat(result.ContextPack.Lessons))
        {
            RetrievedContext.Add(new AgentContextItemViewModel
            {
                Source = item.Source,
                Title = item.Title,
                Content = item.Content,
                ScoreDisplay = item.Score > 0 ? item.Score.ToString("F3") : string.Empty,
                Locator = item.Locator ?? string.Empty
            });
        }

        ContextReceipt.Clear();
        foreach (var section in AgentContextReceiptBuilder.Build(result.ContextPack))
            ContextReceipt.Add(new AgentContextReceiptSectionViewModel(section));

        if (!isChildStep)
            RefreshTaskPreview();
    }

    /// <summary>
    /// "[sub-task 2/4: security] " style prefix identifying which child a
    /// step belongs to, looked up from the opened parent's SubTaskPlan
    /// (r15 02-orchestration-ui.md 2.2). Empty if the child id is not (yet)
    /// found in CurrentTask's plan, e.g. a stale CurrentTask snapshot.
    /// </summary>
    private string BuildSubTaskLabel(string childTaskId)
    {
        var specs = CurrentTask?.SubTaskPlan;
        if (specs is null) return string.Empty;
        var index = specs.FindIndex(s => s.TaskId == childTaskId);
        return index < 0 ? string.Empty : $"[sub-task {index + 1}/{specs.Count}: {specs[index].ProfileName}] ";
    }

    /// <summary>
    /// Milestone-only agent narration (never per-step, never model text
    /// verbatim): task started, waiting for approval/reply (Critical - the
    /// run is blocked on the user), and terminal Complete/Failed states.
    /// No-op when no orchestrator was wired in (e.g. plain unit tests).
    /// </summary>
    private void NarrateStatusTransition(AgentTaskStatus? previousStatus, AgentTaskState state)
    {
        if (_voice is null || previousStatus == state.Status)
            return;

        switch (state.Status)
        {
            case AgentTaskStatus.WaitingForUser:
                var reason = state.PendingToolAction is not null ? "waiting for your approval" : "waiting for your reply";
                Narrate($"Agent task {reason}.", VoicePriority.Critical, $"{state.TaskId}:waiting:{state.StepCount}");
                break;
            case AgentTaskStatus.Complete:
                Narrate("Agent task completed.", VoicePriority.Normal, $"{state.TaskId}:complete");
                break;
            case AgentTaskStatus.Failed:
                Narrate("Agent task failed.", VoicePriority.Critical, $"{state.TaskId}:failed");
                break;
        }
    }

    private void Narrate(string text, VoicePriority priority, string dedupeKey)
    {
        if (_voice is null) return;
        _ = _voice.EnqueueAsync(new VoiceUtterance(text, VoiceChannel.Agent, priority, ResolveWorkspaceVoiceOverride(), dedupeKey));
    }

    private string? ResolveWorkspaceVoiceOverride()
    {
        if (_settings is null || string.IsNullOrWhiteSpace(_activeWorkspaceVoiceProfile))
            return null;
        var profile = _settings.Settings.Tts.Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, _activeWorkspaceVoiceProfile, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(profile?.VoiceId) ? null : profile.VoiceId;
    }

    /// <summary>
    /// Uses the open task's OWN stored workspace root when it has one
    /// (r16 01-orchestration-hardening.md 1.4), so resuming a task steps
    /// against its own workspace even when the workbench's active
    /// <see cref="WorkspaceRoot"/> currently points somewhere else. Falls
    /// back to the workbench root for task creation (no task open yet) and
    /// for pre-r16 task state with no stored root.
    /// </summary>
    private AgentWorkspaceOptions BuildOptions() => new(
        CurrentTask?.WorkspaceRoot is { Length: > 0 } root ? root : WorkspaceRoot,
        SelectedDataset?.Id,
        SelectedModel?.Id ?? string.Empty);

    private bool CanStart() =>
        !IsRunning
        && !string.IsNullOrWhiteSpace(GoalText)
        && !string.IsNullOrWhiteSpace(WorkspaceRoot)
        && SelectedModel is not null;

    private bool CanRunStep() => !IsRunning && CurrentTask is not null && SelectedModel is not null;

    private async Task RefreshRecentAsync()
    {
        RecentTasks.Clear();
        foreach (var task in await _agent.LoadRecentTasksAsync())
            RecentTasks.Add(new AgentTaskListItemViewModel(task));
    }

    private async Task RefreshLogAsync()
    {
        if (CurrentTask is null) return;
        var path = Path.Combine(_store.GetTaskDirectory(CurrentTask.TaskId), "agent.log");
        LogPreview = File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;
    }

    private void RefreshTaskPreview()
    {
        OnPropertyChanged(nameof(CurrentTaskStatusLabel));
        OnPropertyChanged(nameof(CurrentStepCountLabel));
        OnPropertyChanged(nameof(CurrentTaskGoalLabel));
        OnPropertyChanged(nameof(CurrentTaskSummaryLabel));
        OnPropertyChanged(nameof(QueuedPatchCount));
        OnPropertyChanged(nameof(PendingPatchCount));
        OnPropertyChanged(nameof(AppliedPatchCount));
        OnPropertyChanged(nameof(RejectedPatchCount));
        OnPropertyChanged(nameof(BlockedPatchCount));
        OnPropertyChanged(nameof(HasQueuedPatches));
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(HasSubTaskPlan));
        OnPropertyChanged(nameof(CurrentTaskParentGoalLabel));
        OnPropertyChanged(nameof(HasCurrentTaskParentGoal));

        if (CurrentTask is null)
        {
            TaskStatePreview = string.Empty;
            CurrentStep = string.Empty;
            QueuedPatches.Clear();
            return;
        }

        CurrentStep = CurrentTask.ActiveStep;
        TaskStatePreview = JsonSerializer.Serialize(CurrentTask, new JsonSerializerOptions { WriteIndented = true });
        _ = RefreshQueuedPatchesAsync();
    }

    private void SetError(string message)
    {
        IsError = true;
        StatusMessage = message;
        _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Agent, message));
    }

    partial void OnGoalTextChanged(string value) => StartCommand.NotifyCanExecuteChanged();
    partial void OnWorkspaceRootChanged(string value)
    {
        StartCommand.NotifyCanExecuteChanged();
        ExplainWorkspaceCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasWorkspace));
    }
    /// <summary>
    /// r12 02-async-and-threading.md 2.3: reuses the 300 ms + CTS debounce
    /// shape from <see cref="MainWindowViewModel.OnSearchQueryChanged"/>
    /// instead of firing a full workspace file enumeration per keystroke.
    /// </summary>
    partial void OnWorkspaceFileQueryChanged(string value)
    {
        _workspaceFileQueryCts?.Cancel();
        _workspaceFileQueryCts?.Dispose();
        _workspaceFileQueryCts = new CancellationTokenSource();
        var token = _workspaceFileQueryCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                if (token.IsCancellationRequested) return;
                await RunOnUiAsync(RefreshWorkspaceFilesAsync);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    partial void OnSelectedWorkspaceFileChanged(AgentWorkspaceFileViewModel? value)
    {
        var generation = ++_workspaceFileSelectionGeneration;
        _ = LoadSelectedWorkspaceFileAsync(value, generation);
    }
    partial void OnSelectedModelChanged(LlmModel? value)
    {
        StartCommand.NotifyCanExecuteChanged();
        RunStepCommand.NotifyCanExecuteChanged();
        if (ScenarioSuite is not null)
            ScenarioSuite.ModelId = value?.Id ?? string.Empty;
    }
    partial void OnCurrentTaskChanged(AgentTaskState? value)
    {
        RunStepCommand.NotifyCanExecuteChanged();
        SendReplyCommand.NotifyCanExecuteChanged();
        NewTaskCommand.NotifyCanExecuteChanged();
        ContinueTaskCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsWaitingForReply));
        OnPropertyChanged(nameof(CanShowNewTaskButton));
        OnPropertyChanged(nameof(IsTaskTerminal));
        OnPropertyChanged(nameof(PrematureCompleteNote));
        OnPropertyChanged(nameof(HasPrematureCompleteNote));
        _ = RefreshQueuedPatchesAsync();
    }
    partial void OnReplyTextChanged(string value) => SendReplyCommand.NotifyCanExecuteChanged();
    partial void OnIsRunningChanged(bool value)
    {
        NewTaskCommand.NotifyCanExecuteChanged();
        ContinueTaskCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowNewTaskButton));
    }
}
