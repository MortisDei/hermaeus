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

public partial class AgentViewModel : ObservableObject
{
    private readonly IAgentService _agent;
    private readonly IAgentTaskStateStore _store;
    private readonly IAgentWorkspaceMemoryStore _workspaceMemory;
    private readonly ILlmService _llm;
    private readonly RagQueryService _rag;
    private readonly IRuntimeLogService _logs;
    private CancellationTokenSource? _cts;

    public ObservableCollection<LlmModel> AvailableModels { get; } = [];
    public ObservableCollection<RagDataset> Datasets { get; } = [];
    public ObservableCollection<AgentTaskListItem> RecentTasks { get; } = [];
    public ObservableCollection<AgentReviewQueueItemViewModel> ReviewQueue { get; } = [];
    public ObservableCollection<AgentWorkspaceMemoryEntryViewModel> WorkspaceMemory { get; } = [];
    public ObservableCollection<AgentContextItemViewModel> RetrievedContext { get; } = [];

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
    [ObservableProperty] private bool _isError;

    public AgentViewModel(
        IAgentService agent,
        IAgentTaskStateStore store,
        IAgentWorkspaceMemoryStore workspaceMemory,
        ILlmService llm,
        RagQueryService rag,
        IRuntimeLogService logs)
    {
        _agent = agent;
        _store = store;
        _workspaceMemory = workspaceMemory;
        _llm = llm;
        _rag = rag;
        _logs = logs;
        WorkspaceRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

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
        if (CurrentTask is null)
        {
            TaskStatePreview = string.Empty;
            CurrentStep = string.Empty;
            return;
        }

        CurrentStep = CurrentTask.ActiveStep;
        TaskStatePreview = JsonSerializer.Serialize(CurrentTask, new JsonSerializerOptions { WriteIndented = true });
    }

    private void SetError(string message)
    {
        IsError = true;
        StatusMessage = message;
        _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Agent, message));
    }

    partial void OnGoalTextChanged(string value) => StartCommand.NotifyCanExecuteChanged();
    partial void OnWorkspaceRootChanged(string value) => StartCommand.NotifyCanExecuteChanged();
    partial void OnSelectedModelChanged(LlmModel? value)
    {
        StartCommand.NotifyCanExecuteChanged();
        RunStepCommand.NotifyCanExecuteChanged();
    }
    partial void OnCurrentTaskChanged(AgentTaskState? value) => RunStepCommand.NotifyCanExecuteChanged();
}
