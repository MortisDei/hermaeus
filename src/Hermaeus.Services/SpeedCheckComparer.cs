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

    private static SpeedCheckSide ToSide(BenchmarkRun run) => new(
        run.Id,
        run.StartedAt,
        run.Metadata.SpeculativeSummary,
        run.AverageApproxTokensPerSecond,
        run.Results.Where(r => r.PromptTokensPerSecond.HasValue).Select(r => r.PromptTokensPerSecond!.Value).DefaultIfEmpty(0).Average(),
        run.AverageFirstTokenMs);

    private static string Describe(string name, string id) => string.IsNullOrWhiteSpace(name) ? id : name;
}
