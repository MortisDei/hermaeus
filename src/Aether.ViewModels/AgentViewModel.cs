using System.Collections.ObjectModel;
using System.Text.Json;
using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag;
using Aether.Rag.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public sealed class AgentContextItemViewModel
{
    public string Source { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string ScoreDisplay { get; init; } = string.Empty;
}

public sealed class AgentReviewQueueItemViewModel
{
    public AgentReviewQueueItemViewModel(AgentReviewQueueItem item)
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
    }

    public string TaskId { get; }
    public string Goal { get; }
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
    public string StatusLabel => Status.ToString();
    public string CreatedLabel => $"Created {CreatedAt:yyyy-MM-dd HH:mm} UTC";
    public bool CanReview => Status != AgentDraftPatchStatus.Applied;
    public string OutcomeLabel => Status switch
    {
        AgentDraftPatchStatus.Pending => "Pending review",
        AgentDraftPatchStatus.Applied => $"Applied {ApprovedAt:yyyy-MM-dd HH:mm} by {ApprovedBy}",
        AgentDraftPatchStatus.Approved => $"Approved {ApprovedAt:yyyy-MM-dd HH:mm} by {ApprovedBy}",
        AgentDraftPatchStatus.Rejected => $"Rejected {BlockedAt:yyyy-MM-dd HH:mm} by {BlockedBy}",
        AgentDraftPatchStatus.Blocked => string.IsNullOrWhiteSpace(BlockReason)
            ? $"Blocked {BlockedAt:yyyy-MM-dd HH:mm} by {BlockedBy}"
            : $"Blocked {BlockedAt:yyyy-MM-dd HH:mm} by {BlockedBy}: {BlockReason}",
        _ => Status.ToString()
    };
}

public partial class AgentViewModel : ObservableObject
{
    private readonly IAgentService _agent;
    private readonly IAgentTaskStateStore _store;
    private readonly IAgentWorkspaceMemoryStore _workspaceMemory;
    private readonly IAgentWorkspaceTools _workspaceTools;
    private readonly ILlmService _llm;
    private readonly RagQueryService _rag;
    private readonly IRuntimeLogService _logs;
    private CancellationTokenSource? _cts;

    public ObservableCollection<LlmModel> AvailableModels { get; } = [];
    public ObservableCollection<RagDataset> Datasets { get; } = [];
    public ObservableCollection<AgentTaskListItem> RecentTasks { get; } = [];
    public ObservableCollection<AgentReviewQueueItemViewModel> ReviewQueue { get; } = [];
    public ObservableCollection<AgentWorkspaceMemoryEntryViewModel> WorkspaceMemory { get; } = [];
    public ObservableCollection<AgentWorkspaceFileViewModel> WorkspaceFiles { get; } = [];
    public ObservableCollection<AgentContextItemViewModel> RetrievedContext { get; } = [];
    public ObservableCollection<AgentDraftPatchViewModel> QueuedPatches { get; } = [];

    public IReadOnlyList<string> CapabilityNotes { get; } =
    [
        "Read-first workspace inspection: list, search, read, and summarise local files.",
        "Approval-gated patch drafting: propose content, queue it for review, and apply only after approval.",
        "No general shell, network, or remote-control actions in this slice.",
        "Workspace memory and review queues remain local and explicit."
    ];

    public string CapabilityLabel => "Current agent scope";

    public Action? RequestWorkspaceRootPicker { get; set; }

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

