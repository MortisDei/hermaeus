using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

/// <summary>
/// One scenario's row in the Agent workbench's Scenario Evals panel. Result
/// fields start unset (never run this session) and are populated in place
/// after a run so the list does not lose scroll position/selection mid-suite.
/// </summary>
public sealed partial class AgentScenarioRowViewModel : ObservableObject
{
    public AgentScenarioRowViewModel(AgentScenario scenario)
    {
        Id = scenario.Manifest.Id;
        Title = scenario.Manifest.Title;
        Tags = string.Join(", ", scenario.Manifest.Tags);
        SourceLabel = scenario.IsBuiltIn ? "built-in" : "user";
    }

    public string Id { get; }
    public string Title { get; }
    public string Tags { get; }
    public string SourceLabel { get; }

    [ObservableProperty] private bool? _passed;
    [ObservableProperty] private string _failedCheckSummary = string.Empty;
    [ObservableProperty] private int _steps;
    [ObservableProperty] private string _durationDisplay = string.Empty;

    public bool HasFailure => Passed == false;
    public string StatusLabel => Passed switch
    {
        true => "PASS",
        false => "FAIL",
        null => "Not run"
    };

    public void ApplyResult(AgentScenarioRunResult result)
    {
        Passed = result.Passed;
        Steps = result.Steps;
        DurationDisplay = $"{result.DurationMs} ms";
        FailedCheckSummary = result.Passed
            ? string.Empty
            : string.Join("; ", result.Checks.Where(c => !c.Passed).Select(c => $"{c.CheckId}: {c.Detail}"));
    }

    public void ResetResult()
    {
        Passed = null;
        FailedCheckSummary = string.Empty;
        Steps = 0;
        DurationDisplay = string.Empty;
    }

    partial void OnPassedChanged(bool? value)
    {
        OnPropertyChanged(nameof(HasFailure));
        OnPropertyChanged(nameof(StatusLabel));
    }
}

/// <summary>
/// Drives the Agent workbench's Scenario Evals panel: loads the built-in +
/// user scenario library and runs it (as a suite or one row at a time)
/// against the workbench's currently selected model. All execution happens
/// through <see cref="IAgentScenarioRunner"/> in an isolated sandbox; this
/// ViewModel only reflects results, it never touches agent data directly.
/// </summary>
public sealed partial class AgentScenarioSuiteViewModel : ObservableObject
{
    private readonly IAgentScenarioStore _store;
    private readonly IAgentScenarioRunner _runner;
    private readonly IToastService _toasts;
    private IReadOnlyList<AgentScenario> _loadedScenarios = [];
    private CancellationTokenSource? _cts;

    public UiBoundCollection<AgentScenarioRowViewModel> Scenarios { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _headlineResult = string.Empty;
    [ObservableProperty] private string _modelId = string.Empty;

    public int ScenarioCount => Scenarios.Count;

    public AgentScenarioSuiteViewModel(IAgentScenarioStore store, IAgentScenarioRunner runner, IToastService toasts)
    {
        _store = store;
        _runner = runner;
        _toasts = toasts;
        Scenarios.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ScenarioCount));
            RunSuiteCommand.NotifyCanExecuteChanged();
        };
    }

    [RelayCommand]
    public async Task LoadScenariosAsync()
    {
        IsLoading = true;
        try
        {
            var warnings = new List<string>();
            _loadedScenarios = await _store.LoadAllAsync(warnings);
            Scenarios.Clear();
            foreach (var scenario in _loadedScenarios)
                Scenarios.Add(new AgentScenarioRowViewModel(scenario));

            StatusMessage = $"{Scenarios.Count} scenario(s) loaded.";
            if (warnings.Count > 0)
                _toasts.Show("Scenario library warnings", string.Join("; ", warnings), ToastKind.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunSuiteAsync()
    {
        if (_loadedScenarios.Count == 0) return;

        IsRunning = true;
        HeadlineResult = string.Empty;
        foreach (var row in Scenarios)
            row.ResetResult();

        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var suite = await _runner.RunSuiteAsync(_loadedScenarios, ModelId, progress, _cts.Token);
            foreach (var result in suite.Results)
            {
                var row = Scenarios.FirstOrDefault(r => r.Id == result.ScenarioId);
                row?.ApplyResult(result);
            }

            HeadlineResult = $"{suite.PassedCount}/{suite.Total} passed - report in eval-runs/{suite.Id}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Suite run canceled.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsRunning = false;
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task RunScenarioAsync(AgentScenarioRowViewModel? row)
    {
        if (row is null || IsRunning || IsLoading || string.IsNullOrWhiteSpace(ModelId)) return;
        var scenario = _loadedScenarios.FirstOrDefault(s => s.Manifest.Id == row.Id);
        if (scenario is null) return;

        IsRunning = true;
        row.ResetResult();
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var result = await _runner.RunScenarioAsync(scenario, ModelId, progress, _cts.Token);
            row.ApplyResult(result);
            StatusMessage = result.Passed ? $"{row.Id}: passed." : $"{row.Id}: failed.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scenario run canceled.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsRunning = false;
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelSuite() => _cts?.Cancel();

    private bool CanRun() => !IsRunning && !IsLoading && !string.IsNullOrWhiteSpace(ModelId) && Scenarios.Count > 0;

    partial void OnIsRunningChanged(bool value) => RunSuiteCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value) => RunSuiteCommand.NotifyCanExecuteChanged();
    partial void OnModelIdChanged(string value) => RunSuiteCommand.NotifyCanExecuteChanged();
}
