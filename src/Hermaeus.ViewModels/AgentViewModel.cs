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

public sealed record AgentSubTaskModelOption(string ModelId, string Label, bool IsAvailable = true);

public partial class AgentSubTaskModelChoiceViewModel : ObservableObject
{
    public int Index { get; init; }
    public string Goal { get; init; } = string.Empty;
    public UiBoundCollection<AgentSubTaskModelOption> Options { get; } = [];
    [ObservableProperty] private AgentSubTaskModelOption? _selectedOption;
}

public sealed class AgentReviewQueueItemViewModel
{
    public AgentReviewQueueItemViewModel(
        AgentReviewQueueItem item,
        string recipePreview = "",
        string activeWorkspaceRoot = "",
        IReadOnlyList<LlmModel>? availableModels = null,
        AgentTaskState? fullState = null)
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
        // Computed fresh from the pending action as just loaded, not read off
        // a possibly-stale stored value, so a legacy (pre-r23) task without a
        // stored Fingerprint still gets a correct one here (r23 4.1). This is
        // "the fingerprint of the action actually rendered": AppendApprovalAsync
        // recomputes the same way from current state and compares.
        PendingFingerprint = AgentApprovalFingerprint.Resolve(item.PendingToolAction);
        RecipePreview = recipePreview;
        ParentGoal = item.ParentGoal ?? string.Empty;
        ParentTaskId = item.ParentTaskId;
        WorkspaceRoot = item.WorkspaceRoot ?? string.Empty;
        DifferentWorkspaceLabel = WorkspaceRoot.Length > 0
            && !string.Equals(WorkspaceRoot, activeWorkspaceRoot, StringComparison.OrdinalIgnoreCase)
                ? $"workspace: {Path.GetFileName(WorkspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}"
                : string.Empty;
        BuildSubTaskModelChoices(item.PendingToolAction, availableModels ?? [], fullState);
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
        : $"Last review {LocalTimeFormat.DateTimeMinutes(LastApprovalAt.Value)}";

    /// <summary>Name of the gated tool waiting on approval, e.g. "run_command"; empty if this queue entry has none (r6 1.7).</summary>
    public string PendingToolName { get; }
    public AgentRiskLevel? PendingRiskLevel { get; }
    public string PendingRiskLabel => PendingRiskLevel?.ToString() ?? string.Empty;
    /// <summary>Why the safety gate gated this action (AgentToolPolicyDecision.Reason).</summary>
    public string PendingReason { get; }
    /// <summary>The pending action's fingerprint as rendered here; passed back to AppendApprovalAsync so approval executes only what was actually shown (r23 4.1).</summary>
    public string PendingFingerprint { get; }
    public bool HasPendingAction => !string.IsNullOrEmpty(PendingToolName);

    /// <summary>r26 01 1.3: WaitingForUser with nothing pending means the agent asked a
    /// question; the row offers Open (which reaches the reply box), not Approve.</summary>
    public bool NeedsReply => !HasPendingAction && Status == AgentTaskStatus.WaitingForUser;

    /// <summary>r26 01 1.3: a Blocked run needs an instruction, not a decision; Open reaches
    /// the Continue box.</summary>
    public bool NeedsInstruction => !HasPendingAction && Status == AgentTaskStatus.Blocked;

    /// <summary>The single honest line that replaces Approve/Reject on a row with no pending action.</summary>
    public string NoDecisionLabel => NeedsReply
        ? "The agent asked you a question. Open it to reply."
        : NeedsInstruction ? "This run stopped and needs an instruction. Open it to continue." : string.Empty;

    /// <summary>What a pending run_command approval will actually execute (r6 3.2); empty for non-command tools.</summary>
    public string RecipePreview { get; }
    public bool HasRecipePreview => !string.IsNullOrWhiteSpace(RecipePreview);
    public UiBoundCollection<AgentSubTaskModelChoiceViewModel> SubTaskModelChoices { get; } = [];
    public bool HasSubTaskModelChoices => SubTaskModelChoices.Count > 0;

    /// <summary>
    /// The command family this row is blocked on, when the agent asked for a
    /// real family this workspace simply has not declared. Empty when the
    /// request was not a command at all, or was not a family the agent can
    /// ever run: there is nothing to offer in that case, and offering
    /// something would imply an approval that will never exist.
    /// </summary>
    public string UndeclaredCommandFamily { get; init; } = string.Empty;
    public bool CanDeclareCommandFamily => UndeclaredCommandFamily.Length > 0;
    public string DeclareCommandFamilyLabel => $"Allow '{UndeclaredCommandFamily}' in this workspace";

    private void BuildSubTaskModelChoices(
        AgentPendingToolAction? pending,
        IReadOnlyList<LlmModel> availableModels,
        AgentTaskState? state)
    {
        if (pending is null || !string.Equals(pending.ToolName, "plan_subtasks", StringComparison.OrdinalIgnoreCase)
            || !pending.Arguments.TryGetValue("subtasks", out var raw)
            || raw is not JsonElement { ValueKind: JsonValueKind.Array } array)
            return;

        var parentLabel = string.IsNullOrWhiteSpace(state?.ModelDisplayName) ? state?.ModelId ?? "parent" : state.ModelDisplayName;
        var subtasks = array.EnumerateArray().ToArray();
        for (var index = 0; index < subtasks.Length; index++)
        {
            var subtask = subtasks[index];
            if (subtask.ValueKind != JsonValueKind.Object) continue;
            var goal = subtask.TryGetProperty("goal", out var goalValue) ? goalValue.GetString() ?? string.Empty : string.Empty;
            var proposed = subtask.TryGetProperty("model_id", out var modelValue) ? modelValue.GetString() ?? string.Empty : string.Empty;
            var choice = new AgentSubTaskModelChoiceViewModel { Index = index, Goal = goal };
            choice.Options.Add(new AgentSubTaskModelOption(string.Empty, $"Inherit {parentLabel}"));
            foreach (var model in availableModels.Where(model => model.IsVisible))
                choice.Options.Add(new AgentSubTaskModelOption(model.Id, model.DisplayName));
            choice.SelectedOption = choice.Options.FirstOrDefault(option => string.Equals(option.ModelId, proposed, StringComparison.Ordinal));
            if (choice.SelectedOption is null)
            {
                choice.SelectedOption = new AgentSubTaskModelOption(proposed, $"Unavailable: {proposed}", false);
                choice.Options.Add(choice.SelectedOption);
            }
            SubTaskModelChoices.Add(choice);
        }
    }
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
        HasReservations = item.HasReservations;
        ModelId = item.ModelId;
        ModelDisplayName = item.ModelDisplayName;
    }

    public string TaskId { get; }
    public string Goal { get; }
    public AgentTaskStatus Status { get; }
    public DateTime UpdatedAt { get; }
    public string? ParentTaskId { get; }
    public int PendingStepCount { get; }
    public bool HasReservations { get; }
    public string ModelId { get; }
    public string ModelDisplayName { get; }
    public string ModelLabel => string.IsNullOrWhiteSpace(ModelId)
        ? "Model inherits on next run"
        : $"{(string.IsNullOrWhiteSpace(ModelDisplayName) ? ModelId : ModelDisplayName)} ({ModelId})";
    public bool IsSubTask => !string.IsNullOrWhiteSpace(ParentTaskId);
    public bool CanDelete => !IsSubTask && Status != AgentTaskStatus.Running;
    /// <summary>r23 2.3: presentation only - status stays Complete; a non-empty Reservations list just changes what this label says.</summary>
    public string StatusLabel => Status == AgentTaskStatus.Complete && HasReservations ? "Completed with reservations" : Status.ToString();

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
    public string ModifiedLabel => LocalTimeFormat.DateTimeMinutes(ModifiedUtc);
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

/// <summary>
/// One pickable command family in the workbench's recipe editor. The list is
/// fixed by the safety gate, so this is a chooser, never a free-text field:
/// anything outside these families is refused at run time regardless.
/// </summary>
public sealed class CommandFamilyOptionViewModel
{
    public CommandFamilyOptionViewModel(string family)
    {
        Family = family;
        Description = WorkspaceCommandRecipes.DescribeFamily(family);
    }

    public string Family { get; }
    public string Description { get; }
    public string Display => Description.Length == 0 ? Family : $"{Family} - {Description}";
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
    public string CreatedLabel => $"Created {LocalTimeFormat.DateTimeMinutes(CreatedAt)}";
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

/// <summary>One row of the Changes (Run Ledger) view's file list (r23 1.1/1.2).</summary>
public sealed class AgentLedgerFileEntryViewModel
{
    public AgentLedgerFileEntryViewModel(AgentLedgerFileEntry entry, bool conflicted)
    {
        RelativePath = entry.RelativePath;
        Kind = entry.Kind;
        AppliedPatchCount = entry.AppliedPatchCount;
        LineDelta = entry.LineDelta;
        TaskId = entry.TaskId;
        EarliestPreImageContent = entry.EarliestPreImageContent ?? string.Empty;
        LatestAppliedContent = entry.LatestAppliedContent;
        // The builder only ever reports Applied/Reverted (r23 1.1); Conflicted
        // is layered on here, the caller with workspace access, once a live
        // read shows the file no longer matches what the run last wrote.
        Status = conflicted && entry.Status == AgentLedgerFileStatus.Applied
            ? AgentLedgerFileStatus.Conflicted
            : entry.Status;
    }

