using System.Collections.ObjectModel;
using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class BenchmarkViewModel : ObservableObject
{
    private readonly IBenchmarkService _benchmarks;
    private readonly ILlmService _llm;
    private readonly IModelProfileService _profiles;
    private readonly ISettingsService _settings;
    private readonly IToastService _toasts;
    private CancellationTokenSource? _runCts;

    public ObservableCollection<BenchmarkSuite> Suites { get; } = [];
    public ObservableCollection<BenchmarkRunViewModel> Runs { get; } = [];
    public ObservableCollection<BenchmarkRunViewModel> RankedRuns { get; } = [];
    public ObservableCollection<LlmModel> Models { get; } = [];
    public ObservableCollection<BenchmarkResultViewModel> SelectedResults { get; } = [];

    [ObservableProperty] private BenchmarkSuite? _selectedSuite;
    [ObservableProperty] private BenchmarkRunViewModel? _selectedRun;
    [ObservableProperty] private BenchmarkResultViewModel? _selectedResult;
    [ObservableProperty] private LlmModel? _selectedModel;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _useJudge;
    [ObservableProperty] private string _judgeModelId = string.Empty;
    [ObservableProperty] private int _timeoutSeconds = 120;
    [ObservableProperty] private int _maxCases;
    [ObservableProperty] private double _temperature = 0.7;

    public BenchmarkViewModel(
        IBenchmarkService benchmarks,
        ILlmService llm,
        IModelProfileService profiles,
        ISettingsService settings,
        IToastService toasts)
    {
        _benchmarks = benchmarks;
        _llm = llm;
        _profiles = profiles;
        _settings = settings;
        _toasts = toasts;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await _benchmarks.InitializeAsync();
        Suites.Clear();
        foreach (var suite in await _benchmarks.GetSuitesAsync())
            Suites.Add(suite);
        SelectedSuite ??= Suites.FirstOrDefault();
        ApplySuiteDefaults();

        Models.Clear();
        var models = await _llm.GetModelsAsync();
        _profiles.ApplyProfiles(models);
        foreach (var model in models.Where(m => m.IsVisible))
            Models.Add(model);
        SelectedModel ??= Models.FirstOrDefault(m => m.Id == _settings.Settings.Llm.DefaultModel) ?? Models.FirstOrDefault();

        await ReloadRunsAsync();
        Status = $"Loaded {Suites.Count} suite(s), {Runs.Count} run(s).";
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (SelectedSuite is null || SelectedModel is null) return;
        IsRunning = true;
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        try
        {
            var suite = CloneSuite(SelectedSuite);
            suite.Temperature = Temperature;
            suite.TimeoutSeconds = TimeoutSeconds;
            suite.MaxCases = MaxCases;
            suite.UseJudge = UseJudge;
            suite.JudgeModelId = JudgeModelId;
            var run = await _benchmarks.RunAsync(
                suite,
                SelectedModel,
                new Progress<string>(s => Status = s),
                _runCts.Token);
            await ReloadRunsAsync();
            SelectedRun = Runs.FirstOrDefault(r => r.Id == run.Id);
            _toasts.Show("Benchmark complete", $"{run.SuiteName} on {run.ModelName}: {run.RankingScore:P0}", ToastKind.Success, 7000);
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
            Status = "Ready.";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _runCts?.Cancel();
        Status = "Cancelling benchmark...";
    }

    [RelayCommand]
    private async Task RerunAsync(BenchmarkRunViewModel? run)
    {
        if (run is null) return;
        IsRunning = true;
        _runCts = new CancellationTokenSource();
        try
        {
            var rerun = await _benchmarks.RerunAsync(run.Id, new Progress<string>(s => Status = s), _runCts.Token);
            await ReloadRunsAsync();
            SelectedRun = Runs.FirstOrDefault(r => r.Id == rerun.Id);
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
            Status = "Ready.";
        }
    }

    [RelayCommand]
    private async Task DeleteRunAsync(BenchmarkRunViewModel? run)
    {
        if (run is null) return;
        await _benchmarks.DeleteRunAsync(run.Id);
        await ReloadRunsAsync();
        _toasts.Show("Benchmark deleted", run.Title, ToastKind.Info);
    }

    [RelayCommand]
    private async Task ExportRunAsync(BenchmarkRunViewModel? run)
    {
        if (run is null) return;
        var root = Aether.Services.SettingsService.ResolveDataRoot(_settings.Settings);
        var path = await _benchmarks.ExportAsync(run.Id, Path.Combine(root, "benchmark-exports"));
        _toasts.Show("Benchmark exported", path, ToastKind.Success, 7000);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    private async Task ReloadRunsAsync()
    {
        var runs = (await _benchmarks.GetRunsAsync()).Select(r => new BenchmarkRunViewModel(r)).ToList();
        Runs.Clear();
        foreach (var run in runs)
            Runs.Add(run);

        RankedRuns.Clear();
        foreach (var run in _benchmarks.Rank(runs.Select(r => r.Run)).Select(r => new BenchmarkRunViewModel(r)))
            RankedRuns.Add(run);
        SelectedRun ??= Runs.FirstOrDefault();
    }

    private bool CanRun() => !IsRunning && SelectedSuite is not null && SelectedModel is not null;

    partial void OnSelectedSuiteChanged(BenchmarkSuite? value)
    {
        ApplySuiteDefaults();
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedModelChanged(LlmModel? value) => RunCommand.NotifyCanExecuteChanged();
    partial void OnIsRunningChanged(bool value) => RunCommand.NotifyCanExecuteChanged();

    partial void OnSelectedRunChanged(BenchmarkRunViewModel? value)
    {
        SelectedResults.Clear();
        if (value is null) return;
        foreach (var result in value.Run.Results.Select(r => new BenchmarkResultViewModel(r)))
            SelectedResults.Add(result);
        SelectedResult = SelectedResults.FirstOrDefault();
    }

    private void ApplySuiteDefaults()
    {
        if (SelectedSuite is null) return;
        Temperature = SelectedSuite.Temperature;
        TimeoutSeconds = SelectedSuite.TimeoutSeconds;
        MaxCases = SelectedSuite.MaxCases;
        UseJudge = SelectedSuite.UseJudge;
        JudgeModelId = SelectedSuite.JudgeModelId;
    }

    private static BenchmarkSuite CloneSuite(BenchmarkSuite suite) => new()
    {
        Id = suite.Id,
        Name = suite.Name,
        Description = suite.Description,
        Temperature = suite.Temperature,
        TimeoutSeconds = suite.TimeoutSeconds,
        MaxCases = suite.MaxCases,
        UseJudge = suite.UseJudge,
        JudgeModelId = suite.JudgeModelId,
        Cases = suite.Cases.Select(c => new BenchmarkCase
        {
            Id = c.Id,
            Name = c.Name,
            Prompt = c.Prompt,
            SystemPrompt = c.SystemPrompt,
            ExpectedKeywords = c.ExpectedKeywords.ToList(),
            ExpectedRegexes = c.ExpectedRegexes.ToList(),
            ShouldRefuse = c.ShouldRefuse,
            Tags = c.Tags.ToList()
        }).ToList()
    };
}

public sealed class BenchmarkRunViewModel
{
    public BenchmarkRun Run { get; }
    public string Id => Run.Id;
    public string Title => $"{Run.SuiteName} · {Run.ModelName}";
    public string Model => string.IsNullOrWhiteSpace(Run.Provider) ? Run.ModelName : $"{Run.ModelName} [{Run.Provider}]";
    public string Status => Run.Status;
    public string Started => Run.StartedAt.ToLocalTime().ToString("g");
    public string Score => $"{Run.RankingScore:P0}";
    public string PassRate => $"{Run.PassRate:P0}";
    public string Speed => $"median {Run.MedianApproxTokensPerSecond:F1} tok/s";
    public string FirstToken => $"median {Run.MedianFirstTokenMs:F0} ms";
    public string Summary => $"{Score} · pass {PassRate} · {Speed} · first {FirstToken} · failures {Run.FailureCount}";
    public BenchmarkRunViewModel(BenchmarkRun run) => Run = run;
}

public sealed class BenchmarkResultViewModel
{
    public BenchmarkResult Result { get; }
    public string Title => $"{Result.Phase} {Result.IterationIndex + 1} · {(Result.Passed ? "PASS" : "FAIL")} · {Result.CaseName}";
    public string Timings => $"{Result.FirstTokenMs} ms first · {Result.TotalMs} ms total · {Result.ApproxTokensPerSecond:F1} tok/s";
    public string Quality => $"{Result.QualityScore:P0}";
    public string Checks => $"keyword {Result.KeywordHit} · regex {Result.RegexHit} · refusal {Result.RefusalCorrect} · failure {Result.FailureCategory}";
    public string Error => Result.Error;
    public string Output => Result.Output;
    public BenchmarkResultViewModel(BenchmarkResult result) => Result = result;
}
