namespace Aether.Core.Models;

public sealed record ModelAggregate(
    string ModelId,
    string ModelName,
    string Quantization,
    string RuntimeKind,
    int RunCount,
    int CaseCount,
    double QualityScore,
    double TokensPerSecond,
    double StabilityScore,
    double RankingScore,
    double QualityPerSecond,
    DateTime LastRunAt,
    bool IsStale);

public sealed record TagLeaderboard(string Tag, IReadOnlyList<ModelAggregate> Ranked);

public sealed record ModelComparison(
    string ModelA,
    string ModelB,
    string Tag,
    double QualityDeltaPercent,
    double SpeedRatio,
    string Sentence);

public sealed record BenchmarkInsightsReport(
    int TotalRuns,
    int ComparableRuns,
    int ModelCount,
    DateTime? OldestComparableRun,
    IReadOnlyList<ModelAggregate> Models,
    IReadOnlyList<TagLeaderboard> TagLeaderboards,
    IReadOnlyList<ModelComparison> Comparisons,
    IReadOnlyList<string> Caveats,
    IReadOnlyList<UsageInsight>? UsageInsights = null)
{
    public bool HasData => ComparableRuns > 0;
    public ModelAggregate? BestOverall => Models.Count == 0 ? null : Models[0];
    public IReadOnlyList<UsageInsight> UsageInsightsOrEmpty => UsageInsights ?? [];
}

/// <summary>
/// One activity kind's usage-aware insight (r6 02-usage-history-recommendations.md
/// 2.3): which model the user actually reaches for, and, when benchmark
/// data disagrees enough, a recommendation. Never a recommendation from
/// usage alone - only when a real leaderboard entry outranks the dominant
/// model by more than the same threshold as the r5 Doctor advisory.
/// </summary>
public sealed record UsageInsight(
    TraceKind Kind,
    string DominantModelId,
    string DominantModelName,
    double DominantShare,
    long TotalCalls,
    string? RecommendedModelName,
    double? RankingGapPoints,
    string Sentence);

/// <summary>
/// Deterministic aggregation of benchmark runs into leaderboards and
/// comparisons. No LLM, no network: everything here is reproducible from
/// stored runs. See docs/review/02-benchmark-insights.md.
/// </summary>
public static class BenchmarkInsightsMath
{
    private const int MinRunsForEvidence = 2;
    private const int MinCasesForEvidence = 10;
    private const int StaleAfterDays = 60;
    private const double HardwareVramTolerance = 0.10;
    private const int MinCallsForUsageInsight = 20;
    private const double UsageRankingGapThreshold = 10;

    /// <summary>
    /// The one file of scoring truth for which tag leaderboard (or overall,
    /// for empty string) a usage kind's recommendation compares against.
    /// LocalApi is deliberately absent - it is not one of the three named
    /// activities in r6 02-usage-history-recommendations.md 2.3 and has no
    /// natural benchmark tag to compare against.
    /// </summary>
    private static readonly IReadOnlyDictionary<TraceKind, (string Tag, string ActivityLabel)> UsageKindTags =
        new Dictionary<TraceKind, (string, string)>
        {
            [TraceKind.Chat] = (string.Empty, "chat"),
            [TraceKind.Rag] = ("rag", "RAG queries"),
            [TraceKind.Agent] = ("coding", "agent tasks")
        };

    public static double QualityPerSecond(double quality, double tokensPerSecond) =>
        quality * Math.Log2(1 + Math.Max(0, tokensPerSecond));

