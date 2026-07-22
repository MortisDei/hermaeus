using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IBenchmarkInsightsService
{
    Task<BenchmarkInsightsReport> LoadReportAsync(CancellationToken ct = default);
}
