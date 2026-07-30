namespace Hermaeus.Core.Models;

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
    bool IsStale,
    /// <summary>
    /// r25 doc 04: how many cases this aggregate was actually scored over. For
    /// the overall ranking this is the size of the common case set, not
    /// everything the model happened to run, so the number on screen is the
    /// number the comparison rests on.
    /// </summary>
    int ComparedCaseCount = 0,
    /// <summary>Per-case rows behind this aggregate, for the drill-down (r25 doc 04 4.2).</summary>
    IReadOnlyList<ModelCaseResult>? Cases = null)
{
    public IReadOnlyList<ModelCaseResult> CasesOrEmpty => Cases ?? [];
}

/// <summary>
/// One case's contribution to a <see cref="ModelAggregate"/> (r25 doc 04 4.2):
/// what the model scored on a specific case, so "best overall" can be checked
/// rather than taken on trust.
/// </summary>
public sealed record ModelCaseResult(
    string CaseId,
    string CaseName,
    string CaseVersion,
    IReadOnlyList<string> Tags,
    double QualityScore,
    double TokensPerSecond,
    bool Succeeded);

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
    IReadOnlyList<UsageInsight>? UsageInsights = null,
    /// <summary>
    /// r25 doc 04 4.1: how many cases every ranked model actually ran. The
    /// overall ranking rests on exactly these cases, and 0 means there was no
    /// shared exam to rank on.
    /// </summary>
    int ComparisonBasisCaseCount = 0)
{
    public bool HasData => ComparableRuns > 0;

    /// <summary>
    /// r25 doc 04 4.1: null when there is no shared case set big enough to name
    /// a winner honestly.
    ///
    /// Before r25 this was simply <c>Models[0]</c> over per-model averages taken
    /// across whatever cases each model happened to have run, with no
    /// requirement that two models sat the same exam: a model that ran one
    /// short, easy suite could outrank a model that ran everything. An honest
    /// "not enough shared results" is worth more than a confident wrong answer.
    /// </summary>
    public ModelAggregate? BestOverall =>
        Models.Count == 0 || ComparisonBasisCaseCount <= 0 ? null : Models[0];

    /// <summary>
    /// r25 doc 04 4.3: <see cref="ModelAggregate.QualityPerSecond"/> is a blend
    /// of quality and speed, so the overall leader can be second on quality.
    /// That is the most decision-relevant fact on the page and it was invisible
    /// before r25.
    /// </summary>
    public ModelAggregate? QualityLeader => Models.Count == 0
        ? null
        : Models.OrderByDescending(m => m.QualityScore)
            .ThenBy(m => m.ModelId, StringComparer.Ordinal)
            .First();

    public bool QualityLeaderDiffersFromBest =>
        BestOverall is not null && QualityLeader is not null &&
        !string.Equals(QualityLeader.ModelId, BestOverall.ModelId, StringComparison.OrdinalIgnoreCase);

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

        var (overallRanked, basisCaseCount) =
            BuildOverallRanking(comparableRuns, now, currentAppVersion, caveats);
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
            usageInsights,
            basisCaseCount);
    }

    /// <summary>A case's comparison identity. <see cref="BenchmarkResult.CaseVersion"/> is part of
    /// the key deliberately: scoring model A on v1 of a case against model B on v2 is the same
    /// unfairness one level down, and the field exists precisely so this is checkable.</summary>
    private readonly record struct CaseKey(string CaseId, string CaseVersion);

    /// <summary>
    /// Falls back to <see cref="BenchmarkResult.CaseName"/> when
    /// <see cref="BenchmarkResult.CaseId"/> is absent, so a run recorded before
    /// case ids were stored still contributes an identity rather than collapsing
    /// every case into one key.
    /// </summary>
    private static CaseKey KeyOf(BenchmarkResult result)
    {
        var id = string.IsNullOrWhiteSpace(result.CaseId) ? result.CaseName : result.CaseId;
        return new CaseKey(id ?? string.Empty, result.CaseVersion ?? string.Empty);
    }

    private sealed class ModelGroup
    {
        public required string Key { get; init; }
        public required List<BenchmarkRun> Runs { get; init; }
        public required List<(BenchmarkRun Run, BenchmarkResult Result)> Results { get; init; }
        public required HashSet<CaseKey> CaseKeys { get; init; }
    }

    /// <summary>
    /// r25 doc 04 4.1: rank the overall board on cases every ranked model
    /// actually ran, instead of averaging each model over its own exam.
    /// </summary>
    private static (List<ModelAggregate> Ranked, int BasisCaseCount) BuildOverallRanking(
        IReadOnlyList<BenchmarkRun> comparableRuns, DateTime now, string currentAppVersion, List<string> caveats)
    {
        var groups = new List<ModelGroup>();
        foreach (var group in comparableRuns.Where(r => r.Results.Count > 0)
                     .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase))
        {
            var runs = group.ToList();
            var results = runs.SelectMany(r => r.Results.Select(res => (Run: r, Result: res))).ToList();
            var latest = runs.OrderByDescending(r => r.StartedAt).First();

            // The existing evidence floor, unchanged: a volume gate, not a
            // comparability gate. 4.1 adds the comparability gate on top.
            if (runs.Count < MinRunsForEvidence || runs.Sum(r => r.Total) < MinCasesForEvidence)
            {
                caveats.Add(BuildShortfallCaveat(latest.ModelName, runs.Count, runs.Sum(r => r.Total)));
                continue;
            }

            groups.Add(new ModelGroup
            {
                Key = group.Key,
                Runs = runs,
                Results = results,
                CaseKeys = results.Select(t => KeyOf(t.Result)).ToHashSet()
            });
        }

        if (groups.Count == 0)
            return ([], 0);

        var (chosen, commonCases) = ChooseComparisonSet(groups);
        if (chosen.Count == 0 || commonCases.Count == 0)
        {
            caveats.Add(
                "No two models have run enough of the same cases to rank overall. " +
                "Run the same suite on each model to compare them.");
            return ([], 0);
        }

        foreach (var excluded in groups.Where(g => !chosen.Contains(g)))
        {
            var missing = commonCases.Count(c => !excluded.CaseKeys.Contains(c));
            caveats.Add(
                $"{excluded.Runs[^1].ModelName} is not comparable yet: has not run " +
                $"{missing} of the {commonCases.Count} shared case(s).");
        }

        var aggregates = chosen
            .Select(g => ScoreOverCases(g, commonCases, now, currentAppVersion))
            .OrderByDescending(a => a.QualityPerSecond)
            .ThenBy(a => a.ModelId, StringComparer.Ordinal)
            .ToList();

        return (aggregates, commonCases.Count);
    }

    /// <summary>
    /// Picks the largest set of models whose shared case set still clears
    /// <see cref="MinCasesForEvidence"/>, so one sparsely-benchmarked model
    /// cannot flatten the whole leaderboard. Deterministic: candidates are
    /// dropped smallest-coverage first, ties broken on the group key.
    ///
    /// A single model is a legitimate answer. There is no comparison to be
    /// unfair about, and suppressing it would be a regression for the common
    /// case of benchmarking one model.
    /// </summary>
    private static (List<ModelGroup> Chosen, HashSet<CaseKey> CommonCases) ChooseComparisonSet(List<ModelGroup> groups)
    {
        // With one candidate there is no comparison to be unfair about, so the
        // common-case-set requirement does not apply: the evidence floor already
        // covers whether there is enough data. Requiring a shared exam here would
        // silently remove the board for the common case of benchmarking a single
        // model.
        if (groups.Count == 1)
            return (groups, groups[0].CaseKeys);

        var remaining = groups
            .OrderByDescending(g => g.CaseKeys.Count)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Two or more candidates: never degrade to a single-model board. Dropping
        // models until only one is left and then calling it "best overall" would
        // be the exact dishonesty 4.1 exists to remove, dressed up as a
        // comparison.
        while (remaining.Count >= 2)
        {
            var common = new HashSet<CaseKey>(remaining[0].CaseKeys);
            foreach (var group in remaining.Skip(1))
                common.IntersectWith(group.CaseKeys);

            if (common.Count >= MinCasesForEvidence)
                return (remaining, common);

            // Drop the narrowest contributor and try again.
            remaining.RemoveAt(remaining.Count - 1);
        }

        return ([], []);
    }

    /// <summary>
    /// Scores one model over a fixed case set, result by result. Copies
    /// <see cref="BuildTagLeaderboards"/>'s result-level scoring rather than the
    /// run-level weighted average, because a case-restricted ranking has to look
    /// at cases, not whole runs.
    /// </summary>
    private static ModelAggregate ScoreOverCases(
        ModelGroup group, HashSet<CaseKey> cases, DateTime now, string currentAppVersion)
    {
        var scored = group.Results.Where(t => cases.Contains(KeyOf(t.Result))).ToList();
        var caseCount = scored.Count;
        var latest = scored.OrderByDescending(t => t.Run.StartedAt).First().Run;

        var quality = scored.Average(t => t.Result.QualityScore);
        var tokensPerSecond = scored.Average(t => t.Result.ApproxTokensPerSecond);
        var resource = scored.Average(t => t.Result.ResourceScore);
        var stability = scored.Count(t => !t.Result.HasError && !t.Result.TimedOut && !t.Result.Cancelled)
                        / (double)caseCount;
        var lastRunAt = group.Runs.Max(r => r.StartedAt);
        var isStale = (now - lastRunAt).TotalDays > StaleAfterDays
                      || !VersionMajorMinorMatches(latest.Metadata.AppVersion, currentAppVersion);

        // One row per case, averaging repeats of the same case, so the drill-down
        // shows the same numbers the ranking used.
        var perCase = scored
            .GroupBy(t => KeyOf(t.Result))
            .Select(g => new ModelCaseResult(
                g.Key.CaseId,
                g.Select(t => t.Result.CaseName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? g.Key.CaseId,
                g.Key.CaseVersion,
                g.SelectMany(t => t.Result.Tags).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
                Math.Round(g.Average(t => t.Result.QualityScore), 4),
                Math.Round(g.Average(t => t.Result.ApproxTokensPerSecond), 2),
                g.All(t => !t.Result.HasError && !t.Result.TimedOut && !t.Result.Cancelled)))
            .OrderBy(c => c.CaseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.CaseId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModelAggregate(
            latest.ModelId, latest.ModelName, latest.Metadata.Quantization, latest.Metadata.RuntimeKind,
            group.Runs.Count, group.Runs.Sum(r => r.Total),
            Math.Round(quality, 4), Math.Round(tokensPerSecond, 2), Math.Round(stability, 4),
            BenchmarkScoring.RankingScore(quality, tokensPerSecond, stability, resource),
            Math.Round(QualityPerSecond(quality, tokensPerSecond), 4),
            lastRunAt, isStale,
            // Distinct shared cases, not result rows: a case run three times is
            // still one case for the purpose of "what does this comparison rest
            // on", even though all three rows feed the averages above.
            perCase.Count, perCase);
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