    public string CurrentTaskStatusLabel => CurrentTask is null ? "No active task" : CurrentTask.Status.ToString();
    public string CurrentTaskGoalLabel => CurrentTask is null || string.IsNullOrWhiteSpace(CurrentTask.Goal) ? "No goal loaded" : CurrentTask.Goal;
    public string CurrentTaskSummaryLabel => CurrentTask is null || string.IsNullOrWhiteSpace(CurrentTask.Summary) ? "No summary yet" : CurrentTask.Summary;
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
        IRuntimeLogService logs)
    {
        _agent = agent;
        _store = store;
        _workspaceMemory = workspaceMemory;
        _workspaceTools = workspaceTools;
        _llm = llm;
        _rag = rag;
        _logs = logs;
        WorkspaceRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
    }

    public bool HasTaskHistory => RecentTaskCount > 0;
    public bool HasReviewQueue => ReviewQueueCount > 0;
    public bool HasWorkspaceMemory => WorkspaceMemoryCount > 0;
    public bool HasWorkspaceFiles => WorkspaceFileCount > 0;
    public bool HasRetrievedContext => RetrievedContextCount > 0;
    public int WorkspaceFileCount => WorkspaceFiles.Count;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var models = await _llm.GetModelsAsync();
            AvailableModels.Clear();
            foreach (var model in models.Where(m => m.IsVisible)) AvailableModels.Add(model);
            SelectedModel ??= AvailableModels.FirstOrDefault();

            var datasets = await _rag.GetDatasetsAsync();
            Datasets.Clear();
            foreach (var dataset in datasets) Datasets.Add(dataset);

            await RefreshRecentAsync();
            await RefreshReviewQueueAsync();
            await RefreshWorkspaceMemoryAsync();
            await RefreshWorkspaceFilesAsync();
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
        }
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private async Task LoadTaskAsync(AgentTaskListItem? item)
    {
        if (item is null) return;
        CurrentTask = await _store.LoadAsync(item.TaskId);
        RefreshTaskPreview();
        await RefreshQueuedPatchesAsync();
        RunStepCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task RefreshReviewQueueAsync()
    {
        ReviewQueue.Clear();
        foreach (var item in await _store.ListReviewQueueAsync())
            ReviewQueue.Add(new AgentReviewQueueItemViewModel(item));
    }

    [RelayCommand]
    private async Task ApproveReviewAsync(AgentReviewQueueItemViewModel? item)
    {
        if (item is null) return;
        await _agent.AppendApprovalAsync(item.TaskId, "review_queue", approved: true);
        await RefreshReviewQueueAsync();
        await LoadTaskIfOpenAsync(item.TaskId);
    }

    [RelayCommand]
    private async Task RejectReviewAsync(AgentReviewQueueItemViewModel? item)
    {
        if (item is null) return;
        await _agent.AppendApprovalAsync(item.TaskId, "review_queue", approved: false);
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

    private async Task LoadSelectedWorkspaceFileAsync(AgentWorkspaceFileViewModel? file)
    {
        if (file is null || string.IsNullOrWhiteSpace(WorkspaceRoot))
        {
            WorkspaceFilePreview = string.Empty;
            WorkspaceFileSummary = string.Empty;
            DraftProposedContent = string.Empty;
            return;
        }

        var options = BuildOptions();
        var preview = await Task.Run(() => _workspaceTools.ReadFile(options, file.RelativePath));
        var summary = await Task.Run(() => _workspaceTools.SummarizeFile(options, file.RelativePath));
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
                var selectedPath = SelectedWorkspaceFile?.RelativePath;
                _workspaceTools.ApplyDraftPatch(BuildOptions(), found.RelativePath, found.ProposedContent);
                found.Status = AgentDraftPatchStatus.Applied;
                found.ApprovedAt = DateTime.UtcNow;
                found.ApprovedBy = "User";
                found.BlockedAt = null;
                found.BlockedBy = null;
                found.BlockReason = string.Empty;
                await _store.SaveAsync(CurrentTask);
                await _agent.AppendApprovalAsync(CurrentTask.TaskId, "draft_patch_apply", approved: true);
                StatusMessage = $"Patch for {patch.RelativePath} applied.";
                await RefreshWorkspaceFilesAsync();
                if (!string.IsNullOrWhiteSpace(selectedPath))
                    SelectedWorkspaceFile = WorkspaceFiles.FirstOrDefault(file => file.RelativePath == selectedPath);
                await LoadTaskIfOpenAsync(CurrentTask.TaskId);
            }
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
                found.Status = AgentDraftPatchStatus.Rejected;
                found.BlockedAt = DateTime.UtcNow;
                found.BlockedBy = "User";
                found.BlockReason = "Rejected during review.";
                await _store.SaveAsync(CurrentTask);
                await _agent.AppendApprovalAsync(CurrentTask.TaskId, "draft_patch_reject", approved: false);
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
                found.Status = AgentDraftPatchStatus.Blocked;
                found.BlockedAt = DateTime.UtcNow;
                found.BlockedBy = "User";
                found.BlockReason = string.IsNullOrWhiteSpace(found.BlockReason)
                    ? "Blocked during review."
                    : found.BlockReason;
                await _store.SaveAsync(CurrentTask);
                await _agent.AppendApprovalAsync(CurrentTask.TaskId, "draft_patch_block", approved: false);
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

    private async Task LoadTaskIfOpenAsync(string taskId)
    {
        if (CurrentTask?.TaskId != taskId)
            return;

        CurrentTask = await _store.LoadAsync(taskId);
        RefreshTaskPreview();
        await RefreshRecentAsync();
    }

    private async Task RunCurrentStepAsync()
    {
        if (CurrentTask is null) return;
        StatusMessage = "Building context and asking the agent...";
        var result = await _agent.RunStepAsync(CurrentTask.TaskId, BuildOptions(), _cts?.Token ?? CancellationToken.None);
        CurrentTask = result.State;
        CurrentStep = result.State.ActiveStep;
        StatusMessage = result.LogEntry;
        NextActionPreview = JsonSerializer.Serialize(result.PlannerResponse.NextAction, new JsonSerializerOptions { WriteIndented = true });
        RetrievedContext.Clear();
        foreach (var item in result.ContextPack.RetrievedMemory.Concat(result.ContextPack.RetrievedFiles))
        {
            RetrievedContext.Add(new AgentContextItemViewModel
            {
                Source = item.Source,
                Title = item.Title,
                Content = item.Content,
                ScoreDisplay = item.Score > 0 ? item.Score.ToString("F3") : string.Empty
            });
        }

        RefreshTaskPreview();
        await RefreshLogAsync();
        _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Agent,
            $"Agent step complete: {result.State.ActiveStep}"));
    }

    private AgentWorkspaceOptions BuildOptions() => new(
        WorkspaceRoot,
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
            RecentTasks.Add(task);
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
        OnPropertyChanged(nameof(CurrentTaskGoalLabel));
        OnPropertyChanged(nameof(CurrentTaskSummaryLabel));
        OnPropertyChanged(nameof(QueuedPatchCount));
        OnPropertyChanged(nameof(PendingPatchCount));
        OnPropertyChanged(nameof(AppliedPatchCount));
        OnPropertyChanged(nameof(RejectedPatchCount));
        OnPropertyChanged(nameof(BlockedPatchCount));
        OnPropertyChanged(nameof(HasQueuedPatches));

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
    partial void OnWorkspaceRootChanged(string value) => StartCommand.NotifyCanExecuteChanged();
    partial void OnWorkspaceFileQueryChanged(string value) => _ = RefreshWorkspaceFilesAsync();
    partial void OnSelectedWorkspaceFileChanged(AgentWorkspaceFileViewModel? value) => _ = LoadSelectedWorkspaceFileAsync(value);
    partial void OnSelectedModelChanged(LlmModel? value)
    {
        StartCommand.NotifyCanExecuteChanged();
        RunStepCommand.NotifyCanExecuteChanged();
    }
    partial void OnCurrentTaskChanged(AgentTaskState? value)
    {
        RunStepCommand.NotifyCanExecuteChanged();
        _ = RefreshQueuedPatchesAsync();
    }
}
