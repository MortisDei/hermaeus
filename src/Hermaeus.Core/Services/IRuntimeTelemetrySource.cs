using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface IRuntimeTelemetrySource
{
    Task<IReadOnlyList<RuntimeTelemetrySample>> CaptureAsync(
        RuntimeTelemetryRequest request,
        CancellationToken ct = default);
}
