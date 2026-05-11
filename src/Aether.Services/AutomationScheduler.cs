using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class AutomationScheduler : IAutomationScheduler
{
    private readonly ISettingsService _settings;
    private readonly IToastService _toasts;
    private Timer? _timer;

    public AutomationScheduler(ISettingsService settings, IToastService toasts)
    {
        _settings = settings;
        _toasts = toasts;
    }

    public void Start()
    {
        _timer ??= new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        var now = DateTime.Now;
        foreach (var task in _settings.Settings.Tasks.Where(t => t.Status != LocalTaskStatus.Done && t.DueAt is not null && !t.ReminderShown))
        {
            if (task.DueAt!.Value > now) continue;
            task.ReminderShown = true;
            _toasts.Show("Task due", task.Title, ToastKind.Warning, 7000);
        }

        foreach (var automation in _settings.Settings.Automations.Where(a => a.Enabled && a.NextRunAt is not null && a.NextRunAt <= now))
        {
            automation.Enabled = false;
            automation.RunHistory.Insert(0, new AutomationRunHistory
            {
                StartedAt = DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow,
                Succeeded = true,
                Result = "Queued reminder. Background generation is app-running only and will be expanded with a confirmation flow."
            });
            _toasts.Show("Automation due", automation.Title, ToastKind.Info, 7000);
        }

        _ = _settings.SaveAsync();
    }

    public void Dispose() => Stop();
}
