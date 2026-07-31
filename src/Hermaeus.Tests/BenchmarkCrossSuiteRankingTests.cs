using Hermaeus.Core.Models;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// Best across every suite, keyed by suite. The overall board ranks on one
/// shared case set; this ranks each suite separately and then averages the
/// standings, so every suite counts once and a 40 case suite cannot outvote a
/// 5 case suite (docs/review/archived/r26 doc 04).
/// </summary>
public sealed class BenchmarkCrossSuiteRankingTests
{
    private static BenchmarkRun Run(
        string suiteId, string suiteName,
        string modelId, string modelName,
        IEnumerable<string> caseIds,
        double quality, double tokensPerSecond, DateTime startedAt)
    {
        var results = caseIds.Select(id => new BenchmarkResult
        {
            CaseId = id,
            CaseName = $"Case {id}",
            QualityScore = quality,
            ApproxTokensPerSecond = tokensPerSecond,
            ResourceScore = 1
        }).ToList();

        return new BenchmarkRun
        {
            SuiteId = suiteId,
            SuiteName = suiteName,
            ModelId = modelId,
            ModelName = modelName,
            StartedAt = startedAt,
            Metadata = new BenchmarkRunMetadata { Quantization = "", RuntimeKind = "llama.cpp", AppVersion = "0.10.0.0" },
            Results = results
        };
    }

    private static string[] Cases(string prefix, int count) =>
        [.. Enumerable.Range(0, count).Select(i => $"{prefix}{i}")];

    /// <summary>Two runs per model per suite: the evidence floor is 2 runs and 10 cases.</summary>
    private static IEnumerable<BenchmarkRun> Sat(
        string suiteId, string suiteName, string modelId, string modelName,
        string[] cases, double quality, double tokensPerSecond, DateTime now)
    {
        yield return Run(suiteId, suiteName, modelId, modelName, cases, quality, tokensPerSecond, now.AddDays(-1));
        yield return Run(suiteId, suiteName, modelId, modelName, cases, quality, tokensPerSecond, now);
    }

    [Fact]
    public void A_model_that_wins_both_suites_is_the_cross_suite_leader()
    {
        var now = DateTime.UtcNow;
        var alpha = Cases("alpha-", 12);
        var beta = Cases("beta-", 12);

        var runs = Sat("s1", "Alpha suite", "good", "Good", alpha, 0.9, 20, now)
            .Concat(Sat("s1", "Alpha suite", "poor", "Poor", alpha, 0.5, 20, now))
            .Concat(Sat("s2", "Beta suite", "good", "Good", beta, 0.9, 20, now))
            .Concat(Sat("s2", "Beta suite", "poor", "Poor", beta, 0.5, 20, now))
            .ToList();

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);
        var crossSuite = report.CrossSuiteOrNone;

