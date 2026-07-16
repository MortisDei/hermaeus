using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class BenchmarkViewModel : ObservableObject
{
    private readonly BenchmarkService _benchmarks;
    private readonly ILlmService _llm;
    private readonly ModelProfileService _profiles;
    private readonly ISettingsService _settings;
    private readonly IToastService _toasts;
    private readonly ServicesViewModel? _services;
    private readonly IBenchmarkInsightsService? _insights;
    private readonly IVoiceOrchestrator? _voice;
    private CancellationTokenSource? _runCts;
    private bool _isLoading;

    public UiBoundCollection<BenchmarkSuite> Suites { get; } = [];
    public UiBoundCollection<BenchmarkRunViewModel> Runs { get; } = [];
    public UiBoundCollection<BenchmarkRunViewModel> RankedRuns { get; } = [];
    public UiBoundCollection<LlmModel> Models { get; } = [];
    public UiBoundCollection<BenchmarkResultViewModel> SelectedResults { get; } = [];
    public UiBoundCollection<TagLeaderboardViewModel> InsightsLeaderboards { get; } = [];
    public UiBoundCollection<string> InsightsCaveats { get; } = [];
    /// <summary>"Based on your usage" card rows; empty when no activity kind has enough calls yet (r6 2.3).</summary>
    public UiBoundCollection<UsageInsightViewModel> InsightsUsage { get; } = [];

    [ObservableProperty] private bool _isLoadingInsights;
    [ObservableProperty] private string _insightsHeader = string.Empty;
    [ObservableProperty] private bool _insightsHasData;
    [ObservableProperty] private ModelAggregateViewModel? _insightsBestOverall;

    public Func<Task<bool>>? RequestClearRunHistoryConfirmation { get; set; }
    public Func<BenchmarkResultViewModel, Task>? RequestShowCaseInfo { get; set; }
    public Func<BenchmarkRunViewModel, Task>? RequestShowRunInfo { get; set; }

    [ObservableProperty] private BenchmarkSuite? _selectedSuite;
    [ObservableProperty] private BenchmarkRunViewModel? _selectedRun;
    [ObservableProperty] private BenchmarkResultViewModel? _selectedResult;
    [ObservableProperty] private LlmModel? _selectedModel;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _timeoutSeconds = 120;
    [ObservableProperty] private int _maxCases;
    [ObservableProperty] private double _temperature = 0.7;
    [ObservableProperty] private bool _runAllSuites = true;

    public BenchmarkViewModel(
        BenchmarkService benchmarks,
        ILlmService llm,
        ModelProfileService profiles,
        ISettingsService settings,
        IToastService toasts,
        ServicesViewModel? services = null,
        IBenchmarkInsightsService? insights = null,
        IVoiceOrchestrator? voice = null)
    {
        _benchmarks = benchmarks;
        _llm = llm;
        _profiles = profiles;
        _settings = settings;
        _toasts = toasts;
        _services = services;
        _insights = insights;
        _voice = voice;
        InsightsUsage.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasInsightsUsage));
        Runs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRuns));
    }

    public bool HasInsightsUsage => InsightsUsage.Count > 0;

    /// <summary>Drives the "no runs yet" empty state (r8 02-onboarding-and-usability.md 2.6).</summary>
    public bool HasRuns => Runs.Count > 0;

    [RelayCommand]
    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            await _benchmarks.InitializeAsync();
            Suites.Clear();
            foreach (var suite in await _benchmarks.GetSuitesAsync())
                Suites.Add(suite);
            SelectedSuite = Suites.FirstOrDefault();
            ApplySuiteDefaults();

            Models.Clear();
            var models = await _llm.GetModelsAsync();
            _profiles.ApplyProfiles(models);
            foreach (var model in models.Where(m => m.IsVisible))
                Models.Add(model);
            foreach (var model in DiscoverLocalGgufModels(models.Select(m => m.Id)))
                Models.Add(model);
            SelectedModel = Models.FirstOrDefault(m => m.Id == _settings.Settings.Llm.DefaultModel) ?? Models.FirstOrDefault();

            await ReloadRunsAsync();
            Status = $"Loaded {Suites.Count} suite(s), {Runs.Count} run(s).";
        }
        finally
        {
            _isLoading = false;
        }
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
            await PrepareSelectedModelAsync(_runCts.Token);
            var suites = RunAllSuites ? Suites.ToList() : [SelectedSuite];
            BenchmarkRun? run = null;
            for (var i = 0; i < suites.Count; i++)
            {
                var suite = ConfigureSuite(suites[i]);
                Status = $"Running suite {i + 1}/{suites.Count}: {suite.Name}";
                run = await _benchmarks.RunAsync(
                    suite,
                    SelectedModel,
                    new Progress<string>(s => Status = $"{suite.Name}: {s}"),
                    _runCts.Token);
            }

            await ReloadRunsAsync();
            if (run is not null)
                SelectedRun = Runs.FirstOrDefault(r => r.Id == run.Id);
            _toasts.Show("Benchmark complete", $"{suites.Count} suite(s) on {SelectedModel.Name}", ToastKind.Success, 7000);
            NarrateCompletion(run, SelectedModel.Name);
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
            Status = "Ready.";
        }
    }

    private void NarrateCompletion(BenchmarkRun? run, string modelName)
    {
        if (_voice is null || run is null)
            return;

        var text = run.Status == "Cancelled"
            ? $"Benchmark {run.SuiteName} on {modelName} cancelled."
            : $"Benchmark {run.SuiteName} on {modelName} complete: {run.Passed} of {run.Total} passed.";
        _ = _voice.EnqueueAsync(new VoiceUtterance(text, VoiceChannel.Benchmark, VoicePriority.Normal, DedupeKey: $"benchmark:{run.Id}"));
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

    [RelayCommand(CanExecute = nameof(CanExportAll))]
    private async Task ExportAllRunsAsync()
    {
        var root = Aether.Services.SettingsService.ResolveDataRoot(_settings.Settings);
        var path = await _benchmarks.ExportAllAsync(Path.Combine(root, "benchmark-exports"));
        _toasts.Show("All benchmarks exported", path, ToastKind.Success, 7000);
    }

    [RelayCommand]
    private async Task ClearRunHistoryAsync()
    {
        if (Runs.Count == 0)
            return;

        if (RequestClearRunHistoryConfirmation is not null
            && !await RequestClearRunHistoryConfirmation())
            return;

        await _benchmarks.ClearRunsAsync();
        SelectedRun = null;
        SelectedResult = null;
        SelectedResults.Clear();
        await ReloadRunsAsync();
        _toasts.Show("Benchmark history cleared", "Saved benchmark runs were removed.", ToastKind.Info);
    }

    [RelayCommand]
    public async Task ExportRunAsync(BenchmarkRunViewModel? run)
    {
        if (run is null) return;
        var root = Aether.Services.SettingsService.ResolveDataRoot(_settings.Settings);
        var path = await _benchmarks.ExportAsync(run.Id, Path.Combine(root, "benchmark-exports"));
        _toasts.Show("Benchmark exported", path, ToastKind.Success, 7000);
    }

    [RelayCommand]
    private async Task ShowRunInfoAsync(BenchmarkRunViewModel? run)
    {
        if (run is null) return;
        // Prefer delegate-based dialog from view layer
        if (RequestShowRunInfo is not null)
        {
            await RequestShowRunInfo(run);
            return;
        }

        var result = run.FirstResult;
        if (result is not null && RequestShowCaseInfo is not null)
        {
            await RequestShowCaseInfo(result);
            return;
        }

        var md = $"{run.Title}\n{run.Summary}\nStarted: {run.Started}\nStatus: {run.Status}";
        _toasts.Show("Run info", md, ToastKind.Info, 8000);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    /// <summary>
    /// Loaded on demand (button/tab), never on page open: deserializing every
    /// stored run for aggregation should not tax normal page navigation.
    /// </summary>
    [RelayCommand]
    public async Task LoadInsightsAsync()
    {
        if (_insights is null || IsLoadingInsights) return;
        IsLoadingInsights = true;
        try
        {
            var report = await _insights.LoadReportAsync();
            InsightsHasData = report.HasData;
            InsightsHeader = report.HasData
                ? $"You've run {report.TotalRuns} benchmark(s) across {report.ModelCount} model(s) on this hardware."
                : $"No comparable benchmark data yet ({report.TotalRuns} run(s) recorded). Run a starter suite to get recommendations.";
            InsightsBestOverall = report.BestOverall is null ? null : new ModelAggregateViewModel(report.BestOverall);

            InsightsLeaderboards.Clear();
            foreach (var board in report.TagLeaderboards)
                InsightsLeaderboards.Add(new TagLeaderboardViewModel(board, report.Comparisons));

            InsightsCaveats.Clear();
            foreach (var caveat in report.Caveats)
                InsightsCaveats.Add(caveat);

            InsightsUsage.Clear();
            foreach (var usage in report.UsageInsightsOrEmpty)
                InsightsUsage.Add(new UsageInsightViewModel(usage));
        }
        catch (Exception ex)
        {
            _toasts.Show("Benchmark insights unavailable", ex.Message, ToastKind.Warning, 6000);
        }
        finally
        {
            IsLoadingInsights = false;
        }
    }

    [RelayCommand]
    private async Task RerunFromInsightsAsync(ModelAggregateViewModel? model)
    {
        if (model is null) return;
        var suite = Suites.FirstOrDefault(s => s.Id == SelectedSuite?.Id) ?? Suites.FirstOrDefault();
        var candidate = Models.FirstOrDefault(m => m.Id == model.ModelId);
        if (suite is null || candidate is null)
        {
            _toasts.Show("Re-run unavailable", "Could not find a matching suite or model to re-run.", ToastKind.Info, 5000);
            return;
        }

        SelectedSuite = suite;
        SelectedModel = candidate;
        await RunAsync();
    }

    [RelayCommand]
    private async Task ShowCaseInfoAsync(BenchmarkResultViewModel? result)
    {
        if (result is not null && RequestShowCaseInfo is not null)
            await RequestShowCaseInfo(result);
    }

    private async Task ReloadRunsAsync()
    {
        var runs = (await _benchmarks.GetRunsAsync()).Select(r => new BenchmarkRunViewModel(r)).ToList();
        Runs.Clear();
        foreach (var run in runs)
            Runs.Add(run);

        UpdateRankedRuns(runs);
        if (SelectedRun is not null && Runs.All(r => r.Id != SelectedRun.Id))
            SelectedRun = null;
        SelectedRun ??= Runs.FirstOrDefault();
        ExportAllRunsCommand.NotifyCanExecuteChanged();
    }

    private void UpdateRankedRuns(List<BenchmarkRunViewModel> runs)
    {
        RankedRuns.Clear();
        var list = runs.Select(r => r.Run).ToList();
        if (SelectedSuite is not null)
            list = list.Where(r => r.SuiteId == SelectedSuite.Id).ToList();

        // counts per model for display
        var counts = list.GroupBy(r => GetRankingGroupKey(r), StringComparer.OrdinalIgnoreCase)
                         .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var candidates = list.GroupBy(r => GetRankingGroupKey(r), StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.OrderByDescending(r => r.StartedAt).First());

        var ranked = _benchmarks.Rank(candidates);
        foreach (var run in ranked)
        {
            counts.TryGetValue(GetRankingGroupKey(run), out var count);
            RankedRuns.Add(new BenchmarkRunViewModel(run, Math.Max(1, count)));
        }
    }

    private bool CanRun() => !IsRunning && SelectedSuite is not null && SelectedModel is not null;

    private bool CanExportAll() => !IsRunning && Runs.Count > 0;

    partial void OnSelectedSuiteChanged(BenchmarkSuite? value)
    {
        ApplySuiteDefaults();
        RunCommand.NotifyCanExecuteChanged();
        if (!_isLoading)
        {
            // Refresh ranking view to show only runs for the selected suite.
            _ = ReloadRunsAsync();
        }
    }

    partial void OnSelectedModelChanged(LlmModel? value)
    {
        RunCommand.NotifyCanExecuteChanged();
        if (_isLoading || value is null || !IsManagedLocalGguf(value))
            return;

        _ = AutoSwitchSelectedModelAsync(value);
    }
    partial void OnIsRunningChanged(bool value)
    {
        RunCommand.NotifyCanExecuteChanged();
        ExportAllRunsCommand.NotifyCanExecuteChanged();
    }

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
    }

    private BenchmarkSuite ConfigureSuite(BenchmarkSuite source)
    {
        var suite = CloneSuite(source);
        suite.Temperature = Temperature;
        suite.TimeoutSeconds = TimeoutSeconds;
        suite.MaxCases = MaxCases;
        return suite;
    }

    private async Task AutoSwitchSelectedModelAsync(LlmModel model)
    {
        try
        {
            Status = $"Starting managed llama.cpp for {model.Name}...";
            await PrepareModelAsync(model, CancellationToken.None);
            Status = $"Managed llama.cpp ready for {model.Name}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not start managed llama.cpp: {ex.Message}";
            _toasts.Show("Model switch failed", ex.Message, ToastKind.Warning, 7000);
        }
    }

    private Task PrepareSelectedModelAsync(CancellationToken ct) =>
        SelectedModel is null ? Task.CompletedTask : PrepareModelAsync(SelectedModel, ct);

    private async Task PrepareModelAsync(LlmModel model, CancellationToken ct)
    {
        if (_services is null || !IsManagedLocalGguf(model))
            return;

        // Only restart if the model has actually changed to avoid 1-2 minute delays on every run.
        // This is a coarse, model-ID-based fast skip; SelectModelAndRestartAsync
        // (ServicesViewModel.cs) also guards by normalized file path further in,
        // so an unchanged model never restarts even if this check is bypassed.
        if (string.Equals(_settings.Settings.Llm.DefaultModel, model.Id, StringComparison.OrdinalIgnoreCase))
            return;

        await _services.SelectChatModelAndRestartAsync(model.Id, ct);
    }

    private List<LlmModel> DiscoverLocalGgufModels(IEnumerable<string> existingIds)
    {
        var existing = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        return Aether.Services.LocalAiAssetLocator.FindGgufModels(_settings.Settings.DataManagement.LocalAiAssetsRoot)
            .Where(path => !existing.Contains(path))
            .Select(path => new LlmModel
            {
                Id = path,
                Name = Path.GetFileNameWithoutExtension(path),
                Provider = "local GGUF",
                ProviderTag = "llama.cpp",
                SizeBytes = new FileInfo(path).Length,
                ModifiedAt = File.GetLastWriteTimeUtc(path),
                Tags = ["managed", "gguf"]
            })
            .ToList();
    }

    private static bool IsManagedLocalGguf(LlmModel model) =>
        model.Id.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
        && File.Exists(model.Id);

    private static string GetRankingGroupKey(BenchmarkRun run) =>
        string.IsNullOrWhiteSpace(run.ModelId) ? run.ModelName : run.ModelId;

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
    public int RunCount { get; }
    public string Id => Run.Id;
    public string Title => $"{Run.SuiteName} · {Run.ModelName}";
    public string Model => string.IsNullOrWhiteSpace(Run.Provider) ? Run.ModelName : $"{Run.ModelName} [{Run.Provider}]";
    public string Status => Run.Status;
    public string Started => Run.StartedAt.ToLocalTime().ToString("g");
    public string Score => $"{Run.RankingScore:P0}";
    public string PassRate => $"{Run.PassRate:P0}";
    public string Speed => $"median {Run.MedianApproxTokensPerSecond:F1} tok/s";
    public string FirstToken => $"median {Run.MedianFirstTokenMs:F0} ms";
    public string RunCountLabel => RunCount == 1 ? "Best run" : $"Best of {RunCount} runs";
    public bool HasResults => Run.Results.Count > 0;
    public BenchmarkResultViewModel? FirstResult => Run.Results.FirstOrDefault() is { } result ? new BenchmarkResultViewModel(result) : null;
    public string Summary => $"{Score} · pass {PassRate} · {Speed} · first {FirstToken} · failures {Run.FailureCount}";
    public BenchmarkRunViewModel(BenchmarkRun run, int runCount = 1)
    {
        Run = run;
        RunCount = Math.Max(1, runCount);
    }
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

public sealed class ModelAggregateViewModel
{
    public ModelAggregate Aggregate { get; }
    public string ModelId => Aggregate.ModelId;
    public string DisplayName => string.IsNullOrWhiteSpace(Aggregate.Quantization)
        ? Aggregate.ModelName
        : $"{Aggregate.ModelName} ({Aggregate.Quantization})";
    public string QualityLabel => $"{Aggregate.QualityScore:P0}";
    public string SpeedLabel => $"{Aggregate.TokensPerSecond:F1} tok/s";
    public string EvidenceLabel => $"{Aggregate.RunCount} run(s), {Aggregate.CaseCount} case(s)";
    public string StaleLabel => Aggregate.IsStale ? "Stale - consider re-running" : string.Empty;
    public bool IsStale => Aggregate.IsStale;
    public ModelAggregateViewModel(ModelAggregate aggregate) => Aggregate = aggregate;
}

public sealed class TagLeaderboardViewModel
{
    public string Tag { get; }
    public IReadOnlyList<ModelAggregateViewModel> Ranked { get; }
    public IReadOnlyList<string> ComparisonSentences { get; }

    public TagLeaderboardViewModel(TagLeaderboard board, IReadOnlyList<ModelComparison> allComparisons)
    {
        Tag = board.Tag;
        Ranked = board.Ranked.Select(a => new ModelAggregateViewModel(a)).ToList();
        ComparisonSentences = allComparisons.Where(c => c.Tag == board.Tag).Select(c => c.Sentence).ToList();
    }
}

/// <summary>One row of the Insights tab's "Based on your usage" card (r6 02-usage-history-recommendations.md 2.3).</summary>
public sealed class UsageInsightViewModel
{
    public UsageInsightViewModel(UsageInsight insight) => Insight = insight;

    public UsageInsight Insight { get; }
    public string KindLabel => Insight.Kind.ToString();
    public string Sentence => Insight.Sentence;
    public bool HasRecommendation => Insight.RecommendedModelName is not null;
}
