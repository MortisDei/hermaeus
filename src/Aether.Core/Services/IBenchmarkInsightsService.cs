using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IBenchmarkInsightsService
{
    Task<BenchmarkInsightsReport> LoadReportAsync(CancellationToken ct = default);
}