        Assert.True(crossSuite.HasAnswer);
        Assert.Equal("Good", crossSuite.Leader!.ModelName);
        Assert.Equal(2, crossSuite.SuiteCount);
        Assert.Equal(1, crossSuite.Leader.MeanPosition);
        Assert.Equal(2, crossSuite.Leader.Placements.Count);
    }

    [Fact]
    public void A_split_result_breaks_the_tie_on_mean_quality_per_second_and_is_order_independent()
    {
        var now = DateTime.UtcNow;
        var alpha = Cases("alpha-", 12);
        var beta = Cases("beta-", 12);

        // Each model wins one suite, so both have mean position 1.5. "fast"
        // wins the tie on mean QualityPerSecond.
        var runs = Sat("s1", "Alpha suite", "fast", "Fast", alpha, 0.9, 60, now)
            .Concat(Sat("s1", "Alpha suite", "slow", "Slow", alpha, 0.7, 10, now))
            .Concat(Sat("s2", "Beta suite", "fast", "Fast", beta, 0.6, 10, now))
            .Concat(Sat("s2", "Beta suite", "slow", "Slow", beta, 0.9, 20, now))
            .ToList();

        var forward = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now).CrossSuiteOrNone;
        var reversed = runs.AsEnumerable().Reverse().ToList();
        var backward = BenchmarkInsightsMath.BuildReport(reversed, reversed, "0.10.0.0", now).CrossSuiteOrNone;

        Assert.True(forward.HasAnswer);
        Assert.Equal(1.5, forward.Leader!.MeanPosition);
        Assert.Equal(forward.Leader.ModelId, backward.Leader!.ModelId);
        Assert.Equal(
            forward.Ranked.Select(s => s.ModelId),
            backward.Ranked.Select(s => s.ModelId));
    }

    [Fact]
    public void A_model_that_did_not_run_every_suite_is_excluded_and_named()
    {
        var now = DateTime.UtcNow;
        var alpha = Cases("alpha-", 12);
        var beta = Cases("beta-", 12);
        var gamma = Cases("gamma-", 12);

        var runs = new[] { ("s1", "Alpha suite", alpha), ("s2", "Beta suite", beta), ("s3", "Gamma suite", gamma) }
            .SelectMany(s => Sat(s.Item1, s.Item2, "good", "Good", s.Item3, 0.9, 20, now)
                .Concat(Sat(s.Item1, s.Item2, "poor", "Poor", s.Item3, 0.5, 20, now)))
            // Ran one suite of three.
            .Concat(Sat("s1", "Alpha suite", "partial", "Partial", alpha, 0.99, 40, now))
            .ToList();

        var crossSuite = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now).CrossSuiteOrNone;

        Assert.True(crossSuite.HasAnswer);
        Assert.DoesNotContain(crossSuite.Ranked, s => s.ModelId == "partial");
        Assert.Contains(crossSuite.Caveats, c => c.Contains("Partial", StringComparison.Ordinal) && c.Contains("1 of the 3", StringComparison.Ordinal));

        // The other two rank exactly as they would have without it.
        Assert.Equal("Good", crossSuite.Leader!.ModelName);
        Assert.Equal(2, crossSuite.Ranked.Count);
    }

    [Fact]
    public void One_usable_suite_is_not_a_cross_suite_comparison()
    {
        var now = DateTime.UtcNow;
        var alpha = Cases("alpha-", 12);

        var runs = Sat("s1", "Alpha suite", "good", "Good", alpha, 0.9, 20, now)
            .Concat(Sat("s1", "Alpha suite", "poor", "Poor", alpha, 0.5, 20, now))
            .ToList();

        var crossSuite = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now).CrossSuiteOrNone;

        Assert.False(crossSuite.HasAnswer);
        Assert.Equal(1, crossSuite.SuiteCount);
        Assert.Contains("single suite", crossSuite.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_suite_whose_models_share_no_cases_reports_a_zero_basis_and_is_excluded_by_name()
    {
        var now = DateTime.UtcNow;
        var alpha = Cases("alpha-", 12);
        var beta = Cases("beta-", 12);

        var runs = Sat("s1", "Alpha suite", "good", "Good", alpha, 0.9, 20, now)
            .Concat(Sat("s1", "Alpha suite", "poor", "Poor", alpha, 0.5, 20, now))
            .Concat(Sat("s2", "Beta suite", "good", "Good", beta, 0.9, 20, now))
            .Concat(Sat("s2", "Beta suite", "poor", "Poor", beta, 0.5, 20, now))
            // Nobody shares a case in this suite: each model sat its own exam.
            .Concat(Sat("s3", "Disjoint suite", "good", "Good", Cases("g-", 12), 0.9, 20, now))
            .Concat(Sat("s3", "Disjoint suite", "poor", "Poor", Cases("p-", 12), 0.5, 20, now))
            .ToList();

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);
        var disjoint = Assert.Single(report.SuiteLeaderboardsOrEmpty, b => b.SuiteId == "s3");

        Assert.Equal(0, disjoint.ComparisonBasisCaseCount);
        Assert.False(disjoint.IsUsable);

        var crossSuite = report.CrossSuiteOrNone;
        Assert.Equal(2, crossSuite.SuiteCount);
        Assert.Contains(crossSuite.Caveats, c => c.Contains("Disjoint suite", StringComparison.Ordinal));
    }

    [Fact]
    public void A_suite_only_one_model_ran_is_not_a_comparison_and_says_so()
    {
        var now = DateTime.UtcNow;
        var alpha = Cases("alpha-", 12);
        var beta = Cases("beta-", 12);
        var solo = Cases("solo-", 12);

        var runs = Sat("s1", "Alpha suite", "good", "Good", alpha, 0.9, 20, now)
            .Concat(Sat("s1", "Alpha suite", "poor", "Poor", alpha, 0.5, 20, now))
            .Concat(Sat("s2", "Beta suite", "good", "Good", beta, 0.9, 20, now))
            .Concat(Sat("s2", "Beta suite", "poor", "Poor", beta, 0.5, 20, now))
            .Concat(Sat("s3", "Solo suite", "good", "Good", solo, 0.9, 20, now))
            .ToList();

        var crossSuite = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now).CrossSuiteOrNone;

        Assert.Equal(2, crossSuite.SuiteCount);
        Assert.Contains(crossSuite.Caveats, c => c.Contains("Solo suite", StringComparison.Ordinal) && c.Contains("one model", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same unfairness r25 removed from the overall board, one level down:
    /// per-suite averaging over ALL runs would hand "Alpha suite" to Easy, and
    /// averaging over the shared set does not. The shared set is the one that
    /// decides, because both boards go through the same code path.
    /// </summary>
    [Fact]
    public void Suite_boards_use_the_same_shared_case_set_rule_as_the_overall_board()
    {
        var now = DateTime.UtcNow;
        var shared = Cases("shared-", 12);
        var hard = Cases("hard-", 12);

        var runs = Sat("s1", "Alpha suite", "easy", "Easy", shared, 0.80, 20, now)
            .Concat(Sat("s1", "Alpha suite", "thorough", "Thorough", shared, 0.95, 20, now))
            // Thorough also attempts the hard cases in the same suite and does
            // badly, which would sink its all-runs average below Easy's.
            .Concat([Run("s1", "Alpha suite", "thorough", "Thorough", hard, 0.10, 20, now)])
            .ToList();

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);
        var board = Assert.Single(report.SuiteLeaderboardsOrEmpty);

        Assert.Equal(12, board.ComparisonBasisCaseCount);
        Assert.Equal("Thorough", board.Ranked[0].ModelName);
        Assert.Equal(0.95, board.Ranked[0].QualityScore, 3);
    }

    [Fact]
    public void A_report_constructed_without_suite_leaderboards_behaves_as_it_did_before()
    {
        var report = new BenchmarkInsightsReport(
            TotalRuns: 3, ComparableRuns: 3, ModelCount: 0,
            OldestComparableRun: null,
            Models: [], TagLeaderboards: [], Comparisons: [], Caveats: []);

        Assert.Empty(report.SuiteLeaderboardsOrEmpty);
        Assert.Null(report.CrossSuite);
        Assert.False(report.CrossSuiteOrNone.HasAnswer);
        Assert.Equal(0, report.CrossSuiteOrNone.SuiteCount);
        Assert.True(report.HasData);
        Assert.Null(report.BestOverall);
    }

    [Fact]
    public void No_runs_at_all_produces_no_cross_suite_answer_rather_than_a_wrong_one()
    {
        var crossSuite = BenchmarkInsightsMath.BuildReport([], [], "0.10.0.0", DateTime.UtcNow).CrossSuiteOrNone;

        Assert.False(crossSuite.HasAnswer);
        Assert.Equal(0, crossSuite.SuiteCount);
        Assert.False(string.IsNullOrWhiteSpace(crossSuite.Explanation));
    }

    [Fact]
    public void Every_placement_names_its_suite_and_the_numbers_behind_it()
    {
        var now = DateTime.UtcNow;
        var alpha = Cases("alpha-", 12);
        var beta = Cases("beta-", 12);

        var runs = Sat("s1", "Alpha suite", "good", "Good", alpha, 0.9, 25, now)
            .Concat(Sat("s1", "Alpha suite", "poor", "Poor", alpha, 0.5, 20, now))
            .Concat(Sat("s2", "Beta suite", "good", "Good", beta, 0.8, 30, now))
            .Concat(Sat("s2", "Beta suite", "poor", "Poor", beta, 0.5, 20, now))
            .ToList();

        var leader = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now).CrossSuiteOrNone.Leader;

        Assert.NotNull(leader);
        var alphaPlacement = Assert.Single(leader!.Placements, p => p.SuiteName == "Alpha suite");
        Assert.Equal(1, alphaPlacement.Position);
        Assert.Equal(0.9, alphaPlacement.QualityScore, 3);
        Assert.Equal(25, alphaPlacement.TokensPerSecond, 1);
    }
}
