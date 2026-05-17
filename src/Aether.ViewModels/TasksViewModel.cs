using System.Collections.ObjectModel;
using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class TasksViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IToastService _toasts;
    private readonly ChatViewModel _chat;

    public ObservableCollection<LocalTaskItemViewModel> Tasks { get; } = [];
    public ObservableCollection<AutomationItemViewModel> Automations { get; } = [];
    public LocalTaskStatus[] TaskStatuses { get; } =
    [
        LocalTaskStatus.Todo,
        LocalTaskStatus.Doing,
        LocalTaskStatus.Done
    ];

    [ObservableProperty] private string _newTaskTitle = string.Empty;
    [ObservableProperty] private string _newTaskNotes = string.Empty;
    [ObservableProperty] private DateTimeOffset? _newTaskDueAt;
    [ObservableProperty] private string _newAutomationTitle = string.Empty;
    [ObservableProperty] private string _newAutomationPrompt = string.Empty;
    [ObservableProperty] private DateTimeOffset? _newAutomationRunAt;

    public TasksViewModel(ISettingsService settings, IToastService toasts, ChatViewModel chat)
    {
        _settings = settings;
        _toasts = toasts;
        _chat = chat;
        Reload();
    }

    public void Reload()
    {
        Tasks.Clear();
        foreach (var task in _settings.Settings.Tasks.OrderBy(t => t.Status).ThenBy(t => t.DueAt ?? DateTime.MaxValue))
            Tasks.Add(new LocalTaskItemViewModel(task));

        Automations.Clear();
        foreach (var automation in _settings.Settings.Automations.OrderBy(a => a.NextRunAt ?? DateTime.MaxValue))
            Automations.Add(new AutomationItemViewModel(automation));
    }

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync()
    {
        var task = new LocalTaskItem
        {
            Title = NewTaskTitle.Trim(),
            Notes = NewTaskNotes.Trim(),
            DueAt = TaskDateTimeHelpers.ToUtc(NewTaskDueAt),
            LinkedConversationId = _chat.CurrentConversationId
        };
        _settings.Settings.Tasks.Add(task);
        await _settings.SaveAsync();
        NewTaskTitle = string.Empty;
        NewTaskNotes = string.Empty;
        NewTaskDueAt = null;
        Reload();
        _toasts.Show("Task created", task.Title, ToastKind.Success);
    }

    [RelayCommand]
    private async Task SaveTaskAsync(LocalTaskItemViewModel? item)
    {
        if (item is null) return;
        var existing = _settings.Settings.Tasks.FirstOrDefault(t => t.Id == item.Id);
        if (existing is null) return;
        item.ApplyTo(existing);
        await _settings.SaveAsync();
        Reload();
        _toasts.Show("Task saved", existing.Title, ToastKind.Success);
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(LocalTaskItemViewModel? item)
    {
        if (item is null) return;
        _settings.Settings.Tasks.RemoveAll(t => t.Id == item.Id);
        await _settings.SaveAsync();
        Reload();
        _toasts.Show("Task deleted", item.Title, ToastKind.Info);
    }

    [RelayCommand(CanExecute = nameof(CanAddAutomation))]
    private async Task AddAutomationAsync()
    {
        var automation = new ScheduledAutomation
        {
            Title = NewAutomationTitle.Trim(),
            Prompt = NewAutomationPrompt.Trim(),
            NextRunAt = TaskDateTimeHelpers.ToUtc(NewAutomationRunAt),
            ModelId = _chat.SelectedModel?.Id ?? string.Empty
        };
        _settings.Settings.Automations.Add(automation);
        await _settings.SaveAsync();
        NewAutomationTitle = string.Empty;
        NewAutomationPrompt = string.Empty;
        NewAutomationRunAt = null;
        Reload();
        _toasts.Show("Automation scheduled", automation.Title, ToastKind.Success);
    }

    [RelayCommand]
    private async Task SaveAutomationAsync(AutomationItemViewModel? item)
    {
        if (item is null) return;
        var existing = _settings.Settings.Automations.FirstOrDefault(a => a.Id == item.Id);
        if (existing is null) return;
        item.ApplyTo(existing);
        await _settings.SaveAsync();
        Reload();
        _toasts.Show("Automation saved", existing.Title, ToastKind.Success);
    }

    [RelayCommand]
    private async Task DeleteAutomationAsync(AutomationItemViewModel? item)
    {
        if (item is null) return;
        _settings.Settings.Automations.RemoveAll(a => a.Id == item.Id);
        await _settings.SaveAsync();
        Reload();
        _toasts.Show("Automation deleted", item.Title, ToastKind.Info);
    }

    private bool CanAddTask() => !string.IsNullOrWhiteSpace(NewTaskTitle);
    private bool CanAddAutomation() => !string.IsNullOrWhiteSpace(NewAutomationTitle)
                                      && !string.IsNullOrWhiteSpace(NewAutomationPrompt);

    partial void OnNewTaskTitleChanged(string value) => AddTaskCommand.NotifyCanExecuteChanged();
    partial void OnNewAutomationTitleChanged(string value) => AddAutomationCommand.NotifyCanExecuteChanged();
    partial void OnNewAutomationPromptChanged(string value) => AddAutomationCommand.NotifyCanExecuteChanged();
}

public partial class LocalTaskItemViewModel : ObservableObject
{
    public string Id { get; }
    [ObservableProperty] private string _title;
    [ObservableProperty] private LocalTaskStatus _status;
    [ObservableProperty] private DateTimeOffset? _dueAt;
    [ObservableProperty] private string _notes;
    public string LinkedConversationId { get; }

    public LocalTaskItemViewModel(LocalTaskItem task)
    {
        Id = task.Id;
        _title = task.Title;
        _status = task.Status;
        _dueAt = task.DueAt is null ? null : new DateTimeOffset(TaskDateTimeHelpers.ToLocal(task.DueAt.Value));
        _notes = task.Notes;
        LinkedConversationId = task.LinkedConversationId;
    }

    public void ApplyTo(LocalTaskItem task)
    {
        task.Title = Title.Trim();
        task.Status = Status;
        task.DueAt = TaskDateTimeHelpers.ToUtc(DueAt);
        task.Notes = Notes.Trim();
        task.ReminderShown = false;
    }
}

public partial class AutomationItemViewModel : ObservableObject
{
    public string Id { get; }
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _prompt;
    [ObservableProperty] private DateTimeOffset? _nextRunAt;
    [ObservableProperty] private bool _enabled;
    public string LastRun { get; }

    public AutomationItemViewModel(ScheduledAutomation automation)
    {
        Id = automation.Id;
        _title = automation.Title;
        _prompt = automation.Prompt;
        _nextRunAt = automation.NextRunAt is null ? null : new DateTimeOffset(TaskDateTimeHelpers.ToLocal(automation.NextRunAt.Value));
        _enabled = automation.Enabled;
        LastRun = automation.RunHistory.FirstOrDefault()?.Result ?? string.Empty;
    }

    public void ApplyTo(ScheduledAutomation automation)
    {
        automation.Title = Title.Trim();
        automation.Prompt = Prompt.Trim();
        automation.NextRunAt = TaskDateTimeHelpers.ToUtc(NextRunAt);
        automation.Enabled = Enabled;
    }
}

internal static class TaskDateTimeHelpers
{
    public static DateTime? ToUtc(DateTimeOffset? value) => value?.ToUniversalTime().UtcDateTime;

    public static DateTime ToLocal(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Local => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
        };
}