    public string RelativePath { get; }
    public AgentLedgerFileKind Kind { get; }
    public int AppliedPatchCount { get; }
    public int LineDelta { get; }
    public string TaskId { get; }
    public string EarliestPreImageContent { get; }
    public string LatestAppliedContent { get; }
    public AgentLedgerFileStatus Status { get; }

    public string KindLabel => Kind == AgentLedgerFileKind.Created ? "created" : "edited";
    public string StatusLabel => Status switch
    {
        AgentLedgerFileStatus.Applied => "applied",
        AgentLedgerFileStatus.Reverted => "reverted",
        AgentLedgerFileStatus.Conflicted => "conflicted",
        _ => Status.ToString()
    };
    public string LineDeltaLabel => LineDelta > 0 ? $"+{LineDelta}" : LineDelta.ToString();
    public string PatchCountLabel => $"{AppliedPatchCount} patch{(AppliedPatchCount == 1 ? string.Empty : "es")}";
}

/// <summary>One row of the Changes view's command list (r23 1.1/1.2).</summary>
public sealed class AgentLedgerCommandEntryViewModel
{
    public AgentLedgerCommandEntryViewModel(AgentLedgerCommandEntry entry)
    {
        Command = entry.Command;
        ExitCode = entry.ExitCode;
        TimedOut = entry.TimedOut;
        Timestamp = entry.Timestamp;
    }

    public string Command { get; }
    public int? ExitCode { get; }
    public bool TimedOut { get; }
    public DateTime Timestamp { get; }
    public string OutcomeLabel => TimedOut ? "timed out" : ExitCode is { } code ? $"exit {code}" : "no exit code recorded";
}

/// <summary>One row of the Changes view's approvals list (r23 1.1/1.2).</summary>
public sealed class AgentLedgerApprovalEntryViewModel
{
    public AgentLedgerApprovalEntryViewModel(AgentLedgerApprovalEntry entry)
    {
        Action = entry.Action;
        Approved = entry.Approved;
        Timestamp = entry.Timestamp;
    }

    public string Action { get; }
    public bool Approved { get; }
    public DateTime Timestamp { get; }
    public string OutcomeLabel => Approved ? "approved" : "rejected";
}

/// <summary>
/// What Rewind is about to do, for the confirmation dialog (r23 1.4): every
/// file it will restore, and every file it will delete (created by the run,
/// so nothing existed before it). The dialog is not optional.
/// </summary>
public sealed record AgentTaskRewindConfirmation(IReadOnlyList<string> FilesToRestore, IReadOnlyList<string> FilesToDelete);

public partial class AgentViewModel : ViewModelBase
{
    public Func<DraftPatchPreviewRequest, Task<bool>>? RequestDraftPatchPreview { get; set; }
    public Func<string, Task<bool>>? RequestCopyToClipboard { get; set; }

    [RelayCommand]
    private async Task CopyResponseAsync()
    {
        var text = CurrentTaskSummaryLabel;
        if (RequestCopyToClipboard is null || string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "Response copying is unavailable.";
            return;
        }

        StatusMessage = await RequestCopyToClipboard(text) ? "Copied Agent response." : "Could not copy Agent response.";
    }
    /// <summary>
    /// Emitted before RewindTaskAsync touches anything, listing exactly which
    /// files will be restored and which will be deleted; returning false
    /// cancels without reverting a single file (r23 1.4). Destructive-adjacent,
    /// so this confirmation is never optional and has no "do not ask again".
    /// </summary>
    public Func<AgentTaskRewindConfirmation, Task<bool>>? RequestRewindConfirmation { get; set; }
    private readonly IAgentService _agent;
    private readonly IAgentTaskStateStore _store;
    private readonly Hermaeus.Services.Recall.RecallIndexingService? _recallIndexing;
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
    /// <summary>r29 doc 03 3.5: steering refusals need a reason the user sees.
    /// Optional so the existing test constructions are unaffected.</summary>
    private readonly IToastService? _toasts;
    private CancellationTokenSource? _cts;
    private string? _activeRunTaskId;
    private bool _stopRequested;
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
    /// <summary>The Run Ledger's files section for the open task, folding in any orchestration children (r23 1.1/1.2).</summary>
    public UiBoundCollection<AgentLedgerFileEntryViewModel> LedgerFiles { get; } = [];
    public UiBoundCollection<AgentLedgerCommandEntryViewModel> LedgerCommands { get; } = [];
    public UiBoundCollection<AgentLedgerApprovalEntryViewModel> LedgerApprovals { get; } = [];
    public UiBoundCollection<ProjectInstructionFileViewModel> ProjectInstructions { get; } = [];
    public UiBoundCollection<WorkspaceCommandRecipeViewModel> CommandRecipes { get; } = [];
    public UiBoundCollection<string> WorkspaceRisks { get; } = [];
    public UiBoundCollection<string> InstructionWarnings { get; } = [];
    /// <summary>Raw glob rules behind WorkspacePolicySummary, for the capability disclosure's expandable detail (r23 3.3). Read-only; the manifest is hand-edited, not edited from here.</summary>
    public UiBoundCollection<string> WorkspacePolicyRules { get; } = [];
    [ObservableProperty] private string _workspacePolicySummary = string.Empty;
    public bool HasWorkspacePolicy => WorkspacePolicySummary.Length > 0;
    partial void OnWorkspacePolicySummaryChanged(string value)
    {
        OnPropertyChanged(nameof(HasWorkspacePolicy));
        RefreshCapabilityNotes();
    }

    /// <summary>
    /// r26 03 3.1: derived from the executor's tool set, this workspace's own
    /// declared command recipes, its policy summary and whether an MCP bridge
    /// is configured, instead of the five hardcoded sentences this used to be.
    /// Rebuilt by <see cref="RefreshCapabilityNotes"/> whenever any of those change.
    /// </summary>
    public UiBoundCollection<string> CapabilityNotes { get; } = [];

    public string CapabilityLabel => "What the agent can do here";

    private void RefreshCapabilityNotes()
    {
        var notes = AgentCapabilityNotes.Describe(new AgentCapabilityContext(
            HasWorkspace: !string.IsNullOrWhiteSpace(WorkspaceRoot),
            CommandRecipes: [.. CommandRecipes.Select(recipe => recipe.Command)],
            WorkspacePolicySummary: WorkspacePolicySummary,
            HasMcpBridge: _settings?.Settings.Mcp.Servers.Any(server => server.Enabled) == true));

        CapabilityNotes.Clear();
        foreach (var note in notes) CapabilityNotes.Add(note);
    }

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
    public Func<AgentTaskListItemViewModel, Task<bool>>? RequestDeleteTaskConfirmation { get; set; }

    /// <summary>r24 doc 01 1.6: id of the active project, captured onto every new task at
    /// creation time. Wired by MainWindowViewModel; a running task never rereads this.</summary>
    public string ActiveProjectId { get; set; } = string.Empty;

