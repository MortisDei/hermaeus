using Aether.Core.Models;

namespace Aether.Core.Services;

public interface ITrustService
{
    Task<TrustScanReport> ScanAsync(AppSettings settings, CancellationToken ct = default);
    IReadOnlyList<TrustItem> AnalyzeServerExtraArgs(ServerConfig server, DateTime scannedAt);
}
