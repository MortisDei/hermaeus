using Hermaeus.Core.Models;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r25 doc 04 4.1: "Best overall" used to average each model over whatever cases
/// that model happened to have run, with no requirement that two models sat the
/// same exam. The only gate was a volume floor (2 runs, 10 cases), so a model
/// that ran one short easy suite could outrank a model that ran everything.
/// </summary>
public sealed class BenchmarkCommonCaseSetTests
{
    /// <summary>Builds a run over explicitly named cases, so a fixture can express
    /// "these two models sat different exams".</summary>
    private static BenchmarkRun Run(
        string modelId, string modelName, IEnumerable<string> caseIds,
        double quality, double tokensPerSecond, DateTime startedAt, string appVersion = "0.10.0.0")
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
            ModelId = modelId,
            ModelName = modelName,
            StartedAt = startedAt,
            Metadata = new BenchmarkRunMetadata { Quantization = "", RuntimeKind = "llama.cpp", AppVersion = appVersion },
            Results = results
        };
    }

    private static string[] Cases(string prefix, int count) =>
        Enumerable.Range(0, count).Select(i => $"{prefix}{i}").ToArray();

    /// <summary>
    /// The exact unfairness the owner reported. "Easy" ran twelve easy cases and
    /// scores perfectly on them; "Thorough" ran those twelve plus twelve hard
    /// ones it does badly at. Ranking over each model's own cases hands the win
    /// to Easy; ranking over the twelve they share does not.
    /// </summary>
    [Fact]
    public void Ranking_uses_only_cases_every_ranked_model_has_run()
    {
        var now = DateTime.UtcNow;
        var shared = Cases("shared-", 12);
        var hard = Cases("hard-", 12);

        var runs = new[]
        {
            Run("easy", "Easy", shared, quality: 0.80, tokensPerSecond: 20, now.AddDays(-1)),
            Run("easy", "Easy", shared, quality: 0.80, tokensPerSecond: 20, now),
            // Beats Easy on the shared cases, but drags its own overall average
            // down by also attempting the hard ones.
            Run("thorough", "Thorough", shared, quality: 0.95, tokensPerSecond: 20, now.AddDays(-1)),
            Run("thorough", "Thorough", shared.Concat(hard), quality: 0.95, tokensPerSecond: 20, now),
            Run("thorough", "Thorough", hard, quality: 0.10, tokensPerSecond: 20, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Equal(12, report.ComparisonBasisCaseCount);
        Assert.NotNull(report.BestOverall);
        Assert.Equal("Thorough", report.BestOverall!.ModelName);

        // And it did not gain or lose from the cases the other model never ran.
        Assert.Equal(12, report.BestOverall.ComparedCaseCount);
        Assert.Equal(0.95, report.BestOverall.QualityScore, 3);
    }

    [Fact]
    public void Two_models_with_disjoint_case_sets_produce_no_winner_and_a_caveat()
    {
        var now = DateTime.UtcNow;
        var runs = new[]
        {
            Run("a", "Model A", Cases("a-", 12), 0.9, 20, now.AddDays(-1)),
            Run("a", "Model A", Cases("a-", 12), 0.9, 20, now),
            Run("b", "Model B", Cases("b-", 12), 0.5, 20, now.AddDays(-1)),
            Run("b", "Model B", Cases("b-", 12), 0.5, 20, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Null(report.BestOverall);
        Assert.Equal(0, report.ComparisonBasisCaseCount);
        Assert.Empty(report.Models);
        Assert.Contains(report.Caveats, c => c.Contains("same cases", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A single sparsely-benchmarked model must not flatten the whole
    /// leaderboard: the two models that do share an exam are still ranked, and
    /// the third is reported as not comparable yet.
    /// </summary>
    [Fact]
    public void A_sparse_model_is_excluded_rather_than_destroying_the_leaderboard()
    {
        var now = DateTime.UtcNow;
        var shared = Cases("shared-", 14);

        var runs = new[]
        {
            Run("a", "Model A", shared, 0.9, 20, now.AddDays(-1)),
            Run("a", "Model A", shared, 0.9, 20, now),
            Run("b", "Model B", shared, 0.6, 20, now.AddDays(-1)),
            Run("b", "Model B", shared, 0.6, 20, now),
            // Cleared the volume floor on its own cases, but shares almost nothing.
            Run("c", "Model C", shared.Take(2).Concat(Cases("other-", 10)), 0.99, 40, now.AddDays(-1)),
            Run("c", "Model C", shared.Take(2).Concat(Cases("other-", 10)), 0.99, 40, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Equal(14, report.ComparisonBasisCaseCount);
        Assert.Equal(2, report.Models.Count);
        Assert.Equal("Model A", report.BestOverall!.ModelName);
        Assert.DoesNotContain(report.Models, m => m.ModelName == "Model C");
        Assert.Contains(report.Caveats, c => c.Contains("Model C") && c.Contains("not comparable"));
    }

    /// <summary>
    /// CaseVersion is part of the comparison key: scoring model A on v1 of a case
    /// against model B on v2 is the same unfairness one level down.
    /// </summary>
    [Fact]
    public void A_case_run_at_different_versions_is_not_treated_as_shared()
    {
        var now = DateTime.UtcNow;

        static BenchmarkRun Versioned(string modelId, string name, string version, DateTime at) => new()
        {
            ModelId = modelId,
            ModelName = name,
            StartedAt = at,
            Metadata = new BenchmarkRunMetadata { Quantization = "", RuntimeKind = "llama.cpp", AppVersion = "0.10.0.0" },
            Results = Enumerable.Range(0, 12).Select(i => new BenchmarkResult
            {
                CaseId = $"case-{i}",
                CaseName = $"Case {i}",
                CaseVersion = version,
                QualityScore = 0.8,
                ApproxTokensPerSecond = 20,
                ResourceScore = 1
            }).ToList()
        };

        var runs = new[]
        {
            Versioned("a", "Model A", "v1", now.AddDays(-1)),
            Versioned("a", "Model A", "v1", now),
            Versioned("b", "Model B", "v2", now.AddDays(-1)),
            Versioned("b", "Model B", "v2", now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Null(report.BestOverall);
        Assert.Equal(0, report.ComparisonBasisCaseCount);
    }

    /// <summary>
    /// One model has nothing to be compared against, so the shared-exam
    /// requirement does not apply. Suppressing its card would be a regression
    /// for the common case of benchmarking a single model.
    /// </summary>
    [Fact]
    public void A_single_model_is_still_ranked_over_its_own_cases()
    {
        var now = DateTime.UtcNow;
        var runs = new[]
        {
            Run("solo", "Solo", Cases("c-", 12), 0.75, 18, now.AddDays(-1)),
            Run("solo", "Solo", Cases("c-", 12), 0.75, 18, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.NotNull(report.BestOverall);
        Assert.Equal("Solo", report.BestOverall!.ModelName);
        Assert.Equal(12, report.ComparisonBasisCaseCount);
    }

    /// <summary>
    /// r25 doc 04 4.3: QualityPerSecond blends quality with speed, so the overall
    /// leader can be second on quality. That is the most decision-relevant fact
    /// on the page and it was invisible before r25.
    /// </summary>
    [Fact]
    public void The_quality_leader_is_flagged_when_it_is_not_the_blend_leader()
    {
        var now = DateTime.UtcNow;
        var shared = Cases("shared-", 12);

        var runs = new[]
        {
            Run("fast", "Fast", shared, quality: 0.80, tokensPerSecond: 60, now.AddDays(-1)),
            Run("fast", "Fast", shared, quality: 0.80, tokensPerSecond: 60, now),
            Run("careful", "Careful", shared, quality: 0.95, tokensPerSecond: 6, now.AddDays(-1)),
            Run("careful", "Careful", shared, quality: 0.95, tokensPerSecond: 6, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Equal("Fast", report.BestOverall!.ModelName);
        Assert.Equal("Careful", report.QualityLeader!.ModelName);
        Assert.True(report.QualityLeaderDiffersFromBest);
    }

    [Fact]
    public void The_quality_leader_is_not_flagged_when_it_is_also_the_blend_leader()
    {
        var now = DateTime.UtcNow;
        var shared = Cases("shared-", 12);

        var runs = new[]
        {
            Run("good", "Good", shared, quality: 0.95, tokensPerSecond: 30, now.AddDays(-1)),
            Run("good", "Good", shared, quality: 0.95, tokensPerSecond: 30, now),
            Run("worse", "Worse", shared, quality: 0.50, tokensPerSecond: 10, now.AddDays(-1)),
            Run("worse", "Worse", shared, quality: 0.50, tokensPerSecond: 10, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Equal("Good", report.BestOverall!.ModelName);
        Assert.False(report.QualityLeaderDiffersFromBest);
    }

    /// <summary>r25 doc 04 4.2: the drill-down shows the same numbers the ranking used.</summary>
    [Fact]
    public void Per_case_rows_are_attached_to_each_ranked_aggregate()
    {
        var now = DateTime.UtcNow;
        var shared = Cases("shared-", 12);
        var runs = new[]
        {
            Run("a", "Model A", shared, 0.9, 20, now.AddDays(-1)),
            Run("a", "Model A", shared, 0.9, 20, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);
        var cases = report.BestOverall!.CasesOrEmpty;

        Assert.Equal(12, cases.Count);
        Assert.All(cases, c => Assert.Equal(0.9, c.QualityScore, 3));
        Assert.All(cases, c => Assert.True(c.Succeeded));
        // Deterministic ordering, so the breakdown does not reshuffle between loads.
        Assert.Equal(cases.Select(c => c.CaseName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase), cases.Select(c => c.CaseName));
    }

    [Fact]
    public void A_model_below_the_volume_floor_still_gets_its_shortfall_caveat()
    {
        var now = DateTime.UtcNow;
        var runs = new[] { Run("a", "Model A", Cases("c-", 20), 0.9, 20, now) };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Empty(report.Models);
        Assert.Null(report.BestOverall);
        Assert.Contains(report.Caveats, c => c.Contains("Model A") && c.Contains("more run"));
    }
}
