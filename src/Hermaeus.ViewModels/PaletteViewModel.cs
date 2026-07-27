using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services.Recall;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public sealed record PaletteCommandGroup(string Area, IReadOnlyList<AppCommand> Commands);

/// <summary>
/// r24 doc 02 2.5: Ctrl+K from anywhere. Empty query lists every registered
/// command grouped by area (the direct answer to "what can it even do");
/// a word or phrase matches commands instantly while Recall results stream
/// in underneath, debounced so a keystroke never fires four store queries
/// and an embedding call.
/// </summary>
public partial class PaletteViewModel : ViewModelBase
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly ICommandRegistry _commands;
    private readonly RecallService _recall;
    private CancellationTokenSource? _debounceCts;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _queryText = string.Empty;
    [ObservableProperty] private bool _scopeToActiveProject;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _footerText = string.Empty;
    [ObservableProperty] private bool _hasActiveProject;
    [ObservableProperty] private string _activeProjectName = string.Empty;

    private string _activeProjectId = string.Empty;

    public UiBoundCollection<PaletteCommandGroup> CommandGroups { get; } = [];
    public UiBoundCollection<AppCommand> MatchedCommands { get; } = [];
    public UiBoundCollection<RecallHit> RecallHits { get; } = [];

    public bool IsEmptyQuery => string.IsNullOrWhiteSpace(QueryText);

    /// <summary>Wired by MainWindowViewModel: navigates to where a recall hit points.</summary>
    public Func<RecallHit, Task>? RequestNavigate { get; set; }
    public Action? RequestClose { get; set; }

    public PaletteViewModel(ICommandRegistry commands, RecallService recall)
    {
        _commands = commands;
        _recall = recall;
    }

    /// <summary>Called by MainWindowViewModel on project switch, so the scope chip
    /// defaults to the current project (doc 01 1.6).</summary>
    public void SetActiveProject(string projectId, string projectName)
    {
        _activeProjectId = projectId;
        HasActiveProject = !string.IsNullOrWhiteSpace(projectId);
        ActiveProjectName = projectName;
        ScopeToActiveProject = HasActiveProject;
    }

    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
        QueryText = string.Empty;
        FooterText = string.Empty;
        RefreshEmptyState();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        RequestClose?.Invoke();
    }

    partial void OnQueryTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmptyQuery));
        _ = HandleQueryChangedAsync(value);
    }

    partial void OnScopeToActiveProjectChanged(bool value) => _ = HandleQueryChangedAsync(QueryText);

    private async Task HandleQueryChangedAsync(string value)
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        if (string.IsNullOrWhiteSpace(value))
        {
            RefreshEmptyState();
            RunOnUi(() => RecallHits.Clear());
            FooterText = string.Empty;
            return;
        }

        RunOnUi(() =>
        {
            CommandGroups.Clear();
            MatchedCommands.Clear();
            foreach (var c in _commands.All.Where(c => MatchesCommand(c, value)).Take(20))
                MatchedCommands.Add(c);
        });

        try
        {
            await Task.Delay(DebounceDelay, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested) return;

        IsSearching = true;
        try
        {
            var scope = ScopeToActiveProject ? _activeProjectId : string.Empty;
            var result = await _recall.SearchAsync(value, scope, cts.Token);
            if (cts.IsCancellationRequested) return;

            RunOnUi(() =>
            {
                RecallHits.Clear();
                foreach (var h in result.Hits) RecallHits.Add(h);
            });

            FooterText = result.OmittedSources.Count > 0
                ? $"{string.Join(", ", result.OmittedSources)} did not respond in time; results may be incomplete."
                : result.KeywordOnly
                    ? "Keyword search only - no embedding model configured or reachable."
                    : string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cts.IsCancellationRequested)
                IsSearching = false;
        }
    }

    private void RefreshEmptyState()
    {
        RunOnUi(() =>
        {
            MatchedCommands.Clear();
            RecallHits.Clear();
            CommandGroups.Clear();
            foreach (var group in _commands.All.GroupBy(c => c.Area).OrderBy(g => g.Key, StringComparer.Ordinal))
                CommandGroups.Add(new PaletteCommandGroup(group.Key, group.ToList()));
        });
    }

    private static bool MatchesCommand(AppCommand c, string query) =>
        c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || c.Area.Contains(query, StringComparison.OrdinalIgnoreCase)
        || c.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private async Task ExecuteCommandAsync(AppCommand? command)
    {
        if (command is null || !command.CanExecute()) return;
        Close();
        await command.Execute();
    }

    [RelayCommand]
    private async Task OpenHitAsync(RecallHit? hit)
    {
        if (hit is null) return;
        Close();
        if (RequestNavigate is not null)
            await RequestNavigate(hit);
    }
}
