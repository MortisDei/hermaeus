using System.Reflection;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

/// <summary>
/// Turns stored benchmark runs into a <see cref="BenchmarkInsightsReport"/>:
/// resolves case tags onto results that predate tag propagation (suite-join
/// fallback), filters to the current machine's hardware, then hands off to
/// the pure <see cref="BenchmarkInsightsMath"/>. See
/// docs/review/02-benchmark-insights.md.
/// </summary>
public sealed class BenchmarkInsightsService : IBenchmarkInsightsService
{
    private readonly BenchmarkService _benchmarks;
    private readonly ISystemInfoService _systemInfo;

    public BenchmarkInsightsService(BenchmarkService benchmarks, ISystemInfoService systemInfo)
    {
        _benchmarks = benchmarks;
        _systemInfo = systemInfo;
    }

    public async Task<BenchmarkInsightsReport> LoadReportAsync(CancellationToken ct = default)
    {
        var runs = await _benchmarks.GetRunsAsync(ct);
        var suites = await _benchmarks.GetSuitesAsync(ct);
        var currentSnapshot = await _systemInfo.CaptureAsync(ct);

        ResolveTags(runs, suites);

        var comparableRuns = runs
            .Where(r => BenchmarkInsightsMath.IsHardwareComparable(r.HardwareSnapshot, currentSnapshot))
            .ToList();

        var currentAppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? string.Empty;

        return BenchmarkInsightsMath.BuildReport(runs, comparableRuns, currentAppVersion);
    }

    /// <summary>
    /// Runs recorded before tag propagation (r5) carry no
    /// <see cref="BenchmarkResult.Tags"/>. When the originating suite still
    /// exists, join the case's tags in by <see cref="BenchmarkResult.CaseId"/>
    /// so old runs still contribute to per-tag leaderboards. A deleted suite
    /// leaves the result untagged; it still counts toward overall stats.
    /// </summary>
    private static void ResolveTags(IReadOnlyList<BenchmarkRun> runs, IReadOnlyList<BenchmarkSuite> suites)
    {
        var suitesById = suites.ToDictionary(s => s.Id, s => s);
        foreach (var run in runs)
        {
            if (!suitesById.TryGetValue(run.SuiteId, out var suite))
                continue;

            var casesById = suite.Cases.ToDictionary(c => c.Id, c => c);
            foreach (var result in run.Results)
            {
                if (result.Tags.Count > 0)
                    continue;
                if (casesById.TryGetValue(result.CaseId, out var benchmarkCase) && benchmarkCase.Tags.Count > 0)
                    result.Tags = benchmarkCase.Tags.ToList();
            }
        }
    }
}
