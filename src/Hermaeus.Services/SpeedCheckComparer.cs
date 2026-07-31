using Hermaeus.Core.Models;

namespace Hermaeus.Services;

/// <summary>
/// r27 03-drafting-and-proof.md 3.6: puts two speed-check runs of the same
/// suite against the same model side by side, with the configuration difference
/// that separates them.
/// Pure over two <see cref="BenchmarkRun"/> objects: no store, no process, no
/// judgement. The app reports what happened; it does not rate itself.
/// </summary>
public static class SpeedCheckComparer
{
    public static SpeedCheckComparisonResult Compare(BenchmarkRun? baseline, BenchmarkRun? candidate)
    {
        if (baseline is null || candidate is null)
            return SpeedCheckComparisonResult.Refuse("Two completed runs are needed to compare.");

        if (string.Equals(baseline.Id, candidate.Id, StringComparison.Ordinal))
            return SpeedCheckComparisonResult.Refuse("A run cannot be compared with itself.");

        // Comparing across models or suites would put a difference on screen
        // that the configuration delta does not explain.
        if (!string.Equals(baseline.ModelId, candidate.ModelId, StringComparison.Ordinal))
            return SpeedCheckComparisonResult.Refuse(
                $"These runs used different models ({Describe(baseline.ModelName, baseline.ModelId)} and {Describe(candidate.ModelName, candidate.ModelId)}). Compare runs of the same model.");

        if (!string.Equals(baseline.SuiteId, candidate.SuiteId, StringComparison.Ordinal))
            return SpeedCheckComparisonResult.Refuse(
                $"These runs used different suites ({baseline.SuiteName} and {candidate.SuiteName}). Compare runs of the same suite.");

        if (baseline.Results.Count == 0 || candidate.Results.Count == 0)
            return SpeedCheckComparisonResult.Refuse("A run with no results cannot be compared.");

        return SpeedCheckComparisonResult.From(new SpeedCheckComparison(
            baseline.ModelId,
            baseline.SuiteId,
            ToSide(baseline),
            ToSide(candidate)));
    }

    private static SpeedCheckSide ToSide(BenchmarkRun run)
    {
        var perIteration = run.Results.Select(r => r.ApproxTokensPerSecond).ToList();
        // Only the results that actually carried draft counters contribute;
        // a run where the server reported none stays null all the way to the
        // display rather than summing to a misleading zero (r28 doc 02 2.4).
        var drafted = run.Results.Where(r => r.DraftTokens.HasValue).ToList();

        return new SpeedCheckSide(
            run.Id,
            run.StartedAt,
            run.Metadata.SpeculativeSummary,
            Median(perIteration),
            run.Results.Where(r => r.PromptTokensPerSecond.HasValue).Select(r => r.PromptTokensPerSecond!.Value).DefaultIfEmpty(0).Average(),
            run.AverageFirstTokenMs,
            perIteration.DefaultIfEmpty(0).Min(),
            perIteration.DefaultIfEmpty(0).Max(),
            perIteration.Count,
            drafted.Count == 0 ? null : drafted.Sum(r => r.DraftTokens!.Value),
            drafted.Count == 0 ? null : drafted.Sum(r => r.DraftTokensAccepted ?? 0));
    }

    /// <summary>
    /// Middle value, averaging the two middles on an even count. Preferred
    /// over the mean because iteration 0 is deliberately a cold pass and a
    /// mean lets it drag the headline number (r28 doc 02 2.3).
    /// </summary>
    internal static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    private static string Describe(string name, string id) => string.IsNullOrWhiteSpace(name) ? id : name;
}
