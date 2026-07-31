using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IBenchmarkInsightsService
{
    Task<BenchmarkInsightsReport> LoadReportAsync(CancellationToken ct = default);

    /// <summary>
    /// The most recent completed Speed Check run for a model, or null when
    /// that model has never been through one (r28 doc 02 2.5). Kept here
    /// rather than teaching Doctor how to query benchmark storage: Doctor
    /// asks the benchmark subsystem a question and applies a deterministic
    /// rule to the answer.
    /// </summary>
    Task<BenchmarkRun?> GetLatestSpeedCheckRunAsync(string modelId, CancellationToken ct = default) =>
        Task.FromResult<BenchmarkRun?>(null);
}
