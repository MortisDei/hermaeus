using System.Reflection;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r19 6.6: the Rankings tab gets a rank column, a labeled score bar, and a
/// "run two or more models to compare" empty state. Exercises the pure
/// projection (BenchmarkViewModel.UpdateRankedRuns is private - reflection
/// invocation mirrors the r17 FillTiming/FillResources precedent rather than
/// standing up full SQLite-backed run persistence for a rank-ordering check).
/// </summary>
public sealed class BenchmarkRankingsTests
{
    private static BenchmarkRun NewRun(string modelId, double qualityScore)
    {
        return new BenchmarkRun
        {
            SuiteId = "suite-1",
            SuiteName = "Suite",
            ModelId = modelId,
            ModelName = modelId,
            StartedAt = DateTime.UtcNow,
            Results =
            [
                new BenchmarkResult
                {
                    QualityScore = qualityScore,
                    ApproxTokensPerSecond = 20,
                    ResourceScore = 1,
                    Passed = true
                }
            ]
        };
    }

    private static BenchmarkViewModel NewViewModel(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var llm = new FakeLlm();
        var benchmarks = new BenchmarkService(settings, llm, new FakeSystemInfo(), new FakeEvalStore());
        return new BenchmarkViewModel(benchmarks, llm, new ModelProfileService(settings), settings, new FakeToasts());
    }

    private static void InvokeUpdateRankedRuns(BenchmarkViewModel vm, List<BenchmarkRunViewModel> runs)
    {
        var method = typeof(BenchmarkViewModel).GetMethod("UpdateRankedRuns", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("UpdateRankedRuns not found - has it been renamed?");
        method.Invoke(vm, [runs]);
    }

    [Fact]
    public void RankedRuns_orders_by_score_descending_and_assigns_1_based_rank()
    {
        using var temp = new TempDir();
        var vm = NewViewModel(temp);
        var runs = new List<BenchmarkRunViewModel>
        {
            new(NewRun("model-low", 0.3)),
            new(NewRun("model-high", 0.9)),
            new(NewRun("model-mid", 0.6))
        };

        InvokeUpdateRankedRuns(vm, runs);

        Assert.Equal(["model-high", "model-mid", "model-low"], vm.RankedRuns.Select(r => r.Model));
        Assert.Equal([1, 2, 3], vm.RankedRuns.Select(r => r.Rank));
    }

    [Fact]
    public void HasComparableRankings_is_false_for_a_single_model_and_true_for_two_or_more()
    {
        using var temp = new TempDir();
        var vm = NewViewModel(temp);

        InvokeUpdateRankedRuns(vm, [new BenchmarkRunViewModel(NewRun("solo-model", 0.5))]);
        Assert.False(vm.HasComparableRankings);

        InvokeUpdateRankedRuns(vm, [new BenchmarkRunViewModel(NewRun("model-a", 0.5)), new BenchmarkRunViewModel(NewRun("model-b", 0.7))]);
        Assert.True(vm.HasComparableRankings);
    }

    [Fact]
    public void ScorePercent_mirrors_the_0_to_1_ranking_score_as_a_0_to_100_fill()
    {
        var vm = new BenchmarkRunViewModel(NewRun("model-a", 1.0));
        Assert.InRange(vm.ScorePercent, 0, 100);
        Assert.True(vm.ScorePercent > 0, "a passing, non-trivial run should have a non-zero score fill");
    }
}