    /// <summary>r24 doc 01 1.6: pre-fills the workspace root box from a newly activated
    /// project's folder root. Pre-fill only, never auto-select: it never overwrites a
    /// root the user already has typed, and starting a task still requires the user's
    /// own confirmation exactly as before (docs/features.md 300-303).</summary>
    public void PrefillWorkspaceRootFromProject(string folderRoot)
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) && !string.IsNullOrWhiteSpace(folderRoot))
            WorkspaceRoot = folderRoot;
    }

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

    /// <summary>
    /// The rotating "still working" line shown while a run is in flight
    /// ("Step 3: Weighing the plan... 12s"). Empty when idle, and for the
    /// first <see cref="AgentActivityPhase.GraceMs"/> of a step so a fast step
    /// never flickers a placeholder.
    /// </summary>
    [ObservableProperty] private string _activityStatus = string.Empty;
    public bool HasActivityStatus => ActivityStatus.Length > 0;
    partial void OnActivityStatusChanged(string value) => OnPropertyChanged(nameof(HasActivityStatus));

    /// <summary>Steps taken by the autonomous run currently in flight, which is what MaxAutoSteps actually caps.</summary>
    private int _stepsThisRun;

    private const int ActivityTickMs = 500;
    private readonly System.Diagnostics.Stopwatch _activityClock = new();
    private CancellationTokenSource? _activityCts;
    private int _activityWordOffset;
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

    /// <summary>r23 2.3: presentation only - status stays Complete; a non-empty Reservations list just changes what this label says.</summary>
    public string CurrentTaskStatusLabel => CurrentTask switch
    {
        null => "No active task",
        { StepBudgetExhausted: true } => "Paused at step budget",
        { Status: AgentTaskStatus.Complete, Reservations.Count: > 0 } => "Completed with reservations",
        { Status: AgentTaskStatus.Blocked, UserTransitions: var transitions } when transitions.LastOrDefault()?.Kind == AgentTaskTransitionKind.StopRun => "Stopped",
        { Status: AgentTaskStatus.Interrupted } => "Interrupted during startup recovery",
        _ => CurrentTask.Status.ToString()
    };
    public bool HasReservations => CurrentTask is { Reservations.Count: > 0 };
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
            {
                // StepCount is the task's whole life; MaxAutoSteps caps ONE
                // autonomous run. Printing them as "step 111/20" compared two
                // different things and read as a broken counter. The budget is
                // only shown while a run is actually spending it.
                var total = $"step {CurrentTask.StepCount}";
                if (!IsRunning || _stepsThisRun <= 0)
                    return total;

                var budget = Math.Max(_settings?.Settings.Agent.MaxAutoSteps ?? 20, 1);
                return $"{total} ({_stepsThisRun} of {budget} this run)";
            }

            var specs = CurrentTask.SubTaskPlan;
            var firstUnfinished = specs.FindIndex(s => s.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running);
            var progress = firstUnfinished < 0 ? specs.Count : firstUnfinished + 1;
            return $"sub-task {progress}/{specs.Count}, step {CurrentTask.OrchestrationStepsUsed}";
        }
    }
    public string CurrentTaskGoalLabel => CurrentTask is null || string.IsNullOrWhiteSpace(CurrentTask.Goal) ? "No goal loaded" : CurrentTask.Goal;
    public string CurrentTaskModelLabel => CurrentTask is null || string.IsNullOrWhiteSpace(CurrentTask.ModelId)
        ? string.Empty
        : $"{(string.IsNullOrWhiteSpace(CurrentTask.ModelDisplayName) ? CurrentTask.ModelId : CurrentTask.ModelDisplayName)} ({CurrentTask.ModelId})";
    public string CurrentTaskSummaryLabel
    {
        get
        {
            if (CurrentTask is null)
                return "No summary yet";
            if (CurrentTask.StepBudgetExhausted)
                return "The automatic run paused after reaching its step budget. Continue to resume the remaining work.";
            if (CurrentTask.Status is (AgentTaskStatus.Complete or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled or AgentTaskStatus.Interrupted)
                && !string.IsNullOrWhiteSpace(CurrentTask.LastUserMessage))
                return CurrentTask.LastUserMessage;
            return string.IsNullOrWhiteSpace(CurrentTask.Summary) ? "No summary yet" : CurrentTask.Summary;
        }
    }

    /// <summary>r19 3.1/3.3: a task that will not resume its own loop without user action - the
    /// Continue affordance and New task button both key off this.</summary>
    public bool IsTaskTerminal => CurrentTask?.Status is AgentTaskStatus.Complete or AgentTaskStatus.Failed
        or AgentTaskStatus.Blocked or AgentTaskStatus.Cancelled or AgentTaskStatus.Interrupted;

    /// <summary>r19 3.3: presentation only - a terminal task whose own plan still lists pending
    /// steps declared victory prematurely; pairs with the Continue box (3.1) to answer
    /// "it stopped halfway, now what".</summary>
    public string PrematureCompleteNote => CurrentTask?.StepBudgetExhausted == true
        ? "Paused after reaching the automatic step budget."
        : CurrentTask is { } t && IsTaskTerminal && t.PendingSteps.Count > 0
        ? $"Finished with {t.PendingSteps.Count} planned step{(t.PendingSteps.Count == 1 ? "" : "s")} not run."
        : string.Empty;
    public bool HasPrematureCompleteNote => !string.IsNullOrEmpty(PrematureCompleteNote);

    /// <summary>r23 2.1: the task is paused at the opt-in plan-approval checkpoint, distinct from an ordinary ask_user reply-wait.</summary>
    public bool IsWaitingForPlanApproval => CurrentTask is { Status: AgentTaskStatus.WaitingForUser, PendingToolAction: null } && CurrentTask.PlanApprovalPending;
    /// <summary>The existing "Continue task" box (r19 3.1) also covers resuming past the plan-approval checkpoint (r23 2.1) - same mechanism, ContinueTaskAsync, either way.</summary>
    public bool ShowContinueBox => IsTaskTerminal || IsWaitingForPlanApproval;
    /// <summary>r23 2.2: "revised at step N" annotation on the plan panel, shown once set_plan has replaced a non-empty plan at least once.</summary>
    public string PlanRevisedLabel => CurrentTask?.PlanRevisedAtStep is { } step ? $"revised at step {step}" : string.Empty;
    public bool HasPlanRevision => PlanRevisedLabel.Length > 0;
    /// <summary>True when the task is asking a question, not waiting on a tool approval; only then does the reply box apply.</summary>
    public bool IsWaitingForReply => CurrentTask is { Status: AgentTaskStatus.WaitingForUser, PendingToolAction: null, StepBudgetExhausted: false };

    public bool IsStepBudgetExhausted => CurrentTask?.StepBudgetExhausted == true;
    public bool HasPendingPlan => CurrentTask is { PendingSteps.Count: > 0 };
    public string ContinueBoxTitle => IsStepBudgetExhausted ? "Continue after step budget" : "Continue task";
    public string ContinueInstructionWatermark => IsStepBudgetExhausted
        ? "Tell it what to do next"
        : "Describe the follow-up instruction";
    public bool ShowFinishRun => !IsRunning && CurrentTask is not null
        && (IsWaitingForReply || ShowContinueBox) && CurrentTask.PendingToolAction is null;
    public bool ShowRunStep => !IsRunning && CurrentTask is { Status: AgentTaskStatus.New or AgentTaskStatus.Running }
        && (SelectedModel is not null || !string.IsNullOrWhiteSpace(CurrentTask.ModelId));

    /// <summary>
    /// What the agent actually asked, shown with the reply box. The question
    /// only ever reached the log and the transcript, so the workbench offered
    /// somewhere to answer without saying what the question was.
    /// </summary>
    public string CurrentQuestion => CurrentTask?.LastUserMessage ?? string.Empty;
    public bool HasCurrentQuestion => CurrentQuestion.Length > 0;
    public int RecentTaskCount => RecentTasks.Count;
    public int ReviewQueueCount => ReviewQueue.Count;

    /// <summary>r26 01 1.5: the queue now lists only what needs a decision, so the label
    /// names the decision rather than the object it used to over-count.</summary>
    public string ReviewQueueLabel => ReviewQueueCount == 0
        ? "Nothing waiting on you"
        : $"{ReviewQueueCount} waiting on you";

    public int WorkspaceMemoryCount => WorkspaceMemory.Count;
    public int RetrievedContextCount => RetrievedContext.Count;
    public int QueuedPatchCount => QueuedPatches.Count;
    public int PendingPatchCount => QueuedPatches.Count(patch => patch.Status == AgentDraftPatchStatus.Pending);
    public int AppliedPatchCount => QueuedPatches.Count(patch => patch.Status == AgentDraftPatchStatus.Applied);
    public int RejectedPatchCount => QueuedPatches.Count(patch => patch.Status == AgentDraftPatchStatus.Rejected);
    public int BlockedPatchCount => QueuedPatches.Count(patch => patch.Status == AgentDraftPatchStatus.Blocked);
    public bool HasQueuedPatches => QueuedPatchCount > 0;

    /// <summary>
    /// Which workbench tab is showing. Not persisted: the Agent panel opens on
    /// Run every time, because Run is what the panel is for. Nothing in the
    /// app moves this on the user's behalf; a finished run lights the Changes
    /// badge and says so in the run outcome instead of jumping the page.
    /// </summary>
    [ObservableProperty] private int _selectedTabIndex = RunTabIndex;

    public const int RunTabIndex = 0;
    public const int ChangesTabIndex = 1;
    public const int WorkspaceTabIndex = 2;
    public const int HistoryTabIndex = 3;

    /// <summary>The Changes tab's badge: a patch waiting for review is work assigned to the
    /// user, so it earns a count. The other tabs get none; a badge that is always lit
    /// teaches the user to ignore badges.</summary>
    public bool ShowPendingPatchBadge => PendingPatchCount > 0;

    [RelayCommand]
    private void ShowChangesTab() => SelectedTabIndex = ChangesTabIndex;

    [RelayCommand]
    private void ShowRunTab() => SelectedTabIndex = RunTabIndex;

    private static readonly HashSet<AgentTaskStatus> RewindEligibleStatuses =
    [
        AgentTaskStatus.Complete, AgentTaskStatus.Failed, AgentTaskStatus.Blocked,
        AgentTaskStatus.WaitingForUser, AgentTaskStatus.Cancelled, AgentTaskStatus.Interrupted
    ];

    public bool HasLedgerEntries => LedgerFiles.Count > 0 || LedgerCommands.Count > 0 || LedgerApprovals.Count > 0;

    /// <summary>
    /// r26 03 3.2: the deterministic counterpart to the model's own summary.
    /// <see cref="AgentRunOutcomeSummary.None"/> until the open task reaches a
    /// terminal status, so a running task shows no outcome block at all.
    /// </summary>
    [ObservableProperty] private AgentRunOutcomeSummary _runOutcome = AgentRunOutcomeSummary.None;

    [ObservableProperty] private AgentLedgerFileEntryViewModel? _selectedLedgerFile;
    public bool HasSelectedLedgerFile => SelectedLedgerFile is not null;
    partial void OnSelectedLedgerFileChanged(AgentLedgerFileEntryViewModel? value) => OnPropertyChanged(nameof(HasSelectedLedgerFile));

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
        AgentScenarioSuiteViewModel? scenarioSuite = null,
        Hermaeus.Services.Recall.RecallIndexingService? recallIndexing = null,
        IToastService? toasts = null)
    {
        _toasts = toasts;
        _agent = agent;
        _store = store;
        _recallIndexing = recallIndexing;
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
        _patchReview = new AgentPatchReviewService(workspaceTools, store, workspaceManifests);
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
            OnPropertyChanged(nameof(ReviewQueueLabel));
            OnPropertyChanged(nameof(HasReviewQueue));
            OnPropertyChanged(nameof(HasDecisionWaiting));
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
            OnPropertyChanged(nameof(ShowPendingPatchBadge));
            OnPropertyChanged(nameof(AppliedPatchCount));
            OnPropertyChanged(nameof(RejectedPatchCount));
            OnPropertyChanged(nameof(BlockedPatchCount));
            OnPropertyChanged(nameof(HasQueuedPatches));
        };

        ProjectInstructions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ProjectInstructionCount));
        CommandRecipes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CommandRecipeCount));
            // The capability text names this workspace's declared recipes
            // (r26 03 3.1), so it is derived state over this collection.
            RefreshCapabilityNotes();
        };
        WorkspaceRisks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(WorkspaceRiskCount));
        NewLessons.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNewLessons));

        RefreshCapabilityNotes();
    }

    public bool HasTaskHistory => RecentTaskCount > 0;
    public bool HasReviewQueue => ReviewQueueCount > 0;

    /// <summary>
    /// Whether the pinned decision strip has anything to show. Approval cards
    /// remain here; reply and continue actions are summarized here and owned
    /// by the Run tab beside the response they act on.
    /// </summary>
    public bool HasNonReviewDecision => IsWaitingForReply || ShowContinueBox;
    public bool HasDecisionWaiting => HasReviewQueue || HasNonReviewDecision;
    public string DecisionStripLabel => HasReviewQueue ? ReviewQueueLabel : "Action needed in Run";

    /// <summary>Plain-language next step for the normal workbench flow, not an internal state label.</summary>
    public string NextUserActionLabel => DescribeNextUserAction(CurrentTask);

    public static string DescribeNextUserAction(AgentTaskState? task) => task switch
    {
        null => "Describe a goal, choose a workspace and model, then start the agent.",
        { Status: AgentTaskStatus.Running } => "Agent is working. You can follow progress above or stop the task.",
        { Status: AgentTaskStatus.WaitingForUser, PendingToolAction: not null } => "Review the requested action above, then approve or reject it.",
        { StepBudgetExhausted: true } => "Step budget exhausted. Add steps or continue the remaining plan, or stop the task.",
        { Status: AgentTaskStatus.WaitingForUser } => "Agent needs your answer. Reply below its response in the Run tab.",
        { Status: AgentTaskStatus.Blocked, UserTransitions: var transitions } when transitions.LastOrDefault()?.Kind == AgentTaskTransitionKind.StopRun
            => "The run was stopped. Review its outcome, continue planned work, add an instruction, or finish it.",
        { Status: AgentTaskStatus.Blocked } => "Agent is blocked. Read the reason in Run, then provide an instruction or change the workspace policy.",
        { Status: AgentTaskStatus.Complete } => "Review the outcome below, then inspect Changes or start a follow-up task.",
        { Status: AgentTaskStatus.Failed } => "Review the failure and transcript, then provide a new instruction or start again.",
        { Status: AgentTaskStatus.Cancelled } => "This task was stopped. Review its outcome or start a new task.",
        _ => "Review the task details, then run or continue it when ready."
    };

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

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "agent.start-task", Title: "Start agent task", Area: "Agent",
            Description: "Start a new agent task with the current goal.",
            Keywords: ["agent", "task", "start", "run"], Shortcut: "",
            CanExecute: () => StartCommand.CanExecute(null),
            DisabledReason: () => string.IsNullOrWhiteSpace(GoalText) ? "No goal entered." : "Agent is already running.",
            Execute: () => StartCommand.ExecuteAsync(null)));

        registry.Register(new AppCommand(
            Id: "agent.stop-task", Title: "Stop agent task", Area: "Agent",
            Description: "Stop the running agent task.",
            Keywords: ["agent", "stop", "cancel"], Shortcut: "",
            CanExecute: () => IsRunning,
            DisabledReason: () => "No agent task is running.",
            Execute: () => { StopCommand.Execute(null); return Task.CompletedTask; }));

        registry.Register(new AppCommand(
            Id: "agent.choose-workspace-root", Title: "Choose workspace folder", Area: "Agent",
            Description: "Pick the folder the agent works in.",
            Keywords: ["agent", "workspace", "folder", "root"], Shortcut: "",
            CanExecute: () => RequestWorkspaceRootPicker is not null,
            DisabledReason: () => "Workspace picker is not available.",
            Execute: () => { RequestWorkspaceRootPicker?.Invoke(); return Task.CompletedTask; }));
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        _stopRequested = false;
        _activeRunTaskId = null;
        IsError = false;
        StatusMessage = string.Empty;
        _cts = new CancellationTokenSource();
        try
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Agent,
                $"Agent started: {GoalText}"));
            CurrentTask = await _agent.CreateTaskAsync(GoalText, BuildOptions(), _cts.Token, ActiveProjectId);
            _openedTaskId = CurrentTask.TaskId;
            _activeRunTaskId = CurrentTask.TaskId;
            _currentTaskParentGoal = string.Empty;
            Narrate("Agent task started.", VoicePriority.Normal, $"{CurrentTask.TaskId}:started");
            await RunAgentLoopAsync();
            await RefreshRecentAsync();
            // A run that pauses for approval belongs in the queue immediately
            // (r26 01 1.4); it used to wait for a manual refresh click.
            await RefreshReviewQueueAsync();
        }
        catch (OperationCanceledException)
        {
            await FinalizeRequestedStopAsync();
            StatusMessage = _stopRequested ? "Agent stopped. Completed work remains available." : "Agent stopped.";
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            StartCommand.NotifyCanExecuteChanged();
            RunStepCommand.NotifyCanExecuteChanged();
            SendReplyCommand.NotifyCanExecuteChanged();
            ContinuePlannedTaskCommand.NotifyCanExecuteChanged();
            FinishTaskCommand.NotifyCanExecuteChanged();
            _activeRunTaskId = null;
            _stopRequested = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunStep))]
    private async Task RunStepAsync()
    {
        IsRunning = true;
        _stopRequested = false;
        _activeRunTaskId = CurrentTask?.TaskId;
        IsError = false;
        _cts = new CancellationTokenSource();
        try
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Agent,
                $"Agent step started for task {CurrentTask?.TaskId}"));
            await RunCurrentStepAsync();
            await RefreshRecentAsync();
            await RefreshReviewQueueAsync();
        }
        catch (OperationCanceledException)
        {
            await FinalizeRequestedStopAsync();
            StatusMessage = _stopRequested ? "Agent stopped. Completed work remains available." : "Agent stopped.";
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            StartCommand.NotifyCanExecuteChanged();
            RunStepCommand.NotifyCanExecuteChanged();
            SendReplyCommand.NotifyCanExecuteChanged();
            ContinuePlannedTaskCommand.NotifyCanExecuteChanged();
            FinishTaskCommand.NotifyCanExecuteChanged();
            _activeRunTaskId = null;
            _stopRequested = false;
        }
    }

    [RelayCommand]
    private Task StopAsync()
    {
        if (!IsRunning)
            return Task.CompletedTask;

        _stopRequested = true;
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task FinalizeRequestedStopAsync()
    {
        if (!_stopRequested || string.IsNullOrWhiteSpace(_activeRunTaskId))
            return;

        try
        {
            CurrentTask = await _agent.StopTaskAsync(_activeRunTaskId, CancellationToken.None);
            RefreshTaskPreview();
            await RefreshRecentAsync();
            await RefreshReviewQueueAsync();
        }
        catch (Exception ex)
        {
            SetError($"The run stopped, but its stop transition could not be recorded: {ex.Message}");
        }
    }

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

    [RelayCommand]
    private async Task DeleteRecentTaskAsync(AgentTaskListItemViewModel? item)
    {
        if (item is null || !item.CanDelete || RequestDeleteTaskConfirmation is null)
            return;
        if (!await RequestDeleteTaskConfirmation(item))
            return;

        try
        {
            await _agent.DeleteTaskAsync(item.TaskId);
            if (CurrentTask?.TaskId == item.TaskId)
                NewTask();
            await RefreshRecentAsync();
            await RefreshReviewQueueAsync();
            StatusMessage = $"Deleted historical agent run: {item.Goal}";
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
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
        SelectedTabIndex = RunTabIndex;

        // r25 follow-up: r16 1.4 persists the workspace a task was created against,
        // precisely so an approval executes where it was approved. Resuming the task
        // never restored it into the workbench, so the box still showed whatever was
        // there before and the user had to remember and retype it.
        if (CurrentTask?.WorkspaceRoot is { Length: > 0 } persistedRoot
            && !string.Equals(WorkspaceRoot, persistedRoot, StringComparison.OrdinalIgnoreCase))
            WorkspaceRoot = persistedRoot;

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
            var fullState = await _store.LoadAsync(item.TaskId);
            // The recipe preview describes what a pending run_command would
            // execute; it must reflect the TASK's own workspace, not the
            // workbench's active one (r16 01-orchestration-hardening.md 1.4).
            var previewOptions = item.WorkspaceRoot is { Length: > 0 } root ? options with { WorkspaceRoot = root } : options;
            var preview = item.PendingToolAction is null ? string.Empty : AgentApprovalPreview.Describe(item.PendingToolAction, previewOptions);
            ReviewQueue.Add(new AgentReviewQueueItemViewModel(item, preview, WorkspaceRoot, AvailableModels, fullState)
            {
                UndeclaredCommandFamily = ResolveUndeclaredFamily(item)
            });
        }
    }

    /// <summary>
    /// A blocked run_command whose family the agent CAN run but this workspace
    /// has not declared. Anything else returns empty: a command outside the
    /// fixed families has nothing the user could usefully allow, and pretending
    /// otherwise would offer a button that cannot help.
    /// </summary>
    private string ResolveUndeclaredFamily(AgentReviewQueueItem item)
    {
        if (item.PendingToolAction is not { } pending
            || !string.Equals(pending.ToolName, "run_command", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var requested = pending.Arguments.TryGetValue("command", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        var family = WorkspaceCommandRecipes.ExtractFamily(requested);
        if (family is null)
            return string.Empty;

        var declared = CommandRecipes.Any(r =>
            string.Equals(WorkspaceCommandRecipes.ExtractFamily(r.Command) ?? r.Command.Trim(), family, StringComparison.OrdinalIgnoreCase));
        return declared ? string.Empty : family;
    }

    /// <summary>
    /// Declares the family the blocked row named, from the row itself. This
    /// grants nothing beyond what the Command Recipes editor already grants:
    /// the family was always one the gate accepts, the workspace just had not
    /// said yes to it, and the action still needs its own approval afterwards.
    /// </summary>
    [RelayCommand]
    private async Task DeclareCommandFamilyAsync(AgentReviewQueueItemViewModel? item)
    {
        if (item is null || !item.CanDeclareCommandFamily) return;

        var family = item.UndeclaredCommandFamily;
        CommandRecipes.Add(new WorkspaceCommandRecipeViewModel(
            new WorkspaceCommandRecipe(family, WorkspaceCommandRecipes.DescribeFamily(family), AgentRiskLevel.Medium)));
        await SaveWorkspaceManifestAsync();
        await RefreshReviewQueueAsync();
        StatusMessage = $"'{family}' is now allowed in this workspace. The agent still needs your approval to run it.";
    }

    [RelayCommand]
    private async Task ApproveReviewAsync(AgentReviewQueueItemViewModel? item)
    {
        if (item is null) return;
        var expectedFingerprint = await PersistSubTaskModelChoicesAsync(item) ?? item.PendingFingerprint;
        var result = await _agent.AppendApprovalAsync(item.TaskId, "review_queue", approved: true, expectedFingerprint, BuildOptions());
        await RefreshReviewQueueAsync();
        await LoadTaskIfOpenAsync(item.TaskId);
        if (!result.Applied)
        {
            // The pending action changed since this row was rendered (r23
            // 4.1); the refresh above already shows the current one. Nothing
            // executed, so there is nothing to resume.
            StatusMessage = result.Message;
            return;
        }

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

    private async Task<string?> PersistSubTaskModelChoicesAsync(AgentReviewQueueItemViewModel item)
    {
        if (!item.HasSubTaskModelChoices) return null;
        var state = await _store.LoadAsync(item.TaskId);
        var pending = state?.PendingToolAction;
        if (state is null || pending is null
            || !string.Equals(AgentApprovalFingerprint.Resolve(pending), item.PendingFingerprint, StringComparison.Ordinal)
            || !pending.Arguments.TryGetValue("subtasks", out var raw)
            || raw is not JsonElement { ValueKind: JsonValueKind.Array } array)
            return item.PendingFingerprint;

        var nodes = System.Text.Json.Nodes.JsonNode.Parse(array.GetRawText())?.AsArray();
        if (nodes is null) return item.PendingFingerprint;
        foreach (var choice in item.SubTaskModelChoices)
        {
            if (choice.Index >= nodes.Count || nodes[choice.Index] is not System.Text.Json.Nodes.JsonObject node) continue;
            var modelId = choice.SelectedOption?.ModelId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modelId)) node.Remove("model_id");
            else node["model_id"] = modelId;
        }
        pending.Arguments["subtasks"] = JsonSerializer.SerializeToElement(nodes);
        pending.Fingerprint = AgentApprovalFingerprint.Compute(pending.ToolName, pending.Arguments);
        await _store.SaveAsync(state);
        return pending.Fingerprint;
    }

    /// <summary>
    /// r29 doc 03 3.5: the same box does two things and says which. While the
    /// task is running it steers the run; while the task is paused on a
    /// question it answers that question. Presenting one control that silently
    /// meant two things would be worse than either.
    /// </summary>
    public bool IsSteering => IsRunning && CurrentTask is not null;

    public string ReplyBoxTitle => IsSteering
        ? "Send an instruction to the running task"
        : "The agent is waiting for your reply";

    public string ReplyWatermark => IsSteering
        ? "Tell the agent what to do differently..."
        : "Answer the agent's question...";

    public string ReplyButtonLabel => IsSteering ? "Steer" : "Send";

    /// <summary>The reply row is visible while a question is open OR while a run
    /// is in flight, which is the whole point of steering.</summary>
    public bool ShowReplyBox => IsWaitingForReply || IsSteering;

    [RelayCommand(CanExecute = nameof(CanSendReply))]
    private async Task SendReplyAsync()
    {
        if (CurrentTask is null || string.IsNullOrWhiteSpace(ReplyText)) return;
        var taskId = CurrentTask.TaskId;

        // Routing, not a second control: a running task gets a steering
        // instruction, a paused one gets an answer to its question.
        if (IsSteering)
        {
            await SteerRunningTaskAsync(taskId);
            return;
        }

        try
        {
            await _agent.AppendUserReplyAsync(taskId, ReplyText);
            ReplyText = string.Empty;
            await LoadTaskIfOpenAsync(taskId);
            // The task has left the queue as of this moment, so the strip has
            // to say so now. Refreshing only after the resumed run finished
            // left "1 waiting on you" on screen, with the answered question
            // still under it, for the whole length of the run.
            await RefreshReviewQueueAsync();
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

    /// <summary>
    /// r29 doc 03 3.5: an accepted instruction has to appear in the transcript
    /// immediately, before the model has responded to it. A steer that produces
    /// no visible acknowledgement gets sent twice.
    /// </summary>
    private async Task SteerRunningTaskAsync(string taskId)
    {
        var instruction = ReplyText;
        try
        {
            var result = await _agent.SteerTaskAsync(taskId, instruction);
            if (!result.Accepted)
            {
                _toasts?.Show("Instruction not sent", result.Message, ToastKind.Warning, 6000);
                SetError(result.Message);
                return;
            }

            ReplyText = string.Empty;
            _toasts?.Show("Instruction sent", "The agent will pick it up at its next step.", ToastKind.Success);
            // Reload so the transcript shows the instruction now, before the
            // model has responded to it.
            await LoadTaskIfOpenAsync(taskId);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    // r29 doc 03 3.5: !IsRunning became a ROUTING condition in SendReplyAsync
    // rather than a block. The box is enabled while the task runs (to steer)
    // and while it waits on a question (to answer), and nothing else.
    private bool CanSendReply() => (IsSteering || IsWaitingForReply) && !string.IsNullOrWhiteSpace(ReplyText);

    /// <summary>Bound to the typed "Continue with instruction" path (r19 3.1); empty text is refused.</summary>
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
            await RefreshReviewQueueAsync();
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

    private bool CanContinueTask() => !IsRunning
        && !string.IsNullOrWhiteSpace(ContinueInstructionText)
        && (IsTaskTerminal || IsWaitingForPlanApproval);

    [RelayCommand(CanExecute = nameof(CanContinuePlannedTask))]
    private async Task ContinuePlannedTaskAsync()
    {
        if (CurrentTask is null) return;
        var taskId = CurrentTask.TaskId;
        try
        {
            var options = CurrentTask.WorkspaceRoot is { Length: > 0 } root ? BuildOptions() with { WorkspaceRoot = root } : BuildOptions();
            await _agent.ContinuePlannedTaskAsync(taskId, options);
            await LoadTaskIfOpenAsync(taskId);
            await RefreshReviewQueueAsync();
            await ResumeAgentLoopIfRunnableAsync(taskId);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private bool CanContinuePlannedTask() => !IsRunning && HasPendingPlan
        && (IsTaskTerminal || IsWaitingForPlanApproval);

    [RelayCommand(CanExecute = nameof(CanFinishTask))]
    private async Task FinishTaskAsync()
    {
        if (CurrentTask is null) return;
        var taskId = CurrentTask.TaskId;
        try
        {
            CurrentTask = await _agent.FinishTaskAsync(taskId);
            RefreshTaskPreview();
            await RefreshRecentAsync();
            await RefreshReviewQueueAsync();
            StatusMessage = "Run finished. Its history and evidence remain available.";
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private bool CanFinishTask() => ShowFinishRun;

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
        _stopRequested = false;
        _activeRunTaskId = taskId;
        IsError = false;
        _cts = new CancellationTokenSource();
        try
        {
            var options = target.WorkspaceRoot is { Length: > 0 } root ? BuildOptions() with { WorkspaceRoot = root } : BuildOptions();
            await RunAgentLoopAsync(taskId, options);
            await RefreshRecentAsync();
            await RefreshReviewQueueAsync();
        }
        catch (OperationCanceledException)
        {
            await FinalizeRequestedStopAsync();
            StatusMessage = _stopRequested ? "Agent stopped. Completed work remains available." : "Agent stopped.";
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            StartCommand.NotifyCanExecuteChanged();
            RunStepCommand.NotifyCanExecuteChanged();
            SendReplyCommand.NotifyCanExecuteChanged();
            ContinuePlannedTaskCommand.NotifyCanExecuteChanged();
            FinishTaskCommand.NotifyCanExecuteChanged();
            _activeRunTaskId = null;
            _stopRequested = false;
        }
    }

    /// <summary>
    /// Abandons a queued task the user is done with: the pending action is
    /// discarded without executing, the task becomes terminal, and the row
    /// leaves the queue for good. Distinct from Reject, which decides the
    /// action but leaves the task waiting; a rejected task with nothing else
    /// to say had no way out of the queue before this.
    /// </summary>
    [RelayCommand]
    private async Task DismissReviewAsync(AgentReviewQueueItemViewModel? item)
    {
        if (item is null) return;
        var result = await _agent.DismissTaskAsync(item.TaskId);
        await RefreshReviewQueueAsync();
        await RefreshRecentAsync();
        await LoadTaskIfOpenAsync(item.TaskId);
        StatusMessage = result.Applied ? "Task dismissed." : result.Message;
    }

    [RelayCommand]
    private async Task RejectReviewAsync(AgentReviewQueueItemViewModel? item)
    {
        if (item is null) return;
        var result = await _agent.AppendApprovalAsync(item.TaskId, "review_queue", approved: false, item.PendingFingerprint, BuildOptions());
        await RefreshReviewQueueAsync();
        await LoadTaskIfOpenAsync(item.TaskId);
        // Same contract ApproveReviewAsync uses (r23 4.1, r26 01 1.2): a
        // rejection that applied to nothing says why instead of looking done.
        if (!result.Applied)
            StatusMessage = result.Message;
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

    /// <summary>
    /// Rebuilds the Changes (Run Ledger) view from the open task, folding in
    /// any orchestration children's own entries (r23 1.1/1.2). Conflict
    /// detection - a live file no longer matching what the run last wrote -
    /// happens here, not in AgentRunLedgerBuilder, because only this layer
    /// has workspace access.
    /// </summary>
    [RelayCommand]
    private async Task RefreshRunLedgerAsync()
    {
        LedgerFiles.Clear();
        LedgerCommands.Clear();
        LedgerApprovals.Clear();
        SelectedLedgerFile = null;

        if (CurrentTask is null)
        {
            RunOutcome = AgentRunOutcomeSummary.None;
            OnPropertyChanged(nameof(HasLedgerEntries));
            RewindTaskCommand.NotifyCanExecuteChanged();
            return;
        }

        var children = new List<AgentTaskState>();
        foreach (var spec in CurrentTask.SubTaskPlan)
        {
            if (string.IsNullOrEmpty(spec.TaskId))
                continue;
            var child = await _store.LoadAsync(spec.TaskId);
            if (child is not null)
                children.Add(child);
        }

        var options = BuildOptions();
        var workspaceRootsByTaskId = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CurrentTask.TaskId] = CurrentTask.WorkspaceRoot is { Length: > 0 } root ? root : options.WorkspaceRoot
        };
        foreach (var child in children)
            workspaceRootsByTaskId[child.TaskId] = child.WorkspaceRoot is { Length: > 0 } childRoot ? childRoot : options.WorkspaceRoot;

        var ledger = AgentRunLedgerBuilder.Build(CurrentTask, children);
        foreach (var file in ledger.Files)
        {
            var conflicted = false;
            if (file.Status == AgentLedgerFileStatus.Applied)
            {
                var fileRoot = workspaceRootsByTaskId.GetValueOrDefault(file.TaskId, options.WorkspaceRoot);
                try
                {
                    var live = await _workspaceTools.ReadFileForRevertAsync(options with { WorkspaceRoot = fileRoot }, file.RelativePath);
                    conflicted = live != file.LatestAppliedContent;
                }
                catch { /* best effort; a failed live read leaves the file shown as Applied, not falsely Conflicted */ }
            }
            LedgerFiles.Add(new AgentLedgerFileEntryViewModel(file, conflicted));
        }
        foreach (var command in ledger.Commands)
            LedgerCommands.Add(new AgentLedgerCommandEntryViewModel(command));
        foreach (var approval in ledger.Approvals)
            LedgerApprovals.Add(new AgentLedgerApprovalEntryViewModel(approval));

        // r26 03 3.2: what the finished run actually did, above the fold on the
        // Run tab, composed from the ledger just built and the task's own state.
        RunOutcome = AgentRunOutcome.Describe(ledger, CurrentTask);

        OnPropertyChanged(nameof(HasLedgerEntries));
        RewindTaskCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Reverts the whole open run (r23 1.3/1.4): every file it touched,
    /// restored or deleted per AgentPatchReviewService.RevertTaskAsync's
    /// truthful partial-success report. The confirmation dialog is mandatory;
    /// declining it leaves every file untouched.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRewindTask))]
    private async Task RewindTaskAsync()
    {
        if (CurrentTask is null) return;

        var applied = LedgerFiles.Where(f => f.Status == AgentLedgerFileStatus.Applied).ToList();
        var filesToRestore = applied.Where(f => f.Kind == AgentLedgerFileKind.Edited).Select(f => f.RelativePath).ToList();
        var filesToDelete = applied.Where(f => f.Kind == AgentLedgerFileKind.Created).Select(f => f.RelativePath).ToList();
        var confirmation = new AgentTaskRewindConfirmation(filesToRestore, filesToDelete);
        if (RequestRewindConfirmation is null || !await RequestRewindConfirmation(confirmation))
            return;

        try
        {
            var result = await _patchReview.RevertTaskAsync(CurrentTask, BuildOptions());
            StatusMessage = result.Summary;
            await RefreshWorkspaceFilesAsync();
            await LoadTaskIfOpenAsync(CurrentTask.TaskId);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private bool CanRewindTask() =>
        CurrentTask is not null
        && RewindEligibleStatuses.Contains(CurrentTask.Status)
        && LedgerFiles.Any(f => f.Status == AgentLedgerFileStatus.Applied);

    /// <summary>Shows a ledger file's before/after content, reusing the existing patch preview presentation rather than a new diff control (r23 1.2).</summary>
    [RelayCommand]
    private void SelectLedgerFile(AgentLedgerFileEntryViewModel? file) =>
        SelectedLedgerFile = SelectedLedgerFile == file ? null : file;

    /// <summary>Bound directly to a refresh button (AgentView.axaml) as well as called
    /// from several other commands here, so it must never let an exception escape - a
    /// workspace root renamed or deleted out from under the app must surface as a status
    /// message, not an unobserved AsyncRelayCommand fault that crashes the whole app
    /// (that exact crash was reported against a workspace folder renamed after selection).</summary>
    [RelayCommand]
    private async Task RefreshWorkspaceFilesAsync()
    {
        WorkspaceFiles.Clear();
        WorkspaceFilePreview = string.Empty;
        WorkspaceFileSummary = string.Empty;
        SelectedWorkspaceFile = null;

        if (string.IsNullOrWhiteSpace(WorkspaceRoot))
            return;

        try
        {
            var files = await Task.Run(() =>
            {
                var options = BuildOptions();
                return string.IsNullOrWhiteSpace(WorkspaceFileQuery)
                    ? _workspaceTools.ListFiles(options)
                        .Select(path => new AgentWorkspaceFileViewModel(path, string.Empty, DateTime.MinValue))
                        .ToList()
                    : _workspaceTools.SearchFiles(options, WorkspaceFileQuery)
                        .Where(result => !result.IsTruncationNotice)
                        .Select(result => new AgentWorkspaceFileViewModel(result.RelativePath, result.Snippet, result.ModifiedUtc))
                        .ToList();
            });

            foreach (var file in files)
                WorkspaceFiles.Add(file);

            OnPropertyChanged(nameof(WorkspaceFileCount));
            OnPropertyChanged(nameof(HasWorkspaceFiles));
        }
        catch (Exception ex)
        {
            SetError($"Could not list workspace files: {ex.Message}");
        }
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
                await _patchReview.RejectAsync(CurrentTask, found);
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
                await _patchReview.BlockAsync(CurrentTask, found);
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

        await RefreshWorkspacePolicyDisclosureAsync();
    }

    /// <summary>
    /// Populates the capability disclosure's "Workspace policy" line and
    /// expandable raw-glob detail (r23 3.3), and surfaces a malformed-policy
    /// rejection as a workspace risk. The manifest is hand-edited, like
    /// AllowedCommands already is; there is no policy editor here.
    /// </summary>
    private async Task RefreshWorkspacePolicyDisclosureAsync()
    {
        WorkspacePolicyRules.Clear();
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || !Directory.Exists(WorkspaceRoot))
        {
            WorkspacePolicySummary = string.Empty;
            return;
        }

        var manifest = await _workspaceManifests.LoadAsync(WorkspaceRoot);
        if (manifest?.PolicyRejectionWarning is { Length: > 0 } warning)
            WorkspaceRisks.Add(warning);

        if (manifest?.Policy is not { } policy)
        {
            WorkspacePolicySummary = string.Empty;
            return;
        }

        WorkspacePolicySummary =
            $"Workspace policy: reads {DescribePolicyAllowCount(policy.ReadAllow.Count)}, "
            + $"writes {DescribePolicyAllowCount(policy.WriteAllow.Count)}, "
            + $"{policy.Never.Count} path{(policy.Never.Count == 1 ? string.Empty : "s")} off limits.";
        foreach (var rule in policy.ReadAllow) WorkspacePolicyRules.Add($"read allow: {rule}");
        foreach (var rule in policy.WriteAllow) WorkspacePolicyRules.Add($"write allow: {rule}");
        foreach (var rule in policy.Never) WorkspacePolicyRules.Add($"never: {rule}");
        if (policy.MaxFileReadsPerTask > 0)
            WorkspacePolicyRules.Add($"max file reads per task: {policy.MaxFileReadsPerTask}");
    }

    private static string DescribePolicyAllowCount(int count) =>
        count == 0 ? "unrestricted" : $"limited to {count} rule{(count == 1 ? string.Empty : "s")}";

    // ── Command recipes the user can actually manage ──
    //
    // A workspace with no declared recipes cannot run anything, and until now
    // the only way to declare one was to hand-edit .hermaeus/workspace.json,
    // which assumes the user knows both that the file exists and which command
    // families the safety gate will accept. Both are now on screen: pick a
    // family, optionally narrow it with an argument, add it. Nothing here
    // widens what the gate allows; it only lets the user say which of the
    // fixed, already-safe families this workspace is permitted to use.

    /// <summary>The complete set of families the gate can ever allow, as pickable rows.</summary>
    public IReadOnlyList<CommandFamilyOptionViewModel> AvailableCommandFamilies { get; } =
        [.. WorkspaceCommandRecipes.KnownFamilies.Select(f => new CommandFamilyOptionViewModel(f))];

    [ObservableProperty] private CommandFamilyOptionViewModel? _selectedCommandFamily;
    /// <summary>Optional argument narrowing the recipe, e.g. the project path on "dotnet test".</summary>
    [ObservableProperty] private string _newRecipeArgument = string.Empty;

    public bool CanAddCommandRecipe => SelectedCommandFamily is not null && HasWorkspace;
    partial void OnSelectedCommandFamilyChanged(CommandFamilyOptionViewModel? value)
    {
        OnPropertyChanged(nameof(CanAddCommandRecipe));
        AddCommandRecipeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddCommandRecipe))]
    private async Task AddCommandRecipeAsync()
    {
        if (SelectedCommandFamily is null) return;

        var argument = NewRecipeArgument.Trim();
        var command = argument.Length == 0
            ? SelectedCommandFamily.Family
            : $"{SelectedCommandFamily.Family} {argument}";

        // The gate is the authority on what counts as a recipe, so ask it
        // rather than trusting the composed string.
        if (WorkspaceCommandRecipes.ExtractFamily(command) is null)
        {
            SetError($"'{command}' is not one of the command families the agent can run.");
            return;
        }

        if (CommandRecipes.Any(r => string.Equals(r.Command, command, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "That recipe is already declared for this workspace.";
            return;
        }

        CommandRecipes.Add(new WorkspaceCommandRecipeViewModel(
            new WorkspaceCommandRecipe(command, SelectedCommandFamily.Description, AgentRiskLevel.Medium)));
        NewRecipeArgument = string.Empty;

        await SaveWorkspaceManifestAsync();
        StatusMessage = $"Added '{command}'. The agent can now request it, and it still needs your approval each time.";
    }

    [RelayCommand]
    private async Task RemoveCommandRecipeAsync(WorkspaceCommandRecipeViewModel? recipe)
    {
        if (recipe is null) return;
        CommandRecipes.Remove(recipe);
        await SaveWorkspaceManifestAsync();
        StatusMessage = $"Removed '{recipe.Command}'. The agent can no longer run it here.";
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
        _stepsThisRun = 0;
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
        // A step just landed, so the next wait is a new one: restart the clock
        // and the word rotation rather than letting the line report the age of
        // the whole run.
        if (IsRunning)
        {
            _stepsThisRun++;
            RestartActivityClock();
        }
        OnPropertyChanged(nameof(CurrentStepCountLabel));
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

    private bool CanRunStep() => ShowRunStep;

    [RelayCommand(CanExecute = nameof(CanChangeTaskModel))]
    private async Task ChangeTaskModelAsync()
    {
        if (CurrentTask is null || SelectedModel is null) return;
        try
        {
            CurrentTask = await _agent.ChangeTaskModelAsync(CurrentTask.TaskId, SelectedModel.Id);
            RefreshTaskPreview();
            await RefreshRecentAsync();
            StatusMessage = $"Task model changed to {SelectedModel.DisplayName}. Run or continue when ready.";
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private bool CanChangeTaskModel() => !IsRunning && CurrentTask is not null && SelectedModel is not null
        && !string.Equals(CurrentTask.ModelId, SelectedModel.Id, StringComparison.Ordinal);

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
        OnPropertyChanged(nameof(NextUserActionLabel));
        OnPropertyChanged(nameof(HasReservations));
        OnPropertyChanged(nameof(CurrentStepCountLabel));
        OnPropertyChanged(nameof(CurrentTaskGoalLabel));
        OnPropertyChanged(nameof(CurrentTaskModelLabel));
        OnPropertyChanged(nameof(CurrentTaskSummaryLabel));
        OnPropertyChanged(nameof(QueuedPatchCount));
        OnPropertyChanged(nameof(PendingPatchCount));
        OnPropertyChanged(nameof(ShowPendingPatchBadge));
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
            _ = RefreshRunLedgerAsync();
            return;
        }

        CurrentStep = CurrentTask.ActiveStep;
        TaskStatePreview = JsonSerializer.Serialize(CurrentTask, new JsonSerializerOptions { WriteIndented = true });
        _ = RefreshQueuedPatchesAsync();
        _ = RefreshRunLedgerAsync();
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
        RefreshCapabilityNotes();
        // The file list only ever populated on panel load, so choosing a
        // workspace (or opening a task belonging to a different one) left an
        // empty list behind until the user found the Refresh button. The list
        // describes this root, so it follows the root.
        _ = RunOnUiAsync(RefreshWorkspaceFilesAsync);
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
        ChangeTaskModelCommand.NotifyCanExecuteChanged();
        if (ScenarioSuite is not null)
            ScenarioSuite.ModelId = value?.Id ?? string.Empty;
    }
    partial void OnCurrentTaskChanged(AgentTaskState? value)
    {
        RunStepCommand.NotifyCanExecuteChanged();
        SendReplyCommand.NotifyCanExecuteChanged();
        NewTaskCommand.NotifyCanExecuteChanged();
        ContinueTaskCommand.NotifyCanExecuteChanged();
        ContinuePlannedTaskCommand.NotifyCanExecuteChanged();
        FinishTaskCommand.NotifyCanExecuteChanged();
        ChangeTaskModelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CurrentTaskStatusLabel));
        OnPropertyChanged(nameof(NextUserActionLabel));
        OnPropertyChanged(nameof(CurrentTaskSummaryLabel));
        OnPropertyChanged(nameof(HasPendingPlan));
        OnPropertyChanged(nameof(ShowFinishRun));
        OnPropertyChanged(nameof(IsWaitingForReply));
        OnPropertyChanged(nameof(IsSteering));
        OnPropertyChanged(nameof(ReplyBoxTitle));
        OnPropertyChanged(nameof(ReplyWatermark));
        OnPropertyChanged(nameof(ReplyButtonLabel));
        OnPropertyChanged(nameof(ShowReplyBox));
        OnPropertyChanged(nameof(IsStepBudgetExhausted));
        OnPropertyChanged(nameof(HasPendingPlan));
        OnPropertyChanged(nameof(ContinueBoxTitle));
        OnPropertyChanged(nameof(ContinueInstructionWatermark));
        OnPropertyChanged(nameof(ShowFinishRun));
        OnPropertyChanged(nameof(CurrentQuestion));
        OnPropertyChanged(nameof(HasCurrentQuestion));
        OnPropertyChanged(nameof(CurrentTaskStatusLabel));
        OnPropertyChanged(nameof(CurrentTaskSummaryLabel));
        OnPropertyChanged(nameof(ShowRunStep));
        OnPropertyChanged(nameof(CanShowNewTaskButton));
        OnPropertyChanged(nameof(IsTaskTerminal));
        OnPropertyChanged(nameof(PrematureCompleteNote));
        OnPropertyChanged(nameof(HasPrematureCompleteNote));
        OnPropertyChanged(nameof(IsWaitingForPlanApproval));
        OnPropertyChanged(nameof(ShowContinueBox));
        OnPropertyChanged(nameof(HasNonReviewDecision));
        OnPropertyChanged(nameof(HasDecisionWaiting));
        OnPropertyChanged(nameof(DecisionStripLabel));
        OnPropertyChanged(nameof(PlanRevisedLabel));
        OnPropertyChanged(nameof(HasPlanRevision));
        _ = RefreshQueuedPatchesAsync();
        _ = RefreshRunLedgerAsync();

        // r24 doc 02 2.3: re-index on terminal transition only, never per step.
        if (value is { Status: AgentTaskStatus.Complete or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled or AgentTaskStatus.Interrupted })
            _ = IndexTaskForRecallAsync(value);
    }

    /// <summary>Used by MainWindowViewModel's startup Recall backfill (doc 02 2.1/2.3):
    /// only terminal tasks are indexable material.</summary>
    public async Task<IReadOnlyList<Hermaeus.Core.Models.RecallTaskInput>> BuildRecallTaskInputsAsync()
    {
        var recent = await _store.ListRecentAsync(limit: 200);
        var inputs = new List<Hermaeus.Core.Models.RecallTaskInput>();
        foreach (var item in recent.Where(t => t.Status is AgentTaskStatus.Complete or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled or AgentTaskStatus.Interrupted))
        {
            var full = await _store.LoadAsync(item.TaskId);
            if (full is null) continue;
            var parts = new List<string> { full.Goal, full.Summary };
            if (full.Reservations.Count > 0) parts.Add(string.Join("; ", full.Reservations));
            if (full.Plan.Count > 0) parts.Add(string.Join(" ", full.Plan.Select(p => p.Description)));
            var body = string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            inputs.Add(new Hermaeus.Core.Models.RecallTaskInput(full.TaskId, full.ParentTaskId, full.Goal, body, full.ProjectId, full.CreatedAt));
        }
        return inputs;
    }

    private async Task IndexTaskForRecallAsync(AgentTaskState task)
    {
        if (_recallIndexing is null) return;
        try
        {
            var parts = new List<string> { task.Goal, task.Summary };
            if (task.Reservations.Count > 0)
                parts.Add("Reservations: " + string.Join("; ", task.Reservations));
            if (task.Plan.Count > 0)
                parts.Add(string.Join(" ", task.Plan.Select(p => p.Description)));

            var reportPath = Path.Combine(_store.GetTaskDirectory(task.TaskId), "report.md");
            if (File.Exists(reportPath))
                parts.Add(await File.ReadAllTextAsync(reportPath));

            var body = string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            await _recallIndexing.IndexTaskAsync(new Hermaeus.Core.Models.RecallTaskInput(
                task.TaskId, task.ParentTaskId, task.Goal, body, task.ProjectId, task.CreatedAt));
        }
        catch (Exception ex)
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Agent,
                $"Recall indexing failed for task {task.TaskId}: {ex.Message}"));
        }
    }
    partial void OnReplyTextChanged(string value) => SendReplyCommand.NotifyCanExecuteChanged();
    partial void OnContinueInstructionTextChanged(string value)
    {
        ContinueTaskCommand.NotifyCanExecuteChanged();
        FinishTaskCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsRunningChanged(bool value)
    {
        NewTaskCommand.NotifyCanExecuteChanged();
        ContinueTaskCommand.NotifyCanExecuteChanged();
        ContinuePlannedTaskCommand.NotifyCanExecuteChanged();
        FinishTaskCommand.NotifyCanExecuteChanged();
        SendReplyCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowNewTaskButton));
        // r29 doc 03 3.5: the reply row's caption, watermark, button label and
        // visibility all turn on whether a run is in flight.
        OnPropertyChanged(nameof(IsSteering));
        OnPropertyChanged(nameof(ReplyBoxTitle));
        OnPropertyChanged(nameof(ReplyWatermark));
        OnPropertyChanged(nameof(ReplyButtonLabel));
        OnPropertyChanged(nameof(ShowReplyBox));
        OnPropertyChanged(nameof(HasNonReviewDecision));
        OnPropertyChanged(nameof(HasDecisionWaiting));

        OnPropertyChanged(nameof(CurrentStepCountLabel));
        OnPropertyChanged(nameof(ShowRunStep));
        OnPropertyChanged(nameof(ShowFinishRun));

        if (value)
            StartActivityTicker();
        else
            StopActivityTicker();
    }

    /// <summary>
    /// Drives <see cref="ActivityStatus"/> while a run is in flight. The
    /// workbench set one status message when the run started and did not touch
    /// it again until a step finished, so a long model call showed frozen text
    /// and read as a hung app. Unlike chat there is no token stream to end the
    /// wait, so this ticks until the run stops.
    /// </summary>
    private void StartActivityTicker()
    {
        StopActivityTicker();
        _activityCts = new CancellationTokenSource();
        var token = _activityCts.Token;
        RestartActivityClock();

        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(ActivityTickMs, token);
                    if (token.IsCancellationRequested) return;
                    RunOnUi(UpdateActivityStatus);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }, token);
    }

    private void StopActivityTicker()
    {
        _activityCts?.Cancel();
        _activityCts?.Dispose();
        _activityCts = null;
        _activityClock.Reset();
        ActivityStatus = string.Empty;
    }

    /// <summary>
    /// Each step restarts the clock and picks a new starting word, so the line
    /// reports how long the CURRENT step has taken rather than the whole run,
    /// and a long run does not show the same rotation over and over.
    /// </summary>
    private void RestartActivityClock()
    {
        _activityWordOffset = Random.Shared.Next(AgentActivityPhase.WhimsyWords.Count);
        _activityClock.Restart();
        UpdateActivityStatus();
    }

    private void UpdateActivityStatus()
    {
        var elapsed = _activityClock.ElapsedMilliseconds;
        ActivityStatus = AgentActivityPhase.Describe(
            elapsed,
            IsRunning,
            CurrentTask?.StepCount ?? 0,
            _activityWordOffset + (int)(elapsed / 2_500));
    }
}
