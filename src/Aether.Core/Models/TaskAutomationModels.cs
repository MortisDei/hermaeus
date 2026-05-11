namespace Aether.Core.Models;

public enum LocalTaskStatus
{
    Todo,
    Doing,
    Done
}

public sealed class LocalTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public LocalTaskStatus Status { get; set; } = LocalTaskStatus.Todo;
    public DateTime? DueAt { get; set; }
    public string LinkedConversationId { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool ReminderShown { get; set; }
}

public sealed class ScheduledAutomation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string RuntimeProfileId { get; set; } = string.Empty;
    public DateTime? NextRunAt { get; set; }
    public bool Enabled { get; set; } = true;
    public List<AutomationRunHistory> RunHistory { get; set; } = [];
}

public sealed class AutomationRunHistory
{
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime FinishedAt { get; set; } = DateTime.UtcNow;
    public bool Succeeded { get; set; }
    public string Result { get; set; } = string.Empty;
}
