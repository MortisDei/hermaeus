using Hermaeus.Core.Models;
using Xunit;

namespace Hermaeus.Tests;

public sealed class BenchmarkInsightsMathTests
{
    private static BenchmarkRun BuildRun(
        string modelId, string modelName, string quantization, string runtimeKind,
        int caseCount, double quality, double tokensPerSecond, DateTime startedAt, string appVersion = "0.10.0.0")
    {
        var results = new List<BenchmarkResult>();
        for (var i = 0; i < caseCount; i++)
            results.Add(new BenchmarkResult { QualityScore = quality, ApproxTokensPerSecond = tokensPerSecond, ResourceScore = 1 });

        return new BenchmarkRun
        {
            ModelId = modelId,
            ModelName = modelName,
            StartedAt = startedAt,
            Metadata = new BenchmarkRunMetadata { Quantization = quantization, RuntimeKind = runtimeKind, AppVersion = appVersion },
            Results = results
        };
    }

    [Fact]
    public void BuildReport_groups_same_model_by_quantization_separately()
    {
        var now = DateTime.UtcNow;
        var runs = new[]
        {
            BuildRun("model-a", "Model A", "Q4", "llama.cpp", 10, 0.8, 20, now.AddDays(-1)),
            BuildRun("model-a", "Model A", "Q4", "llama.cpp", 10, 0.8, 20, now),
            BuildRun("model-a", "Model A", "Q8", "llama.cpp", 10, 0.9, 15, now.AddDays(-1)),
            BuildRun("model-a", "Model A", "Q8", "llama.cpp", 10, 0.9, 15, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Equal(2, report.Models.Count);
        Assert.Contains(report.Models, m => m.Quantization == "Q4");
        Assert.Contains(report.Models, m => m.Quantization == "Q8");
    }

    [Fact]
    public void BuildReport_excludes_models_with_only_one_run_and_records_a_caveat()
    {
        var now = DateTime.UtcNow;
        var runs = new[] { BuildRun("model-a", "Model A", "", "", 20, 0.8, 20, now) };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Empty(report.Models);
        Assert.Contains(report.Caveats, c => c.Contains("Model A"));
    }

    [Fact]
    public void BuildReport_excludes_models_below_the_case_count_floor()
    {
        var now = DateTime.UtcNow;
        var runs = new[]
        {
            BuildRun("model-a", "Model A", "", "", 3, 0.8, 20, now.AddDays(-1)),
            BuildRun("model-a", "Model A", "", "", 3, 0.8, 20, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Empty(report.Models);
    }

    [Fact]
    public void BuildReport_weights_model_aggregate_quality_by_case_count()
    {
        var now = DateTime.UtcNow;
        var runs = new[]
        {
            BuildRun("model-a", "Model A", "", "", 40, 0.9, 20, now.AddDays(-1)),
            BuildRun("model-a", "Model A", "", "", 10, 0.5, 20, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now);

        Assert.Single(report.Models);
        Assert.Equal(0.82, report.Models[0].QualityScore, precision: 4);
    }

    [Fact]
    public void BuildReport_flags_stale_when_last_run_is_old_or_a_different_minor_version()
    {
        var now = DateTime.UtcNow;
        var oldRuns = new[]
        {
            BuildRun("model-a", "Model A", "", "", 10, 0.8, 20, now.AddDays(-90)),
            BuildRun("model-a", "Model A", "", "", 10, 0.8, 20, now.AddDays(-91))
        };
        var freshRuns = new[]
        {
            BuildRun("model-b", "Model B", "", "", 10, 0.8, 20, now.AddDays(-1)),
            BuildRun("model-b", "Model B", "", "", 10, 0.8, 20, now)
        };
        var all = oldRuns.Concat(freshRuns).ToList();

        var report = BenchmarkInsightsMath.BuildReport(all, all, "0.10.0.0", now);

        Assert.True(report.Models.First(m => m.ModelId == "model-a").IsStale);
        Assert.False(report.Models.First(m => m.ModelId == "model-b").IsStale);
    }

    [Fact]
    public void BuildReport_records_a_caveat_for_runs_excluded_by_the_hardware_filter()
    {
        var now = DateTime.UtcNow;
        var comparable = new[]
        {
            BuildRun("model-a", "Model A", "", "", 10, 0.8, 20, now.AddDays(-1)),
            BuildRun("model-a", "Model A", "", "", 10, 0.8, 20, now)
        };
        var all = comparable.Append(BuildRun("model-b", "Model B", "", "", 10, 0.7, 10, now)).ToList();

        var report = BenchmarkInsightsMath.BuildReport(all, comparable, "0.10.0.0", now);

        Assert.Contains(report.Caveats, c => c.Contains("1 run(s) from different hardware"));
    }

    [Fact]
    public void QualityPerSecond_rewards_speed_without_it_buying_past_a_large_quality_gap()
    {
        var betterQualitySlower = BenchmarkInsightsMath.QualityPerSecond(0.80, 20);
        var worseQualityFaster = BenchmarkInsightsMath.QualityPerSecond(0.84, 6);
        Assert.True(betterQualitySlower > worseQualityFaster);

        var strongQualityModestSpeed = BenchmarkInsightsMath.QualityPerSecond(0.85, 15);
        var weakQualityHighSpeed = BenchmarkInsightsMath.QualityPerSecond(0.40, 80);
        Assert.True(strongQualityModestSpeed > weakQualityHighSpeed);
    }

    [Fact]
    public void Compare_produces_the_expected_sentence_for_known_inputs()
    {
        var a = new ModelAggregate("id-a", "Model A", "", "",
            RunCount: 2, CaseCount: 20, QualityScore: 0.80, TokensPerSecond: 46, StabilityScore: 1,
            RankingScore: 0, QualityPerSecond: 0, LastRunAt: DateTime.UtcNow, IsStale: false);
        var b = new ModelAggregate("id-b", "Model B", "", "",
            RunCount: 2, CaseCount: 20, QualityScore: 0.833, TokensPerSecond: 20, StabilityScore: 1,
            RankingScore: 0, QualityPerSecond: 0, LastRunAt: DateTime.UtcNow, IsStale: false);

        var comparison = BenchmarkInsightsMath.Compare(a, b);

        Assert.Equal(-4, comparison.QualityDeltaPercent);
        Assert.Equal(2.3, comparison.SpeedRatio);
        Assert.Equal("Model A scores 4% lower than Model B but runs 2.3x faster.", comparison.Sentence);
    }

    [Theory]
    [InlineData("RTX 4060", 8_000_000_000, "RTX 4060", 8_200_000_000, true)]
    [InlineData("RTX 4060", 8_000_000_000, "RTX 3080", 10_000_000_000, false)]
    [InlineData("RTX 4060", 8_000_000_000, "RTX 4060", 20_000_000_000, false)]
    public void IsHardwareComparable_matches_gpu_name_and_vram_within_tolerance(
        string nameA, long vramA, string nameB, long vramB, bool expected)
    {
        var a = new SystemSnapshot { Gpus = [new GpuInfo { Name = nameA, MemoryTotalBytes = vramA }] };
        var b = new SystemSnapshot { Gpus = [new GpuInfo { Name = nameB, MemoryTotalBytes = vramB }] };

        Assert.Equal(expected, BenchmarkInsightsMath.IsHardwareComparable(a, b));
    }

    [Fact]
    public void IsHardwareComparable_treats_two_cpu_only_snapshots_as_comparable()
    {
        Assert.True(BenchmarkInsightsMath.IsHardwareComparable(new SystemSnapshot(), new SystemSnapshot()));
    }

    [Fact]
    public void IsHardwareComparable_rejects_gpu_snapshot_against_cpu_only()
    {
        var withGpu = new SystemSnapshot { Gpus = [new GpuInfo { Name = "RTX 4060", MemoryTotalBytes = 8_000_000_000 }] };
        Assert.False(BenchmarkInsightsMath.IsHardwareComparable(withGpu, new SystemSnapshot()));
    }

    // r6 02-usage-history-recommendations.md 2.3: usage-aware insights.

    private static KindUsageSummary ChatUsage(int callCount, string dominantModelId) =>
        new(TraceKind.Chat, callCount, [new ModelUsageShare(dominantModelId, callCount, callCount * 100, 1.0)]);

    [Fact]
    public void BuildReport_omits_usage_insight_below_the_call_floor()
    {
        var now = DateTime.UtcNow;
        var runs = new[]
        {
            BuildRun("model-a", "Model A", "", "", 10, 0.8, 20, now.AddDays(-1)),
            BuildRun("model-a", "Model A", "", "", 10, 0.8, 20, now)
        };

        var below = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now, usage: [ChatUsage(19, "model-a")]);
        Assert.Empty(below.UsageInsightsOrEmpty);

        var atFloor = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now, usage: [ChatUsage(20, "model-a")]);
        Assert.Single(atFloor.UsageInsightsOrEmpty);
    }

    [Fact]
    public void BuildReport_usage_insight_has_no_recommendation_when_dominant_model_is_already_the_leaderboard_top()
    {
        var now = DateTime.UtcNow;
        var runs = new[]
        {
            BuildRun("model-a", "Model A", "", "", 10, 0.9, 40, now.AddDays(-1)),
            BuildRun("model-a", "Model A", "", "", 10, 0.9, 40, now),
            BuildRun("model-b", "Model B", "", "", 10, 0.3, 5, now.AddDays(-1)),
            BuildRun("model-b", "Model B", "", "", 10, 0.3, 5, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now, usage: [ChatUsage(30, "model-a")]);

        var insight = Assert.Single(report.UsageInsightsOrEmpty);
        Assert.Null(insight.RecommendedModelName);
        Assert.Contains("model-a", insight.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain(";", insight.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReport_usage_insight_recommends_a_switch_when_the_leaderboard_top_clears_the_gap_threshold()
    {
        var now = DateTime.UtcNow;
        var runs = new[]
        {
            BuildRun("model-a", "Dominant Model", "", "", 10, 0.5, 10, now.AddDays(-1)),
            BuildRun("model-a", "Dominant Model", "", "", 10, 0.5, 10, now),
            BuildRun("model-b", "Better Model", "", "", 10, 0.95, 60, now.AddDays(-1)),
            BuildRun("model-b", "Better Model", "", "", 10, 0.95, 60, now)
        };

        var report = BenchmarkInsightsMath.BuildReport(runs, runs, "0.10.0.0", now, usage: [ChatUsage(25, "model-a")]);

        var insight = Assert.Single(report.UsageInsightsOrEmpty);
        Assert.Equal("Better Model", insight.RecommendedModelName);
        Assert.True(insight.RankingGapPoints > 10);
        Assert.Contains("model-a", insight.Sentence, StringComparison.Ordinal);
        Assert.Contains("Better Model", insight.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReport_usage_insight_describes_usage_without_recommendation_when_there_is_no_benchmark_data()
    {
        var report = BenchmarkInsightsMath.BuildReport([], [], "0.10.0.0", DateTime.UtcNow, usage: [ChatUsage(50, "model-a")]);

        var insight = Assert.Single(report.UsageInsightsOrEmpty);
        Assert.Null(insight.RecommendedModelName);
        Assert.Contains("model-a", insight.Sentence, StringComparison.Ordinal);
    }
}
