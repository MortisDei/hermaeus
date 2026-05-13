using System.Collections.ObjectModel;
using System.Linq;
using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    private readonly IRuntimeLogService _logs;
    private readonly IRedactionService _redactor;

    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private string _statusText = "";

    public ObservableCollection<string> Filters { get; } =
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

    public ObservableCollection<RuntimeLogEntry> VisibleEntries { get; } = [];

    public Action<string>? RequestCopyToClipboard { get; set; }
    public Action<string>? RequestOpenFolder { get; set; }

    public LogsViewModel(IRuntimeLogService logs, IRedactionService redactor)
    {
        _logs = logs;
        _redactor = redactor;
        _logs.LogAdded += _ => Refresh();
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var filtered = ApplyFilter(_logs.GetEntries(), SelectedFilter).ToList();
        VisibleEntries.Clear();
        foreach (var entry in filtered)
            VisibleEntries.Add(entry);
        StatusText = $"{filtered.Count} log line(s)";
    }

    [RelayCommand]
    private void ClearView()
    {
        VisibleEntries.Clear();
        StatusText = "View cleared";
    }

    [RelayCommand]
    private void CopyVisibleLogs()
    {
        if (RequestCopyToClipboard is null) return;
        var payload = string.Join("\n", VisibleEntries.Select(FormatEntry));
        RequestCopyToClipboard(payload);
        StatusText = "Copied visible logs";
    }

    [RelayCommand]
    private void CopyRedactedDiagnostics()
    {
        if (RequestCopyToClipboard is null) return;
        var payload = string.Join("\n", VisibleEntries.Select(e => _redactor.Redact(FormatEntry(e))));
        RequestCopyToClipboard(payload);
        StatusText = "Copied redacted diagnostics";
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
        => $"{entry.Timestamp:HH:mm:ss} [{entry.Level}] [{entry.Category}] {entry.Message}";

    partial void OnSelectedFilterChanged(string value) => Refresh();
}
