using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class BenchmarkViewModelInsightsTests
{
    [Fact]
    public void HasRunsDrivesTheRunHistoryEmptyState()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var benchmarks = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());
        var vm = new BenchmarkViewModel(benchmarks, new FakeLlm(), new ModelProfileService(settings), settings, new FakeToasts());

        Assert.False(vm.HasRuns, "No runs should show the empty state.");

        vm.Runs.Add(new BenchmarkRunViewModel(new BenchmarkRun { SuiteName = "Suite", ModelName = "Model" }));

        Assert.True(vm.HasRuns, "Adding a run must hide the empty state.");
    }

    [Fact]
    public async Task LoadInsightsAsync_populates_header_leaderboards_and_best_overall()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var benchmarks = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

        var best = new ModelAggregate("id-a", "Model A", "Q4", "llama.cpp", 3, 30, 0.9, 25, 1, 0.9, 4.5, DateTime.UtcNow, false);
        var report = new BenchmarkInsightsReport(
            TotalRuns: 5, ComparableRuns: 4, ModelCount: 2, OldestComparableRun: DateTime.UtcNow.AddDays(-10),
            Models: [best],
            TagLeaderboards: [new TagLeaderboard("coding", [best])],
            Comparisons: [],
            Caveats: ["1 run(s) from different hardware were ignored."]);

        var vm = new BenchmarkViewModel(benchmarks, new FakeLlm(), new ModelProfileService(settings), settings, new FakeToasts(),
            insights: new FakeInsightsService(report));

        await vm.LoadInsightsCommand.ExecuteAsync(null);

        Assert.True(vm.InsightsHasData);
        Assert.Contains("5 benchmark(s)", vm.InsightsHeader);
        Assert.NotNull(vm.InsightsBestOverall);
        Assert.Equal("Model A (Q4)", vm.InsightsBestOverall!.DisplayName);
        Assert.Single(vm.InsightsLeaderboards);
        Assert.Single(vm.InsightsCaveats);
    }

    [Fact]
    public async Task LoadInsightsAsync_populates_the_usage_card_when_the_report_has_usage_insights()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var benchmarks = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

        var usage = new UsageInsight(TraceKind.Chat, "model-a", "model-a", 1.0, 25, null, null, "You mostly use model-a for chat.");
        var report = new BenchmarkInsightsReport(3, 3, 1, DateTime.UtcNow, [], [], [], [], UsageInsights: [usage]);
        var vm = new BenchmarkViewModel(benchmarks, new FakeLlm(), new ModelProfileService(settings), settings, new FakeToasts(),
            insights: new FakeInsightsService(report));

        Assert.False(vm.HasInsightsUsage, "the usage card should be hidden before insights load");
        await vm.LoadInsightsCommand.ExecuteAsync(null);

        Assert.True(vm.HasInsightsUsage);
        Assert.Single(vm.InsightsUsage);
        Assert.Equal("You mostly use model-a for chat.", vm.InsightsUsage[0].Sentence);
        Assert.False(vm.InsightsUsage[0].HasRecommendation);
    }

    [Fact]
    public async Task LoadInsightsAsync_shows_empty_state_when_report_has_no_comparable_data()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var benchmarks = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());
        var report = new BenchmarkInsightsReport(2, 0, 0, null, [], [], [], []);
        var vm = new BenchmarkViewModel(benchmarks, new FakeLlm(), new ModelProfileService(settings), settings, new FakeToasts(),
            insights: new FakeInsightsService(report));

        await vm.LoadInsightsCommand.ExecuteAsync(null);

        Assert.False(vm.InsightsHasData);
        Assert.Null(vm.InsightsBestOverall);
        Assert.Contains("No comparable benchmark data yet", vm.InsightsHeader);
    }

    [Fact]
    public async Task LoadInsightsAsync_sets_the_busy_flag_for_the_duration_of_the_load()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var benchmarks = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());
        var gate = new TaskCompletionSource();
        var vm = new BenchmarkViewModel(benchmarks, new FakeLlm(), new ModelProfileService(settings), settings, new FakeToasts(),
            insights: new GatedInsightsService(gate.Task));

        var loadTask = vm.LoadInsightsCommand.ExecuteAsync(null);
        Assert.True(vm.IsLoadingInsights);

        gate.SetResult();
        await loadTask;

        Assert.False(vm.IsLoadingInsights);
    }

    [Fact]
    public async Task RunAsync_narrates_benchmark_completion_with_pass_count()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var benchmarks = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());
        var voice = new FakeVoiceOrchestrator();
        var vm = new BenchmarkViewModel(benchmarks, new FakeLlm(), new ModelProfileService(settings), settings, new FakeToasts(), voice: voice);

        await vm.LoadAsync();
        vm.RunAllSuites = false;
        vm.MaxCases = 1;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Contains(voice.Enqueued, u => u.Channel == VoiceChannel.Benchmark && u.Text.Contains("complete"));
    }

    private sealed class FakeInsightsService : IBenchmarkInsightsService
    {
        private readonly BenchmarkInsightsReport _report;
        public FakeInsightsService(BenchmarkInsightsReport report) => _report = report;
        public Task<BenchmarkInsightsReport> LoadReportAsync(CancellationToken ct = default) => Task.FromResult(_report);
    }

    private sealed class GatedInsightsService : IBenchmarkInsightsService
    {
        private readonly Task _gate;
        public GatedInsightsService(Task gate) => _gate = gate;

        public async Task<BenchmarkInsightsReport> LoadReportAsync(CancellationToken ct = default)
        {
            await _gate;
            return new BenchmarkInsightsReport(0, 0, 0, null, [], [], [], []);
        }
    }
}
