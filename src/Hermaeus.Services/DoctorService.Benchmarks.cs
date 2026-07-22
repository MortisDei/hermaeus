using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Storage;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Voice;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hermaeus.Services;

public sealed partial class DoctorService
{
    /// <summary>
    /// Info-only: when benchmark data shows the currently selected chat
    /// model ranking well behind the best comparable model on this
    /// hardware, says so. Never Warning/Error, never switches anything -
    /// recommendations inform, the user decides. Omitted entirely (not just
    /// "no issue") when there is no comparable benchmark data, so a fresh
    /// install's Doctor page is not cluttered with an always-present row.
    /// docs/review/02-benchmark-insights.md ("2.4").
    /// </summary>
    private async Task<DoctorCheck?> CheckBenchmarkAdvisoryAsync(CancellationToken ct)
    {
        if (_benchmarkInsights is null)
            return null;

        const double rankingGapThreshold = 10;
        try
        {
            var report = await _benchmarkInsights.LoadReportAsync(ct);
            var currentModelId = _settings.Settings.Llm.DefaultModel;

            DoctorCheck? check = null;
            var best = report.BestOverall;
            var current = best is null ? null : report.Models.FirstOrDefault(m => m.ModelId == currentModelId);
            if (best is not null && current is not null && current.ModelId != best.ModelId)
            {
                var gap = (best.RankingScore - current.RankingScore) * 100;
                if (gap > rankingGapThreshold)
                {
                    check = BuildCheck(
                        "benchmark-advisory",
                        "Benchmark data suggests a better default model",
                        DoctorCheckStatus.Info,
                        $"Benchmark data suggests {best.ModelName} may serve you better overall than {current.ModelName}.",
                        $"{best.ModelName} ranks {gap:F0} points higher (ranking score {best.RankingScore:P0} vs {current.RankingScore:P0}) " +
                        $"across {best.RunCount} comparable run(s). This is informational only; nothing switches automatically.",
                        "Open Benchmarks",
                        false,
                        string.Empty,
                        "Benchmarks");
                }
            }

            // Usage-aware extension (r6 02-usage-history-recommendations.md
            // 2.3): at most one extra sentence, appended to the r5 advisory
            // if it already fired, or standalone if usage alone recommends a
            // switch the ranking-only check above did not catch. Same
            // philosophy as r5: informational only, never auto-switches.
            var usageInsight = report.UsageInsightsOrEmpty
                .FirstOrDefault(u => u.Kind == TraceKind.Chat && u.RecommendedModelName is not null);
            if (usageInsight is not null)
            {
                var usageSentence = $"Usage-aware: {usageInsight.Sentence}";
                check = check is null
                    ? BuildCheck(
                        "benchmark-advisory",
                        "Benchmark data suggests a better default model",
                        DoctorCheckStatus.Info,
                        usageInsight.Sentence,
                        $"{usageSentence} This is informational only; nothing switches automatically.",
                        "Open Benchmarks",
                        false,
                        string.Empty,
                        "Benchmarks")
                    : check with { Detail = $"{check.Detail} {usageSentence}" };
            }

            return check;
        }
        catch
        {
            // Best-effort advisory; never let a benchmark aggregation failure break the Doctor scan.
            return null;
        }
    }
}
