using System.Linq;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public class LogEntryDisplayViewModel
{
    public string Formatted { get; }
    public RuntimeLogEntry Entry { get; }

    public LogEntryDisplayViewModel(RuntimeLogEntry entry)
    {
        Entry = entry;
        Formatted = $"{LocalTimeFormat.ClockSeconds(entry.Timestamp)} [{entry.Level}] [{entry.Category}] {entry.Message}";
    }
}

public partial class LogsViewModel : ViewModelBase
{
    private readonly IRuntimeLogService _logs;
    private readonly RedactionService _redactor;
    private readonly object _pendingLock = new();
    private List<RuntimeLogEntry> _pendingEntries = [];
    private bool _flushScheduled;
    private const int MaxVisibleEntries = 1000;

    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private string _statusText = "";

    public UiBoundCollection<string> Filters { get; } =
    [
        "All",
        "Errors",
        "Warnings",
        "Startup",
        "Network",
        "Model load",
        "Voice",
        "RAG",
        "Agent",
        "Service"
    ];

    public UiBoundCollection<LogEntryDisplayViewModel> VisibleEntries { get; } = [];

    public Func<string, Task<bool>>? RequestCopyToClipboard { get; set; }
    public Action<string>? RequestOpenFolder { get; set; }

    public LogsViewModel(IRuntimeLogService logs, RedactionService redactor)
    {
        _logs = logs;
        _redactor = redactor;
        _logs.LogAdded += OnLogAdded;
        Refresh();
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "logs.refresh", Title: "Refresh logs", Area: "System",
            Description: "Reload the runtime log view.",
            Keywords: ["logs", "refresh"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => { RefreshCommand.Execute(null); return Task.CompletedTask; }));

        registry.Register(new AppCommand(
            Id: "logs.open-folder", Title: "Open log folder", Area: "System",
            Description: "Open the folder containing runtime log files.",
            Keywords: ["logs", "folder", "open"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => { OpenLogFolderCommand.Execute(null); return Task.CompletedTask; }));
    }

    [RelayCommand]
    private void Refresh()
    {
        var filtered = ApplyFilter(_logs.GetEntries(), SelectedFilter).ToList();
        VisibleEntries.Clear();
        foreach (var entry in filtered)
            VisibleEntries.Add(new LogEntryDisplayViewModel(entry));
        StatusText = $"{filtered.Count} log line(s)";
    }

    /// <summary>
    /// r12 02-async-and-threading.md 2.4: a llama-server startup can emit
    /// hundreds of log lines in seconds; posting a full O(n) list rebuild
    /// per line was O(n^2) and flooded the dispatcher exactly when the app
    /// was busiest. Entries that pass the current filter are appended
    /// incrementally instead, and bursts are coalesced behind a
    /// pending-refresh flag so overlapping <see cref="IRuntimeLogService.LogAdded"/>
    /// callbacks (often from a background reader thread) result in one
    /// posted flush, not one post per line. Full rebuilds are kept only for
    /// filter changes and <see cref="ClearView"/>.
    /// </summary>
    private void OnLogAdded(RuntimeLogEntry entry)
    {
        bool shouldSchedule;
        lock (_pendingLock)
        {
            _pendingEntries.Add(entry);
            shouldSchedule = !_flushScheduled;
            _flushScheduled = true;
        }

        if (shouldSchedule)
            RunOnUi(FlushPendingEntries);
    }

    private void FlushPendingEntries()
    {
        List<RuntimeLogEntry> batch;
        lock (_pendingLock)
        {
            batch = _pendingEntries;
            _pendingEntries = [];
            _flushScheduled = false;
        }

        var matched = 0;
        foreach (var entry in ApplyFilter(batch, SelectedFilter))
        {
            VisibleEntries.Add(new LogEntryDisplayViewModel(entry));
            matched++;
        }

        if (matched == 0)
            return;

        while (VisibleEntries.Count > MaxVisibleEntries)
            VisibleEntries.RemoveAt(0);

        StatusText = $"{VisibleEntries.Count} log line(s)";
    }

    [RelayCommand]
    private void ClearView()
    {
        lock (_pendingLock)
        {
            _pendingEntries = [];
        }
        _logs.ClearInMemory();
        Refresh();
        StatusText = "Logs cleared";
    }

    [RelayCommand]
    private async Task CopyVisibleLogsAsync()
    {
        if (RequestCopyToClipboard is null) return;
        var payload = string.Join("\n", VisibleEntries.Select(e => e.Formatted));
        StatusText = await RequestCopyToClipboard(payload) ? "Copied visible logs" : "Could not copy visible logs";
    }

    [RelayCommand]
    private async Task CopyRedactedDiagnosticsAsync()
    {
        if (RequestCopyToClipboard is null) return;
        var payload = string.Join("\n", VisibleEntries.Select(e => _redactor.Redact(e.Formatted)));
        StatusText = await RequestCopyToClipboard(payload) ? "Copied redacted diagnostics" : "Could not copy redacted diagnostics";
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        var path = _logs.GetLogDirectory();
        RequestOpenFolder?.Invoke(path);
        StatusText = "Opened log folder";
    }

    private static IEnumerable<RuntimeLogEntry> ApplyFilter(IReadOnlyList<RuntimeLogEntry> entries, string filter)
    {
        if (entries.Count == 0) return entries;
        return filter switch
        {
            "Errors" => entries.Where(e => e.Level == RuntimeLogLevel.Error),
            "Warnings" => entries.Where(e => e.Level == RuntimeLogLevel.Warning),
            "Startup" => entries.Where(e => e.Category == RuntimeLogCategory.Startup),
            "Network" => entries.Where(e => e.Category == RuntimeLogCategory.Network),
            "Model load" => entries.Where(e => e.Category == RuntimeLogCategory.ModelLoad),
            "Voice" => entries.Where(e => e.Category == RuntimeLogCategory.Voice),
            "RAG" => entries.Where(e => e.Category == RuntimeLogCategory.Rag),
            "Agent" => entries.Where(e => e.Category == RuntimeLogCategory.Agent),
            "Service" => entries.Where(e => e.Category == RuntimeLogCategory.Service),
            _ => entries
        };
    }

    private static string FormatEntry(RuntimeLogEntry entry)
        => $"{LocalTimeFormat.ClockSeconds(entry.Timestamp)} [{entry.Level}] [{entry.Category}] {entry.Message}";

    partial void OnSelectedFilterChanged(string value) => Refresh();
}
