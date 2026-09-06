using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

/// <summary>
/// One scenario's row in the Agent workbench's Scenario Evals panel. Result
/// fields start as Unknown and are populated from applicable persisted evidence
/// or a live run so the list does not lose scroll position/selection mid-suite.
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
    [ObservableProperty] private AgentScenarioEvidenceStatus _evidenceStatus;

    public bool HasFailure => Passed == false;
    public string StatusLabel => EvidenceStatus switch
    {
        AgentScenarioEvidenceStatus.Pass => "PASS",
        AgentScenarioEvidenceStatus.Fail => "FAIL",
        AgentScenarioEvidenceStatus.Stale => "STALE",
        _ => "Unknown"
    };

    public string EvidenceTooltip => EvidenceStatus switch
    {
        AgentScenarioEvidenceStatus.Pass => "Applicable persisted evidence passed against the current model content, scenario definition, and evaluator contract.",
        AgentScenarioEvidenceStatus.Fail => "Applicable persisted evidence failed against the current model content, scenario definition, and evaluator contract.",
        AgentScenarioEvidenceStatus.Stale => "Historical evidence exists, but its model content, scenario definition, or evaluator contract no longer matches the current run.",
        _ => "No applicable persisted evidence is available for the current model and evaluator contract."
    };

    public void ApplyResult(AgentScenarioRunResult result)
    {
        Passed = result.Passed;
        EvidenceStatus = result.Passed ? AgentScenarioEvidenceStatus.Pass : AgentScenarioEvidenceStatus.Fail;
        Steps = result.Steps;
        DurationDisplay = $"{result.DurationMs} ms";
        FailedCheckSummary = result.Passed
            ? string.Empty
            : string.Join("; ", result.Checks.Where(c => !c.Passed).Select(c => $"{c.CheckId}: {c.Detail}"));
    }

    public void ApplyPersistedResult(AgentScenarioRunResult result, AgentScenarioEvidenceStatus status)
    {
        Passed = result.Passed;
        EvidenceStatus = status;
        Steps = result.Steps;
        DurationDisplay = $"{result.DurationMs} ms";
        FailedCheckSummary = result.Passed
            ? string.Empty
            : string.Join("; ", result.Checks.Where(c => !c.Passed).Select(c => $"{c.CheckId}: {c.Detail}"));
    }

    public void ResetResult()
    {
        Passed = null;
        EvidenceStatus = AgentScenarioEvidenceStatus.Unknown;
        FailedCheckSummary = string.Empty;
        Steps = 0;
        DurationDisplay = string.Empty;
    }

    partial void OnPassedChanged(bool? value)
    {
        OnPropertyChanged(nameof(HasFailure));
        OnPropertyChanged(nameof(StatusLabel));
    }

    partial void OnEvidenceStatusChanged(AgentScenarioEvidenceStatus value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(EvidenceTooltip));
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
    private readonly SemaphoreSlim _restoreGate = new(1, 1);
    private readonly Func<string, CancellationToken, Task<string>> _computeModelContentHashAsync;
    private readonly Dictionary<string, CachedModelHash> _modelHashCache = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly object _modelHashGate = new();
    private readonly Dictionary<string, InFlightModelHash> _inFlightModelHashes = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private CancellationTokenSource? _restoreCts;
    private Task? _restoreTask;
    private string _restoreModelId = string.Empty;
    private int _restoreScenarioGeneration;
    private int _scenarioGeneration;
    private int _restoreEpoch;
    private long _modelHashUseCounter;
    private const int ModelHashCacheLimit = 8;
    private static readonly JsonSerializerOptions EvalResultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public UiBoundCollection<AgentScenarioRowViewModel> Scenarios { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRestoringEvidence;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _headlineResult = string.Empty;
    [ObservableProperty] private string _modelId = string.Empty;
    [ObservableProperty] private string _runningHeadline = string.Empty;
    [ObservableProperty] private string _scenarioProgressLabel = string.Empty;
    [ObservableProperty] private string _currentScenarioLabel = string.Empty;
    [ObservableProperty] private string _currentStepLabel = string.Empty;
    [ObservableProperty] private string _runningCountsLabel = string.Empty;

    private int _runningTotal;
    private int _completedCount;
    private int _passedCount;
    private int _failedCount;

    public int ScenarioCount => Scenarios.Count;

    public AgentScenarioSuiteViewModel(
        IAgentScenarioStore store,
        IAgentScenarioRunner runner,
        IToastService toasts,
        IEvalStore? evalStore = null,
        Func<string, CancellationToken, Task<string>>? computeModelContentHashAsync = null)
    {
        _store = store;
        _runner = runner;
        _toasts = toasts;
        _evalStore = evalStore;
        _computeModelContentHashAsync = computeModelContentHashAsync ?? AgentScenarioEvidenceContract.ComputeModelContentHashAsync;
        Scenarios.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ScenarioCount));
            RunSuiteCommand.NotifyCanExecuteChanged();
            RunScenarioCommand.NotifyCanExecuteChanged();
        };
    }

    private readonly IEvalStore? _evalStore;

    [RelayCommand]
    public Task LoadScenariosAsync() => LoadScenariosCoreAsync(restoreEvidence: true);

    public Task LoadDefinitionsAsync() => LoadScenariosCoreAsync(restoreEvidence: false);

    private async Task LoadScenariosCoreAsync(bool restoreEvidence)
    {
        CancelEvidenceRestore();
        IsLoading = true;
        try
        {
            var warnings = new List<string>();
            _loadedScenarios = await _store.LoadAllAsync(warnings);
            _scenarioGeneration++;
            Scenarios.Clear();
            foreach (var scenario in _loadedScenarios)
                Scenarios.Add(new AgentScenarioRowViewModel(scenario));

            RunScenarioCommand.NotifyCanExecuteChanged();

            StatusMessage = $"{Scenarios.Count} scenario(s) loaded.";
            if (warnings.Count > 0)
                _toasts.Show("Scenario library warnings", string.Join("; ", warnings), ToastKind.Warning);
            if (restoreEvidence)
                QueueEvidenceRestore();
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

        CancelEvidenceRestore();
        IsRunning = true;
        HeadlineResult = string.Empty;
        foreach (var row in Scenarios)
            row.ResetResult();

        ResetRunningProgress(_loadedScenarios.Count, "Running scenario suite");
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(UpdateRunningProgress);
            var suite = await _runner.RunSuiteAsync(_loadedScenarios, ModelId, progress, _cts.Token);
            foreach (var result in suite.Results)
            {
                var row = Scenarios.FirstOrDefault(r => r.Id == result.ScenarioId);
                row?.ApplyResult(result);
            }

            ApplyFinalCounts(suite.Results, _loadedScenarios.Count);
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

    [RelayCommand(CanExecute = nameof(CanRunScenario))]
    private async Task RunScenarioAsync(AgentScenarioRowViewModel? row)
    {
        if (row is null || IsRunning || IsLoading || string.IsNullOrWhiteSpace(ModelId)) return;
        var scenario = _loadedScenarios.FirstOrDefault(s => s.Manifest.Id == row.Id);
        if (scenario is null) return;

        CancelEvidenceRestore();
        IsRunning = true;
        row.ResetResult();
        ResetRunningProgress(1, $"Running scenario: {row.Title}");
        CurrentScenarioLabel = FormatScenarioLabel(row.Id);
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(UpdateRunningProgress);
            var result = await _runner.RunScenarioAsync(scenario, ModelId, progress, _cts.Token);
            row.ApplyResult(result);
            ApplyFinalCounts([result], 1);
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

    private bool CanRunScenario(AgentScenarioRowViewModel? row) => row is not null && CanRun();

    private void ResetRunningProgress(int total, string headline)
    {
        _runningTotal = total;
        _completedCount = 0;
        _passedCount = 0;
        _failedCount = 0;
        RunningHeadline = headline;
        ScenarioProgressLabel = $"0 / {total} complete";
        CurrentScenarioLabel = "Current scenario: preparing...";
        CurrentStepLabel = "Current step: preparing scenario";
        RunningCountsLabel = "0 passed · 0 failed";
    }

    private void UpdateRunningProgress(string message)
    {
        StatusMessage = message;
        if (message.StartsWith("completed ", StringComparison.OrdinalIgnoreCase))
        {
            var separator = message.IndexOf(": ", StringComparison.Ordinal);
            if (separator > "completed ".Length)
            {
                var countText = message["completed ".Length..separator];
                var counts = countText.Split('/', 2, StringSplitOptions.TrimEntries);
                if (counts.Length == 2 && int.TryParse(counts[0], out var completed) && int.TryParse(counts[1], out var total))
                {
                    _completedCount = Math.Clamp(completed, 0, total);
                    _runningTotal = total;
                    ScenarioProgressLabel = $"{_completedCount} / {_runningTotal} complete";
                }

                var resultText = message[(separator + 2)..];
                var resultSeparator = resultText.LastIndexOf(" - ", StringComparison.Ordinal);
                var scenarioId = resultSeparator >= 0 ? resultText[..resultSeparator] : resultText;
                var outcome = resultSeparator >= 0 ? resultText[(resultSeparator + 3)..] : string.Empty;
                CurrentScenarioLabel = FormatScenarioLabel(scenarioId);
                CurrentStepLabel = string.IsNullOrWhiteSpace(outcome)
                    ? "Current step: finished"
                    : $"Current step: finished - {outcome}";
                if (string.Equals(outcome, "PASS", StringComparison.OrdinalIgnoreCase))
                    _passedCount++;
                else if (string.Equals(outcome, "FAIL", StringComparison.OrdinalIgnoreCase))
                    _failedCount++;
                RunningCountsLabel = $"{_passedCount} passed · {_failedCount} failed";
                return;
            }
        }

        var firstSeparator = message.IndexOf(": ", StringComparison.Ordinal);
        if (firstSeparator > 0)
        {
            var prefix = message[..firstSeparator];
            var counts = prefix.Split('/', 2, StringSplitOptions.TrimEntries);
            if (counts.Length == 2 && int.TryParse(counts[0], out _) && int.TryParse(counts[1], out var total))
            {
                _runningTotal = total;
                ScenarioProgressLabel = $"{_completedCount} / {_runningTotal} complete";
                CurrentScenarioLabel = FormatScenarioLabel(message[(firstSeparator + 2)..]);
                CurrentStepLabel = "Current step: preparing scenario";
                return;
            }
        }

        var stepMarker = message.IndexOf(" step ", StringComparison.Ordinal);
        var stepSeparator = stepMarker >= 0 ? message.IndexOf(": ", stepMarker, StringComparison.Ordinal) : -1;
        if (stepMarker > 0 && stepSeparator > stepMarker)
        {
            var scenarioId = message[..stepMarker];
            var stepNumber = message[(stepMarker + " step ".Length)..stepSeparator];
            CurrentScenarioLabel = FormatScenarioLabel(scenarioId);
            CurrentStepLabel = $"Current step: {stepNumber} - {message[(stepSeparator + 2)..]}";
        }
    }

    private void ApplyFinalCounts(IReadOnlyList<AgentScenarioRunResult> results, int total)
    {
        _runningTotal = total;
        _completedCount = results.Count;
        _passedCount = results.Count(result => result.Passed);
        _failedCount = results.Count - _passedCount;
        ScenarioProgressLabel = $"{_completedCount} / {_runningTotal} complete";
        RunningCountsLabel = $"{_passedCount} passed · {_failedCount} failed";
    }

    private string FormatScenarioLabel(string scenarioId)
    {
        var row = Scenarios.FirstOrDefault(candidate => string.Equals(candidate.Id, scenarioId, StringComparison.Ordinal));
        return row is null ? $"Current scenario: {scenarioId}" : $"Current scenario: {row.Title} ({row.Id})";
    }

    partial void OnIsRunningChanged(bool value)
    {
        RunSuiteCommand.NotifyCanExecuteChanged();
        RunScenarioCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        RunSuiteCommand.NotifyCanExecuteChanged();
        RunScenarioCommand.NotifyCanExecuteChanged();
    }
    private void QueueEvidenceRestore()
    {
        if (_evalStore is null || _loadedScenarios.Count == 0 || string.IsNullOrWhiteSpace(ModelId))
            return;

        var modelId = ModelId;
        var scenarioGeneration = _scenarioGeneration;
        if (_restoreTask is { IsCompleted: false }
            && string.Equals(_restoreModelId, modelId, StringComparison.OrdinalIgnoreCase)
            && _restoreScenarioGeneration == scenarioGeneration)
            return;

        CancelEvidenceRestore();
        var cts = new CancellationTokenSource();
        _restoreCts = cts;
        _restoreModelId = modelId;
        _restoreScenarioGeneration = scenarioGeneration;
        var epoch = _restoreEpoch;
        IsRestoringEvidence = true;
        _restoreTask = RestorePersistedResultsAsync(modelId, scenarioGeneration, epoch, cts);
    }

    private async Task RestorePersistedResultsAsync(
        string modelId,
        int scenarioGeneration,
        int epoch,
        CancellationTokenSource owner)
    {
        var enteredGate = false;
        try
        {
            if (_evalStore is null || _loadedScenarios.Count == 0 || string.IsNullOrWhiteSpace(modelId))
                return;

            await _restoreGate.WaitAsync(owner.Token);
            enteredGate = true;
            if (!IsCurrentEvidenceRestore(modelId, scenarioGeneration, epoch))
                return;

            var modelHash = await ResolveModelContentHashAsync(modelId, owner.Token);
            if (!IsCurrentEvidenceRestore(modelId, scenarioGeneration, epoch))
                return;

            var runs = await _evalStore.GetRunsAsync(EvalMode.AgentScenario, owner.Token);
            if (!IsCurrentEvidenceRestore(modelId, scenarioGeneration, epoch))
                return;

            var latestByScenario = runs
                .OrderByDescending(run => run.StartedAt)
                .SelectMany(run => run.CaseResults.Select(caseResult => (run, caseResult)))
                .Where(item => string.Equals(item.run.Target.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                .Where(item => item.caseResult.Metadata?.ContainsKey(AgentScenarioEvidenceContract.ResultJsonKey) == true)
                .GroupBy(item => item.caseResult.CaseId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().caseResult, StringComparer.Ordinal);

            foreach (var scenario in _loadedScenarios)
            {
                if (!IsCurrentEvidenceRestore(modelId, scenarioGeneration, epoch))
                    return;
                if (!latestByScenario.TryGetValue(scenario.Manifest.Id, out var caseResult)
                    || caseResult.Metadata is null
                    || !caseResult.Metadata.TryGetValue(AgentScenarioEvidenceContract.ResultJsonKey, out var resultJson))
                    continue;

                AgentScenarioRunResult? result;
                try
                {
                    result = JsonSerializer.Deserialize<AgentScenarioRunResult>(resultJson, EvalResultJsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (result is null)
                    continue;

                var status = AgentScenarioEvidenceContract.Assess(result, scenario, modelId, modelHash);
                Scenarios.FirstOrDefault(row => string.Equals(row.Id, scenario.Manifest.Id, StringComparison.Ordinal))
                    ?.ApplyPersistedResult(result, status);
            }
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _toasts.Show("Scenario history unavailable", ex.Message, ToastKind.Warning);
        }
        finally
        {
            if (enteredGate)
                _restoreGate.Release();
            if (epoch == _restoreEpoch && ReferenceEquals(_restoreCts, owner))
            {
                IsRestoringEvidence = false;
                _restoreTask = null;
                _restoreCts = null;
            }
            owner.Dispose();
        }
    }

    partial void OnModelIdChanged(string value)
    {
        RunSuiteCommand.NotifyCanExecuteChanged();
        RunScenarioCommand.NotifyCanExecuteChanged();
        CancelEvidenceRestore();
        QueueEvidenceRestore();
    }

    private bool IsCurrentEvidenceRestore(string modelId, int scenarioGeneration, int epoch) =>
        epoch == _restoreEpoch
        && scenarioGeneration == _scenarioGeneration
        && !IsRunning
        && string.Equals(ModelId, modelId, StringComparison.OrdinalIgnoreCase);

    private void CancelEvidenceRestore()
    {
        _restoreEpoch++;
        _restoreCts?.Cancel();
        _restoreCts = null;
        _restoreTask = null;
        IsRestoringEvidence = false;
    }

    private async Task<string> ResolveModelContentHashAsync(string modelId, CancellationToken ct)
    {
        if (!File.Exists(modelId))
            return await _computeModelContentHashAsync(modelId, ct);

        var path = Path.GetFullPath(modelId);
        var before = new FileInfo(path);
        if (!before.Exists)
            return string.Empty;

        Task<string> hashTask;
        var attachCleanup = false;
        lock (_modelHashGate)
        {
            if (_modelHashCache.TryGetValue(path, out var cached)
                && cached.Length == before.Length
                && cached.LastWriteTimeUtc == before.LastWriteTimeUtc)
            {
                _modelHashCache[path] = cached with { LastUsed = ++_modelHashUseCounter };
                return cached.Hash;
            }

            if (_inFlightModelHashes.TryGetValue(path, out var inFlight)
                && inFlight.Length == before.Length
                && inFlight.LastWriteTimeUtc == before.LastWriteTimeUtc)
            {
                hashTask = inFlight.Task;
            }
            else
            {
                hashTask = ComputeAndCacheModelHashAsync(path, before);
                _inFlightModelHashes[path] = new InFlightModelHash(before.Length, before.LastWriteTimeUtc, hashTask);
                attachCleanup = true;
            }
        }

        if (attachCleanup)
        {
            _ = hashTask.ContinueWith(completed =>
            {
                _ = completed.Exception;
                lock (_modelHashGate)
                {
                    if (_inFlightModelHashes.TryGetValue(path, out var current)
                        && ReferenceEquals(current.Task, completed))
                        _inFlightModelHashes.Remove(path);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        return await hashTask.WaitAsync(ct);
    }

    private async Task<string> ComputeAndCacheModelHashAsync(string path, FileInfo before)
    {
        var hash = await _computeModelContentHashAsync(path, CancellationToken.None);
        var after = new FileInfo(path);
        if (after.Exists && after.Length == before.Length && after.LastWriteTimeUtc == before.LastWriteTimeUtc)
        {
            lock (_modelHashGate)
            {
                _modelHashCache[path] = new CachedModelHash(
                    before.Length,
                    before.LastWriteTimeUtc,
                    hash,
                    ++_modelHashUseCounter);
                while (_modelHashCache.Count > ModelHashCacheLimit)
                {
                    var oldest = _modelHashCache.MinBy(pair => pair.Value.LastUsed).Key;
                    _modelHashCache.Remove(oldest);
                }
            }
        }

        return hash;
    }

    private sealed record CachedModelHash(long Length, DateTime LastWriteTimeUtc, string Hash, long LastUsed);
    private sealed record InFlightModelHash(long Length, DateTime LastWriteTimeUtc, Task<string> Task);
}