    /// <summary>
    /// Two runs' hardware snapshots are comparable when the primary GPU
    /// matches by name and total VRAM within 10%, or both ran CPU-only.
    /// </summary>
    public static bool IsHardwareComparable(SystemSnapshot a, SystemSnapshot b)
    {
        var gpuA = a.Gpus.FirstOrDefault();
        var gpuB = b.Gpus.FirstOrDefault();
        if (gpuA is null && gpuB is null)
            return true;
        if (gpuA is null || gpuB is null)
            return false;
        if (!string.Equals(gpuA.Name, gpuB.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (gpuA.MemoryTotalBytes is null || gpuB.MemoryTotalBytes is null)
            return true;

        var diff = Math.Abs(gpuA.MemoryTotalBytes.Value - gpuB.MemoryTotalBytes.Value);
        var avg = (gpuA.MemoryTotalBytes.Value + gpuB.MemoryTotalBytes.Value) / 2.0;
        return avg <= 0 || diff / avg <= HardwareVramTolerance;
    }

    public static string GroupKey(BenchmarkRun run) =>
        $"{run.ModelId}|{run.Metadata.Quantization}|{run.Metadata.RuntimeKind}";

    public static ModelComparison Compare(ModelAggregate a, ModelAggregate b, string tag = "")
    {
        var qualityDeltaPercent = b.QualityScore <= 0
            ? 0
            : Math.Round((a.QualityScore - b.QualityScore) / b.QualityScore * 100, 0, MidpointRounding.AwayFromZero);
        var speedRatio = b.TokensPerSecond <= 0
            ? 0
            : Math.Round(a.TokensPerSecond / b.TokensPerSecond, 1);
        return new ModelComparison(a.ModelName, b.ModelName, tag, qualityDeltaPercent, speedRatio,
            BuildSentence(a.ModelName, b.ModelName, qualityDeltaPercent, speedRatio));
    }

    private static string BuildSentence(string a, string b, double qualityDeltaPercent, double speedRatio)
    {
        var qualityPhrase = Math.Abs(qualityDeltaPercent) < 1
            ? $"matches {b}'s quality"
            : qualityDeltaPercent > 0
                ? $"scores {Math.Abs(qualityDeltaPercent):F0}% higher than {b}"
                : $"scores {Math.Abs(qualityDeltaPercent):F0}% lower than {b}";

        var speedPhrase = speedRatio switch
        {
            <= 0 => "runs at a comparable speed",
            >= 1 => $"runs {speedRatio:F1}x faster",
            _ => $"runs {1 / speedRatio:F1}x slower"
        };

        return $"{a} {qualityPhrase} but {speedPhrase}.";
    }

    /// <summary>
    /// Builds the full report from a set of runs already filtered to the
    /// current hardware (<paramref name="comparableRuns"/>) plus the
    /// unfiltered total (<paramref name="allRuns"/>) for the caveat count.
    /// Every <see cref="BenchmarkResult"/> in <paramref name="comparableRuns"/>
    /// is assumed to already carry resolved tags (suite-join fallback is the
    /// caller's job; this stays a pure function of the runs it is given).
    /// </summary>
    public static BenchmarkInsightsReport BuildReport(
        IReadOnlyList<BenchmarkRun> allRuns,
        IReadOnlyList<BenchmarkRun> comparableRuns,
        string currentAppVersion,
        DateTime? nowUtc = null,
        IReadOnlyList<KindUsageSummary>? usage = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var caveats = new List<string>();
        var foreignCount = Math.Max(0, allRuns.Count - comparableRuns.Count);
        if (foreignCount > 0)
            caveats.Add($"{foreignCount} run(s) from different hardware were ignored.");

        var aggregates = BuildModelAggregates(comparableRuns, now, currentAppVersion, caveats);
        var overallRanked = aggregates.OrderByDescending(a => a.QualityPerSecond).ToList();
        var tagLeaderboards = BuildTagLeaderboards(comparableRuns);

        var comparisons = BuildComparisons(overallRanked, string.Empty)
            .Concat(tagLeaderboards.SelectMany(t => BuildComparisons(t.Ranked, t.Tag)))
            .ToList();

        var usageInsights = BuildUsageInsights(usage, overallRanked, tagLeaderboards);

        return new BenchmarkInsightsReport(
            allRuns.Count,
            comparableRuns.Count,
            overallRanked.Count,
            comparableRuns.Count == 0 ? null : comparableRuns.Min(r => r.StartedAt),
            overallRanked,
            tagLeaderboards,
            comparisons,
            caveats,
            usageInsights);
    }

    private static List<UsageInsight> BuildUsageInsights(
        IReadOnlyList<KindUsageSummary>? usage,
        IReadOnlyList<ModelAggregate> overallRanked,
        IReadOnlyList<TagLeaderboard> tagLeaderboards)
    {
        var insights = new List<UsageInsight>();
        if (usage is null)
            return insights;

        foreach (var summary in usage)
        {
            if (summary.TotalCalls < MinCallsForUsageInsight)
                continue;
            if (!UsageKindTags.TryGetValue(summary.Kind, out var mapping))
                continue;
            var dominant = summary.Dominant;
            if (dominant is null)
                continue;

            var leaderboardTop = string.IsNullOrEmpty(mapping.Tag)
                ? overallRanked.FirstOrDefault()
                : tagLeaderboards.FirstOrDefault(t => string.Equals(t.Tag, mapping.Tag, StringComparison.OrdinalIgnoreCase))?.Ranked.FirstOrDefault();

            string? recommendedName = null;
            double? gap = null;
            var sentence = $"You mostly use {dominant.ModelId} for {mapping.ActivityLabel}.";

            if (leaderboardTop is not null && !string.Equals(leaderboardTop.ModelId, dominant.ModelId, StringComparison.OrdinalIgnoreCase))
            {
                var dominantAggregate = overallRanked.FirstOrDefault(a => string.Equals(a.ModelId, dominant.ModelId, StringComparison.OrdinalIgnoreCase))
                    ?? tagLeaderboards.SelectMany(t => t.Ranked).FirstOrDefault(a => string.Equals(a.ModelId, dominant.ModelId, StringComparison.OrdinalIgnoreCase));
                if (dominantAggregate is not null)
                {
                    var points = (leaderboardTop.RankingScore - dominantAggregate.RankingScore) * 100;
                    if (points > UsageRankingGapThreshold)
                    {
                        recommendedName = leaderboardTop.ModelName;
                        gap = points;
                        var tagLabel = string.IsNullOrEmpty(mapping.Tag) ? "overall" : $"{mapping.Tag}";
                        sentence = $"You mostly use {dominant.ModelId} for {mapping.ActivityLabel}; your benchmarks rank {leaderboardTop.ModelName} higher for {tagLabel} tasks.";
                    }
                }
            }

            insights.Add(new UsageInsight(
                summary.Kind, dominant.ModelId, dominant.ModelId, dominant.Share, summary.TotalCalls,
                recommendedName, gap, sentence));
        }

        return insights;
    }

    private static List<ModelAggregate> BuildModelAggregates(
        IReadOnlyList<BenchmarkRun> comparableRuns, DateTime now, string currentAppVersion, List<string> caveats)
    {
        var aggregates = new List<ModelAggregate>();
        var groups = comparableRuns.Where(r => r.Results.Count > 0).GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var runs = group.ToList();
            var runCount = runs.Count;
            var caseCount = runs.Sum(r => r.Total);
            var latest = runs.OrderByDescending(r => r.StartedAt).First();

            if (runCount < MinRunsForEvidence || caseCount < MinCasesForEvidence)
            {
                caveats.Add(BuildShortfallCaveat(latest.ModelName, runCount, caseCount));
                continue;
            }

            double WeightedAvg(Func<BenchmarkRun, double> selector) =>
                runs.Sum(r => selector(r) * r.Total) / caseCount;

            var quality = WeightedAvg(r => r.QualityScore);
            var tokensPerSecond = WeightedAvg(r => r.AverageApproxTokensPerSecond);
            var stability = WeightedAvg(r => r.StabilityScore);
            var ranking = WeightedAvg(r => r.RankingScore);
            var lastRunAt = runs.Max(r => r.StartedAt);
            var isStale = (now - lastRunAt).TotalDays > StaleAfterDays
                || !VersionMajorMinorMatches(latest.Metadata.AetherVersion, currentAppVersion);

            aggregates.Add(new ModelAggregate(
                latest.ModelId, latest.ModelName, latest.Metadata.Quantization, latest.Metadata.RuntimeKind,
                runCount, caseCount, Math.Round(quality, 4), Math.Round(tokensPerSecond, 2), Math.Round(stability, 4),
                Math.Round(ranking, 4), Math.Round(QualityPerSecond(quality, tokensPerSecond), 4), lastRunAt, isStale));
        }

        return aggregates;
    }

    private static List<TagLeaderboard> BuildTagLeaderboards(IReadOnlyList<BenchmarkRun> comparableRuns)
    {
        var tags = comparableRuns.SelectMany(r => r.Results.SelectMany(res => res.Tags))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var leaderboards = new List<TagLeaderboard>();
        foreach (var tag in tags)
        {
            var perModel = new List<ModelAggregate>();
            foreach (var group in comparableRuns.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase))
            {
                var tagged = group
                    .SelectMany(r => r.Results.Where(res => res.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                        .Select(res => (Run: r, Result: res)))
                    .ToList();
                var caseCount = tagged.Count;
                var runCount = tagged.Select(t => t.Run.Id).Distinct().Count();
                if (runCount < MinRunsForEvidence || caseCount < MinCasesForEvidence)
                    continue;

                var quality = tagged.Average(t => t.Result.QualityScore);
                var tokensPerSecond = tagged.Average(t => t.Result.ApproxTokensPerSecond);
                var resource = tagged.Average(t => t.Result.ResourceScore);
                var stability = tagged.Count(t => !t.Result.HasError && !t.Result.TimedOut && !t.Result.Cancelled) / (double)caseCount;
                var latest = tagged.OrderByDescending(t => t.Run.StartedAt).First().Run;
                var lastRunAt = tagged.Max(t => t.Run.StartedAt);
                var ranking = BenchmarkScoring.RankingScore(quality, tokensPerSecond, stability, resource);

                perModel.Add(new ModelAggregate(
                    latest.ModelId, latest.ModelName, latest.Metadata.Quantization, latest.Metadata.RuntimeKind,
                    runCount, caseCount, Math.Round(quality, 4), Math.Round(tokensPerSecond, 2), Math.Round(stability, 4),
                    ranking, Math.Round(QualityPerSecond(quality, tokensPerSecond), 4), lastRunAt, false));
            }

            if (perModel.Count == 0)
                continue;

            leaderboards.Add(new TagLeaderboard(tag, perModel.OrderByDescending(a => a.QualityPerSecond).Take(3).ToList()));
        }

        return leaderboards;
    }

    private static List<ModelComparison> BuildComparisons(IReadOnlyList<ModelAggregate> ranked, string tag)
    {
        var top = ranked.Take(3).ToList();
        var comparisons = new List<ModelComparison>();
        for (var i = 0; i < top.Count; i++)
            for (var j = i + 1; j < top.Count; j++)
                comparisons.Add(Compare(top[i], top[j], tag));
        return comparisons;
    }

    private static string BuildShortfallCaveat(string modelName, int runCount, int caseCount)
    {
        var shortRuns = Math.Max(0, MinRunsForEvidence - runCount);
        var shortCases = Math.Max(0, MinCasesForEvidence - caseCount);
        var need = shortRuns > 0 && shortCases > 0
            ? $"{shortRuns} more run(s) and {shortCases} more case(s) needed"
            : shortRuns > 0
                ? $"{shortRuns} more run(s) needed"
                : $"{shortCases} more case(s) needed";
        return $"{modelName} needs more data: {need}.";
    }

    private static bool VersionMajorMinorMatches(string runVersion, string currentVersion)
    {
        var run = MajorMinor(runVersion);
        var current = MajorMinor(currentVersion);
        return run is null || current is null || run == current;
    }

    private static (int Major, int Minor)? MajorMinor(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        var core = version.Split('-', '+')[0];
        var parts = core.Split('.');
        return parts.Length >= 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor)
            ? (major, minor)
            : null;
    }
}
