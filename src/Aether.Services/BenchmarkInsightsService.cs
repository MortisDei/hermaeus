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
    private readonly IModelUsageService? _modelUsage;

    public BenchmarkInsightsService(BenchmarkService benchmarks, ISystemInfoService systemInfo, IModelUsageService? modelUsage = null)
    {
        _benchmarks = benchmarks;
        _systemInfo = systemInfo;
        _modelUsage = modelUsage;
    }

    public async Task<BenchmarkInsightsReport> LoadReportAsync(CancellationToken ct = default)
    {
        var runs = await _benchmarks.GetRunsAsync(ct);
        var suites = await _benchmarks.GetSuitesAsync(ct);
        var currentSnapshot = await _systemInfo.CaptureAsync(ct);

        ResolveTags(runs, suites);
        NormalizeRuntimeKind(runs);

        var comparableRuns = runs
            .Where(r => BenchmarkInsightsMath.IsHardwareComparable(r.HardwareSnapshot, currentSnapshot))
            .ToList();

        var currentAppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? string.Empty;

        IReadOnlyList<KindUsageSummary>? usage = null;
        if (_modelUsage is not null)
        {
            try { usage = await _modelUsage.GetKindSummariesAsync(30, ct); }
            catch { /* usage insights are a best-effort addition; never break the benchmark report */ }
        }

        return BenchmarkInsightsMath.BuildReport(runs, comparableRuns, currentAppVersion, usage: usage);
    }

    /// <summary>
    /// Legacy runs recorded before r17 02-benchmark-truth.md 2.4 all stamped
    /// <c>Metadata.RuntimeKind = "dotnet"</c> regardless of the model, which made the Insights
    /// grouping key (ModelId|Quantization|RuntimeKind) degenerate. In-memory only - this does
    /// not write back to the store - map that placeholder onto a best-effort kind derived from
    /// the run's own <see cref="BenchmarkRun.Provider"/> string, so an old "dotnet" run and a new
    /// correctly-stamped run of the same model still land in one aggregate group.
    /// </summary>
    private static void NormalizeRuntimeKind(IReadOnlyList<BenchmarkRun> runs)
    {
        foreach (var run in runs)
        {
            if (!string.Equals(run.Metadata.RuntimeKind, "dotnet", StringComparison.OrdinalIgnoreCase))
                continue;

            var provider = run.Provider ?? string.Empty;
            run.Metadata.RuntimeKind =
                provider.Contains("llama", StringComparison.OrdinalIgnoreCase) || provider.Contains("gguf", StringComparison.OrdinalIgnoreCase) ? "llama.cpp" :
                provider.Contains("ollama", StringComparison.OrdinalIgnoreCase) ? "ollama" :
                provider.Contains("openai", StringComparison.OrdinalIgnoreCase) ? "openai-compatible" :
                provider;
        }
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
