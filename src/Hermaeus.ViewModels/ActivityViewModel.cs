using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public sealed class ActivityRowViewModel
{
    public DateTime Timestamp { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public ActivityOutcome Outcome { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
    public string RelativeTime => FormatRelative(Timestamp);

    private static string FormatRelative(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return utc.ToLocalTime().ToString("yyyy-MM-dd");
    }
}

/// <summary>
/// r24 doc 04 4.2: "did that actually work" - a reverse-chronological, filterable
/// record of background work, backed by the same ITraceStore chat/RAG/agent
/// traces already use, projected through TraceKind.System.
/// </summary>
public partial class ActivityViewModel : ViewModelBase
{
    /// <summary>Mirrors SqliteTraceStore.MaxTracesPerKind; kept as a separate constant
    /// since that one is internal to Hermaeus.Services.</summary>
    public const int RetainedEventCount = 500;

    private readonly ITraceStore? _traceStore;
    private readonly IToastService _toasts;

    public UiBoundCollection<ActivityRowViewModel> Events { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _projectFilter = string.Empty;
    [ObservableProperty] private string _kindFilter = string.Empty;

    public string RetentionNote => $"Keeps the most recent {RetainedEventCount} events.";

    public Func<Task<bool>>? RequestConfirmClear { get; set; }

    public bool HasEvents => Events.Count > 0;

    public ActivityViewModel(IToastService toasts, ITraceStore? traceStore = null)
    {
        _toasts = toasts;
        _traceStore = traceStore;
        Events.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEvents));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_traceStore is null) return;
        IsLoading = true;
        try
        {
            var records = await _traceStore.GetRecentAsync(TraceKind.System, RetainedEventCount);
            var rows = records.Select(Map)
                .Where(r => string.IsNullOrEmpty(ProjectFilter) || r.ProjectId == ProjectFilter)
                .Where(r => string.IsNullOrEmpty(KindFilter) || r.Operation.StartsWith(KindFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            RunOnUi(() =>
            {
                Events.Clear();
                foreach (var row in rows) Events.Add(row);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>2.0-style destructive control: removes trace rows only, never the
    /// durable model_usage rollup (doc 04 4.2).</summary>
    [RelayCommand]
    public async Task ClearAsync()
    {
        if (_traceStore is null) return;
        var confirmed = RequestConfirmClear is null || await RequestConfirmClear();
        if (!confirmed) return;

        var removed = await _traceStore.DeleteByKindAsync(TraceKind.System);
        await RefreshAsync();
        _toasts.Show("Activity history cleared", $"Removed {removed} event(s).", ToastKind.Info);
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "activity.refresh", Title: "Refresh activity", Area: "Activity",
            Description: "Reload the activity feed.",
            Keywords: ["activity", "refresh", "history"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => RefreshCommand.ExecuteAsync(null)));

        registry.Register(new AppCommand(
            Id: "activity.clear", Title: "Clear activity history", Area: "Activity",
            Description: "Delete every recorded activity event.",
            Keywords: ["activity", "clear", "history"], Shortcut: "",
            CanExecute: () => Events.Count > 0,
            DisabledReason: () => "No activity to clear.",
            Execute: () => ClearCommand.ExecuteAsync(null)));
    }

    private static ActivityRowViewModel Map(TraceRecord record)
    {
        var detail = TryParse(record.DetailJson);
        return new ActivityRowViewModel
        {
            Timestamp = record.CreatedAt,
            Operation = record.Operation,
            SourceId = record.SourceId,
            Outcome = Enum.TryParse<ActivityOutcome>(detail?.Outcome, out var o) ? o : ActivityOutcome.Succeeded,
            Title = detail?.Title ?? record.Operation,
            Reason = detail?.Reason ?? record.Error,
            ProjectId = detail?.ProjectId ?? string.Empty
        };
    }

    private sealed record ActivityDetailShape(string Outcome, string Title, string Reason, string ProjectId);

    private static ActivityDetailShape? TryParse(string json)
    {
        try { return JsonSerializer.Deserialize<ActivityDetailShape>(json); }
        catch (JsonException) { return null; }
    }
}
